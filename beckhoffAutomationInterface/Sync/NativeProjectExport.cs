using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Xml.Linq;

namespace BeckhoffAutomationInterface.Sync
{
    // The native snapshot retains configuration for which no editable manifest exists
    // (for example NC axes). It is also the byte-exact recovery path for unsupported POUs.
    static class NativeProjectExport
    {
        public static void Write(string tsproj, string output)
        {
            string root = Path.GetDirectoryName(Path.GetFullPath(tsproj));
            output = Path.GetFullPath(output);
            if (output.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) || output.Equals(root, StringComparison.OrdinalIgnoreCase))
                throw new IOException("Native export must be outside the project folder.");
            string destination = Path.Combine(output, "native-project.zip");
            if (File.Exists(destination)) throw new IOException("Native snapshot already exists; export into a new folder.");
            var files = ConfigurationExporter.Files(root).ToArray();
            // External native references cannot be restored by extracting this folder.
            foreach (string path in files.Where(p => p.EndsWith(".tsproj", StringComparison.OrdinalIgnoreCase) || p.EndsWith(".xti", StringComparison.OrdinalIgnoreCase)))
            {
                foreach (var reference in XDocument.Load(path).Descendants().Attributes().Where(a =>
                    a.Name == "PrjFilePath" || a.Name == "TmcFilePath" ||
                    (a.Name == "File" && a.Value.EndsWith(".xti", StringComparison.OrdinalIgnoreCase))))
                {
                    string resolved = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(path), reference.Value));
                    if (!resolved.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                        throw new IOException("External native project reference is not portable: " + reference.Value);
                    if (reference.Name == "File" && !File.Exists(resolved))
                    {
                        // Decomposed TwinCAT I/O uses bare File names whose files live
                        // under _Config/IO/<device>, not beside the .tsproj.
                        var matches = files.Where(p => p.EndsWith(Path.DirectorySeparatorChar + reference.Value.Replace('/', Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase)).ToArray();
                        if (matches.Length == 1) resolved = matches[0];
                        else throw new IOException("Native XTI reference is missing or ambiguous: " + reference.Value);
                    }
                    if (reference.Name != "TmcFilePath" && !File.Exists(resolved))
                        throw new FileNotFoundException("Native project reference is missing: " + resolved, resolved);
                }
            }
            var inventory = new XElement("NativeProject", new XAttribute("Project", Path.GetFileName(tsproj)));
            string temporary = destination + ".partial";
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None))
            using (var zip = new ZipArchive(stream, ZipArchiveMode.Create))
            {
                foreach (string path in files)
                {
                    string relative = path.Substring(root.Length + 1).Replace('\\', '/');
                    using (var source = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
                    {
                        string hash = Hash(source); source.Position = 0;
                        using (var target = zip.CreateEntry(relative, CompressionLevel.Optimal).Open()) source.CopyTo(target);
                        inventory.Add(new XElement("File", new XAttribute("Path", relative), new XAttribute("Bytes", source.Length), new XAttribute("Sha256", hash)));
                    }
                }
            }
            using (var zip = ZipFile.OpenRead(temporary))
            {
                foreach (var file in inventory.Elements("File"))
                {
                    var entry = zip.GetEntry((string)file.Attribute("Path"));
                    using (var stream = entry.Open())
                        if (entry.Length != (long)file.Attribute("Bytes") || Hash(stream) != (string)file.Attribute("Sha256"))
                            throw new IOException("Native archive verification failed: " + entry.FullName);
                    string path = ConfigurationExporter.Within(root, entry.FullName);
                    if (SafetyProjectSync.Hash(path) != (string)file.Attribute("Sha256"))
                        throw new IOException("Native project changed during export: " + path);
                }
            }
            if (!files.SequenceEqual(ConfigurationExporter.Files(root))) throw new IOException("Native file list changed during export.");
            File.Move(temporary, destination);
            new XDocument(inventory).Save(Path.Combine(output, "native-inventory.xml"));
        }

        static string Hash(Stream stream)
        {
            using (var sha = SHA256.Create()) return BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", "");
        }
    }
}
