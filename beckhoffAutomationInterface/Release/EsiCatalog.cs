using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using static BeckhoffAutomationInterface.Release.ArtifactFiles;

namespace BeckhoffAutomationInterface.Release
{
    internal static class EsiCatalog
    {
        internal static Dictionary<string, object> Execute(string system, string devices, string destination, bool install)
        {
            destination = Path.GetFullPath(destination); NoLinks(destination);
            var allowed = new HashSet<string>(new[] { ".xml", ".xsd", ".bmp", ".ico", ".png" }, Names);
            var planned = new Dictionary<string, Dictionary<string, object>>(Names);
            foreach (var source in new[] { system, devices })
            {
                if (Names.Equals(Path.GetFullPath(source).TrimEnd('\\'), destination.TrimEnd('\\')) || Inside(destination, source) || Inside(source, destination))
                    throw new InvalidDataException("ESI source and destination trees must be separate.");
                foreach (var file in Inventory(source).Where(p => allowed.Contains(Path.GetExtension(p.Key))))
                {
                    if (planned.TryGetValue(file.Key, out var existing)) CheckHash(Text(existing, "sha256"), Text(file.Value, "sha256"));
                    else
                    {
                        var item = new Dictionary<string, object>(file.Value) { ["source"] = Path.Combine(source, file.Key) };
                        planned.Add(file.Key, item);
                    }
                }
            }
            if (planned.Count == 0) throw new InvalidDataException("No ESI catalog files found.");
            // Complete conflict preflight before the first write. Never replace an installed file.
            foreach (var file in planned)
            {
                string target = Path.Combine(destination, file.Key); NoLinks(target);
                if (Directory.Exists(target)) throw new IOException("ESI file path is a directory: " + target);
                if (File.Exists(target)) CheckHash(Hash(target), Text(file.Value, "sha256"));
                else if (!install) throw new FileNotFoundException("ESI file is not installed: " + target);
            }
            foreach (var file in planned)
            {
                string target = Path.Combine(destination, file.Key);
                if (!File.Exists(target))
                {
                    NoLinks(target);
                    using (var input = new FileStream(Text(file.Value, "source"), FileMode.Open, FileAccess.Read, FileShare.Read))
                    {
                        CheckHash(Hash(input), Text(file.Value, "sha256")); input.Position = 0;
                        Directory.CreateDirectory(Path.GetDirectoryName(target));
                        using (var output = new FileStream(target, FileMode.CreateNew, FileAccess.Write, FileShare.None)) input.CopyTo(output);
                    }
                }
                CheckHash(Hash(target), Text(file.Value, "sha256"));
            }
            return new Dictionary<string, object> { ["status"] = "Passed", ["destination"] = destination, ["files"] = planned.Values.ToArray(), ["installed"] = install };
        }
    }
}
