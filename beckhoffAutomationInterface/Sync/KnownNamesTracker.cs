using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace BeckhoffAutomationInterface.Sync
{
    /// <summary>
    /// Warn-only drift detection for renamed/deleted .st content. Records every synced
    /// object's full name ("FB_Motor", "FB_Motor.Init", "PRG_Main.Setpoint") in
    /// .st-known-names in the source folder, and on later runs reports names that
    /// disappeared from the parse: content that still compiles in the PLC project
    /// although its .st source is gone. Never deletes anything.
    /// </summary>
    static class KnownNamesTracker
    {
        /// <summary>Full names of every parsed object: "Name" for top-level objects,
        /// "Owner.Member" for METHODs/PROPERTYs. Sorted, distinct.</summary>
        public static List<string> CollectNames(IEnumerable<StPouSource> sources) =>
            sources
                .Select(s => s.OwnerName != null ? s.OwnerName + "." + s.Name : s.Name)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(n => n, StringComparer.OrdinalIgnoreCase)
                .ToList();

        /// <summary>Full-sync diff: every previously-known name no longer in the current
        /// parse (the current parse covered the whole source tree, so any missing name
        /// really is gone — renamed or deleted).</summary>
        public static List<string> DiffFull(IReadOnlyCollection<string> previous, IReadOnlyCollection<string> current)
        {
            var currentSet = new HashSet<string>(current, StringComparer.OrdinalIgnoreCase);
            return previous.Where(n => !currentSet.Contains(n))
                .OrderBy(n => n, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        /// <summary>Null when no state file exists yet (first run — nothing to diff against).</summary>
        public static List<string> Read(string path) =>
            File.Exists(path)
                ? File.ReadAllLines(path).Where(l => l.Trim().Length > 0).ToList()
                : null;

        public static void Write(string path, IEnumerable<string> names) =>
            File.WriteAllLines(path, names);
    }
}
