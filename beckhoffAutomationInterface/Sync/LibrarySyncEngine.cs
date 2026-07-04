using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Interop.TCatSysManager;

namespace BeckhoffAutomationInterface.Sync
{
    class LibrarySyncReport
    {
        public List<string> Added { get; } = new List<string>();
        public List<string> Removed { get; } = new List<string>();
    }

    /// <summary>
    /// Reconciles a PLC project's library references (ITcPlcLibraryManager) against
    /// a desired list parsed from a manifest, adding missing references and
    /// removing ones that are no longer desired.
    ///
    /// ITcPlcLibRef only exposes a combined display-name string (e.g.
    /// "Tc2_Standard, * (Beckhoff Automation GmbH)"), so it's parsed back into
    /// Name/Version/Company for comparison. If a display name doesn't match the
    /// expected format, it's left untouched rather than risking an unintended
    /// removal.
    /// </summary>
    static class LibrarySyncEngine
    {
        static readonly Regex DisplayNamePattern = new Regex(@"^(?<name>.+?),\s*(?<version>.+?)\s*\((?<company>.+)\)$");

        public static LibrarySyncReport Sync(ITcPlcLibraryManager libManager, IReadOnlyList<LibraryReference> desired)
        {
            var report = new LibrarySyncReport();

            var existing = new List<(string DisplayName, string Name, string Version, string Company)>();
            ITcPlcReferences references = libManager.References;
            // NOTE: unlike ITcSmTreeItem.Child (1-based), ITcPlcReferences is 0-based.
            for (int i = 0; i < references.Count; i++)
            {
                ITcPlcLibRef libRef = references[i];
                string displayName = libRef.Name;
                Match m = DisplayNamePattern.Match(displayName);
                existing.Add(m.Success
                    ? (displayName, m.Groups["name"].Value, m.Groups["version"].Value, m.Groups["company"].Value)
                    : (displayName, displayName, (string)null, (string)null));
            }

            foreach (LibraryReference lib in desired)
            {
                bool alreadyPresent = existing.Any(e => e.Name == lib.Name && e.Version == lib.Version && e.Company == lib.Company);
                if (alreadyPresent)
                    continue;

                try
                {
                    libManager.AddLibrary(lib.Name, lib.Version, lib.Company);
                    report.Added.Add($"{lib.Name}, {lib.Version} ({lib.Company})");
                }
                catch (ArgumentException ex) when (ex.Message.Contains("already contained"))
                {
                    // The References collection read above can be stale/incomplete right
                    // after a project is (re)opened, so AddLibrary's own duplicate check
                    // is more authoritative than ours \u2014 treat this as an idempotent no-op.
                }
            }

            var desiredKeys = new HashSet<string>(desired.Select(d => $"{d.Name}|{d.Version}|{d.Company}"));
            foreach (var e in existing)
            {
                if (e.Version == null) continue; // unparseable display name; don't touch it
                string key = $"{e.Name}|{e.Version}|{e.Company}";
                if (!desiredKeys.Contains(key))
                {
                    libManager.RemoveReference(e.Name, e.Version, e.Company);
                    report.Removed.Add(e.DisplayName);
                }
            }

            return report;
        }
    }
}
