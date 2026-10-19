using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using static BeckhoffAutomationInterface.Release.ArtifactFiles;

namespace BeckhoffAutomationInterface.Release
{
    internal static class LibraryBaseline
    {
        internal static Dictionary<string, Dictionary<string, object>> Files(object[] entries)
        {
            var result = new Dictionary<string, Dictionary<string, object>>(Names);
            foreach (var item in entries)
            {
                var file = Object(item); string name = Relative(Text(file, "path"));
                if (!Regex.IsMatch(name, @"\A[^/]+/[^/]+/\d+(\.\d+)+/[^/]+\z") || result.ContainsKey(name) || Number(file, "bytes") <= 0)
                    throw new InvalidDataException("Invalid or duplicate library cache entry: " + name);
                CheckHash(Text(file, "sha256"), Text(file, "sha256"));
                result.Add(name, file);
            }
            if (result.Count == 0) throw new InvalidDataException("Empty library cache.");
            return result;
        }
        internal static Dictionary<string, object> ReadLock(string path, string hash)
        {
            Dictionary<string, object> record;
            using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                CheckHash(Hash(stream), hash); stream.Position = 0;
                using (var reader = new StreamReader(stream)) record = Json(reader.ReadToEnd());
            }
            if (Number(record, "schemaVersion") != 1) throw new InvalidDataException("Unsupported library lock schema.");
            var names = new HashSet<string>(Names);
            foreach (var entry in Array(record, "machines"))
            {
                var machine = Object(entry);
                string name = Text(machine, "machine");
                if (!Regex.IsMatch(name, @"\A[A-Za-z][A-Za-z0-9_]*\z") || !names.Add(name))
                    throw new InvalidDataException("Invalid or duplicate machine in library lock.");
                Text(machine, "twinCatVersion"); Text(machine, "targetPlatform");
                Files(Array(machine, "libraries"));
            }
            if (names.Count == 0) throw new InvalidDataException("Empty library baseline.");
            return record;
        }
        internal static Dictionary<string, object> Capture(string project, string machine, string platform, string variant, string manifest = null)
        {
            if (!Regex.IsMatch(machine, @"\A[A-Za-z][A-Za-z0-9_]*\z")) throw new InvalidDataException("Invalid machine label.");
            var root = Path.GetDirectoryName(Path.GetFullPath(project));
            var inventory = Inventory(root);
            var plcPaths = inventory.Keys.Where(p => p.EndsWith(".plcproj", StringComparison.OrdinalIgnoreCase)).ToArray();
            if (plcPaths.Length != 1) throw new InvalidDataException("Expected exactly one PLC project.");
            string plc = Path.Combine(root, plcPaths[0]);
            var modules = Xml(Path.ChangeExtension(plc, ".tmc")).Descendants().Where(e => e.Name.LocalName == "Module" && e.Attribute("TargetPlatform") != null).ToArray();
            if (modules.Length != 1 || (string)modules[0].Attribute("TargetPlatform") != platform || ((string)modules[0].Attribute("ProjectVariant") ?? "") != variant)
                throw new InvalidDataException("Compiled TMC platform or variant differs from the selection.");
            var version = (string)Xml(project).Root.Attribute("TcVersion");
            if (string.IsNullOrWhiteSpace(version)) throw new InvalidDataException("Project has no TwinCAT version.");
            var files = Inventory(Path.Combine(Path.GetDirectoryName(plc), "_Libraries"));
            Files(files.Values.Cast<object>().ToArray());
            if (manifest != null)
                foreach (var required in Xml(manifest).Descendants("Library"))
                {
                    string prefix = (string)required.Attribute("Company") + "/" + (string)required.Attribute("Name") + "/";
                    string requested = (string)required.Attribute("Version");
                    var matches = files.Keys.Where(n => n.StartsWith(prefix, StringComparison.Ordinal)).ToArray();
                    if (matches.Length == 0 || matches.Select(n => n.Split('/')[2]).Distinct(StringComparer.Ordinal).Count() != 1 ||
                        (!string.IsNullOrEmpty(requested) && requested != "*" && matches.Any(n => n.Split('/')[2] != requested)))
                        throw new InvalidDataException("Resolved library does not satisfy manifest: " + prefix);
                }
            return new Dictionary<string, object> { ["machine"] = machine, ["twinCatVersion"] = version, ["targetPlatform"] = platform,
                ["libraries"] = files.OrderBy(p => p.Key, StringComparer.Ordinal).Select(p => p.Value).ToArray() };
        }
        internal static void Verify(Dictionary<string, object> baseline, Dictionary<string, object> actual)
        {
            var entries = Array(baseline, "machines").Select(Object).Where(m => Text(m, "machine") == Text(actual, "machine")).ToArray();
            if (entries.Length != 1 || Text(entries[0], "twinCatVersion") != Text(actual, "twinCatVersion") || Text(entries[0], "targetPlatform") != Text(actual, "targetPlatform"))
                throw new InvalidDataException("Baseline does not cover the selected machine, toolchain and platform.");
            Same(Files(Array(entries[0], "libraries")), Files(Array(actual, "libraries")));
        }
    }
}
