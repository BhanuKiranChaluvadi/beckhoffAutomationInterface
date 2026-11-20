using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text.RegularExpressions;
using BeckhoffAutomationInterface.Machines;
using BeckhoffAutomationInterface.Release;
using Xunit;
using static BeckhoffAutomationInterface.Release.ArtifactFiles;

namespace BeckhoffAutomationInterface.Tests
{
    /// <summary>'release build' and 'release lock' on a disposable git repository with a fake
    /// compile step (was Tests/Tools/test_release_evidence.ps1). No XAE, no runtime activation:
    /// the fake writes the generated project that a TwinCAT build would leave, with the build
    /// identity that bai wrote into the build copy of GVL_BuildInfo.st.</summary>
    public sealed class ReleaseBuildTests : IDisposable
    {
        const string Version = "3.1.4026.24", Platform = "TwinCAT RT (x64)", Library = "compiled fixture library\r\n";
        const string CurrentInfo = "15F5F6E1-C1BA-56ED-2D71-3D614E4C775F", StaleInfo = "24003CFD-B962-1D85-1039-25720DF76BB8";
        readonly string scratch = TestProjects.NewTempDir("bai_release");
        readonly MachineRepoFixture repo;
        string Tool => Path.Combine(scratch, "bai");
        string Esi => Path.Combine(scratch, "esi");
        string UserEsi => Path.Combine(scratch, "user-esi");
        string Components => Path.Combine(scratch, "components");
        string ToolchainZip => Path.Combine(scratch, "toolchain.zip");
        string LockFile => Path.Combine(scratch, "libraries.lock.json");
        string Installed => Path.Combine(scratch, "managed-libraries");
        string fault = "";

        public void Dispose() { try { BuildLocation.Delete(scratch); } catch (IOException) { } catch (UnauthorizedAccessException) { } }

        void Write(string path, string text) { Directory.CreateDirectory(Path.GetDirectoryName(path)); File.WriteAllText(path, text); }
        void Git(params string[] args) => ReleaseBuilder.GitText(repo.Root, args);

        public ReleaseBuildTests()
        {
            repo = new MachineRepoFixture();
            repo.Write("Tools/fixture.txt", "fixture");
            repo.Write("Machines/Fam/libraries.xml", "<Libraries><Library Company=\"Vendor\" Name=\"Library\" Version=\"*\"/></Libraries>");
            Write(Path.Combine(Esi, "device.xml"), "<EtherCATInfo/>");
            Write(Path.Combine(UserEsi, "vendor.xml"), "<EtherCATInfo Vendor=\"user\"/>");
            Write(Path.Combine(Components, "compiler.dll"), "fixture compiler");
            Write(Path.Combine(scratch, "xaeShell", "shell.exe"), "fixture shell");
            ArtifactArchives.CaptureToolchain(Components, Path.Combine(scratch, "xaeShell"), Version, ToolchainZip);
            Write(Path.Combine(Tool, "bai.exe"), "fixture bai");
            Write(Path.Combine(Tool, "dependency.dll"), "fixture dependency");
            Write(Path.Combine(Installed, @"Vendor\Library\1.2.3.4\Library.compiled-library"), Library);
            var library = new Dictionary<string, object> { ["path"] = "Vendor/Library/1.2.3.4/Library.compiled-library", ["bytes"] = Library.Length,
                ["sha256"] = ProjectDrift.Digest(System.Text.Encoding.UTF8.GetBytes(Library)) };
            File.WriteAllText(LockFile, Encode(new Dictionary<string, object> { ["schemaVersion"] = 1, ["machines"] = new[] {
                new Dictionary<string, object> { ["machine"] = "M1", ["twinCatVersion"] = Version, ["targetPlatform"] = Platform, ["libraries"] = new[] { library } } } }));
            Git("init", "-q");
            Git("config", "user.name", "Release fixture");
            Git("config", "user.email", "release-fixture@example.invalid");
            Git("add", ".");
            Git("commit", "-qm", "fixture");
            Git("tag", "v-test");
        }

        ReleaseRequest Request(string bundle, string tag = "v-test", bool workingTree = false) => new ReleaseRequest
        {
            Machines = { "M1" }, Tag = tag, WorkingTree = workingTree, Bundle = Path.Combine(scratch, "bundles", bundle), Bai = Path.Combine(Tool, "bai.exe"),
            BaiSha256 = Hash(Path.Combine(Tool, "bai.exe")), TwinCatVersion = Version, LibraryLock = LockFile, LibraryLockSha256 = Hash(LockFile),
            Toolchain = ToolchainZip, ToolchainSha256 = Hash(ToolchainZip), Manifest = repo.ManifestFile, EsiDirectory = Esi, UserEsiDirectory = UserEsi,
            LibraryRepository = Installed,
        };

        [Theory]
        [InlineData("missing", "Library 1.2.3.4 is missing (installed: none). Install: tcpkg install TwinCAT.XAE.PLC.Lib.Library=1.2.3.4")]
        [InlineData("bytes", "Library 1.2.3.4 has other bytes than the lock")]
        [InlineData("newer", "Library 1.2.4.0 is installed, newer than the locked 1.2.3.4")]
        public void InstalledLibrariesThatDifferFromTheLockStopBeforeTheBuild(string defect, string reason)
        {
            string locked = Path.Combine(Installed, @"Vendor\Library\1.2.3.4\Library.compiled-library");
            if (defect == "missing") BuildLocation.Delete(Installed);
            if (defect == "bytes") File.WriteAllText(locked, "other bytes\r\n");
            if (defect == "newer") Write(Path.Combine(Installed, @"Vendor\Library\1.2.4.0\Library.compiled-library"), "newer\r\n");
            int compiled = 0;
            var ex = Assert.Throws<InvalidDataException>(() => ReleaseBuilder.Build(Request("libraries-" + defect), (a, b, c, d, e, f, g) => ++compiled));
            Assert.Contains("M1: " + reason, ex.Message);
            Assert.Contains("Nothing was built", ex.Message);
            Assert.Equal(0, compiled);
            Assert.False(Directory.Exists(Bundle("libraries-" + defect)));
        }

        [Fact]
        public void NewerLibraryIsAllowedWhenLibrariesXmlFixesTheVersion()
        {
            Write(Path.Combine(Installed, @"Vendor\Library\1.2.4.0\Library.compiled-library"), "newer\r\n");
            string xml = Path.Combine(scratch, "pinned.xml");
            Write(xml, "<Libraries><Library Company=\"Vendor\" Name=\"Library\" Version=\"1.2.3.4\"/></Libraries>");
            var baseline = LibraryBaseline.ReadLock(LockFile, Hash(LockFile));
            Assert.Empty(LibraryBaseline.CheckInstalled(baseline, "M1", Installed, xml));
            Assert.Single(LibraryBaseline.CheckInstalled(baseline, "M1", Installed));
            Assert.Equal("3.3.75", LibraryBaseline.PackageVersion("3.3.75.0"));
            Assert.Equal("1.2.3.4", LibraryBaseline.PackageVersion("1.2.3.4"));
        }

        string Bundle(string name) => Path.Combine(scratch, "bundles", name);

        /// <summary>What a TwinCAT build leaves in the work folder, from the real source assembly and build identity.</summary>
        int FakeCompile(string bai, string manifestFile, string work, IReadOnlyList<string> machines, string platform, string version, bool dirty)
        {
            Assert.Equal(Path.Combine(Path.GetDirectoryName(Path.GetDirectoryName(manifestFile)), "tool", "bai.exe"), bai);
            var manifest = MachineManifest.Load(manifestFile);
            foreach (string m in machines)
            {
                var l = new MachineLayout(manifest, m, simulation: false, work: work);
                MachineSources.Assemble(l, (command, folder) => 0);
                string gvl = MachineSources.RepoPath(l.Src, l.Family.BuildInfo);
                BuildInfo.Write(gvl, BuildInfo.FromGit(l.RepoRoot, m, manifest.BuildInputs, BuildInfo.RunGit, DateTime.UtcNow));
                string stamp = File.ReadAllText(gvl);
                string project = Path.GetDirectoryName(l.Tsproj), plc = Path.Combine(project, "Fam");
                Write(l.Tsproj, "<TcSmProject TcVersion='" + (fault == "version" ? "3.1.9999.0" : fault == "other-build" ? "3.1.4026.27" : Version) + "'/>");
                Write(Path.Combine(plc, "Fam.plcproj"), "<Project><PropertyGroup><CompilerDefines>" + (fault == "simulation" ? "SIMULATION" : "") + "</CompilerDefines></PropertyGroup></Project>");
                Directory.CreateDirectory(Path.Combine(plc, "_Libraries", "Vendor", "Library", "1.2.3.4"));
                if (fault != "libraries")
                    File.WriteAllText(Path.Combine(plc, @"_Libraries\Vendor\Library\1.2.3.4\Library.compiled-library"), fault == "library-content" ? "changed fixture library\r\n" : Library);
                string identity = "<DataType><Name>GVL_BuildInfo</Name>";
                foreach (string field in new[] { "sGitTag", "sGitCommit", "sMachine", "sBuildTime" })
                {
                    var hit = Regex.Match(stamp, field + @"\s*:\s*(STRING\(\d+\))\s*:=\s*'([^']*)'");
                    string value = fault == "identity" && field == "sGitCommit" ? new string('0', 40) : fault == "identity-tag" && field == "sGitTag" ? "wrong-tag" : hit.Groups[2].Value;
                    identity += "<SubItem><Name>" + field + "</Name><Type>" + hit.Groups[1].Value + "</Type><Default><String>" + value + "</String></Default></SubItem>";
                }
                identity += "<SubItem><Name>bDirty</Name><Type>BOOL</Type><Default><Bool>" + (Regex.IsMatch(stamp, @"bDirty\s*:\s*BOOL\s*:=\s*TRUE") ? "true" : "false") + "</Bool></Default></SubItem></DataType>";
                Write(Path.Combine(plc, "Fam.tmc"), "<TcModuleClass><DataTypes>" + identity + "<DataType><Name>GVL_Simulation</Name><SubItem><Name>bActive</Name><Default><Bool>" +
                    (fault == "compiled-simulation" ? "true" : "false") + "</Bool></Default></SubItem></DataType></DataTypes><Modules><Module TargetPlatform='" +
                    (fault == "platform" ? "TwinCAT RT (x86)" : platform) + "' ProjectVariant='" + m + "'/></Modules></TcModuleClass>");
                string boot = Path.Combine(project, "_Boot", m, Platform);
                Write(Path.Combine(boot, "CurrentConfig.xml"), "<TcBootProject Platform='" + Platform + "'/>");
                Write(Path.Combine(boot, "CurrentProjectInfo.json"), "{\"project\":{\"name\":\"" + m + "\",\"platform\":\"" + Platform + "\"}}");
                foreach (string file in new[] { "Port_851.app", "Port_851.crc", "Port_851.autostart" }) Write(Path.Combine(boot, "Plc", file), "fixture");
                // TwinCAT keeps the compile information of earlier compiles; the boot project names its own.
                Write(Path.Combine(plc, "_CompileInfo", CurrentInfo + ".compileinfo"), "current compiler information");
                Write(Path.Combine(plc, "_CompileInfo", StaleInfo + ".compileinfo"), "stale compiler information");
                Write(Path.Combine(boot, "Plc", "Port_851_boot.tizip"), "fixture " + (fault == "boot-names-none" ? "" : fault == "boot-names-two"
                    ? CurrentInfo + " " + StaleInfo.ToLowerInvariant() : CurrentInfo.ToLowerInvariant()));
                Write(l.Tsproj + ".bak", "editor backup");
                Write(Path.Combine(project, "bai-sync-manifest.json"), "{}");
                Write(Path.Combine(project, "Safety", "TargetSystemConfig.xml"), "<TargetSystemConfig ProjectCRC='0x1234'><SerialNumber>42</SerialNumber></TargetSystemConfig>");
                if (fault == "boot") File.Delete(Path.Combine(boot, "CurrentConfig.xml"));
                Write(Path.Combine(l.Dir, m, m + ".sln"), "fixture solution");
                File.WriteAllText(l.Log, "Variable link sync complete (1 linked, 0 unresolved)");
            }
            string bundle = Path.GetDirectoryName(work);
            if (fault == "tool") File.WriteAllText(Path.Combine(bundle, "tool", "dependency.dll"), "tampered");
            if (fault == "esi") File.WriteAllText(Path.Combine(Esi, "device.xml"), "tampered");
            if (fault == "toolchain") File.WriteAllText(Path.Combine(Components, "compiler.dll"), "tampered");
            if (fault == "source") File.WriteAllText(Path.Combine(bundle, "source", "Lib", "Math", "F_Add.st"), "tampered");
            return fault == "build" ? 1 : 0;
        }

        Dictionary<string, object> Release(ReleaseRequest q, string withFault = "")
        {
            fault = withFault;
            try { return ReleaseBuilder.Build(q, FakeCompile); }
            finally
            {
                fault = "";
                Write(Path.Combine(Esi, "device.xml"), "<EtherCATInfo/>");
                Write(Path.Combine(Components, "compiler.dll"), "fixture compiler");
            }
        }

        static Dictionary<string, object> First(object list) => Object(Array(new Dictionary<string, object> { ["x"] = list }, "x")[0]);

        [Fact]
        public void TaggedReleaseKeepsEvidenceAndAVerifiedPackage()
        {
            var record = Release(Request("success"));
            string bundle = Bundle("success");
            Assert.Equal(record["status"], Read(Path.Combine(bundle, "release.json"))["status"]);
            Assert.Equal("BuildPassed", record["status"]);
            Assert.False((bool)record["deploymentApproved"]);
            Assert.Equal("Tag", record["sourceMode"]);
            Assert.Equal("v-test", record["sourceTag"]);
            // The source is identified by the hash of the git archive of the commit; the archive file is not kept.
            string expectedSource = Path.Combine(scratch, "expected-source.zip");
            Git("archive", "--format=zip", "--output=" + expectedSource, "v-test");
            Assert.Equal(Hash(expectedSource).ToUpperInvariant(), record["sourceArchiveSha256"]);
            var project = First(record["projects"]);
            Assert.Equal("Vendor/Library/1.2.3.4/Library.compiled-library", First(project["libraryCache"])["path"]);
            Assert.Equal(Platform, project["targetPlatform"]);
            Assert.Equal(2, Array(record, "baiFiles").Length);
            Assert.Equal(new[] { "system/device.xml", "user/vendor.xml" }, Array(record, "installedEsiFiles").Select(f => (string)Object(f)["path"]));
            // No ESI archive: the inventory (path, bytes, SHA-256 per file) is the evidence.
            Assert.False(record.ContainsKey("esiArchiveSha256"));
            Assert.Equal(Hash(Path.Combine(Esi, "device.xml")).ToUpperInvariant(), Text(First(record["installedEsiFiles"]), "sha256").ToUpperInvariant());
            Assert.Equal(Hash(LockFile).ToUpperInvariant(), record["libraryLockSha256"]);
            // After success the bundle keeps the record, the packages and the build logs only.
            var kept = Directory.GetFiles(bundle, "*", SearchOption.AllDirectories)
                .Select(f => f.Substring(bundle.Length + 1).Replace(Path.DirectorySeparatorChar, '/')).OrderBy(f => f, StringComparer.Ordinal).ToList();
            Assert.Equal(new[] { "packages/M1.zip", "packages/M1.zip.sha256", "release.json" }, kept.Where(f => !f.EndsWith(".log")).ToArray());
            Assert.Contains(kept, f => f.StartsWith("build/") && f.EndsWith("M1.log"));
            Assert.All(kept.Where(f => f.EndsWith(".log")), f => Assert.StartsWith("build/", f));
            Assert.Equal(16, Array(project, "artifacts").Length);

            var package = First(record["packages"]);
            string zip = Path.Combine(bundle, "packages", "M1.zip");
            Assert.Equal(Text(package, "sha256") + "  M1.zip\n", File.ReadAllText(zip + ".sha256"));
            var manifest = PackageVerifier.Verify(zip, Text(package, "sha256"));
            Assert.Equal("ReleaseCandidate", manifest["kind"]);
            Assert.Equal(3, manifest["schemaVersion"]);
            Assert.Equal(11, Array(manifest, "files").Length);   // with README.md
            Assert.Equal("0x1234", First(manifest["safety"])["projectCrc"]);
            Assert.Equal("42", First(manifest["safety"])["serialNumber"]);
            using (var archive = ZipFile.OpenRead(zip))
            {
                Dictionary<string, object> identity;
                using (var reader = new StreamReader(archive.GetEntry("identity.expect.json").Open())) identity = Json(reader.ReadToEnd());
                var checks = Array(Object(identity["variants"]), "M1").Select(Object).ToList();
                Assert.Equal(5, checks.Count);
                Assert.Equal(record["sourceCommit"], checks.Single(c => (string)c["symbol"] == "GVL_BuildInfo.sGitCommit")["equals"]);
                Assert.NotNull(archive.GetEntry("project.zip"));
                // Compact evidence: each long inventory is replaced by the SHA-256 of its JSON in release.json.
                Dictionary<string, object> evidence;
                using (var reader = new StreamReader(archive.GetEntry("evidence.json").Open())) evidence = Json(reader.ReadToEnd());
                Assert.Equal("BuildPassed", evidence["status"]);
                Assert.Equal(record["sourceCommit"], evidence["sourceCommit"]);
                var released = Read(Path.Combine(bundle, "release.json"));
                string Digest(object value) => ProjectDrift.Digest(System.Text.Encoding.UTF8.GetBytes(Encode(value))).ToUpperInvariant();
                foreach (string inventory in new[] { "sourceInputs", "baiFiles", "installedEsiFiles" })
                {
                    Assert.False(evidence.ContainsKey(inventory), inventory);
                    Assert.Equal(Digest(released[inventory]), evidence[inventory + "Sha256"]);
                }
                var compactProject = First(evidence["projects"]);
                Assert.False(compactProject.ContainsKey("artifacts"));
                Assert.Equal(Digest(First(released["projects"])["artifacts"]), compactProject["artifactsSha256"]);
                Assert.Equal(project["buildLogSha256"], compactProject["buildLogSha256"]);
                Assert.True(archive.GetEntry("evidence.json").Length < 8 * 1024, "evidence.json stays small");
            }

            Assert.Throws<IOException>(() => Release(Request("success")));
        }

        [Fact]
        public void PackageAndProjectEvidenceRefuseBadInputsWhileTheProjectExists()
        {
            bool checkedInBuild = false;
            ReleaseBuilder.Build(Request("checks"), (a, manifestFile, work, d, e, f, g) =>
            {
                int code = FakeCompile(a, manifestFile, work, d, e, f, g);
                var layout = new MachineLayout(MachineManifest.Load(manifestFile), "M1", false, work);
                var project = ReleaseBuilder.ProjectEvidence(layout, Version, Platform);
                // A package cannot include its own staging folder.
                Assert.Throws<InvalidOperationException>(() => ReleaseBuilder.Package(layout, project, new Dictionary<string, object>(), Path.Combine(layout.Dir, "M1", "nested-package")));
                // The cache must satisfy the manifest: a missing library, or a pinned version that differs.
                string libraries = Path.Combine(layout.Src, "libraries.xml"), original = File.ReadAllText(libraries);
                foreach (string requirement in new[] { "Name='Missing' Version='*'", "Name='Library' Version='9.0.0.0'" })
                {
                    File.WriteAllText(libraries, "<Libraries><Library Company='Vendor' " + requirement + "/></Libraries>");
                    Assert.Contains("does not satisfy manifest", Assert.Throws<InvalidDataException>(() => ReleaseBuilder.ProjectEvidence(layout, Version, Platform)).Message);
                }
                File.WriteAllText(libraries, original);
                checkedInBuild = true;
                return code;
            });
            Assert.True(checkedInBuild);
        }

        [Fact]
        public void EsiInventoryKeepsDifferentFilesWithTheSameNameInBothScopes()
        {
            Write(Path.Combine(Esi, "vendor.xml"), new string('x', 1024 * 1024));
            var record = Release(Request("esi-scopes"));
            var files = Array(record, "installedEsiFiles").Select(Object).ToDictionary(f => Text(f, "path"), f => Text(f, "sha256").ToUpperInvariant());
            Assert.Equal(3, files.Count);
            Assert.Equal(Hash(Path.Combine(Esi, "vendor.xml")).ToUpperInvariant(), files["system/vendor.xml"]);
            Assert.Equal(Hash(Path.Combine(UserEsi, "vendor.xml")).ToUpperInvariant(), files["user/vendor.xml"]);
            Assert.False(File.Exists(Path.Combine(Bundle("esi-scopes"), "esi.zip")));
        }

        [Fact]
        public void ServiceProjectKeepsCompilerAndLibraryFilesWithoutDuplicatingBootOrIdeCache()
        {
            var q = Request("service-project");
            var record = ReleaseBuilder.Build(q, (a, b, c, d, e, f, g) =>
            {
                int code = FakeCompile(a, b, c, d, e, f, g);
                string project = Path.Combine(c, "Fam", "M1", "M1");
                Write(Path.Combine(project, ".vs", "workspace.cache"), "IDE cache");
                return code;
            });
            string package = Path.Combine(Bundle("service-project"), "packages", "M1.zip");
            PackageVerifier.Verify(package, Text(First(record["packages"]), "sha256"));
            using (var outer = ZipFile.OpenRead(package))
            using (var buffer = new MemoryStream())
            {
                using (var content = outer.GetEntry("project.zip").Open()) content.CopyTo(buffer);
                buffer.Position = 0;
                using (var service = new ZipArchive(buffer, ZipArchiveMode.Read))
                {
                    var names = service.Entries.Select(entry => entry.FullName.Replace('\\', '/')).ToList();
                    Assert.DoesNotContain(names, name => name.Split('/').Any(part => Names.Equals(part, "_Boot") || Names.Equals(part, ".vs")));
                    // Only the compile information that the boot project names (XAE login needs it).
                    Assert.Equal(new[] { CurrentInfo + ".compileinfo" },
                        names.Where(name => name.EndsWith(".compileinfo")).Select(name => name.Split('/').Last()).ToArray());
                    Assert.DoesNotContain(names, name => name.EndsWith(".bak") || name.EndsWith("bai-sync-manifest.json"));
                    Assert.Contains(names, name => name.EndsWith("Library.compiled-library"));
                    Assert.Contains(names, name => name.EndsWith("Fam.tmc"));
                    Assert.Contains(names, name => name.EndsWith("TargetSystemConfig.xml"));
                    Assert.Contains(names, name => name.EndsWith("M1.sln"));
                }
                Assert.NotNull(outer.GetEntry("boot/CurrentConfig.xml"));
                Assert.NotNull(outer.GetEntry("boot/Plc/Port_851.app"));
            }
        }

        [Fact]
        public void RefusalsBeforeTheBundle()
        {
            var q = Request("bad-hash"); q.BaiSha256 = new string('0', 64);
            Assert.Contains("approved SHA256", Assert.Throws<InvalidDataException>(() => Release(q)).Message);
            q = Request("bad-lock"); q.LibraryLockSha256 = new string('0', 64);
            Assert.Throws<InvalidDataException>(() => Release(q));
            q = Request("both", "v-test", workingTree: true);
            Assert.Throws<ArgumentException>(() => Release(q));
            q = Request("half-toolchain"); q.Toolchain = null;
            Assert.Contains("together", Assert.Throws<ArgumentException>(() => Release(q)).Message);
            q = Request("bad-version"); q.TwinCatVersion = "4026";
            Assert.Throws<ArgumentException>(() => Release(q));
            q = Request("test-adapter"); q.Machines = new List<string> { "M2" };
            Assert.Contains("test adapter", Assert.Throws<InvalidOperationException>(() => Release(q)).Message);
            repo.Write("Lib/untracked.st", "x");
            Assert.Contains("clean repository", Assert.Throws<InvalidOperationException>(() => Release(Request("dirty"))).Message);
            foreach (string name in new[] { "bad-hash", "bad-lock", "both", "half-toolchain", "bad-version", "test-adapter", "dirty" }) Assert.False(Directory.Exists(Bundle(name)), name);
        }

        [Fact]
        public void OtherBuildOfTheSameMajorPassesAndIsRecorded()
        {
            var q = Request("other-build"); q.TwinCatVersion = "3.1.4026";
            var record = Release(q, "other-build");  // lock and toolchain: 3.1.4026.24; project: 3.1.4026.27
            Assert.Equal("BuildPassed", record["status"]);
            Assert.Equal("3.1.4026", record["expectedTwinCatVersion"]);
            Assert.Equal("3.1.4026.27", First(record["projects"])["twinCatVersion"]);
        }

        [Fact]
        public void ReleaseWithoutToolchainArchive()
        {
            var q = Request("no-toolchain"); q.Toolchain = null; q.ToolchainSha256 = null;
            var record = Release(q);
            Assert.Equal("BuildPassed", record["status"]);
            Assert.Null(record["toolchainArchiveSha256"]);
            Assert.False(File.Exists(Path.Combine(Bundle("no-toolchain"), "toolchain.zip")));
        }

        [Theory]
        [InlineData("version", "TwinCAT version.*does not match")]
        [InlineData("platform", "compiled TMC does not identify")]
        [InlineData("libraries", "no retained compiled libraries")]
        [InlineData("simulation", "contains SIMULATION")]
        [InlineData("source", "Source snapshot changed")]
        [InlineData("tool", "Private BAI files changed")]
        [InlineData("esi", "Installed ESI inventory changed")]
        [InlineData("build", "Release build failed")]
        [InlineData("boot", "Missing compiled boot payload")]
        [InlineData("compiled-simulation", "Compiled TMC must explicitly initialize")]
        [InlineData("identity", "Compiled build identity differs from the release record")]
        [InlineData("identity-tag", "Compiled build identity differs from the release record")]
        [InlineData("library-content", "resolved libraries differ from the locked baseline")]
        [InlineData("toolchain", "Installed toolchain or its archive differs")]
        [InlineData("boot-names-none", "boot project names 0 of the 2 compile information files")]
        [InlineData("boot-names-two", "boot project names 2 of the 2 compile information files")]
        public void DefectRejectsTheCandidateAndKeepsTheReason(string defect, string reason)
        {
            Assert.ThrowsAny<Exception>(() => Release(Request("fail-" + defect), defect));
            var failed = Read(Path.Combine(Bundle("fail-" + defect), "release.json"));
            Assert.Equal("Failed", failed["status"]);
            // A failed build keeps everything for the diagnosis.
            Assert.True(Directory.Exists(Path.Combine(Bundle("fail-" + defect), "source")), "source kept");
            Assert.Matches(reason, Text(failed, "error"));
        }

        [Fact]
        public void UntaggedHeadMustBePushedAndUsesGitDescribe()
        {
            repo.Write("App/Fam/fixture.txt", "second commit");
            Git("add", ".");
            Git("commit", "-qm", "untagged change");
            Assert.Contains("not on the server", Assert.Throws<InvalidOperationException>(() => Release(Request("head-unpushed", tag: null))).Message);
            Assert.False(Directory.Exists(Bundle("head-unpushed")));
            string origin = Path.Combine(scratch, "origin.git");
            ReleaseBuilder.GitText(scratch, "init", "-q", "--bare", origin);
            Git("remote", "add", "origin", origin);
            Git("push", "-q", "origin", "HEAD:refs/heads/main");
            var record = Release(Request("head", tag: null));
            Assert.Equal("Head", record["sourceMode"]);
            Assert.False((bool)record["sourceTagged"]);
            Assert.Matches("^v-test-1-g[0-9a-f]+$", Text(record, "sourceTag"));
            Assert.False((bool)record["sourceDirty"]);
            Assert.Equal("ReleaseCandidate", PackageVerifier.Verify(Path.Combine(Bundle("head"), "packages", "M1.zip"))["kind"]);
        }

        [Fact]
        public void DefaultsComeFromTheManifestAndTheSource()
        {
            string bai = Path.Combine(Tool, "bai.exe"), json = File.ReadAllText(repo.ManifestFile);
            File.WriteAllText(repo.ManifestFile, "{ \"release\": { \"twincat\": \"3.1.4026\", \"bai\": { \"version\": \"1.9.0\", \"sha256\": \"" + Hash(bai) + "\" } },"
                + json.Substring(json.IndexOf('{') + 1));
            File.Copy(LockFile, repo.Path_("Machines/Fam/libraries.lock.json"));
            Git("add", ".");
            Git("commit", "-qm", "release defaults");
            Git("tag", "v-auto");
            ReleaseRequest Minimal(string bundle) => new ReleaseRequest { Machines = { "M1" }, Tag = "v-auto", Bundle = Bundle(bundle), Bai = bai,
                Manifest = repo.ManifestFile, EsiDirectory = Esi, UserEsiDirectory = UserEsi, LibraryRepository = Installed };
            var record = ReleaseBuilder.BuildAll(Minimal("auto"), FakeCompile).Single();
            Assert.Equal("BuildPassed", record["status"]);
            Assert.Equal("3.1.4026", record["expectedTwinCatVersion"]);
            Assert.Equal(Hash(bai).ToUpperInvariant(), record["baiSha256"]);
            Assert.Equal(Hash(LockFile).ToUpperInvariant(), record["libraryLockSha256"]);

            // Another bai than the approved one, and a lock from outside without its hash, stop before a bundle.
            File.WriteAllText(bai, "another bai");
            Assert.Contains("approved SHA256", Assert.Throws<InvalidDataException>(() => ReleaseBuilder.BuildAll(Minimal("other-bai"), FakeCompile)).Message);
            var q = Minimal("lock-without-hash"); q.LibraryLock = LockFile;
            Assert.Contains("--library-lock-sha256", Assert.Throws<ArgumentException>(() => ReleaseBuilder.BuildAll(q, FakeCompile)).Message);
            Assert.False(Directory.Exists(Bundle("other-bai")));
            Assert.False(Directory.Exists(Bundle("lock-without-hash")));
        }

        [Fact]
        public void TagThatIsNotHeadIsBuiltFromACleanCheckout()
        {
            string tagged = ReleaseBuilder.Git(repo.Root, "rev-parse", "HEAD")[0];
            repo.Write("App/Fam/after-tag.txt", "after the tag");
            Git("add", ".");
            Git("commit", "-qm", "after the tag");
            repo.Write("Lib/uncommitted.st", "not committed");
            Assert.Contains("not on the server", Assert.Throws<InvalidOperationException>(() => ReleaseBuilder.BuildAll(Request("unpushed"), FakeCompile)).Message);
            string origin = Path.Combine(scratch, "origin.git");
            ReleaseBuilder.GitText(scratch, "init", "-q", "--bare", origin);
            Git("remote", "add", "origin", origin);
            Git("push", "-q", "origin", "HEAD:refs/heads/main");

            var record = ReleaseBuilder.BuildAll(Request("checkout"), FakeCompile).Single();
            Assert.Equal("Tag", record["sourceMode"]);
            Assert.Equal("v-test", record["sourceTag"]);
            Assert.Equal(tagged, record["sourceCommit"]);
            Assert.False(File.Exists(Path.Combine(Bundle("checkout"), @"source\App\Fam\after-tag.txt")));
            Assert.True(File.Exists(repo.Path_("Lib/uncommitted.st")));   // the working tree is not touched
            Assert.Empty(Directory.GetDirectories(Path.Combine(repo.Work, "checkout")));
        }

        [Fact]
        public void FamilyNameSelectsItsMachines()
        {
            var manifest = repo.Load();
            Assert.Equal(new[] { "M1", "M2" }, ReleaseBuilder.Machines(manifest, new[] { "fam" }));
            Assert.Equal(new[] { "M1" }, ReleaseBuilder.Machines(manifest, new[] { "m1" }));
            Assert.Contains("Fam (M1, M2)", Assert.Throws<ArgumentException>(() => ReleaseBuilder.Machines(manifest, new[] { "Other" })).Message);
        }

        [Fact]
        public void WorkingTreeGivesADevelopmentPackage()
        {
            repo.Write("Lib/Math/F_Add.st", "FUNCTION F_Add : INT\nVAR_INPUT\n    a : INT;\nEND_VAR\nF_Add := a + 1; // uncommitted\nEND_FUNCTION\n");
            repo.Write("Lib/new-file.txt", "untracked");
            File.Delete(repo.Path_("Tools/fixture.txt"));
            repo.Write("Tools/other.txt", "keeps the area");
            bool overlaid = false;
            var record = ReleaseBuilder.Build(Request("worktree", tag: null, workingTree: true), (a, manifestFile, work, d, e, f, g) =>
            {
                // The snapshot that the compile step builds: HEAD plus the working tree.
                string source = Path.GetDirectoryName(manifestFile);
                Assert.Contains("uncommitted", File.ReadAllText(Path.Combine(source, @"Lib\Math\F_Add.st")));
                Assert.True(File.Exists(Path.Combine(source, @"Lib\new-file.txt")));
                Assert.False(File.Exists(Path.Combine(source, @"Tools\fixture.txt")));
                overlaid = true;
                return FakeCompile(a, manifestFile, work, d, e, f, g);
            });
            Assert.True(overlaid);
            string bundle = Bundle("worktree");
            Assert.Equal("WorkingTree", record["sourceMode"]);
            Assert.True((bool)record["sourceDirty"]);
            // After the build, the bundle keeps what git cannot give: the uncommitted changes.
            Assert.False(Directory.Exists(Path.Combine(bundle, "source")));
            Assert.Contains("uncommitted", File.ReadAllText(Path.Combine(bundle, "working-tree.patch")));
            Assert.Contains("Lib/new-file.txt", File.ReadAllText(Path.Combine(bundle, "working-tree-changes.txt")));
            Assert.Equal("DevelopmentValidation", PackageVerifier.Verify(Path.Combine(bundle, "packages", "M1.zip"))["kind"]);
            Assert.Matches(@"^M1-v-test-dirty-\d{8}-\d{6}$", Path.GetFileName(DefaultBundleName()));
        }

        string DefaultBundleName()
        {
            var q = Request("unused", tag: null, workingTree: true); q.Bundle = null;
            Assert.ThrowsAny<Exception>(() => Release(q, "build"));
            return Directory.GetDirectories(Path.Combine(repo.Work, "release")).Single();
        }

        [Fact]
        public void LockCapturesTheBuiltLibraries()
        {
            // The lock comes from a machine build (the work of bai.machines.json), not from a release bundle.
            string work = Path.Combine(scratch, "machine-work");
            Assert.Equal(0, FakeCompile(Path.Combine(Path.GetDirectoryName(Path.GetDirectoryName(repo.ManifestFile)), "tool", "bai.exe"), repo.ManifestFile, work, new[] { "M1" }, Platform, "v-test", false));
            string output = Path.Combine(scratch, "captured.lock.json");
            var result = ReleaseBuilder.Lock(repo.ManifestFile, work, new[] { "M1" }, Version, null, output);
            string Sha(Dictionary<string, object> baseline) => Text(First(Object(Array(baseline, "machines")[0])["libraries"]), "sha256").ToUpperInvariant();
            Assert.Equal(Sha(LibraryBaseline.ReadLock(LockFile, Hash(LockFile))), Sha(LibraryBaseline.ReadLock(output, Text(result, "sha256"))));
            Assert.Throws<IOException>(() => ReleaseBuilder.Lock(repo.ManifestFile, work, new[] { "M1" }, Version, null, output));
        }
    }
}
