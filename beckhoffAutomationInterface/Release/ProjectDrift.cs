using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Xml;
using System.Xml.Linq;
using static BeckhoffAutomationInterface.Release.ArtifactFiles;

namespace BeckhoffAutomationInterface.Release
{
    /// <summary>
    /// 'package drift': compares a saved TwinCAT service project with its hash-pinned project.zip.
    /// Ignores only build outputs and IDE caches. XML formatting (indentation, attribute order) is
    /// normalized; code, configuration values, element order and comments stay significant.
    /// It does not see unsaved or online edits: an update also checks the live identity.
    /// </summary>
    internal static class ProjectDrift
    {
        static readonly HashSet<string> IgnoredDirs = new HashSet<string>(new[] { ".git", ".vs", "_boot", "_libraries", "_compileinfo", "bin", "obj" });
        static readonly HashSet<string> IgnoredSuffixes = new HashSet<string>(new[] { ".tmc", ".tmi", ".compileinfo", ".compiled-library", ".suo", ".user", ".bak" });
        static readonly HashSet<string> XmlSuffixes = new HashSet<string>(new[] { ".xml", ".tsproj", ".plcproj", ".tcpou", ".tcgvl", ".tcdut",
            ".tctto", ".xti", ".splcproj", ".sds", ".sal", ".sproj" });

        internal static bool Included(string name)
        {
            string[] parts = name.ToLowerInvariant().Split('/');
            string leaf = parts[parts.Length - 1];
            int dot = leaf.LastIndexOf('.');
            return !parts.Any(IgnoredDirs.Contains) && !(dot > 0 && IgnoredSuffixes.Contains(leaf.Substring(dot)));
        }

        static string Suffix(string name)
        {
            string leaf = name.Substring(name.LastIndexOf('/') + 1).ToLowerInvariant();
            int dot = leaf.LastIndexOf('.');
            return dot > 0 ? leaf.Substring(dot) : "";
        }

        /// <summary>The bytes that are compared: XML files as a canonical tree, all other files byte-exact.</summary>
        internal static byte[] Normalized(string name, byte[] data)
        {
            if (!XmlSuffixes.Contains(Suffix(name)))
                return data;
            string upper = Encoding.ASCII.GetString(data).ToUpperInvariant();
            if (upper.Contains("<!DOCTYPE") || upper.Contains("<!ENTITY"))
                throw new InvalidDataException("DTD/entity declarations are not allowed: " + name);
            XDocument doc;
            using (var reader = XmlReader.Create(new MemoryStream(data), new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null }))
                doc = XDocument.Load(reader, LoadOptions.PreserveWhitespace);
            return Encoding.UTF8.GetBytes(Encode(Node(doc.Root)));
        }

        // [name, sorted attributes, content]. An element without element or comment children keeps
        // its text exactly; between children, only text that is not whitespace is kept.
        static object Node(XElement e)
        {
            var attributes = e.Attributes().Where(a => !a.IsNamespaceDeclaration)
                .Select(a => new object[] { a.Name.ToString(), a.Value }).OrderBy(a => (string)a[0], StringComparer.Ordinal).ToArray();
            var content = new List<object>();
            bool structured = e.Nodes().Any(n => n is XElement || n is XComment);
            var text = new StringBuilder();
            void Flush()
            {
                if (!structured || text.ToString().Trim().Length > 0) content.Add(text.ToString());
                text.Clear();
            }
            foreach (XNode n in e.Nodes())
            {
                if (n is XText t) text.Append(t.Value);
                else if (n is XElement child) { Flush(); content.Add(Node(child)); }
                else if (n is XComment c) { Flush(); content.Add(new object[] { "#comment", c.Value }); }
            }
            Flush();
            return new object[] { e.Name.ToString(), attributes, content };
        }

        internal static string Digest(byte[] data) { using (var sha = SHA256.Create()) return BitConverter.ToString(sha.ComputeHash(data)).Replace("-", "").ToLowerInvariant(); }

        /// <summary>The included files of the archive (lower-case names), pinned to the hash of the bytes that are read.</summary>
        internal static Dictionary<string, byte[]> ArchiveFiles(string path, string expectedSha256)
        {
            byte[] content = File.ReadAllBytes(path);
            CheckHash(Digest(content), expectedSha256);
            var result = new Dictionary<string, byte[]>(StringComparer.Ordinal);
            using (var zip = new ZipArchive(new MemoryStream(content), ZipArchiveMode.Read))
                foreach (var entry in PackageVerifier.Entries(zip))
                    if (Included(entry.Key))
                        using (var stream = entry.Value.Open())
                        using (var buffer = new MemoryStream())
                        {
                            stream.CopyTo(buffer);
                            result.Add(entry.Key.ToLowerInvariant(), Normalized(entry.Key, buffer.ToArray()));
                        }
            return result;
        }

        /// <summary>The included files of the service project folder (lower-case names). Links are refused.</summary>
        internal static Dictionary<string, byte[]> DirectoryFiles(string root)
        {
            root = Path.GetFullPath(root);
            if (!Directory.Exists(root)) throw new InvalidDataException("Project must be an existing service-project directory.");
            NoLinks(root);
            var result = new Dictionary<string, byte[]>(StringComparer.Ordinal);
            var pending = new Stack<string>(); pending.Push(root);
            while (pending.Count > 0)
                foreach (string path in Directory.EnumerateFileSystemEntries(pending.Pop()))
                {
                    string name = path.Substring(root.TrimEnd('\\').Length + 1).Replace('\\', '/');
                    if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
                        throw new InvalidDataException("Project contains a link/reparse point: " + name);
                    if (!Included(name)) continue;
                    if (Directory.Exists(path)) { pending.Push(path); continue; }
                    string key = Relative(name).ToLowerInvariant();
                    if (result.ContainsKey(key)) throw new InvalidDataException("Duplicate project path: " + name);
                    result.Add(key, Normalized(name, File.ReadAllBytes(path)));
                }
            return result;
        }

        internal static Dictionary<string, object> Compare(Dictionary<string, byte[]> baseline, Dictionary<string, byte[]> current)
        {
            foreach (var side in new[] { ("baseline", baseline), ("current", current) })
            {
                if (!side.Item2.Keys.Any(p => p.EndsWith(".tsproj"))) throw new InvalidDataException("No TwinCAT project in " + side.Item1);
                if (!side.Item2.Keys.Any(p => p.EndsWith(".plcproj"))) throw new InvalidDataException("No PLC project in " + side.Item1);
            }
            var names = new SortedSet<string>(baseline.Keys.Concat(current.Keys), StringComparer.Ordinal);
            var changes = new List<object>();
            foreach (string name in names)
            {
                baseline.TryGetValue(name, out byte[] before);
                current.TryGetValue(name, out byte[] after);
                if (before != null && after != null && before.SequenceEqual(after)) continue;
                changes.Add(new Dictionary<string, object> { ["path"] = name,
                    ["change"] = before == null ? "added" : after == null ? "removed" : "modified",
                    ["baselineSha256"] = before == null ? null : Digest(before), ["currentSha256"] = after == null ? null : Digest(after) });
            }
            return new Dictionary<string, object> { ["schemaVersion"] = 1, ["status"] = changes.Count > 0 ? "Drift" : "Passed",
                ["comparedFiles"] = names.Count, ["changes"] = changes, ["scope"] = "saved service project; live identity check is also required" };
        }

        /// <summary>Writes the report. 0 = Passed, 1 = Drift, 2 = Error (the report says why).</summary>
        internal static int Run(string baseline, string sha256, string project, string report)
        {
            if (Inside(report, project) || Names.Equals(Path.GetFullPath(report), Path.GetFullPath(project)))
                throw new ArgumentException("Keep the report outside the compared project.");
            Dictionary<string, object> result;
            int code;
            try
            {
                result = Compare(ArchiveFiles(baseline, sha256), DirectoryFiles(project));
                result["baselineSha256"] = sha256.ToLowerInvariant();
                result["project"] = Path.GetFullPath(project);
                code = (string)result["status"] == "Passed" ? 0 : 1;
            }
            catch (Exception ex) when (ex is InvalidDataException || ex is IOException || ex is XmlException || ex is UnauthorizedAccessException)
            {
                result = new Dictionary<string, object> { ["schemaVersion"] = 1, ["status"] = "Error", ["error"] = ex.Message };
                code = 2;
            }
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(report)));
            File.WriteAllText(report, Encode(result) + "\n", new UTF8Encoding(false));
            Console.WriteLine("{0}: {1}", result["status"], Path.GetFullPath(report));
            return code;
        }
    }
}
