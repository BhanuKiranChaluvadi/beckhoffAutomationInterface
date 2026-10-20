using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Web.Script.Serialization;
using System.Xml;
using System.Xml.Linq;

namespace BeckhoffAutomationInterface.Release
{
    // Offline artifact operations. No COM, ADS, machine names or PLC symbol contracts.
    internal static class ArtifactFiles
    {
        internal static readonly StringComparer Names = StringComparer.OrdinalIgnoreCase;
        internal static string Hash(Stream stream)
        {
            using (var sha = SHA256.Create()) return BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", "").ToLowerInvariant();
        }
        internal static string Hash(string path) { using (var stream = File.OpenRead(path)) return Hash(stream); }
        internal static void CheckHash(string actual, string expected)
        {
            if (!Regex.IsMatch(expected ?? "", "\\A[0-9a-fA-F]{64}\\z") || !Names.Equals(actual, expected))
                throw new InvalidDataException("SHA256 differs from the selected baseline.");
        }
        internal static string Relative(string name)
        {
            if (string.IsNullOrEmpty(name) || name.Contains('\\')) throw new InvalidDataException("Invalid relative path: " + name);
            foreach (var part in name.Split('/'))
                if (part.Length == 0 || part == "." || part == ".." || part.EndsWith(" ") || part.EndsWith(".") ||
                    part.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 ||
                    Regex.IsMatch(part, @"\A(CON|PRN|AUX|NUL|COM[1-9]|LPT[1-9])(\.|\z)", RegexOptions.IgnoreCase))
                    throw new InvalidDataException("Unsafe relative path: " + name);
            return name;
        }
        internal static void NoLinks(string path)
        {
            var current = Path.GetFullPath(path);
            while (current != null)
            {
                if ((File.Exists(current) || Directory.Exists(current)) && (File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                    throw new InvalidDataException("Reparse point is not allowed: " + current);
                current = Path.GetDirectoryName(current);
            }
        }
        internal static Dictionary<string, object> Object(object value)
        {
            return value as Dictionary<string, object> ?? throw new InvalidDataException("Expected a JSON object.");
        }
        internal static object Field(Dictionary<string, object> value, string name)
        {
            if (!value.TryGetValue(name, out var field)) throw new InvalidDataException("Missing JSON field: " + name);
            return field;
        }
        internal static string Text(Dictionary<string, object> value, string name)
        {
            var text = Field(value, name) as string;
            if (string.IsNullOrWhiteSpace(text)) throw new InvalidDataException("Expected nonempty string: " + name);
            return text;
        }
        internal static long Number(Dictionary<string, object> value, string name)
        {
            var n = Field(value, name);
            if (n is int i) return i;
            if (n is long l) return l;
            throw new InvalidDataException("Expected integer: " + name);
        }
        internal static object[] Array(Dictionary<string, object> value, string name)
        {
            var a = Field(value, name);
            if (a is object[] items) return items;
            if (a is ArrayList list) return list.ToArray();
            if (a is IEnumerable sequence && !(a is string) && !(a is IDictionary)) return sequence.Cast<object>().ToArray();
            throw new InvalidDataException("Expected array: " + name);
        }
        internal static JavaScriptSerializer Serializer() => new JavaScriptSerializer { MaxJsonLength = 32 * 1024 * 1024, RecursionLimit = 100 };
        internal static Dictionary<string, object> Json(string text) => Object(Serializer().DeserializeObject(text));
        internal static Dictionary<string, object> Read(string path) => Json(File.ReadAllText(path));
        internal static string Encode(object value) => Serializer().Serialize(value);
        internal static void WriteNew(string path, object value)
        {
            NoLinks(path);
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path)));
            using (var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            using (var writer = new StreamWriter(stream, new UTF8Encoding(false))) writer.WriteLine(Encode(value));
        }
        internal static XDocument Xml(string path)
        {
            using (var reader = XmlReader.Create(path, new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null }))
                return XDocument.Load(reader, LoadOptions.PreserveWhitespace);
        }
        internal static Dictionary<string, object> FileRecord(string relative, string path)
        {
            using (var stream = File.OpenRead(path)) return new Dictionary<string, object> {
                ["path"] = Relative(relative), ["bytes"] = stream.Length, ["sha256"] = Hash(stream) };
        }
        /// <summary>Every file below root (links are refused). A file with one of the excluded
        /// names is not opened (for example a runtime journal that is locked).</summary>
        internal static Dictionary<string, Dictionary<string, object>> Inventory(string root, ICollection<string> excludedNames = null)
        {
            root = Path.GetFullPath(root);
            string prefix = root.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            NoLinks(root);
            if (!Directory.Exists(root)) throw new DirectoryNotFoundException(root);
            var result = new Dictionary<string, Dictionary<string, object>>(Names);
            var pending = new Stack<string>(); pending.Push(root);
            while (pending.Count > 0)
                foreach (var path in Directory.EnumerateFileSystemEntries(pending.Pop()))
                {
                    NoLinks(path);
                    if (Directory.Exists(path)) pending.Push(path);
                    else if (excludedNames == null || !excludedNames.Contains(Path.GetFileName(path)))
                    {
                        var name = Relative(path.Substring(prefix.Length).Replace('\\', '/'));
                        if (result.ContainsKey(name)) throw new InvalidDataException("Duplicate path: " + name);
                        result.Add(name, FileRecord(name, path));
                    }
                }
            return result;
        }
        internal static void Same(Dictionary<string, Dictionary<string, object>> expected, Dictionary<string, Dictionary<string, object>> actual)
        {
            if (expected.Count != actual.Count) throw new InvalidDataException("File inventory differs from the baseline.");
            foreach (var pair in expected)
            {
                if (!actual.TryGetValue(pair.Key, out var value) || Number(pair.Value, "bytes") != Number(value, "bytes"))
                    throw new InvalidDataException("Missing file or size mismatch: " + pair.Key);
                CheckHash(Text(value, "sha256"), Text(pair.Value, "sha256"));
            }
        }
        /// <summary>An inventory as evidence records (path order, upper-case SHA256 as in the
        /// earlier PowerShell evidence), each path with an optional prefix.</summary>
        internal static List<Dictionary<string, object>> Evidence(string root, string prefix = "", ICollection<string> excludedNames = null) =>
            Inventory(root, excludedNames).OrderBy(p => p.Key, StringComparer.Ordinal).Select(p => new Dictionary<string, object>
                { ["path"] = prefix + p.Key, ["bytes"] = p.Value["bytes"], ["sha256"] = ((string)p.Value["sha256"]).ToUpperInvariant() }).ToList();
        /// <summary>Evidence records (from this run or read back from JSON) as an inventory.</summary>
        internal static Dictionary<string, Dictionary<string, object>> Records(IEnumerable<object> records)
        {
            var result = new Dictionary<string, Dictionary<string, object>>(Names);
            foreach (var item in records)
            {
                var record = Object(item); string name = Relative(Text(record, "path"));
                if (result.ContainsKey(name)) throw new InvalidDataException("Duplicate path: " + name);
                result.Add(name, record);
            }
            return result;
        }
        /// <summary>Throws "&lt;label&gt; changed" when the two file lists differ.</summary>
        internal static void SameFiles(IEnumerable<object> expected, IEnumerable<object> actual, string label)
        {
            try { Same(Records(expected), Records(actual)); }
            catch (InvalidDataException ex) { throw new InvalidDataException(label + " changed: " + ex.Message); }
        }
        internal static bool Inside(string child, string parent) => Path.GetFullPath(child).StartsWith(
            Path.GetFullPath(parent).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    }
}
