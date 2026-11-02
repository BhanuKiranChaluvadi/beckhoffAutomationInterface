using System;
using System.Collections.Generic;
using System.Linq;

namespace BeckhoffAutomationInterface.Sync
{
    /// <summary>One PLC object to delete in a kept project: DeleteChild(ChildName) on the item at ParentPath.</summary>
    sealed class DeleteStep
    {
        public string ParentPath { get; }
        public string ChildName { get; }

        /// <summary>For the report, e.g. "FB_X (gone)" or "FB_X (moved from App/A)".</summary>
        public string Display { get; }

        public DeleteStep(string parentPath, string childName, string display)
        {
            ParentPath = parentPath;
            ChildName = childName;
            Display = display;
        }
    }

    /// <summary>
    /// The objects a kept project must lose before the sync: objects whose source is gone,
    /// and objects whose source moved to another folder (the sync creates them again there).
    /// </summary>
    sealed class StaleObjectPlan
    {
        public List<DeleteStep> Steps { get; } = new List<DeleteStep>();

        /// <summary>Names the plan cannot place safely (more than one live object); the caller builds fresh.</summary>
        public List<string> Unresolved { get; } = new List<string>();

        /// <param name="previousNames">.st-known-names of the last sync ("Name", "Owner.Member"); null on a first run.</param>
        /// <param name="current">The parsed sources of this sync.</param>
        /// <param name="live">Top-level object name (any case) to every tree path where the kept project has it.</param>
        /// <param name="projectRootPath">The PLC project root; source folders mirror below it.</param>
        public static StaleObjectPlan Build(IReadOnlyCollection<string> previousNames, IReadOnlyList<StPouSource> current,
            IReadOnlyDictionary<string, IReadOnlyList<string>> live, string projectRootPath)
        {
            var plan = new StaleObjectPlan();
            var owners = new List<DeleteStep>();
            var members = new List<DeleteStep>();
            var deletedOwners = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            List<string> stale = KnownNamesTracker.DiffFull(previousNames ?? new string[0], KnownNamesTracker.CollectNames(current));

            foreach (string name in stale.Where(n => !n.Contains(".")))
            {
                string path = OnePath(plan, live, name);
                if (path == null)
                    continue;
                owners.Add(new DeleteStep(Parent(path), name, name + " (gone)"));
                deletedOwners.Add(name);
            }

            // A PROGRAM is updated in place wherever it is (the sync's name fallback), so it never moves.
            foreach (StPouSource source in current.Where(s => s.OwnerName == null && s.Kind != PouKind.Program))
            {
                string path = OnePath(plan, live, source.Name);
                string expected = projectRootPath + Folder(source.RelativeFolder) + "^" + source.Name;
                if (path == null || string.Equals(path, expected, StringComparison.OrdinalIgnoreCase))
                    continue;
                string oldFolder = Parent(path).Substring(Math.Min(projectRootPath.Length + 1, Parent(path).Length)).Replace('^', '/');
                owners.Add(new DeleteStep(Parent(path), source.Name, source.Name + " (moved from " + oldFolder + ")"));
                deletedOwners.Add(source.Name);
            }

            foreach (string name in stale.Where(n => n.Contains(".")))
            {
                int dot = name.IndexOf('.');
                string owner = name.Substring(0, dot);
                if (deletedOwners.Contains(owner))
                    continue;
                string ownerPath = OnePath(plan, live, owner);
                if (ownerPath != null)
                    members.Add(new DeleteStep(ownerPath, name.Substring(dot + 1), name + " (gone)"));
            }

            plan.Steps.AddRange(members);
            plan.Steps.AddRange(owners);
            return plan;
        }

        /// <summary>The single live path of a name; null when the project has none, or more than one
        /// (then the name is Unresolved).</summary>
        static string OnePath(StaleObjectPlan plan, IReadOnlyDictionary<string, IReadOnlyList<string>> live, string name)
        {
            if (!live.TryGetValue(name, out IReadOnlyList<string> paths) || paths.Count == 0)
                return null;
            if (paths.Count == 1)
                return paths[0];
            if (!plan.Unresolved.Contains(name, StringComparer.OrdinalIgnoreCase))
                plan.Unresolved.Add(name);
            return null;
        }

        static string Folder(string relativeFolder) =>
            string.IsNullOrEmpty(relativeFolder) ? "" : "^" + relativeFolder.Replace('/', '^');

        static string Parent(string path) => path.Substring(0, path.LastIndexOf('^'));
    }
}
