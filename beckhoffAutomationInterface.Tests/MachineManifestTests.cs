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
            foreach (string machine in m.AllMachines)
                Assert.True(File.Exists(Path.Combine(f.MachineFolder(m.Root, machine), "variants.xml")), machine);
        }

        [Theory]
        [InlineData(null, ", \"extra\": 1", "unknown key 'extra'")]
        [InlineData("{ \"kind\": \"native\", \"plc\": \"X\" }", null, "'native' is not supported yet")]
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
