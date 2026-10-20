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
        readonly string scratch = TestProjects.NewTempDir("bai_release");
        readonly MachineRepoFixture repo;
        string Tool => Path.Combine(scratch, "bai");
        string Esi => Path.Combine(scratch, "esi");
        string UserEsi => Path.Combine(scratch, "user-esi");
        string Components => Path.Combine(scratch, "components");
        string ToolchainZip => Path.Combine(scratch, "toolchain.zip");
        string LockFile => Path.Combine(scratch, "libraries.lock.json");
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
        };

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
                Write(l.Tsproj, "<TcSmProject TcVersion='" + (fault == "version" ? "3.1.9999.0" : Version) + "'/>");
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
                foreach (string file in new[] { "Port_851.app", "Port_851_boot.tizip", "Port_851.crc", "Port_851.autostart" }) Write(Path.Combine(boot, "Plc", file), "fixture");
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
            Assert.Equal(Hash(Path.Combine(bundle, "source.zip")).ToUpperInvariant(), record["sourceArchiveSha256"]);
            var project = First(record["projects"]);
            Assert.Equal("Vendor/Library/1.2.3.4/Library.compiled-library", First(project["libraryCache"])["path"]);
            Assert.Equal(Platform, project["targetPlatform"]);
            Assert.Equal(2, Array(record, "baiFiles").Length);
            Assert.Equal(new[] { "system/device.xml", "user/vendor.xml" }, Array(record, "installedEsiFiles").Select(f => (string)Object(f)["path"]));
            Assert.Equal(Hash(Path.Combine(Esi, "device.xml")), Hash(Path.Combine(bundle, @"esi\system\device.xml")));
            Assert.Equal(Hash(Path.Combine(UserEsi, "vendor.xml")), Hash(Path.Combine(bundle, @"esi\user\vendor.xml")));
            Assert.Equal(Hash(LockFile), Hash(Path.Combine(bundle, "libraries.lock.json")));
            Assert.Equal(Hash(LockFile).ToUpperInvariant(), record["libraryLockSha256"]);
            Assert.Equal(12, Array(project, "artifacts").Length);

            var package = First(record["packages"]);
            string zip = Path.Combine(bundle, "packages", "M1.zip");
            Assert.Equal(Text(package, "sha256") + "  M1.zip\n", File.ReadAllText(zip + ".sha256"));
            var manifest = PackageVerifier.Verify(zip, Text(package, "sha256"));
            Assert.Equal("ReleaseCandidate", manifest["kind"]);
            Assert.Equal(2, manifest["schemaVersion"]);
            Assert.Equal(10, Array(manifest, "files").Length);
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
                using (var reader = new StreamReader(archive.GetEntry("evidence.json").Open())) Assert.Equal("BuildPassed", Json(reader.ReadToEnd())["status"]);
            }

            // A package cannot include its own staging folder.
            var layout = new MachineLayout(MachineManifest.Load(Path.Combine(bundle, "source", MachineManifest.FileName)), "M1", false, Path.Combine(bundle, "build"));
            Assert.Throws<InvalidOperationException>(() => ReleaseBuilder.Package(layout, project, record, Path.Combine(layout.Dir, "M1", "nested-package")));
            // The cache must satisfy the manifest: a missing library, or a pinned version that differs.
            foreach (string requirement in new[] { "Name='Missing' Version='*'", "Name='Library' Version='9.0.0.0'" })
            {
                File.WriteAllText(Path.Combine(layout.Src, "libraries.xml"), "<Libraries><Library Company='Vendor' " + requirement + "/></Libraries>");
                Assert.Contains("does not satisfy manifest", Assert.Throws<InvalidDataException>(() => ReleaseBuilder.ProjectEvidence(layout, Version, Platform)).Message);
            }
            Assert.Throws<IOException>(() => Release(Request("success")));
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
            q = Request("test-adapter"); q.Machines = new List<string> { "M2" };
            Assert.Contains("test adapter", Assert.Throws<InvalidOperationException>(() => Release(q)).Message);
            repo.Write("Lib/untracked.st", "x");
            Assert.Contains("clean repository", Assert.Throws<InvalidOperationException>(() => Release(Request("dirty"))).Message);
            foreach (string name in new[] { "bad-hash", "bad-lock", "both", "test-adapter", "dirty" }) Assert.False(Directory.Exists(Bundle(name)), name);
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
        public void DefectRejectsTheCandidateAndKeepsTheReason(string defect, string reason)
        {
            Assert.ThrowsAny<Exception>(() => Release(Request("fail-" + defect), defect));
            var failed = Read(Path.Combine(Bundle("fail-" + defect), "release.json"));
            Assert.Equal("Failed", failed["status"]);
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
        public void WorkingTreeGivesADevelopmentPackage()
        {
            repo.Write("Lib/Math/F_Add.st", "FUNCTION F_Add : INT\nVAR_INPUT\n    a : INT;\nEND_VAR\nF_Add := a + 1; // uncommitted\nEND_FUNCTION\n");
            repo.Write("Lib/new-file.txt", "untracked");
            File.Delete(repo.Path_("Tools/fixture.txt"));
            repo.Write("Tools/other.txt", "keeps the area");
            var record = Release(Request("worktree", tag: null, workingTree: true));
            string bundle = Bundle("worktree");
            Assert.Equal("WorkingTree", record["sourceMode"]);
            Assert.True((bool)record["sourceDirty"]);
            Assert.Contains("uncommitted", File.ReadAllText(Path.Combine(bundle, @"source\Lib\Math\F_Add.st")));
            Assert.True(File.Exists(Path.Combine(bundle, @"source\Lib\new-file.txt")));
            Assert.False(File.Exists(Path.Combine(bundle, @"source\Tools\fixture.txt")));
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
            Release(Request("for-lock"));
            string output = Path.Combine(scratch, "captured.lock.json");
            var result = ReleaseBuilder.Lock(Path.Combine(Bundle("for-lock"), "source", MachineManifest.FileName), Path.Combine(Bundle("for-lock"), "build"), new[] { "M1" }, Version, null, output);
            string Sha(Dictionary<string, object> baseline) => Text(First(Object(Array(baseline, "machines")[0])["libraries"]), "sha256").ToUpperInvariant();
            Assert.Equal(Sha(LibraryBaseline.ReadLock(LockFile, Hash(LockFile))), Sha(LibraryBaseline.ReadLock(output, Text(result, "sha256"))));
            Assert.Throws<IOException>(() => ReleaseBuilder.Lock(Path.Combine(Bundle("for-lock"), "source", MachineManifest.FileName), Path.Combine(Bundle("for-lock"), "build"), new[] { "M1" }, Version, null, output));
        }
    }
}
