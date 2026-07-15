using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace BeckhoffAutomationInterface.Sync
{
    /// <summary>
    /// Warn-only drift detection for renamed/deleted .st content (tasks/todo.md
    /// Tasks 7-8, decision: warn always, prune opt-in). Records every synced
    /// object's full name ("FB_Motor", "FB_Motor.Init", "PRG_Main.Setpoint") in a
    /// state file next to .st-sync-state, and on later runs reports names that
    /// disappeared from the parse — content that still compiles silently in the
    /// PLC project even though its .st source is gone. Never deletes anything:
    /// deletion stays where it already is (--incremental --confirm-delete for
    /// whole top-level objects).
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

        /// <summary>
        /// Incremental-sync diff: the current parse only covers CHANGED files, so a
        /// previously-known name missing from it usually just means its file wasn't
        /// re-parsed — not that it's gone. The only disappearances an incremental parse
        /// can prove are member-level: a previously-known "Owner.Member" whose Owner IS
        /// in the current parse (its file was fully re-parsed) but whose Member no longer
        /// is. Top-level deletions are git's job (IncrementalDeleter handles them).
        /// </summary>
        public static List<string> DiffWithinOwners(IReadOnlyCollection<string> previous, IReadOnlyCollection<string> current)
        {
            var currentSet = new HashSet<string>(current, StringComparer.OrdinalIgnoreCase);
            var currentTopLevel = new HashSet<string>(
                current.Where(n => !n.Contains('.')), StringComparer.OrdinalIgnoreCase);

            return previous
                .Where(n =>
                {
                    int dot = n.IndexOf('.');
                    if (dot < 0) return false; // top-level: not provable from a partial parse
                    string owner = n.Substring(0, dot);
                    return currentTopLevel.Contains(owner) && !currentSet.Contains(n);
                })
                .OrderBy(n => n, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        /// <summary>Next recorded state after an incremental run: everything previously
        /// known, minus what provably disappeared, plus everything just parsed. (A full
        /// sync simply records CollectNames' output instead.)</summary>
        public static List<string> Merge(IReadOnlyCollection<string> previous, IReadOnlyCollection<string> current, IReadOnlyCollection<string> disappeared)
        {
            var result = new HashSet<string>(previous, StringComparer.OrdinalIgnoreCase);
            result.ExceptWith(disappeared);
            result.UnionWith(current);
            return result.OrderBy(n => n, StringComparer.OrdinalIgnoreCase).ToList();
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
