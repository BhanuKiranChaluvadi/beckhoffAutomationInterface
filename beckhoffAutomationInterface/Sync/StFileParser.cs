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
    /// FUNCTION, METHOD, or DUT (ENUM/STRUCT).
    ///
    /// This is a naive, line-based parser, validated end-to-end in
    /// docs/ideas/st-source-twincat-sync.md (2026-07-04). It is NOT a full IEC
    /// 61131-3 parser. Conventions it relies on:
    ///   - A FUNCTION_BLOCK's METHODs live inline in the SAME file as the FB
    ///     (e.g. FB_Motor.st contains "FUNCTION_BLOCK FB_Motor ... METHOD Init
    ///     ... METHOD Reset ..."). ParseFile returns one StPouSource per
    ///     FB/METHOD found in the file. A standalone "&lt;Owner&gt;.&lt;Method&gt;.st"
    ///     file is also still supported for a method defined on its own.
    ///   - The declaration/implementation split point within each FB/METHOD/
    ///     PROGRAM/FUNCTION section is the LAST "END_VAR" in that section, so
    ///     multiple VAR/VAR_INPUT/VAR_OUTPUT/VAR_IN_OUT blocks are supported.
    ///   - DUTs (ENUM/STRUCT) have no implementation section; the whole file is
    ///     the declaration.
    ///   - Attribute pragma lines (e.g. {attribute 'qualified_only'}) preceding
    ///     a POU/DUT/METHOD keyword are treated as part of that section's
    ///     declaration and are skipped only when sniffing kind/boundaries.
    /// </summary>
    static class StFileParser
    {
        const string EndVarMarker = "END_VAR";
        static readonly Regex MethodHeaderRegex = new Regex(@"^\s*METHOD\s+(\w+)", RegexOptions.IgnoreCase | RegexOptions.Multiline);
        static readonly Regex FunctionBlockHeaderRegex = new Regex(@"^\s*FUNCTION_BLOCK\s+(\w+)", RegexOptions.IgnoreCase | RegexOptions.Multiline);

        public static List<StPouSource> ParseFile(string stFilePath)
        {
            string fileName = Path.GetFileNameWithoutExtension(stFilePath);
            string source = File.ReadAllText(stFilePath);

            PouKind kind = ClassifyKind(source, stFilePath);

            if (kind == PouKind.FunctionBlock)
                return ParseFunctionBlockFile(stFilePath, source);

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

            if (kind == PouKind.EnumDut || kind == PouKind.StructDut)
                return new List<StPouSource> { new StPouSource(name, kind, null, source.Trim(), null) };

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
        /// StPouSource per inline METHOD section. Boundaries are found by
        /// scanning for lines starting with "METHOD &lt;name&gt;"; any attribute
        /// pragma / blank lines immediately preceding a METHOD line are pulled
        /// into that method's section (so its attributes stay attached to it).
        /// </summary>
        static List<StPouSource> ParseFunctionBlockFile(string filePath, string source)
        {
            string[] lines = source.Replace("\r\n", "\n").Split('\n');

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

            int fbSegmentEnd = methodStarts.Count > 0 ? methodStarts[0] : lines.Length;
            string fbSegment = string.Join("\n", lines, 0, fbSegmentEnd);

            var fbNameMatch = FunctionBlockHeaderRegex.Match(fbSegment);
            if (!fbNameMatch.Success)
                throw new FormatException($"Could not find 'FUNCTION_BLOCK <Name>' header in '{filePath}'.");
            string fbName = fbNameMatch.Groups[1].Value;

            var results = new List<StPouSource>();
            (string fbDeclaration, string fbImplementation) = SplitAtLastEndVar(fbSegment, filePath, fbName);
            results.Add(new StPouSource(fbName, PouKind.FunctionBlock, null, fbDeclaration, fbImplementation));

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
                results.Add(new StPouSource(methodName, PouKind.Method, fbName, methodDeclaration, methodImplementation));
            }

            return results;
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
                if (upper.StartsWith("METHOD")) return PouKind.Method;
                if (upper.StartsWith("TYPE"))
                    return source.ToUpperInvariant().Contains("STRUCT") ? PouKind.StructDut : PouKind.EnumDut;

                throw new FormatException($"Could not classify POU kind for '{filePath}' \u2014 unrecognized first keyword '{line}'.");
            }

            throw new FormatException($"'{filePath}' appears to be empty.");
        }
    }
}
