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
    }
}
