using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using static BeckhoffAutomationInterface.Release.ArtifactFiles;

namespace BeckhoffAutomationInterface.Release
{
    internal static class ArtifactArchives
    {
        internal static void WriteEntry(ZipArchive zip, string name, object value)
        {
            using (var writer = new StreamWriter(zip.CreateEntry(name).Open(), new UTF8Encoding(false))) writer.WriteLine(Encode(value));
        }
        internal static void CopyEntry(ZipArchive zip, string name, string source, Dictionary<string, object> expected)
        {
            NoLinks(source);
            using (var input = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                if (input.Length != Number(expected, "bytes")) throw new InvalidDataException("Source size changed: " + source);
                CheckHash(Hash(input), Text(expected, "sha256")); input.Position = 0;
                using (var output = zip.CreateEntry(Relative(name), CompressionLevel.Fastest).Open()) input.CopyTo(output);
            }
        }
        internal static string Temporary(string output, params string[] sources)
        {
            output = Path.GetFullPath(output); NoLinks(output);
            if (File.Exists(output) || Directory.Exists(output)) throw new IOException("Use a new output path: " + output);
            if (sources.Any(root => Inside(output, root))) throw new IOException("Archive must be outside captured directories.");
            Directory.CreateDirectory(Path.GetDirectoryName(output));
            return output + "." + Guid.NewGuid().ToString("N") + ".partial";
        }
        internal const string ReadmeName = "README.md";

        static string ReadmeField(Dictionary<string, object> metadata, string key) =>
            metadata.TryGetValue(key, out object value) && value is string text && text.Length > 0 ? text : "-";

        /// <summary>What each part of a machine package is for, from its record (release.json without
        /// the file list). BAI writes it into every package; a payload must not bring its own.</summary>
        internal static string PackageReadme(Dictionary<string, object> metadata) => string.Join("\n", new[]
        {
            "# Machine package " + ReadmeField(metadata, "machine") + " " + ReadmeField(metadata, "sourceTag"),
            "",
            "Commit " + ReadmeField(metadata, "sourceCommit") + ". Kind: " + ReadmeField(metadata, "kind") + ".",
            "This package is not a deployment approval: the machine checks and the acceptance remain.",
            "",
            "| Part | What it is | Who uses it | Copy to the IPC |",
            "|---|---|---|---|",
            "| `boot/` | The compiled PLC program and I/O configuration. TwinCAT starts it. | `bai package install` | Yes: into the TwinCAT Boot folder |",
            "| `identity.expect.json` | The values that the running PLC must show after the install (tag, commit, machine). | `bai package install`, `bai verify` | No |",
            "| `project.zip` | The XAE service solution: open the .sln to log in, watch values and debug. The baseline of `bai package drift`. | Service | No: a service PC |",
            "| `evidence.json` | The build record: tools, inputs and checks, as SHA-256 values. | Audit | No |",
            "| `release.json` | This package: machine, commit, PLC and safety CRCs, the SHA-256 of every file. | `bai package verify` | No |",
            "| `" + ReadmeName + "` | This file. | People | No |",
            "",
            "Check the package before use: `bai package verify --package <file>.zip --sha256 <hash from <file>.zip.sha256>`.",
            "",
        });

        internal static Dictionary<string, object> CreatePackage(string directory, string metadataFile, string output)
        {
            var metadata = Read(metadataFile);
            // Release policy remains with the caller. BAI owns packaging/integrity and never grants deployment approval.
            if (metadata.ContainsKey("files")) throw new InvalidDataException("Metadata must not supply an inventory; BAI captures it.");
            var payload = Inventory(directory);
            if (payload.ContainsKey("release.json")) throw new InvalidDataException("Payload must not contain release.json.");
            if (payload.ContainsKey(ReadmeName)) throw new InvalidDataException("Payload must not contain " + ReadmeName + "; BAI writes it from the package record.");
            byte[] readme = new UTF8Encoding(false).GetBytes(PackageReadme(metadata));
            var files = new Dictionary<string, Dictionary<string, object>>(payload, Names);
            using (var content = new MemoryStream(readme))
                files[ReadmeName] = new Dictionary<string, object> { ["path"] = ReadmeName, ["bytes"] = (long)readme.Length, ["sha256"] = Hash(content) };
            metadata["files"] = files.OrderBy(p => p.Key, StringComparer.Ordinal).Select(p => p.Value).ToArray();
            string temporary = Temporary(output, directory);
            try
            {
                using (var stream = new FileStream(temporary, FileMode.CreateNew))
                using (var zip = new ZipArchive(stream, ZipArchiveMode.Create))
                {
                    foreach (var file in files.OrderBy(p => p.Key, StringComparer.Ordinal))
                        if (file.Key == ReadmeName)
                            using (var entry = zip.CreateEntry(ReadmeName, CompressionLevel.Fastest).Open()) entry.Write(readme, 0, readme.Length);
                        else CopyEntry(zip, file.Key, Path.Combine(directory, file.Key), file.Value);
                    WriteEntry(zip, "release.json", metadata);
                }
                Same(payload, Inventory(directory));
                PackageVerifier.Verify(temporary);
                string hash = Hash(temporary);
                File.Move(temporary, output);
                return new Dictionary<string, object> { ["file"] = Path.GetFullPath(output), ["sha256"] = hash, ["files"] = files.Count };
            }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
        }
        static Dictionary<string, Dictionary<string, object>> ToolFiles(Dictionary<string, object> roots)
        {
            if (roots.Count != 2 || !roots.ContainsKey("components") || !roots.ContainsKey("xaeShell")) throw new InvalidDataException("Toolchain requires components and xaeShell roots.");
            var files = new Dictionary<string, Dictionary<string, object>>(Names);
            foreach (var root in roots)
            {
                string path = Text(roots, root.Key);
                if (!Path.IsPathRooted(path)) throw new InvalidDataException("Toolchain roots must be absolute.");
                var inventory = Inventory(path);
                if (inventory.Count == 0) throw new InvalidDataException("Toolchain root is empty: " + path);
                foreach (var file in inventory) files.Add(root.Key + "/" + file.Key,
                    new Dictionary<string, object> { ["bytes"] = file.Value["bytes"], ["sha256"] = file.Value["sha256"] });
            }
            return files;
        }
        internal static Dictionary<string, object> CaptureToolchain(string components, string shell, string version, string output)
        {
            if (!Regex.IsMatch(version, @"\A\d+\.\d+\.\d+\.\d+\z")) throw new InvalidDataException("Invalid TwinCAT version.");
            var roots = new Dictionary<string, object> { ["components"] = Path.GetFullPath(components), ["xaeShell"] = Path.GetFullPath(shell) };
            string temporary = Temporary(output, components, shell);
            try
            {
                var files = ToolFiles(roots);
                var record = new Dictionary<string, object> { ["schemaVersion"] = 1, ["twinCatVersion"] = version, ["roots"] = roots,
                    ["files"] = files, ["scope"] = "Installed TwinCAT Components and TcXaeShell bytes; not a standalone installer" };
                using (var stream = new FileStream(temporary, FileMode.CreateNew))
                using (var zip = new ZipArchive(stream, ZipArchiveMode.Create))
                {
                    foreach (var file in files.OrderBy(p => p.Key, StringComparer.Ordinal))
                    {
                        int slash = file.Key.IndexOf('/');
                        CopyEntry(zip, file.Key, Path.Combine(Text(roots, file.Key.Substring(0, slash)), file.Key.Substring(slash + 1)), file.Value);
                    }
                    WriteEntry(zip, "toolchain.json", record);
                }
                Same(files, ToolFiles(roots));
                string hash = Hash(temporary);
                VerifyToolchain(temporary, hash, version, true);
                File.Move(temporary, output);
                return new Dictionary<string, object> { ["file"] = Path.GetFullPath(output), ["sha256"] = hash, ["files"] = files.Count };
            }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
        }
        internal static Dictionary<string, object> VerifyToolchain(string path, string hash, string version, bool installed)
        {
            Dictionary<string, object> roots;
            var files = new Dictionary<string, Dictionary<string, object>>(Names);
            using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                CheckHash(Hash(stream), hash); stream.Position = 0;
                using (var zip = new ZipArchive(stream, ZipArchiveMode.Read))
                {
                    var entries = PackageVerifier.Entries(zip);
                    if (!entries.TryGetValue("toolchain.json", out var metadata)) throw new InvalidDataException("Missing toolchain.json.");
                    var record = PackageVerifier.ReadEntry(metadata);
                    if (Number(record, "schemaVersion") != 1 || !SameTwinCatMajor(Text(record, "twinCatVersion"), version)) throw new InvalidDataException("Toolchain version mismatch.");
                    roots = Object(Field(record, "roots"));
                    if (roots.Count != 2 || !roots.ContainsKey("components") || !roots.ContainsKey("xaeShell")) throw new InvalidDataException("Incomplete toolchain roots.");
                    foreach (var pair in Object(Field(record, "files")))
                    {
                        string name = Relative(pair.Key);
                        if (!roots.Keys.Any(k => name.StartsWith(k + "/", StringComparison.Ordinal)) || files.ContainsKey(name))
                            throw new InvalidDataException("Invalid or duplicate toolchain file: " + name);
                        var expected = Object(pair.Value);
                        if (!entries.TryGetValue(name, out var entry) || Number(expected, "bytes") < 0 || entry.Length != Number(expected, "bytes"))
                            throw new InvalidDataException("Toolchain inventory mismatch: " + name);
                        using (var content = entry.Open()) CheckHash(Hash(content), Text(expected, "sha256"));
                        files.Add(name, expected);
                    }
                    if (entries.Count != files.Count + 1 || roots.Keys.Any(k => !files.Keys.Any(n => n.StartsWith(k + "/", StringComparison.Ordinal))))
                        throw new InvalidDataException("Toolchain inventory is incomplete.");
                }
            }
            if (installed) Same(files, ToolFiles(roots));
            return new Dictionary<string, object> { ["status"] = "Passed", ["files"] = files.Count, ["sha256"] = hash, ["installedChecked"] = installed };
        }
    }
}
