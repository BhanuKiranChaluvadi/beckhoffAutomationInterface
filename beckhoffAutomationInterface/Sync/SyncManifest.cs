using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Web.Script.Serialization;
using System.Xml;
using System.Xml.Linq;

namespace BeckhoffAutomationInterface.Sync
{
    /// <summary>
    /// bai-sync-manifest.json next to the .tsproj: for each top-level PLC object (with its METHODs
    /// and PROPERTYs) the SHA-256 of the texts bai sends for it and the SHA-256 of its object file
    /// (.TcPOU/.TcDUT/.TcGVL/.TcIO) after the sync saved the project. An entry is recorded only when
    /// that file holds every text as a whole CDATA section, so an entry is a fact about the file:
    /// "a file with this hash holds these texts". The next sync skips the COM read and compare of
    /// an object when both hashes are still the same (the file must be one the .plcproj names). A
    /// changed source, a file saved again (an edit in XAE), a missing or unreadable manifest, or a
    /// manifest of another bai or format: the object is compared as before.
    /// </summary>
    sealed class SyncManifest
    {
        public const int Format = 2;
        public const string FileName = "bai-sync-manifest.json";

        public sealed class Entry
        {
            public Entry(string source, string disk)
            {
                Source = source;
                Disk = disk;
            }

            public string Source { get; }
            public string Disk { get; }
        }

        public SyncManifest(string bai) { Bai = bai; }

        public string Bai { get; }
        public Dictionary<string, Entry> Entries { get; } = new Dictionary<string, Entry>(StringComparer.OrdinalIgnoreCase);

        /// <summary>This bai: its version and the SHA-256 of its program file (a rebuilt bai starts a new manifest).</summary>
        public static string CurrentBai => _currentBai.Value;
        static readonly Lazy<string> _currentBai = new Lazy<string>(() =>
        {
            string exe = Assembly.GetEntryAssembly()?.Location ?? typeof(SyncManifest).Assembly.Location;
            return Cli.CliHelp.Version + " " + Sha256(File.ReadAllBytes(exe));
        });

        /// <summary>Skip the COM read only when the source and the project file are both as recorded.</summary>
        internal static bool CanSkip(Entry entry, string sourceHash, string diskHash) =>
            entry != null && sourceHash != null && diskHash != null && entry.Source == sourceHash && entry.Disk == diskHash;

        /// <summary>The same validated object groups used by conversion and synchronization.</summary>
        internal static Dictionary<string, List<StPouSource>> Groups(IEnumerable<StPouSource> sources)
        {
            return PlcSourceObject.Group(sources).ToDictionary(s => s.Root.Name, s => s.Sources.ToList(), PlcSourceObject.Names);
        }

        /// <summary>SHA-256 of kind, names, base type and texts. Sync manifests also include the destination folder; conversions do not.</summary>
        internal static string SourceHash(IReadOnlyList<StPouSource> group) => PlcSourceObject.SourceHash(group, includeFolder: true);

        internal static string FileExtension(PouKind kind) => PlcSourceObject.FileExtension(kind);

        /// <summary>The object files the .plcproj names (Compile Include), by file name (any case),
        /// as paths relative to the PLC project folder. A file name that is there twice is left out.</summary>
        internal static Dictionary<string, string> ProjectFiles(string plcprojText)
        {
            var files = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var twice = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (XElement compile in XDocument.Parse(plcprojText).Descendants().Where(e => e.Name.LocalName == "Compile"))
            {
                string include = (string)compile.Attribute("Include");
                if (include == null || !PlcSourceObject.IsObjectFile(include))
                    continue;
                string name = Path.GetFileName(include);
                if (files.ContainsKey(name))
                    twice.Add(name);
                files[name] = include;
            }
            foreach (string name in twice)
                files.Remove(name);
            return files;
        }

        /// <summary>True when the object file holds every text bai writes for the group as a whole
        /// CDATA section (line ends and white space at the end ignored, as PouSyncEngine.TextDiffers).
        /// An interface has no bodies: only its declarations count.</summary>
        internal static bool FileHoldsTexts(string fileText, IReadOnlyList<StPouSource> group)
        {
            string file = fileText.Replace("\r\n", "\n");
            bool itf = group[0].Kind == PouKind.Interface;
            foreach (StPouSource s in group)
            {
                var texts = new List<string> { s.DeclarationText };
                if (!itf)
                    texts.AddRange(new[] { s.ImplementationText, s.GetText, s.SetText });
                foreach (string text in texts)
                    if (!HasSection(file, (text ?? "").Replace("\r\n", "\n").TrimEnd()))
                        return false;
            }
            return true;
        }

        /// <summary>The text of an object file as XAE saves it (UTF-8, with or without a byte order mark).</summary>
        internal static string FileText(byte[] bytes)
        {
            using (var reader = new StreamReader(new MemoryStream(bytes), Encoding.UTF8, detectEncodingFromByteOrderMarks: true))
                return reader.ReadToEnd();
        }

        static bool HasSection(string file, string text)
        {
            if (text.Length == 0)
                return true;
            string start = "<![CDATA[" + text;
            for (int i = file.IndexOf(start, StringComparison.Ordinal); i >= 0; i = file.IndexOf(start, i + 1, StringComparison.Ordinal))
            {
                int j = i + start.Length;
                while (j < file.Length && char.IsWhiteSpace(file[j]))
                    j++;
                if (string.CompareOrdinal(file, j, "]]>", 0, 3) == 0)
                    return true;
            }
            return false;
        }

        /// <summary>The groups (top-level names) whose COM read can be skipped. None without a
        /// manifest or a readable .plcproj.</summary>
        internal static List<string> Skippable(SyncManifest manifest, IEnumerable<StPouSource> sources, string plcprojPath)
        {
            var skip = new List<string>();
            Dictionary<string, string> files = manifest == null ? null : ReadProjectFiles(plcprojPath);
            if (files == null)
                return skip;
            foreach (KeyValuePair<string, List<StPouSource>> g in Groups(sources))
            {
                if (!manifest.Entries.TryGetValue(g.Key, out Entry entry))
                    continue;
                byte[] bytes = ReadObjectFile(plcprojPath, files, g.Value[0]);
                if (bytes != null && CanSkip(entry, SourceHash(g.Value), Sha256(bytes)))
                    skip.Add(g.Key);
            }
            return skip;
        }

        /// <summary>A new manifest after a sync and save: each group whose object file the .plcproj
        /// names and which holds its texts.</summary>
        internal static SyncManifest Record(string bai, IEnumerable<StPouSource> sources, string plcprojPath)
        {
            var manifest = new SyncManifest(bai);
            foreach (SavedFile saved in SavedFiles(sources, plcprojPath))
                manifest.Entries[saved.Name] = new Entry(SourceHash(saved.Group), Sha256(saved.Bytes));
            return manifest;
        }

        internal sealed class SavedFile
        {
            public SavedFile(string name, List<StPouSource> group, byte[] bytes, string include)
            {
                Name = name; Group = group; Bytes = bytes; Include = include;
            }
            public string Name { get; }
            public List<StPouSource> Group { get; }
            public byte[] Bytes { get; }
            /// <summary>The path the .plcproj names, relative to the PLC project folder.</summary>
            public string Include { get; }
        }

        /// <summary>The object files of a saved project that the .plcproj names and that hold every
        /// text of their source group. None without a readable .plcproj.</summary>
        internal static IEnumerable<SavedFile> SavedFiles(IEnumerable<StPouSource> sources, string plcprojPath)
        {
            Dictionary<string, string> files = ReadProjectFiles(plcprojPath);
            if (files == null)
                yield break;
            foreach (KeyValuePair<string, List<StPouSource>> g in Groups(sources))
            {
                byte[] bytes = ReadObjectFile(plcprojPath, files, g.Value[0]);
                if (bytes != null && FileHoldsTexts(FileText(bytes), g.Value))
                    yield return new SavedFile(g.Key, g.Value, bytes, files[g.Value[0].Name + FileExtension(g.Value[0].Kind)]);
            }
        }

        internal static Dictionary<string, string> ReadProjectFiles(string plcprojPath)
        {
            try
            {
                return File.Exists(plcprojPath) ? ProjectFiles(File.ReadAllText(plcprojPath)) : null;
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is XmlException)
            {
                return null;
            }
        }

        static byte[] ReadObjectFile(string plcprojPath, Dictionary<string, string> files, StPouSource top)
        {
            if (!files.TryGetValue(top.Name + FileExtension(top.Kind), out string relative))
                return null;
            try
            {
                string path = Path.Combine(Path.GetDirectoryName(plcprojPath), relative);
                return File.Exists(path) ? File.ReadAllBytes(path) : null;
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is ArgumentException || ex is NotSupportedException)
            {
                return null;
            }
        }

        /// <summary>Null when the file is missing or unreadable, or from another bai or format.</summary>
        public static SyncManifest Load(string path, string bai)
        {
            try
            {
                if (!File.Exists(path))
                    return null;
                if (!(new JavaScriptSerializer { MaxJsonLength = int.MaxValue }.DeserializeObject(File.ReadAllText(path)) is Dictionary<string, object> d))
                    return null;
                if (!(d.TryGetValue("format", out object f) && f is int format && format == Format)
                    || !(d.TryGetValue("bai", out object b) && b as string == bai)
                    || !(d.TryGetValue("entries", out object e) && e is Dictionary<string, object> entries))
                    return null;
                var manifest = new SyncManifest(bai);
                foreach (KeyValuePair<string, object> kv in entries)
                    if (kv.Value is Dictionary<string, object> v && v.TryGetValue("source", out object s) && s is string source
                        && v.TryGetValue("disk", out object k) && k is string disk)
                        manifest.Entries[kv.Key] = new Entry(source, disk);
                return manifest;
            }
            catch (Exception ex) when (ex is ArgumentException || ex is InvalidOperationException || ex is IOException || ex is UnauthorizedAccessException)
            {
                return null;
            }
        }

        /// <summary>Through a temporary file: a process that ends during the save leaves the old manifest whole.</summary>
        public void Save(string path)
        {
            var entries = new Dictionary<string, object>();
            foreach (KeyValuePair<string, Entry> kv in Entries.OrderBy(e => e.Key, StringComparer.Ordinal))
                entries[kv.Key] = new Dictionary<string, object> { ["source"] = kv.Value.Source, ["disk"] = kv.Value.Disk };
            string json = new JavaScriptSerializer { MaxJsonLength = int.MaxValue }.Serialize(
                new Dictionary<string, object> { ["format"] = Format, ["bai"] = Bai, ["entries"] = entries });
            string temp = path + ".tmp";
            File.WriteAllText(temp, json, new UTF8Encoding(false));
            if (File.Exists(path))
                File.Replace(temp, path, null);
            else
                File.Move(temp, path);
        }

        internal static string Sha256(byte[] bytes) => PlcSourceObject.Sha256(bytes);
    }
}
