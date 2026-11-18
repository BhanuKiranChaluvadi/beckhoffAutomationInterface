using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml.Linq;

namespace BeckhoffAutomationInterface.Sync
{
    /// <summary>Applies converted objects to a CLOSED tool-owned PLC project. Configuration stays project-local.</summary>
    static class PlcProjectCode
    {
        public static SyncReport Apply(string plcproj, IReadOnlyList<PlcSourceObject> sources, PlcObjectCache cache,
            bool preserveMembers, ICollection<string> skip = null, IReadOnlyCollection<string> previousNames = null,
            Action<string, byte[]> write = null)
        {
            string directory = Path.GetDirectoryName(Path.GetFullPath(plcproj));
            XDocument project = XDocument.Load(plcproj, LoadOptions.PreserveWhitespace);
            XNamespace ns = project.Root.Name.Namespace;
            var report = new SyncReport();
            var batch = new ProjectFileBatch();
            bool projectChanged = false;
            List<XElement> includes = project.Descendants(ns + "Compile").Where(e => e.Attribute("Include") != null).ToList();
            var skipped = new HashSet<string>(skip ?? new string[0], PlcSourceObject.Names);
            var stale = new HashSet<string>(KnownNamesTracker.DiffFull(previousNames ?? new string[0],
                KnownNamesTracker.CollectNames(sources.SelectMany(s => s.Sources))), PlcSourceObject.Names);
            foreach (string name in stale.Where(n => !n.Contains(".")))
            {
                XElement[] matches = includes.Where(e => PlcSourceObject.Names.Equals(
                    Path.GetFileNameWithoutExtension((string)e.Attribute("Include")), name)).ToArray();
                if (matches.Length > 1) throw new InvalidOperationException("Ambiguous stale PLC object: " + name);
                foreach (XElement include in matches)
                {
                    batch.Set(Inside(directory, (string)include.Attribute("Include")), null);
                    include.Remove();
                    includes.Remove(include);
                    projectChanged = true;
                    report.Deleted.Add(name + " (gone)");
                }
            }
            foreach (PlcSourceObject source in sources)
            {
                bool prune = stale.Any(n => n.StartsWith(source.Root.Name + ".", StringComparison.OrdinalIgnoreCase));
                if (skipped.Contains(source.Root.Name) && !prune)
                {
                    Report(report.Unchanged, source);
                    report.Skipped += source.Sources.Count;
                    continue;
                }
                string relative = Path.Combine(source.Root.RelativeFolder.Replace('/', Path.DirectorySeparatorChar), source.FileName);
                XElement include = FindInclude(includes, source);
                // The template's MAIN already has task references; retain its file and ID.
                string oldRelative = (string)include?.Attribute("Include");
                bool moved = oldRelative != null && previousNames != null && source.Root.Kind != PouKind.Program &&
                    !PlcSourceObject.Names.Equals(oldRelative.Replace('/', '\\'), relative.Replace('/', '\\'));
                if (oldRelative != null && !moved) relative = oldRelative;
                string path = Inside(directory, relative);
                string oldPath = oldRelative == null ? path : Inside(directory, oldRelative);
                if (moved && File.Exists(path)) throw new InvalidOperationException("PLC move would overwrite an existing file: " + path);
                byte[] previous = File.Exists(oldPath) ? File.ReadAllBytes(oldPath) : null;
                byte[] bytes = ObjectBytes(source, cache, previous, preserveMembers, stale, report.Deleted);
                bool changed = previous == null || !bytes.SequenceEqual(previous);
                if (changed || moved) batch.Set(path, bytes);
                if (moved)
                {
                    batch.Set(oldPath, null);
                    include.SetAttributeValue("Include", relative.Replace('/', '\\'));
                    projectChanged = true;
                    report.Deleted.Add(source.Root.Name + " (moved from " + Path.GetDirectoryName(oldRelative) + ")");
                }
                Report(include == null ? report.Created : changed ? report.Updated : report.Unchanged, source);
                if (include == null)
                {
                    include = new XElement(ns + "Compile", new XAttribute("Include", relative.Replace('/', '\\')),
                        new XElement(ns + "SubType", "Code"));
                    Items(project, ns).Add(include);
                    includes.Add(include);
                    projectChanged = true;
                }
                AddFolders(project, ns, relative);
            }
            // Finish every conversion and path check before changing any project file.
            if (projectChanged) batch.Set(plcproj, PlcObjectXml.Bytes(project));
            batch.Commit(write);
            return report;
        }

        static XElement FindInclude(IEnumerable<XElement> includes, PlcSourceObject source)
        {
            string[] extensions = { ".TcPOU", ".TcIO", ".TcDUT", ".TcGVL" };
            XElement[] matching = includes.Where(e =>
                string.Equals(Path.GetFileNameWithoutExtension((string)e.Attribute("Include")), source.Root.Name, StringComparison.OrdinalIgnoreCase) &&
                extensions.Contains(Path.GetExtension((string)e.Attribute("Include")), StringComparer.OrdinalIgnoreCase)).ToArray();
            if (matching.Length > 1) throw new InvalidOperationException("Ambiguous PLC project object: " + source.Root.Name);
            XElement item = matching.SingleOrDefault();
            if (item != null && !string.Equals(Path.GetExtension((string)item.Attribute("Include")), Path.GetExtension(source.FileName), StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("PLC object changed file kind; rebuild the project fresh: " + source.Root.Name);
            return item;
        }

        static byte[] ObjectBytes(PlcSourceObject source, PlcObjectCache cache, byte[] previous, bool preserveMembers,
            ISet<string> stale, List<string> deleted)
        {
            XDocument converted;
            using (var input = new MemoryStream(cache.Get(source))) converted = XDocument.Load(input, LoadOptions.PreserveWhitespace);
            if (previous == null) return PlcObjectXml.Bytes(converted);
            using (var input = new MemoryStream(previous))
            {
                XDocument old = XDocument.Load(input, LoadOptions.PreserveWhitespace);
                XElement root = old.Root.Elements().Single();
                if (!string.Equals((string)root.Attribute("Name"), source.Root.Name, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException("PLC file name does not match its object: " + source.FileName);
                foreach (XElement member in root.Elements().Where(e => e.Name.LocalName == "Method" || e.Name.LocalName == "Property").ToList())
                {
                    string name = source.Root.Name + "." + (string)member.Attribute("Name");
                    if (!stale.Contains(name)) continue;
                    member.Remove();
                    deleted.Add(name + " (gone)");
                }
                MergeExisting(converted.Root.Elements().Single(), root, preserveMembers);
            }
            return PlcObjectXml.Bytes(converted);
        }

        /// <summary>Keep project-local IDs and, for standalone sync, members absent from the supplied source.</summary>
        static void MergeExisting(XElement desired, XElement existing, bool preserveMembers)
        {
            if (existing == null || desired.Name != existing.Name) return;
            if (existing.Attribute("Id") != null) desired.SetAttributeValue("Id", (string)existing.Attribute("Id"));
            foreach (XElement old in existing.Elements().Where(e => e.Attribute("Name") != null))
            {
                XElement current = desired.Elements(old.Name).SingleOrDefault(e => PlcSourceObject.Names.Equals(
                    (string)e.Attribute("Name"), (string)old.Attribute("Name")));
                if (current != null) MergeExisting(current, old, preserveMembers);
                else if (preserveMembers && new[] { "Method", "Property", "Get", "Set" }.Contains(old.Name.LocalName))
                    desired.Add(new XElement(old));
            }
        }

        static string Inside(string directory, string relative)
        {
            string prefix = directory.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            string path = Path.GetFullPath(Path.Combine(directory, relative));
            if (Path.IsPathRooted(relative) || !path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("PLC object path must stay inside its project: " + relative);
            return path;
        }

        static XElement Items(XDocument project, XNamespace ns)
        {
            XElement items = project.Root.Elements(ns + "ItemGroup").FirstOrDefault();
            if (items == null) { items = new XElement(ns + "ItemGroup"); project.Root.Add(items); }
            return items;
        }

        static void AddFolders(XDocument project, XNamespace ns, string relative)
        {
            string folder = Path.GetDirectoryName(relative);
            var folders = new HashSet<string>(project.Descendants(ns + "Folder").Select(e => (string)e.Attribute("Include")), StringComparer.OrdinalIgnoreCase);
            while (!string.IsNullOrEmpty(folder))
            {
                if (folders.Add(folder)) Items(project, ns).Add(new XElement(ns + "Folder", new XAttribute("Include", folder)));
                folder = Path.GetDirectoryName(folder);
            }
        }

        static void Report(List<string> report, PlcSourceObject source) => report.AddRange(source.Sources.Select(s =>
            s.OwnerName != null ? s.OwnerName + "." + s.Name : string.IsNullOrEmpty(s.RelativeFolder) ? s.Name : s.RelativeFolder + "/" + s.Name));
    }
}
