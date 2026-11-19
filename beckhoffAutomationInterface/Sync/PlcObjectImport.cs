using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace BeckhoffAutomationInterface.Sync
{
    /// <summary>
    /// Import of object files that XAE saved in another project (PlcObjectFileStore) into an open
    /// project, instead of building each object over COM. Plan and check are pure; the COM call
    /// is PouSyncEngine.Import. After the import the project is saved and checked: every imported
    /// object must be in its folder, with a file that holds every source text (methods and
    /// properties included). XAE reports no error when it skips a file, so this check is the proof.
    /// </summary>
    static class PlcObjectImport
    {
        /// <summary>Automation Interface: ITcSmTreeItem.CreateChild(null, 58, null, string[] paths)
        /// imports existing .TcPOU/.TcDUT/.TcGVL files (Beckhoff InfoSys, "Creating PLC objects").</summary>
        public const int ImportFiles = 58;

        public sealed class Item
        {
            public Item(string name, string folder, List<StPouSource> group, string file)
            {
                Name = name; Folder = folder; Group = group; File = file;
            }

            public string Name { get; }
            /// <summary>The source folder ("Lib/A"; "" for the project root).</summary>
            public string Folder { get; }
            public List<StPouSource> Group { get; }
            /// <summary>The stored file to import.</summary>
            public string File { get; }
        }

        /// <summary>The objects to import: not skipped by the sync manifest, not named in the
        /// project (.plcproj files by file name), and with a stored file.</summary>
        public static List<Item> Plan(IEnumerable<StPouSource> sources, ICollection<string> skip,
            IReadOnlyDictionary<string, string> projectFiles, Func<IReadOnlyList<StPouSource>, string> find)
        {
            var skipped = new HashSet<string>(skip ?? new string[0], PlcSourceObject.Names);
            var plan = new List<Item>();
            foreach (PlcSourceObject o in PlcSourceObject.Group(sources))
            {
                if (skipped.Contains(o.Root.Name) || projectFiles.ContainsKey(o.FileName))
                    continue;
                string file = find(o.Sources);
                if (file != null)
                    plan.Add(new Item(o.Root.Name, o.Root.RelativeFolder, o.Sources.ToList(), file));
            }
            return plan;
        }

        /// <summary>Each imported object that the saved project does not hold in its folder with
        /// all its texts (saved: SyncManifest.SavedFiles of the imported objects).</summary>
        public static List<string> Missing(IEnumerable<Item> imported, IEnumerable<SyncManifest.SavedFile> saved)
        {
            Dictionary<string, SyncManifest.SavedFile> byName = saved.ToDictionary(s => s.Name, PlcSourceObject.Names);
            var missing = new List<string>();
            foreach (Item item in imported)
            {
                if (!byName.TryGetValue(item.Name, out SyncManifest.SavedFile file))
                {
                    missing.Add(item.Name + " (not in the saved project with all its texts)");
                    continue;
                }
                string folder = (Path.GetDirectoryName(file.Include) ?? "").Replace(Path.DirectorySeparatorChar, '/');
                if (!string.Equals(folder, item.Folder ?? "", StringComparison.OrdinalIgnoreCase))
                    missing.Add(item.Name + " (in '" + folder + "', expected '" + item.Folder + "')");
            }
            return missing;
        }
    }
}
