using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;

namespace BeckhoffAutomationInterface.Sync
{
    /// <summary>Where a build error really lives: the original .st relative path and
    /// 1-based line — or, when unmapped, the raw TwinCAT-reported values.</summary>
    class ResolvedErrorLocation
    {
        public string Path { get; }
        public int Line { get; }
        public bool Mapped { get; }

        public ResolvedErrorLocation(string path, int line, bool mapped)
        {
            Path = path;
            Line = line;
            Mapped = mapped;
        }
    }

    /// <summary>
    /// Maps TwinCAT build-error locations back to the original .st source
    /// (tasks/todo.md Task 6), using the provenance StFileParser records on every
    /// StPouSource (Task 5) and the error shapes confirmed empirically against a
    /// real TwinCAT 3.1.4026 build (Task 4, 2026-07-14):
    ///
    ///   FB body:    ...\FB_X.TcPOU (Impl):2            line is IMPLEMENTATION-relative
    ///   METHOD:     ...\FB_X.TcPOU@Method (Impl):2     that method's implementation
    ///   PROPERTY:   ...\FB_X.TcPOU@Prop.Get (Impl):1   that accessor's body
    ///   GVL:        ...\GVL_X.TcGVL:3                  DECLARATION-relative (= file line)
    ///   DUT:        ...\T_X.TcDUT:5                    declaration-relative (= file line)
    ///   project:    empty FileName, Line 0             unmappable
    ///
    /// In every observed case: mapped line = section start line + (reported line - 1).
    /// Anything that doesn't match a known shape or a parsed object falls back to the
    /// raw value, clearly labeled — never silently dropped.
    /// </summary>
    static class ErrorLocationResolver
    {
        // "FB_X.TcPOU", "FB_X.TcPOU@Method (Impl)", "FB_X.TcPOU@Prop.Get (Impl)",
        // "GVL_X.TcGVL", "T_X.TcDUT (Decl)" — captured off the exported file's base name.
        static readonly Regex ExportedNameRegex = new Regex(
            @"^(?<obj>.+)\.Tc(POU|DUT|GVL|IO)(@(?<member>\w+)(\.(?<accessor>Get|Set))?)?( \((?<section>Decl|Impl)\))?$",
            RegexOptions.IgnoreCase);

        /// <summary>Index of parsed sources by full name ("FB_X" / "FB_X.Init"), for Resolve.</summary>
        public static Dictionary<string, StPouSource> BuildIndex(IEnumerable<StPouSource> sources)
        {
            var index = new Dictionary<string, StPouSource>(StringComparer.OrdinalIgnoreCase);
            foreach (StPouSource src in sources)
            {
                string fullName = src.OwnerName != null ? src.OwnerName + "." + src.Name : src.Name;
                index[fullName] = src;
            }
            return index;
        }

        public static ResolvedErrorLocation Resolve(string reportedFileName, int reportedLine, IReadOnlyDictionary<string, StPouSource> index)
        {
            var raw = new ResolvedErrorLocation(reportedFileName, reportedLine, mapped: false);
            if (string.IsNullOrWhiteSpace(reportedFileName))
                return raw;

            Match match = ExportedNameRegex.Match(Path.GetFileName(reportedFileName).Trim());
            if (!match.Success)
                return raw;

            string objName = match.Groups["obj"].Value;
            string member = match.Groups["member"].Success ? match.Groups["member"].Value : null;
            string fullName = member != null ? objName + "." + member : objName;

            if (!index.TryGetValue(fullName, out StPouSource src))
                return raw;

            bool implSection = match.Groups["section"].Success &&
                match.Groups["section"].Value.Equals("Impl", StringComparison.OrdinalIgnoreCase);
            string accessor = match.Groups["accessor"].Success ? match.Groups["accessor"].Value : null;

            int sectionStart;
            if (accessor != null)
            {
                sectionStart = (accessor.Equals("Get", StringComparison.OrdinalIgnoreCase)
                    ? src.GetStartLine : src.SetStartLine) ?? src.DeclarationStartLine;
            }
            else if (implSection)
            {
                sectionStart = src.ImplementationStartLine ?? src.DeclarationStartLine;
            }
            else
            {
                sectionStart = src.DeclarationStartLine;
            }

            int mappedLine = reportedLine > 0 ? sectionStart + reportedLine - 1 : sectionStart;
            return new ResolvedErrorLocation(src.SourceRelativePath, mappedLine, mapped: true);
        }
    }
}
