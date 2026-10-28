using BeckhoffAutomationInterface.Machines;
using Xunit;

namespace BeckhoffAutomationInterface.Tests
{
    /// <summary>bai.machines.json: the machine families of a PLC repository.</summary>
    public class MachineManifestTests
    {
        const string Path0 = @"C:\repo\bai.machines.json";

        static string Json(string family = null, string top = null) =>
            "{ \"work\": \"C:/bai/work\", \"lock\": \"C:/bai/lock/umrt.lock\"" + (top ?? "") + ", \"families\": { \"NFLite\": " +
            (family ?? "{ \"plc\": \"NFLite\", \"sources\": [\"Lib\", \"App/NFLite\"], \"shared\": \"Machines/NFLite\", " +
                       "\"buildInfo\": \"App/NFLite/Version/GVL_BuildInfo.st\", \"tests\": \"Tests/Integration/NFLite\", \"machines\": [\"Shark\", \"Google\"] }") +
            " } }";

        [Fact]
        public void ValidManifest_IsRead_WithDefaults()
        {
            MachineManifest m = MachineManifest.Parse(Json(), Path0);
            Assert.Equal(@"C:\repo", m.Root);
            Assert.Equal(@"C:\bai\work", m.Work);
            Assert.Equal(@"C:\bai\lock\umrt.lock", m.Lock);
            Assert.Empty(m.PreCheck);
            Assert.Equal(new[] { "Lib", "App", "Machines", "Safety", "Tools" }, m.BuildInputs);
            MachineFamily f = Assert.Single(m.Families);
            Assert.Equal("st", f.Kind);
            Assert.Equal("TwinCAT RT (x64)", f.Platform);
            Assert.Equal(new[] { "Lib", "App/NFLite" }, f.Sources);
            Assert.Null(f.SimulationGas);
            Assert.Same(f, m.FamilyOf("google"));
            Assert.Equal("Google", m.Spelling("GOOGLE"));
            Assert.Null(m.FamilyOf("Unknown"));
            Assert.Equal(new[] { "Shark", "Google" }, m.AllMachines);
            Assert.Equal(@"C:\repo\Machines\NFLite\Google", f.MachineFolder(m.Root, "Google"));
        }

        [Fact]
        public void ThePlcRepositoryManifest_IsValid()
        {
            string repo = Environment.GetEnvironmentVariable("BAI_PLC_REPO")
                ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), @"workspace\Active\BeckhoffLib");
            string file = Path.Combine(repo, MachineManifest.FileName);
            if (!File.Exists(file))
                return;
            MachineManifest m = MachineManifest.Load(file);
            Assert.Equal(new[] { "python", "Tools/check_interlock_sources.py" }, m.PreCheck);
            MachineFamily f = m.FamilyOf("Google");
            Assert.Equal("NFLite", f.Plc);
            Assert.Equal(100000, f.SimulationGas.CycleTimeUs);
            foreach (string machine in m.AllMachines.Where(x => !m.FamilyOf(x).IsNative))
                Assert.True(File.Exists(Path.Combine(m.FamilyOf(machine).MachineFolder(m.Root, machine), "variants.xml")), machine);
            foreach (MachineFamily native in m.Families.Where(x => x.IsNative))
            {
                Assert.True(File.Exists(MachineSources.RepoPath(m.Root, native.Native.Configuration)), native.Name);
                Assert.True(Directory.Exists(MachineSources.RepoPath(m.Root, native.Native.Safety)), native.Name);
                Assert.True(File.Exists(Path.Combine(native.MachineFolder(m.Root, native.Machines[0]), "tasks.xml")), native.Name);
            }
        }

        const string NativeFamily =
            "{ \"kind\": \"native\", \"plc\": \"NFPro\", \"platform\": \"TwinCAT OS (x64)\", \"sources\": [\"Lib\", \"App/NFPro\"], " +
            "\"shared\": \"Machines/NFPro\", \"buildInfo\": \"App/NFPro/Version/GVL_BuildInfo.st\", \"tests\": \"Tests/Integration/NFPro\", " +
            "\"machines\": [\"NFPro\"], \"native\": { \"configuration\": \"Machines/NFPro/native/configuration.xml\", " +
            "\"safety\": \"Safety/NFPro/S\", \"runtimeLicenses\": [\"TC3 NC PTP\", \"TC3 NC PTP Axis\"] } }";

        [Fact]
        public void NativeFamily_IsRead_AndItsMachineFolderIsTheFamilyFolder()
        {
            MachineFamily f = Assert.Single(MachineManifest.Parse(Json(NativeFamily), Path0).Families);
            Assert.True(f.IsNative);
            Assert.Equal("TwinCAT OS (x64)", f.Platform);
            Assert.Equal("Machines/NFPro/native/configuration.xml", f.Native.Configuration);
            Assert.Equal("Safety/NFPro/S", f.Native.Safety);
            Assert.Equal(new[] { "TC3 NC PTP", "TC3 NC PTP Axis" }, f.Native.RuntimeLicenses);
            Assert.Equal(@"C:\repo\Machines\NFPro", f.MachineFolder(@"C:\repo", "NFPro"));
            Assert.Null(Assert.Single(MachineManifest.Parse(Json(), Path0).Families).Native);
        }

        [Theory]
        [InlineData("NO_NATIVE", "", "kind 'native' needs \"native\"")]
        [InlineData("[\"NFPro\"]", "[\"NFPro\", \"Other\"]", "exactly one machine")]
        [InlineData("\"runtimeLicenses\"", "\"stubs\"", "unknown key 'stubs'")]
        [InlineData("[\"TC3 NC PTP\", \"TC3 NC PTP Axis\"]", "\"TC3 NC PTP\"", "\"runtimeLicenses\" must be a list")]
        [InlineData("\"safety\": \"Safety/NFPro/S\", ", "", "\"safety\" is required")]
        [InlineData("\"Safety/NFPro/S\"", "\"../S\"", "relative path below the repository root")]
        [InlineData("\"kind\": \"native\", ", "", "\"native\" is only for kind 'native'")]
        public void BadNativeFamilies_AreRefused(string replace, string with, string message)
        {
            string family = replace == "NO_NATIVE"
                ? NativeFamily.Substring(0, NativeFamily.IndexOf(", \"native\"", StringComparison.Ordinal)) + " }"
                : NativeFamily.Replace(replace, with);
            var ex = Assert.Throws<FormatException>(() => MachineManifest.Parse(Json(family), Path0));
            Assert.Contains(message, ex.Message);
        }

        [Theory]
        [InlineData(null, ", \"extra\": 1", "unknown key 'extra'")]
        [InlineData("{ \"kind\": \"other\", \"plc\": \"X\" }", null, "unknown kind")]
        [InlineData("{ \"plc\": \"my-plc\", \"sources\": [\"Lib\"], \"shared\": \"M\", \"buildInfo\": \"Lib/B.st\", \"machines\": [\"A\"] }", null, "not a valid PLC project name")]
        [InlineData("{ \"plc\": \"P\", \"sources\": [\"Lib\"], \"shared\": \"M\", \"buildInfo\": \"App/B.st\", \"machines\": [\"A\"] }", null, "inside one of the \"sources\"")]
        [InlineData("{ \"plc\": \"P\", \"sources\": [\"../Lib\"], \"shared\": \"M\", \"buildInfo\": \"Lib/B.st\", \"machines\": [\"A\"] }", null, "relative path below the repository root")]
        [InlineData("{ \"plc\": \"P\", \"sources\": [\"Lib\"], \"shared\": \"M\", \"buildInfo\": \"Lib/B.st\", \"machines\": [\"9A\"] }", null, "machine name '9A'")]
        [InlineData("{ \"plc\": \"P\", \"sources\": [\"Lib\"], \"shared\": \"M\", \"buildInfo\": \"Lib/B.st\", \"machines\": [\"A\", \"a\"] }", null, "listed more than once")]
        [InlineData("{ \"plc\": \"P\", \"platform\": \"x\", \"sources\": [\"Lib\"], \"shared\": \"M\", \"buildInfo\": \"Lib/B.st\", \"machines\": [\"A\"] }", null, "\"platform\" must be one of")]
        [InlineData("{ \"plc\": \"P\", \"sources\": [\"Lib\"], \"shared\": \"M\", \"buildInfo\": \"Lib/B.st\", \"machines\": [\"A\"], \"simulationGas\": { \"provenance\": \"p.json\", \"program\": \"P\", \"cycleTimeUs\": 0, \"generator\": [\"x\"] } }", null, "cycleTimeUs")]
        public void BadManifests_AreRefused(string family, string top, string message)
        {
            var ex = Assert.Throws<FormatException>(() => MachineManifest.Parse(Json(family, top), Path0));
            Assert.Contains(message, ex.Message);
        }

        const string Sha = "a84bf257d9c84531e7268f1f0d882aade4d3098bba940d581844b14fa636d919";

        [Fact]
        public void ReleaseSettings_AreRead_AndOptional()
        {
            var r = MachineManifest.Parse(Json(top: ", \"release\": { \"twincat\": \"3.1.4026\", \"bai\": { \"version\": \"1.9.0\", \"sha256\": \"" + Sha + "\" } }"), Path0).Release;
            Assert.Equal("3.1.4026", r.TwinCat);
            Assert.Equal("1.9.0", r.BaiVersion);
            Assert.Equal(Sha, r.BaiSha256);
            Assert.Null(MachineManifest.Parse(Json(), Path0).Release);
            Assert.Null(MachineManifest.Parse(Json(top: ", \"release\": { \"twincat\": \"3.1.4026\" }"), Path0).Release.BaiSha256);
        }

        [Theory]
        [InlineData(", \"release\": { \"twincat\": \"4026\" }", "\"twincat\" must look like 3.1.4026")]
        [InlineData(", \"release\": { \"bai\": { \"version\": \"1.9.0\", \"sha256\": \"abc\" } }", "64 hex digits")]
        [InlineData(", \"release\": { \"bai\": { \"sha256\": \"" + Sha + "\" } }", "\"version\" is required")]
        [InlineData(", \"release\": { \"tag\": \"v1\" }", "unknown key 'tag'")]
        public void BadReleaseSettings_AreRefused(string top, string message)
        {
            Assert.Contains(message, Assert.Throws<FormatException>(() => MachineManifest.Parse(Json(top: top), Path0)).Message);
        }

        [Fact]
        public void MissingWork_IsRefused()
        {
            var ex = Assert.Throws<FormatException>(() => MachineManifest.Parse("{ \"lock\": \"x\", \"families\": {} }", Path0));
            Assert.Contains("\"work\" is required", ex.Message);
        }

        [Fact]
        public void NotJson_IsRefused()
        {
            Assert.Throws<FormatException>(() => MachineManifest.Parse("{ work: ", Path0));
        }

        [Fact]
        public void Find_LooksInParentFolders_OrUsesManifest()
        {
            var repo = new MachineRepoFixture();
            string deep = Path.Combine(repo.Root, "App", "Fam");
            Assert.Equal(repo.ManifestFile, MachineManifest.Find(null, deep));
            Assert.Equal(repo.ManifestFile, MachineManifest.Find(repo.Root, @"C:\"));
            Assert.Throws<ArgumentException>(() => MachineManifest.Find(Path.Combine(repo.Root, "none.json"), deep));
            Assert.Throws<ArgumentException>(() => MachineManifest.Find(null, TestProjects.NewTempDir("bai_nomanifest")));
        }
    }
}
