using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace BeckhoffAutomationInterface.Sync
{
    /// <summary>
    /// Parses .st source files into the Declaration/Implementation text pairs that
    /// TwinCAT stores separately on a POU object (see ITcPlcDeclaration /
    /// ITcPlcImplementation), classifying each file as a PROGRAM, FUNCTION_BLOCK,
    /// FUNCTION, INTERFACE, METHOD, or DUT (ENUM/STRUCT/ALIAS).
    ///
    /// This is a naive, line-based parser, validated end-to-end in
    /// docs/ideas/st-source-twincat-sync.md (2026-07-04). It is NOT a full IEC
    /// 61131-3 parser. Conventions it relies on:
    ///   - A FUNCTION_BLOCK's or INTERFACE's METHODs live inline in the SAME
    ///     file as the FB/INTERFACE (e.g. FB_Motor.st contains
    ///     "FUNCTION_BLOCK FB_Motor ... METHOD Init ... METHOD Reset ...").
    ///     ParseFile returns one StPouSource per FB/INTERFACE/METHOD found in
    ///     the file. A standalone "&lt;Owner&gt;.&lt;Method&gt;.st" file is also
    ///     still supported for a method defined on its own.
    ///   - The declaration/implementation split point within each FB/METHOD/
    ///     PROGRAM/FUNCTION section is the LAST "END_VAR" in that section, so
    ///     multiple VAR/VAR_INPUT/VAR_OUTPUT/VAR_IN_OUT blocks are supported.
    ///     An INTERFACE's own header has no VAR block (interfaces don't declare
    ///     variables), only its methods do.
    ///   - DUTs (ENUM/STRUCT/ALIAS) and GVLs have no implementation section;
    ///     the whole file is the declaration.
    ///   - Attribute pragma lines (e.g. {attribute 'qualified_only'}) preceding
    ///     a POU/DUT/METHOD keyword are treated as part of that section's
    ///     declaration and are skipped only when sniffing kind/boundaries.
    /// </summary>
    static class StFileParser
    {
        const string EndVarMarker = "END_VAR";
        // Headers may carry optional modifiers between the keyword and the name, e.g.
        // "FUNCTION_BLOCK ABSTRACT FB_X", "METHOD PUBLIC ABSTRACT Foo". These regexes skip
        // any run of such modifiers so the captured group is always the actual name.
        const string Modifiers = @"(?:(?:PUBLIC|PRIVATE|PROTECTED|INTERNAL|ABSTRACT|FINAL)\s+)*";
        static readonly Regex MethodHeaderRegex = new Regex(@"^\s*METHOD\s+" + Modifiers + @"(\w+)", RegexOptions.IgnoreCase | RegexOptions.Multiline);
        static readonly Regex PropertyHeaderRegex = new Regex(@"^\s*PROPERTY\s+" + Modifiers + @"(\w+)", RegexOptions.IgnoreCase | RegexOptions.Multiline);
        // Captures a property's return type after the colon, e.g. "LREAL" or "STRING(50)".
        static readonly Regex PropertyReturnTypeRegex = new Regex(@"^\s*PROPERTY\s+" + Modifiers + @"\w+\s*:\s*(.+?)\s*$", RegexOptions.IgnoreCase | RegexOptions.Multiline);
        // A member boundary is the start of either a METHOD or a PROPERTY section.
        static readonly Regex MemberHeaderRegex = new Regex(@"^\s*(?:METHOD|PROPERTY)\s", RegexOptions.IgnoreCase | RegexOptions.Multiline);
        static readonly Regex FunctionBlockHeaderRegex = new Regex(@"^\s*FUNCTION_BLOCK\s+" + Modifiers + @"(\w+)", RegexOptions.IgnoreCase | RegexOptions.Multiline);
        static readonly Regex FunctionBlockExtendsRegex = new Regex(@"^\s*FUNCTION_BLOCK\s+" + Modifiers + @"\w+\s+EXTENDS\s+(\w+)", RegexOptions.IgnoreCase | RegexOptions.Multiline);
        static readonly Regex ProgramHeaderRegex = new Regex(@"^\s*PROGRAM\s+(\w+)", RegexOptions.IgnoreCase | RegexOptions.Multiline);
        static readonly Regex InterfaceHeaderRegex = new Regex(@"^\s*INTERFACE\s+" + Modifiers + @"(\w+)", RegexOptions.IgnoreCase | RegexOptions.Multiline);
        static readonly Regex InterfaceExtendsRegex = new Regex(@"^\s*INTERFACE\s+" + Modifiers + @"\w+\s+EXTENDS\s+(\w+)", RegexOptions.IgnoreCase | RegexOptions.Multiline);
        static readonly Regex AliasBaseTypeRegex = new Regex(@"TYPE\s+\w+\s*:\s*(\w+)\s*;", RegexOptions.IgnoreCase);

        public static List<StPouSource> ParseFile(string stFilePath)
        {
            string fileName = Path.GetFileNameWithoutExtension(stFilePath);
            string source = File.ReadAllText(stFilePath);

            PouKind kind = ClassifyKind(source, stFilePath);

            if (kind == PouKind.FunctionBlock)
                return ParseFunctionBlockFile(stFilePath, source);
            if (kind == PouKind.Interface)
                return ParseInterfaceFile(stFilePath, source);
            if (kind == PouKind.Program)
                return ParseProgramFile(stFilePath, source);

            string ownerName = null;
            string name = fileName;
            if (kind == PouKind.Method)
            {
                int dotIndex = fileName.IndexOf('.');
                if (dotIndex < 0)
                    throw new FormatException($"Method file '{stFilePath}' must be named '<OwnerFbName>.<MethodName>.st'.");
                ownerName = fileName.Substring(0, dotIndex);
                name = fileName.Substring(dotIndex + 1);
            }

            if (kind == PouKind.EnumDut || kind == PouKind.StructDut || kind == PouKind.Gvl)
                return new List<StPouSource> { new StPouSource(name, kind, null, source.Trim(), null) };

            if (kind == PouKind.AliasDut)
            {
                var aliasMatch = AliasBaseTypeRegex.Match(source);
                if (!aliasMatch.Success)
                    throw new FormatException($"Could not find the aliased base type (e.g. 'TYPE {name} : LREAL;') in '{stFilePath}'.");
                return new List<StPouSource> { new StPouSource(name, kind, null, source.Trim(), null, aliasMatch.Groups[1].Value) };
            }

            (string declaration, string implementation) = SplitAtLastEndVar(source, stFilePath, name);
            return new List<StPouSource> { new StPouSource(name, kind, ownerName, declaration, implementation) };
        }

        public static List<StPouSource> ParseFolder(string sourceFolder)
        {
            string root = Path.GetFullPath(sourceFolder).TrimEnd(Path.DirectorySeparatorChar);
            var result = new List<StPouSource>();

            foreach (string file in Directory.GetFiles(root, "*.st", SearchOption.AllDirectories))
            {
                string relativeFolder = GetRelativeFolder(root, file);
                foreach (StPouSource src in ParseFile(file))
                {
                    src.RelativeFolder = relativeFolder;
                    result.Add(src);
                }
            }

            return result;
        }

        /// <summary>
        /// Returns the source-root-relative folder of a file, forward-slash
        /// separated (e.g. "App/Shark/FunctionBlocks"), or "" if the file sits
        /// directly in the root. (.NET Framework 4.8 has no Path.GetRelativePath.)
        /// </summary>
        static string GetRelativeFolder(string root, string filePath)
        {
            string dir = Path.GetFullPath(Path.GetDirectoryName(filePath));
            if (string.Equals(dir, root, StringComparison.OrdinalIgnoreCase))
                return "";
            string rel = dir.Substring(root.Length).TrimStart(Path.DirectorySeparatorChar);
            return rel.Replace(Path.DirectorySeparatorChar, '/');
        }

        /// <summary>
        /// Splits a FUNCTION_BLOCK file into the FB's own StPouSource plus one
        /// StPouSource per inline METHOD section.
        /// </summary>
        static List<StPouSource> ParseFunctionBlockFile(string filePath, string source)
        {
            string[] lines = source.Replace("\r\n", "\n").Split('\n');
            List<int> methodStarts = FindMethodBoundaries(lines);

            int fbSegmentEnd = methodStarts.Count > 0 ? methodStarts[0] : lines.Length;
            string fbSegment = string.Join("\n", lines, 0, fbSegmentEnd);

            var fbNameMatch = FunctionBlockHeaderRegex.Match(fbSegment);
            if (!fbNameMatch.Success)
                throw new FormatException($"Could not find 'FUNCTION_BLOCK <Name>' header in '{filePath}'.");
            string fbName = fbNameMatch.Groups[1].Value;

            var fbExtendsMatch = FunctionBlockExtendsRegex.Match(fbSegment);
            string fbExtends = fbExtendsMatch.Success ? fbExtendsMatch.Groups[1].Value : null;

            var results = new List<StPouSource>();
            (string fbDeclaration, string fbImplementation) = SplitAtLastEndVar(fbSegment, filePath, fbName);
            results.Add(new StPouSource(fbName, PouKind.FunctionBlock, null, fbDeclaration, fbImplementation, fbExtends));
            results.AddRange(ParseMethodSegments(filePath, lines, methodStarts, fbName));
            return results;
        }

        /// <summary>
        /// Splits a PROGRAM file into the program's own StPouSource plus one
        /// StPouSource per inline METHOD/PROPERTY section. PROGRAMs commonly carry
        /// private helper METHODs inline (e.g. "PROGRAM PRG_X ... METHOD PRIVATE
        /// _Init ... END_METHOD END_PROGRAM"); without this split, the whole file
        /// was previously treated as one big VAR/END_VAR-delimited blob, dumping
        /// the program's executable body AND the inline methods' headers/bodies
        /// into the PROGRAM's own declaration/implementation text (and leaving a
        /// literal "METHOD ..." line in the middle of the implementation, which
        /// TwinCAT rejects as an "Unexpected statement").
        /// </summary>
        static List<StPouSource> ParseProgramFile(string filePath, string source)
        {
            string[] lines = source.Replace("\r\n", "\n").Split('\n');
            List<int> methodStarts = FindMethodBoundaries(lines);

            int prgSegmentEnd = methodStarts.Count > 0 ? methodStarts[0] : lines.Length;
            string prgSegment = string.Join("\n", lines, 0, prgSegmentEnd);

            var prgNameMatch = ProgramHeaderRegex.Match(prgSegment);
            if (!prgNameMatch.Success)
                throw new FormatException($"Could not find 'PROGRAM <Name>' header in '{filePath}'.");
            string prgName = prgNameMatch.Groups[1].Value;

            var results = new List<StPouSource>();
            (string prgDeclaration, string prgImplementation) = SplitAtLastEndVar(prgSegment, filePath, prgName);
            results.Add(new StPouSource(prgName, PouKind.Program, null, prgDeclaration, prgImplementation));
            results.AddRange(ParseMethodSegments(filePath, lines, methodStarts, prgName));
            return results;
        }

        /// <summary>
        /// Splits an INTERFACE file into the interface's own StPouSource (its
        /// header only \u2014 interfaces declare no variables of their own) plus
        /// one StPouSource per inline METHOD signature.
        /// </summary>
        static List<StPouSource> ParseInterfaceFile(string filePath, string source)
        {
            string[] lines = source.Replace("\r\n", "\n").Split('\n');
            List<int> methodStarts = FindMethodBoundaries(lines);

            int headerEnd = methodStarts.Count > 0 ? methodStarts[0] : lines.Length;
            string headerSegment = string.Join("\n", lines, 0, headerEnd).Trim();

            var interfaceNameMatch = InterfaceHeaderRegex.Match(headerSegment);
            if (!interfaceNameMatch.Success)
                throw new FormatException($"Could not find 'INTERFACE <Name>' header in '{filePath}'.");
            string interfaceName = interfaceNameMatch.Groups[1].Value;

            var interfaceExtendsMatch = InterfaceExtendsRegex.Match(headerSegment);
            string interfaceExtends = interfaceExtendsMatch.Success ? interfaceExtendsMatch.Groups[1].Value : null;

            var results = new List<StPouSource>();
            results.Add(new StPouSource(interfaceName, PouKind.Interface, null, headerSegment, null, interfaceExtends));
            results.AddRange(ParseMethodSegments(filePath, lines, methodStarts, interfaceName));
            return results;
        }

        /// <summary>
        /// Finds line indices where inline METHOD or PROPERTY sections start, absorbing
        /// any immediately preceding attribute-pragma / blank lines into that section (so
        /// its attributes stay attached to it).
        /// </summary>
        static List<int> FindMethodBoundaries(string[] lines)
        {
            var starts = new List<int>();
            for (int i = 0; i < lines.Length; i++)
            {
                if (!MemberHeaderRegex.IsMatch(lines[i]))
                    continue;

                int start = i;
                while (start > 0)
                {
                    string prev = lines[start - 1].Trim();
                    if (prev.Length == 0 || prev.StartsWith("{"))
                        start--;
                    else
                        break;
                }

                if (starts.Count == 0 || start > starts[starts.Count - 1])
                    starts.Add(start);
            }
            return starts;
        }

        static IEnumerable<StPouSource> ParseMethodSegments(string filePath, string[] lines, List<int> memberStarts, string ownerName)
        {
            for (int m = 0; m < memberStarts.Count; m++)
            {
                int segStart = memberStarts[m];
                int segEnd = (m + 1 < memberStarts.Count) ? memberStarts[m + 1] : lines.Length;
                string segment = string.Join("\n", lines, segStart, segEnd - segStart);

                var propMatch = PropertyHeaderRegex.Match(segment);
                var methodMatch = MethodHeaderRegex.Match(segment);
                // Whichever keyword appears first in the segment wins (a segment is one member).
                bool isProperty = propMatch.Success &&
                    (!methodMatch.Success || propMatch.Index <= methodMatch.Index);

                if (isProperty)
                {
                    yield return ParseProperty(filePath, segment, propMatch.Groups[1].Value, ownerName);
                }
                else
                {
                    if (!methodMatch.Success)
                        throw new FormatException($"Could not find 'METHOD/PROPERTY <Name>' header in a section of '{filePath}'.");
                    string methodName = methodMatch.Groups[1].Value;
                    (string decl, string impl) = SplitAtLastEndVar(segment, filePath, methodName);
                    yield return new StPouSource(methodName, PouKind.Method, ownerName, decl, impl);
                }
            }
        }

        /// <summary>
        /// Parses a PROPERTY section into its header declaration plus the GET / SET accessor
        /// bodies, e.g.:
        ///   PROPERTY PUBLIC Setpoint : LREAL
        ///       GET  Setpoint := _x;  END_GET
        ///       SET  _x := Setpoint;  END_SET
        ///   END_PROPERTY
        /// The declaration is everything before the first GET/SET; GET/SET bodies exclude the
        /// GET/END_GET/SET/END_SET keywords.
        /// </summary>
        static StPouSource ParseProperty(string filePath, string segment, string propName, string ownerName)
        {
            string[] lines = segment.Replace("\r\n", "\n").Split('\n');

            int getStart = -1, getEnd = -1, setStart = -1, setEnd = -1, firstAccessor = lines.Length;
            for (int i = 0; i < lines.Length; i++)
            {
                string t = lines[i].Trim();
                if (t.Equals("GET", StringComparison.OrdinalIgnoreCase) || t.StartsWith("GET ", StringComparison.OrdinalIgnoreCase))
                { getStart = i; if (i < firstAccessor) firstAccessor = i; }
                else if (t.Equals("END_GET", StringComparison.OrdinalIgnoreCase)) getEnd = i;
                else if (t.Equals("SET", StringComparison.OrdinalIgnoreCase) || t.StartsWith("SET ", StringComparison.OrdinalIgnoreCase))
                { setStart = i; if (i < firstAccessor) firstAccessor = i; }
                else if (t.Equals("END_SET", StringComparison.OrdinalIgnoreCase)) setEnd = i;
            }

            string declaration = string.Join("\n", lines, 0, firstAccessor).Trim();
            string getText = (getStart >= 0 && getEnd > getStart)
                ? string.Join("\n", lines, getStart + 1, getEnd - getStart - 1).Trim()
                : null;
            string setText = (setStart >= 0 && setEnd > setStart)
                ? string.Join("\n", lines, setStart + 1, setEnd - setStart - 1).Trim()
                : null;

            // A bare "PROPERTY Name : Type" with no GET/SET block at all (common for
            // read-only INTERFACE property signatures, which declare no body anyway) still
            // needs at least one accessor object created, or TwinCAT rejects the property
            // with "The property defines neither a get nor a set accessor." Default such
            // properties to a (bodyless, for interfaces) Get accessor.
            if (getText == null && setText == null)
                getText = "";

            // The property's return type (after the colon) is required by CreateChild's vInfo.
            var typeMatch = PropertyReturnTypeRegex.Match(declaration);
            string returnType = typeMatch.Success ? typeMatch.Groups[1].Value.Trim() : "BOOL";

            return new StPouSource(propName, PouKind.Property, ownerName, declaration, null, returnType, getText, setText);
        }

        static (string declaration, string implementation) SplitAtLastEndVar(string source, string filePath, string sectionName)
        {
            int endVarIndex = source.LastIndexOf(EndVarMarker, StringComparison.OrdinalIgnoreCase);
            if (endVarIndex < 0)
                // No VAR...END_VAR block (e.g. a METHOD or FUNCTION with no local variables):
                // the declaration is just the header line, the rest is the body.
                return SplitAfterHeaderLine(source);

            int declarationEnd = endVarIndex + EndVarMarker.Length;
            string declaration = source.Substring(0, declarationEnd).Trim();
            string implementation = source.Substring(declarationEnd).Trim();
            return (declaration, implementation);
        }

        /// <summary>
        /// Splits a section that has no VAR...END_VAR block into (header line, body): the
        /// declaration is everything up to and including the first POU/METHOD header line
        /// (e.g. "METHOD IsInitialized : BOOL"), the implementation is the rest.
        /// </summary>
        static (string declaration, string implementation) SplitAfterHeaderLine(string source)
        {
            string[] lines = source.Replace("\r\n", "\n").Split('\n');
            int headerIdx = -1;
            for (int i = 0; i < lines.Length; i++)
            {
                string u = lines[i].Trim().ToUpperInvariant();
                if (u.StartsWith("METHOD") || u.StartsWith("FUNCTION_BLOCK") || u.StartsWith("FUNCTION")
                    || u.StartsWith("PROGRAM") || u.StartsWith("INTERFACE"))
                {
                    headerIdx = i;
                    break;
                }
            }
            if (headerIdx < 0)
                return (source.Trim(), "");

            string declaration = string.Join("\n", lines, 0, headerIdx + 1).Trim();
            string implementation = headerIdx + 1 < lines.Length
                ? string.Join("\n", lines, headerIdx + 1, lines.Length - headerIdx - 1).Trim()
                : "";
            return (declaration, implementation);
        }

        static PouKind ClassifyKind(string source, string filePath)
        {
            // Classify against a comment-stripped copy so leading (* ... *) block comments
            // and // line comments (common file headers) don't hide the first keyword, and
            // so a comment containing "STRUCT"/"(" can't misclassify a DUT.
            string stripped = StripComments(source);

            foreach (string rawLine in stripped.Split('\n'))
            {
                string line = rawLine.Trim().TrimEnd('\r');
                if (line.Length == 0) continue;
                if (line.StartsWith("{")) continue;  // attribute pragma, e.g. {attribute 'qualified_only'}

                string upper = line.ToUpperInvariant();
                if (upper.StartsWith("FUNCTION_BLOCK")) return PouKind.FunctionBlock;
                if (upper.StartsWith("FUNCTION")) return PouKind.Function;
                if (upper.StartsWith("PROGRAM")) return PouKind.Program;
                if (upper.StartsWith("INTERFACE")) return PouKind.Interface;
                if (upper.StartsWith("METHOD")) return PouKind.Method;
                if (upper.StartsWith("VAR_GLOBAL")) return PouKind.Gvl;
                if (upper.StartsWith("TYPE"))
                {
                    string upperSource = stripped.ToUpperInvariant();
                    if (upperSource.Contains("STRUCT")) return PouKind.StructDut;
                    if (upperSource.Contains("(")) return PouKind.EnumDut; // enum literal list, e.g. TYPE X : (A, B); END_TYPE
                    return PouKind.AliasDut; // simple alias, e.g. TYPE T_MotorSpeed : LREAL; END_TYPE
                }

                // Not a recognized keyword \u2014 skip it rather than throw. Comment-stripping is
                // best-effort (ST comments can legitimately contain "*)" inside them, e.g.
                // "(AMBT_TEMP_*)", which leaves stray fragments), so tolerate junk lines and
                // keep scanning for the first real POU/DUT/GVL keyword.
            }

            throw new FormatException($"Could not classify POU kind for '{filePath}' \u2014 no FUNCTION_BLOCK/FUNCTION/PROGRAM/INTERFACE/METHOD/VAR_GLOBAL/TYPE keyword found.");
        }

        /// <summary>
        /// Removes ST comments \u2014 (* ... *) block comments (nesting-aware) and // line
        /// comments \u2014 while preserving newlines so line-based scanning still works. Used
        /// only for classification/boundary sniffing; the original text (with comments) is
        /// what gets stored as the POU's declaration/implementation.
        /// </summary>
        static string StripComments(string source)
        {
            var sb = new System.Text.StringBuilder(source.Length);
            int depth = 0;
            for (int i = 0; i < source.Length; i++)
            {
                if (depth == 0 && i + 1 < source.Length && source[i] == '/' && source[i + 1] == '/')
                {
                    while (i < source.Length && source[i] != '\n') i++;
                    if (i < source.Length) sb.Append('\n');
                    continue;
                }
                if (i + 1 < source.Length && source[i] == '(' && source[i + 1] == '*')
                {
                    depth++;
                    i++;
                    continue;
                }
                if (depth > 0 && i + 1 < source.Length && source[i] == '*' && source[i + 1] == ')')
                {
                    depth--;
                    i++;
                    continue;
                }
                if (depth > 0)
                {
                    if (source[i] == '\n') sb.Append('\n');
                    continue;
                }
                sb.Append(source[i]);
            }
            return sb.ToString();
        }
    }
}
