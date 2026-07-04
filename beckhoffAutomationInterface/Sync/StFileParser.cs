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
        // Method name capture skips an optional access modifier (METHOD PUBLIC Init : BOOL),
        // otherwise the modifier keyword itself would be mistaken for the method name.
        static readonly Regex MethodHeaderRegex = new Regex(@"^\s*METHOD\s+(?:(?:PUBLIC|PRIVATE|PROTECTED|INTERNAL)\s+)?(\w+)", RegexOptions.IgnoreCase | RegexOptions.Multiline);
        static readonly Regex FunctionBlockHeaderRegex = new Regex(@"^\s*FUNCTION_BLOCK\s+(\w+)", RegexOptions.IgnoreCase | RegexOptions.Multiline);
        static readonly Regex FunctionBlockExtendsRegex = new Regex(@"^\s*FUNCTION_BLOCK\s+\w+\s+EXTENDS\s+(\w+)", RegexOptions.IgnoreCase | RegexOptions.Multiline);
        static readonly Regex InterfaceHeaderRegex = new Regex(@"^\s*INTERFACE\s+(\w+)", RegexOptions.IgnoreCase | RegexOptions.Multiline);
        static readonly Regex InterfaceExtendsRegex = new Regex(@"^\s*INTERFACE\s+\w+\s+EXTENDS\s+(\w+)", RegexOptions.IgnoreCase | RegexOptions.Multiline);
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
            return Directory.GetFiles(sourceFolder, "*.st", SearchOption.TopDirectoryOnly)
                .SelectMany(ParseFile)
                .ToList();
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
        /// Finds line indices where inline METHOD sections start, absorbing any
        /// immediately preceding attribute-pragma / blank lines into that
        /// method's section (so its attributes stay attached to it).
        /// </summary>
        static List<int> FindMethodBoundaries(string[] lines)
        {
            var methodStarts = new List<int>();
            for (int i = 0; i < lines.Length; i++)
            {
                if (!MethodHeaderRegex.IsMatch(lines[i]))
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

                if (methodStarts.Count == 0 || start > methodStarts[methodStarts.Count - 1])
                    methodStarts.Add(start);
            }
            return methodStarts;
        }

        static IEnumerable<StPouSource> ParseMethodSegments(string filePath, string[] lines, List<int> methodStarts, string ownerName)
        {
            for (int m = 0; m < methodStarts.Count; m++)
            {
                int segStart = methodStarts[m];
                int segEnd = (m + 1 < methodStarts.Count) ? methodStarts[m + 1] : lines.Length;
                string methodSegment = string.Join("\n", lines, segStart, segEnd - segStart);

                var methodNameMatch = MethodHeaderRegex.Match(methodSegment);
                if (!methodNameMatch.Success)
                    throw new FormatException($"Could not find 'METHOD <Name>' header in a section of '{filePath}'.");
                string methodName = methodNameMatch.Groups[1].Value;

                (string methodDeclaration, string methodImplementation) = SplitAtLastEndVar(methodSegment, filePath, methodName);
                yield return new StPouSource(methodName, PouKind.Method, ownerName, methodDeclaration, methodImplementation);
            }
        }

        static (string declaration, string implementation) SplitAtLastEndVar(string source, string filePath, string sectionName)
        {
            int endVarIndex = source.LastIndexOf(EndVarMarker, StringComparison.OrdinalIgnoreCase);
            if (endVarIndex < 0)
                throw new FormatException($"Could not find END_VAR for '{sectionName}' in '{filePath}'.");

            int declarationEnd = endVarIndex + EndVarMarker.Length;
            string declaration = source.Substring(0, declarationEnd).Trim();
            string implementation = source.Substring(declarationEnd).Trim();
            return (declaration, implementation);
        }

        static PouKind ClassifyKind(string source, string filePath)
        {
            foreach (string rawLine in source.Split('\n'))
            {
                string line = rawLine.Trim().TrimEnd('\r');
                if (line.Length == 0) continue;
                if (line.StartsWith("{")) continue;  // attribute pragma, e.g. {attribute 'qualified_only'}
                if (line.StartsWith("//")) continue; // comment line

                string upper = line.ToUpperInvariant();
                if (upper.StartsWith("FUNCTION_BLOCK")) return PouKind.FunctionBlock;
                if (upper.StartsWith("FUNCTION")) return PouKind.Function;
                if (upper.StartsWith("PROGRAM")) return PouKind.Program;
                if (upper.StartsWith("INTERFACE")) return PouKind.Interface;
                if (upper.StartsWith("METHOD")) return PouKind.Method;
                if (upper.StartsWith("VAR_GLOBAL")) return PouKind.Gvl;
                if (upper.StartsWith("TYPE"))
                {
                    string upperSource = source.ToUpperInvariant();
                    if (upperSource.Contains("STRUCT")) return PouKind.StructDut;
                    if (upperSource.Contains("(")) return PouKind.EnumDut; // enum literal list, e.g. TYPE X : (A, B); END_TYPE
                    return PouKind.AliasDut; // simple alias, e.g. TYPE T_MotorSpeed : LREAL; END_TYPE
                }

                throw new FormatException($"Could not classify POU kind for '{filePath}' \u2014 unrecognized first keyword '{line}'.");
            }

            throw new FormatException($"'{filePath}' appears to be empty.");
        }
    }
}
