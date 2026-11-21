using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace BeckhoffAutomationInterface.Sync
{
    /// <summary>
    /// Reads compiler errors from the text of the "Build" output pane. Fallback for
    /// BuildRunner: when the XAE window is hidden, the DTE error list returns items with
    /// empty descriptions, but the output pane still holds every message, e.g.
    ///   C:\…\POUs\MAIN.TcPOU(13) : error: C0032: Cannot convert type …
    /// </summary>
    static class BuildOutputParser
    {
        static readonly Regex ErrorLine = new Regex(
            @"^\s*(?<file>.+?)\((?<line>\d+)\)\s*:\s*error\s*:\s*(?<text>.+?)\s*$",
            RegexOptions.Multiline | RegexOptions.IgnoreCase);

        public static List<BuildError> ParseErrors(string outputText)
        {
            var errors = new List<BuildError>();
            if (string.IsNullOrEmpty(outputText))
                return errors;

            foreach (Match m in ErrorLine.Matches(outputText))
            {
                errors.Add(new BuildError
                {
                    Level = "High",
                    Description = m.Groups["text"].Value,
                    FileName = m.Groups["file"].Value.Trim(),
                    Line = int.Parse(m.Groups["line"].Value)
                });
            }
            return errors;
        }

        static readonly Regex WarningLine = new Regex(
            @"^\s*(?<file>.+?)\((?<line>\d+)\)\s*:\s*warning\s*:\s*(?<text>.+?)\s*$",
            RegexOptions.Multiline | RegexOptions.IgnoreCase);

        /// <summary>Compiler warnings of the Build pane, without the PLC's message pragmas
        /// (C0373, see ParsePragmaMessages). The pane always holds them; the DTE error list
        /// can be empty or without descriptions when the XAE window is hidden.</summary>
        public static List<BuildError> ParseWarnings(string outputText)
        {
            var warnings = new List<BuildError>();
            if (string.IsNullOrEmpty(outputText))
                return warnings;
            foreach (Match m in WarningLine.Matches(outputText))
            {
                string text = m.Groups["text"].Value;
                if (text.StartsWith("C0373", System.StringComparison.OrdinalIgnoreCase))
                    continue;
                warnings.Add(new BuildError
                {
                    Level = "Medium",
                    Description = text,
                    FileName = m.Groups["file"].Value.Trim(),
                    Line = int.Parse(m.Groups["line"].Value)
                });
            }
            return warnings;
        }

        static readonly Regex SummaryLine = new Regex(
            @"^\s*(?<text>Build complete -- .+?)\s*$", RegexOptions.Multiline | RegexOptions.IgnoreCase);

        /// <summary>TwinCAT's own count line ("Build complete -- 0 errors, 3 warnings : ..."),
        /// the last one in the pane; null when there is none.</summary>
        public static string ParseSummary(string outputText)
        {
            if (string.IsNullOrEmpty(outputText))
                return null;
            MatchCollection matches = SummaryLine.Matches(outputText);
            return matches.Count == 0 ? null : matches[matches.Count - 1].Groups["text"].Value;
        }

        // C0373 is the compiler code of the PLC's own message pragmas ({warning '...'}),
        // e.g. "…\MAIN.TcPOU(9) : warning: C0373: VARIANT CHECK: MachineA branch compiled".
        static readonly Regex PragmaMessageLine = new Regex(
            @":\s*(?:warning|info|message)\s*:\s*C0373\s*:\s*(?<text>.+?)\s*$",
            RegexOptions.Multiline | RegexOptions.IgnoreCase);

        /// <summary>Texts of the PLC's message pragmas in a build log — proof of which
        /// conditional-compilation branch was compiled.</summary>
        public static List<string> ParsePragmaMessages(string outputText)
        {
            var messages = new List<string>();
            if (string.IsNullOrEmpty(outputText))
                return messages;
            foreach (Match m in PragmaMessageLine.Matches(outputText))
                messages.Add(m.Groups["text"].Value);
            return messages;
        }
    }
}
