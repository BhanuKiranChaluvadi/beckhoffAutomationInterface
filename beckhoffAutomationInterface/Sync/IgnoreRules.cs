using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace BeckhoffAutomationInterface.Sync
{
    /// <summary>
    /// Gitignore-style pattern matching for excluding .st files from a sync run.
    /// Patterns come from a ".stignore" file at the source root (one pattern per
    /// line, "#" comments and blank lines skipped) merged with any --ignore CLI
    /// arguments. Not a full gitignore implementation (no negation, no directory-
    /// only "/" suffix) — just enough for "skip this file/folder by glob".
    ///
    /// Matching rules:
    ///   - A pattern containing "/" is matched against the whole source-relative
    ///     path (e.g. "Lib/Legacy/**").
    ///   - A pattern with no "/" is matched against the file name at any depth
    ///     (e.g. "*_deprecated.st" matches such a file in any folder), same as
    ///     a plain gitignore entry.
    ///   - "*" matches any run of characters except "/"; "**" matches any run of
    ///     characters including "/"; "?" matches a single non-"/" character.
    /// </summary>
    class IgnoreRules
    {
        readonly List<Regex> _patterns;

        IgnoreRules(List<Regex> patterns)
        {
            _patterns = patterns;
        }

        public static IgnoreRules Load(string sourceFolder, IEnumerable<string> extraPatterns)
        {
            var patterns = new List<string>();

            string stIgnorePath = Path.Combine(sourceFolder, ".stignore");
            if (File.Exists(stIgnorePath))
            {
                foreach (string line in File.ReadAllLines(stIgnorePath))
                {
                    string trimmed = line.Trim();
                    if (trimmed.Length == 0 || trimmed.StartsWith("#"))
                        continue;
                    patterns.Add(trimmed);
                }
            }

            patterns.AddRange(extraPatterns);

            return new IgnoreRules(patterns.Select(CompilePattern).ToList());
        }

        static Regex CompilePattern(string pattern)
        {
            string normalized = pattern.Replace('\\', '/');
            bool anchored = normalized.Contains("/");

            string escaped = Regex.Escape(normalized)
                .Replace(@"\*\*", "\u0001")
                .Replace(@"\*", "[^/]*")
                .Replace(@"\?", "[^/]")
                .Replace("\u0001", ".*");

            string regexText = anchored ? "^" + escaped + "$" : "(^|.*/)" + escaped + "$";
            return new Regex(regexText, RegexOptions.IgnoreCase);
        }

        /// <summary>relativePath is source-root-relative, e.g. "Lib/Legacy/FB_Old.st".</summary>
        public bool IsIgnored(string relativePath)
        {
            string normalized = relativePath.Replace('\\', '/');
            return _patterns.Any(p => p.IsMatch(normalized));
        }
    }
}
