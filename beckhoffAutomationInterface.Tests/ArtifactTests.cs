using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using BeckhoffAutomationInterface.Release;
using Xunit;
using static BeckhoffAutomationInterface.Release.ArtifactFiles;

namespace BeckhoffAutomationInterface.Tests
{
    public sealed class ArtifactTests : IDisposable
    {
        readonly string root = Path.Combine(Path.GetTempPath(), "bai-artifacts-" + Guid.NewGuid().ToString("N"));
        public ArtifactTests() { Directory.CreateDirectory(root); }
        public void Dispose() { Directory.Delete(root, true); }
        string Write(string name, string text) { string p = Path.Combine(root, name); Directory.CreateDirectory(Path.GetDirectoryName(p)); File.WriteAllText(p, text); return p; }
        string Package(string fault = "", int schema = 2)
        {
            var paths = new[] { "boot/CurrentConfig.xml", "boot/CurrentProjectInfo.json", "boot/Plc/Port_851.app", "boot/Plc/Port_851_boot.tizip",
                "boot/Plc/Port_851.crc", "boot/Plc/Port_851.tmc", "boot/Plc/Port_851.autostart", "project.zip", "evidence.json", "identity.expect.json" };
            var records = new List<Dictionary<string, object>>();
            string p = Path.Combine(root, Guid.NewGuid() + ".zip");
            using (var archive = ZipFile.Open(p, ZipArchiveMode.Create))
            {
                foreach (var name in paths.Where(n => !(fault == "missing-identity" && n == "identity.expect.json")))
                {
                    using (var writer = new StreamWriter(archive.CreateEntry(name).Open(), new UTF8Encoding(false))) writer.Write("payload");
                    using (var stream = new MemoryStream(Encoding.UTF8.GetBytes("payload")))
                        records.Add(new Dictionary<string, object> { ["path"] = name, ["bytes"] = 7, ["sha256"] = Hash(stream) });
                }
                if (fault == "hash") records[0]["sha256"] = new string('0', 64);
                if (fault == "size") records[0]["bytes"] = 8;
                if (fault == "fractional") records[0]["bytes"] = 7.1;
                if (fault == "duplicate-record") records.Add(records[0]);
                if (fault == "extra") archive.CreateEntry("unexpected");
                if (fault == "duplicate-entry") archive.CreateEntry("BOOT/CurrentConfig.xml");
                if (fault == "traversal") archive.CreateEntry("../outside");
                if (fault == "ads") archive.CreateEntry("boot/file:stream");
                if (fault == "symlink") archive.CreateEntry("link").ExternalAttributes = unchecked((int)0xA1FF0000);
                var manifest = new Dictionary<string, object> { ["schemaVersion"] = schema, ["status"] = "Packaged", ["kind"] = "DevelopmentValidation",
                    ["deploymentApproved"] = false, ["files"] = records };
                if (fault == "approval-type") manifest["deploymentApproved"] = "false";
                using (var writer = new StreamWriter(archive.CreateEntry("release.json").Open())) writer.Write(Encode(manifest));
            }
            return p;
        }
        [Theory]
        [InlineData(1)] [InlineData(2)]
        public void PackageSupportsRetainedSchemas(int schema)
        {
            string p = Package(schema: schema);
            Assert.Equal(schema, Number(PackageVerifier.Verify(p, Hash(p)), "schemaVersion"));
        }
        [Theory]
        [InlineData("hash")] [InlineData("size")] [InlineData("fractional")] [InlineData("duplicate-record")]
        [InlineData("extra")] [InlineData("duplicate-entry")] [InlineData("traversal")] [InlineData("ads")]
        [InlineData("symlink")] [InlineData("approval-type")] [InlineData("missing-identity")]
        public void PackageRejectsFaults(string fault) => Assert.Throws<InvalidDataException>(() => PackageVerifier.Verify(Package(fault)));
        [Fact]
        public void PackageRejectsWrongSelectedHash() => Assert.Throws<InvalidDataException>(() => PackageVerifier.Verify(Package(), new string('0', 64)));
        [Theory]
        [InlineData("../x")] [InlineData("/root")] [InlineData("A//b")] [InlineData("x.")] [InlineData("CON.txt")]
        [InlineData("x:ads")] [InlineData("x\\y")] [InlineData("x ")]
        public void PathsRejectWindowsAliases(string path) => Assert.Throws<InvalidDataException>(() => Relative(path));
        string Project()
        {
            string p = Write("project/Machine.tsproj", "<TcSmProject TcVersion='3.1.4026.24'/>");
            Write("project/Plc/App.plcproj", "<Project/>");
            Write("project/Plc/App.tmc", "<TcModuleClass><Modules><Module TargetPlatform='TwinCAT RT (x64)' ProjectVariant='Variant'/></Modules></TcModuleClass>");
            Write("project/Plc/_Libraries/Vendor/Library/1.2.3.4/Library.compiled-library", "library");
            return p;
        }
        Dictionary<string, object> Capture(string project) => LibraryBaseline.Capture(project, "Machine", "TwinCAT RT (x64)", "Variant");
        [Fact]
        public void LibraryCaptureAndVerifyUsesCompiledCacheAndVariant()
        {
            string p = Project();
            var actual = Capture(p);
            string path = Path.Combine(root, "baseline.json");
            WriteNew(path, new { schemaVersion = 1, machines = new[] { actual } });
            var baseline = LibraryBaseline.ReadLock(path, Hash(path));
            LibraryBaseline.Verify(baseline, actual);
            Assert.Throws<InvalidDataException>(() => LibraryBaseline.Capture(p, "Machine", "TwinCAT RT (x64)", "Other"));
            Assert.Throws<InvalidDataException>(() => LibraryBaseline.Capture(p, "Machine", "TwinCAT OS (x64)", "Variant"));
            Write("project/Plc/_Libraries/Vendor/Library/1.2.3.4/Library.compiled-library", "changed");
            Assert.Throws<InvalidDataException>(() => LibraryBaseline.Verify(baseline, Capture(p)));
        }
        [Theory]
        [InlineData("*")] [InlineData("1.2.3.4")]
        public void LibraryManifestRejectsAmbiguousCachedVersions(string version)
        {
            string p = Project();
            string manifest = Write("libraries.xml", "<Libraries><Library Company='Vendor' Name='Library' Version='" + version + "'/></Libraries>");
            LibraryBaseline.Capture(p, "Machine", "TwinCAT RT (x64)", "Variant", manifest);
            Write("project/Plc/_Libraries/Vendor/Library/2.0.0.0/Library.compiled-library", "other version");
            Assert.Throws<InvalidDataException>(() => LibraryBaseline.Capture(p, "Machine", "TwinCAT RT (x64)", "Variant", manifest));
        }
        [Fact]
        public void InventoryHandlesTrailingDirectorySeparators()
        {
            Write("inventory/file", "bytes");
            var normal = Inventory(Path.Combine(root, "inventory"));
            var trailing = Inventory(Path.Combine(root, "inventory") + Path.DirectorySeparatorChar);
            Same(normal, trailing);
            Assert.Single(trailing); Assert.True(trailing.ContainsKey("file"));
        }
        [Theory]
        [InlineData("duplicate-machine")] [InlineData("duplicate-library")] [InlineData("hash")] [InlineData("size")]
        [InlineData("path")] [InlineData("schema")] [InlineData("empty")]
        public void LibraryBaselineRejectsInvalidRecords(string fault)
        {
            var entry = Capture(Project());
            var library = Object(Array(entry, "libraries")[0]);
            var record = new Dictionary<string, object> { ["schemaVersion"] = 1, ["machines"] = new[] { entry } };
            if (fault == "duplicate-machine") record["machines"] = new[] { entry, entry };
            if (fault == "duplicate-library") entry["libraries"] = new[] { library, library };
            if (fault == "hash") library["sha256"] = "wrong";
            if (fault == "size") library["bytes"] = 7.1;
            if (fault == "path") library["path"] = "../escape/1.2.3.4/file";
            if (fault == "schema") record["schemaVersion"] = 2;
            if (fault == "empty") entry["libraries"] = new object[0];
            var path = Path.Combine(root, "baseline.json"); WriteNew(path, record);
            Assert.Throws<InvalidDataException>(() => LibraryBaseline.ReadLock(path, Hash(path)));
        }
        [Fact]
        public void LockNeverOverwritesAnExistingBaseline()
        {
            string path = Write("baseline.json", "original");
            Assert.Throws<IOException>(() => WriteNew(path, new { replaced = true }));
            Assert.Equal("original", File.ReadAllText(path));
        }
        [Fact]
        public void PackageCreationRoundTripsAndRefusesOverwrite()
        {
            string original = Package();
            string payload = Path.Combine(root, "payload"); ZipFile.ExtractToDirectory(original, payload);
            var metadata = Read(Path.Combine(payload, "release.json")); metadata.Remove("files");
            File.Delete(Path.Combine(payload, "release.json"));
            string metadataPath = Path.Combine(root, "metadata.json"); WriteNew(metadataPath, metadata);
            string output = Path.Combine(root, "created.zip");
            var record = ArtifactArchives.CreatePackage(payload, metadataPath, output);
            PackageVerifier.Verify(output, Text(record, "sha256"));
            Assert.Throws<IOException>(() => ArtifactArchives.CreatePackage(payload, metadataPath, output));
            Assert.Throws<IOException>(() => ArtifactArchives.CreatePackage(payload, metadataPath, Path.Combine(payload, "bad.zip")));
        }
        [Fact]
        public void FailedPackageCreationDoesNotPublishAnArchive()
        {
            Write("payload/unexpected", "data");
            string metadata = Write("metadata.json", "{\"schemaVersion\":2,\"status\":\"Packaged\",\"kind\":\"ReleaseCandidate\",\"deploymentApproved\":false}");
            string output = Path.Combine(root, "bad.zip");
            Assert.Throws<InvalidDataException>(() => ArtifactArchives.CreatePackage(Path.Combine(root, "payload"), metadata, output));
            Assert.False(File.Exists(output)); Assert.Empty(Directory.GetFiles(root, "*.partial"));
        }
        [Fact]
        public void ToolchainDetectsInstalledDriftAndWrongVersion()
        {
            Write("components/compiler.dll", "compiler"); Write("shell/shell.exe", "shell");
            string archive = Path.Combine(root, "toolchain.zip");
            var result = ArtifactArchives.CaptureToolchain(Path.Combine(root, "components"), Path.Combine(root, "shell"), "3.1.4026.24", archive);
            string hash = Text(result, "sha256");
            ArtifactArchives.VerifyToolchain(archive, hash, "3.1.4026.24", true);
            Assert.Throws<InvalidDataException>(() => ArtifactArchives.VerifyToolchain(archive, hash, "3.1.4026.25", false));
            Assert.Throws<InvalidDataException>(() => ArtifactArchives.VerifyToolchain(archive, new string('0', 64), "3.1.4026.24", false));
            Write("components/compiler.dll", "modified");
            ArtifactArchives.VerifyToolchain(archive, hash, "3.1.4026.24", false);
            Assert.Throws<InvalidDataException>(() => ArtifactArchives.VerifyToolchain(archive, hash, "3.1.4026.24", true));
        }
        [Fact]
        public void ToolchainRejectsAdditionalInstalledFilesAndPreservesBaseline()
        {
            Write("components/compiler.dll", "compiler"); Write("shell/shell.exe", "shell");
            string archive = Path.Combine(root, "toolchain.zip");
            var result = ArtifactArchives.CaptureToolchain(Path.Combine(root, "components"), Path.Combine(root, "shell"), "3.1.4026.24", archive);
            string hash = Text(result, "sha256");
            Assert.Throws<IOException>(() => ArtifactArchives.CaptureToolchain(Path.Combine(root, "components"), Path.Combine(root, "shell"), "3.1.4026.24", archive));
            CheckHash(Hash(archive), hash);
            Write("shell/extra.dll", "extra");
            Assert.Throws<InvalidDataException>(() => ArtifactArchives.VerifyToolchain(archive, hash, "3.1.4026.24", true));
            ArtifactArchives.VerifyToolchain(archive, hash, "3.1.4026.24", false);
        }
        [Fact]
        public void EsiPreservesSystemFilesRejectsConflictsBeforeCopyAndVerifies()
        {
            string system = Path.Combine(root, "system"), devices = Path.Combine(root, "devices"), user = Path.Combine(root, "user");
            Write("system/vendor.xml", "vendor"); Write("devices/custom.xml", "custom"); Write("user/custom.xml", "conflict");
            Assert.Throws<InvalidDataException>(() => EsiCatalog.Execute(system, devices, user, true));
            Assert.False(File.Exists(Path.Combine(user, "vendor.xml")));
            File.Delete(Path.Combine(user, "custom.xml"));
            Assert.Throws<FileNotFoundException>(() => EsiCatalog.Execute(system, devices, user, false));
            EsiCatalog.Execute(system, devices, user, true);
            EsiCatalog.Execute(system, devices, user, false);
            Assert.Equal("vendor", File.ReadAllText(Path.Combine(user, "vendor.xml")));
            Assert.Throws<InvalidDataException>(() => EsiCatalog.Execute(system, devices, system, true));
        }
        [Theory]
        [InlineData(false)] [InlineData(true)]
        public void EsiRejectsParentFileConflictsBeforeAnyCopy(bool plannedConflict)
        {
            string system = Path.Combine(root, "system"), devices = Path.Combine(root, "devices"), user = Path.Combine(root, "user");
            Write("system/first.xml", "first"); Write("devices/parent.xml/child.xml", "child");
            if (plannedConflict) Write("system/parent.xml", "conflicting planned file");
            else Write("user/parent.xml", "conflicting installed file");
            Assert.Throws<IOException>(() => EsiCatalog.Execute(system, devices, user, true));
            Assert.False(File.Exists(Path.Combine(user, "first.xml")));
        }
        [Fact]
        public void CliRejectsUnknownDuplicateAndMissingOptions()
        {
            Assert.Equal(2, ArtifactCommands.Run(new[] { "package", "unknown" }));
            Assert.Equal(2, ArtifactCommands.Run(new[] { "package", "verify", "--package" }));
            Assert.Equal(2, ArtifactCommands.Run(new[] { "package", "verify", "--package", "one", "--package", "two" }));
            Assert.Equal(2, ArtifactCommands.Run(new[] { "libraries", "verify", "--unknown", "value" }));
            Assert.Equal(0, ArtifactCommands.Run(new[] { "package", "--help" }));
        }
        [Fact]
        public void CliReturnsFailureRatherThanThrowingForBadArtifacts()
        {
            Assert.Equal(1, ArtifactCommands.Run(new[] { "package", "verify", "--package", Package(), "--sha256", new string('0', 64) }));
            Assert.Equal(1, ArtifactCommands.Run(new[] { "package", "verify", "--package", Package("extra") }));
        }
    }
}
