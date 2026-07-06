using System.Collections.Generic;
using System.IO;

namespace BeckhoffAutomationInterface.Sync
{
    /// <summary>One formatting issue found in a .st file.</summary>
    class FormatIssue
    {
        public string RelativePath { get; }
        public string Description { get; }

        public FormatIssue(string relativePath, string description)
        {
            RelativePath = relativePath;
            Description = description;
        }
    }

    /// <summary>
    /// Read-only style checker for .st files — the first, deliberately minimal slice of the
    /// "ST formatter" plan item. Checks only low-risk, non-controversial things (trailing
    /// whitespace, mixed line endings, EOF newline hygiene); does NOT re-indent or rewrite
    /// anything based on ST nesting (that's a separate, much riskier piece of future work,
    /// deferred on purpose since it would meaningfully rewrite user source). --format-check
    /// only reports; there is no --format (write) mode yet.
    /// </summary>
    static class StFormatter
    {
        public static List<FormatIssue> CheckFolder(string sourceFolder, IgnoreRules ignore)
        {
            string root = Path.GetFullPath(sourceFolder).TrimEnd(Path.DirectorySeparatorChar);
            var issues = new List<FormatIssue>();
            foreach (string file in StFileParser.GetStFiles(root, ignore))
                issues.AddRange(CheckFile(root, file));
            return issues;
        }

        static IEnumerable<FormatIssue> CheckFile(string root, string filePath)
        {
            string relativePath = filePath.Substring(root.Length).TrimStart('\\', '/').Replace('\\', '/');
            string text = File.ReadAllText(filePath);
            if (text.Length == 0)
                yield break;

            bool hasCrlf = text.Contains("\r\n");
            bool hasLoneLf = text.Replace("\r\n", "").Contains("\n");
            if (hasCrlf && hasLoneLf)
                yield return new FormatIssue(relativePath, "mixed line endings (both CRLF and bare LF)");

            string[] lines = text.Replace("\r\n", "\n").Split('\n');

            int trailingWhitespaceCount = 0;
            // The last array element is the (possibly empty) tail after the final newline,
            // not a real line — don't count it as "trailing whitespace" on its own.
            for (int i = 0; i < lines.Length - 1; i++)
            {
                string line = lines[i];
                if (line.Length > 0 && (line[line.Length - 1] == ' ' || line[line.Length - 1] == '\t'))
                    trailingWhitespaceCount++;
            }
            if (trailingWhitespaceCount > 0)
                yield return new FormatIssue(relativePath, $"{trailingWhitespaceCount} line(s) with trailing whitespace");

            if (lines[lines.Length - 1].Length != 0)
            {
                yield return new FormatIssue(relativePath, "missing trailing newline at end of file");
            }
            else
            {
                int extraBlankLines = 0;
                for (int i = lines.Length - 2; i >= 0 && lines[i].Length == 0; i--)
                    extraBlankLines++;
                if (extraBlankLines > 0)
                    yield return new FormatIssue(relativePath, $"{extraBlankLines} extra blank line(s) at end of file");
            }
        }
    }
}
