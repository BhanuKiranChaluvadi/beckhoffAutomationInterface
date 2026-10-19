using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using static BeckhoffAutomationInterface.Release.ArtifactFiles;

namespace BeckhoffAutomationInterface.Release
{
    internal static class PackageVerifier
    {
        internal static Dictionary<string, ZipArchiveEntry> Entries(ZipArchive zip)
        {
            var files = new Dictionary<string, ZipArchiveEntry>(Names);
            var seen = new HashSet<string>(Names);
            foreach (var entry in zip.Entries)
            {
                string name = Relative(entry.FullName.TrimEnd('/'));
                if (!seen.Add(name) || ((entry.ExternalAttributes >> 16) & 0xF000) == 0xA000)
                    throw new InvalidDataException("Duplicate path or symbolic link in archive: " + name);
                if (!entry.FullName.EndsWith("/")) files.Add(name, entry);
            }
            foreach (var name in seen)
            {
                int slash = name.LastIndexOf('/');
                while (slash >= 0)
                {
                    string parent = name.Substring(0, slash);
                    if (files.ContainsKey(parent)) throw new InvalidDataException("Archive path is both a file and directory: " + parent);
                    slash = parent.LastIndexOf('/');
                }
            }
            return files;
        }
        internal static Dictionary<string, object> ReadEntry(ZipArchiveEntry entry)
        {
            if (entry.Length > 32 * 1024 * 1024) throw new InvalidDataException("JSON manifest is too large.");
            using (var reader = new StreamReader(entry.Open())) return Json(reader.ReadToEnd());
        }
        internal static Dictionary<string, object> Verify(string path, string expectedSha256 = null)
        {
            // Pin and parse the same locked file, including when an expected hash is supplied.
            using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                if (expectedSha256 != null) CheckHash(Hash(stream), expectedSha256);
                stream.Position = 0;
                using (var zip = new ZipArchive(stream, ZipArchiveMode.Read))
                {
                    var files = Entries(zip);
                    if (!files.TryGetValue("release.json", out var manifestEntry)) throw new InvalidDataException("Package has no release.json.");
                    var manifest = ReadEntry(manifestEntry);
                    long schema = Number(manifest, "schemaVersion");
                    string kind = Text(manifest, "kind");
                    if ((schema != 1 && schema != 2) || Text(manifest, "status") != "Packaged" ||
                        (kind != "ReleaseCandidate" && kind != "DevelopmentValidation") ||
                        !(Field(manifest, "deploymentApproved") is bool approved) || approved)
                        throw new InvalidDataException("Invalid package manifest.");
                    var inventory = Array(manifest, "files");
                    var seen = new HashSet<string>(Names);
                    foreach (var item in inventory)
                    {
                        var file = Object(item); string name = Relative(Text(file, "path"));
                        if (Names.Equals(name, "release.json") || !seen.Add(name) || !files.TryGetValue(name, out var entry))
                            throw new InvalidDataException("Missing or duplicate manifest file: " + name);
                        if (Number(file, "bytes") < 0 || entry.Length != Number(file, "bytes"))
                            throw new InvalidDataException("Package size mismatch: " + name);
                        using (var content = entry.Open()) CheckHash(Hash(content), Text(file, "sha256"));
                    }
                    if (files.Count != seen.Count + 1) throw new InvalidDataException("Package has unlisted files.");
                    foreach (var required in new[] { "boot/CurrentConfig.xml", "boot/CurrentProjectInfo.json", "boot/Plc/Port_851.app",
                        "boot/Plc/Port_851_boot.tizip", "boot/Plc/Port_851.crc", "boot/Plc/Port_851.tmc", "boot/Plc/Port_851.autostart", "project.zip", "evidence.json" }
                        .Concat(schema == 2 ? new[] { "identity.expect.json" } : new string[0]))
                        if (!seen.Contains(required)) throw new InvalidDataException("Missing required payload: " + required);
                    return manifest;
                }
            }
        }
    }
}
