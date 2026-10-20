using System.Text;
using System.Xml.Linq;
using BeckhoffAutomationInterface.Machines;
using Xunit;

namespace BeckhoffAutomationInterface.Tests
{
    /// <summary>A native family in 'machine build|test' (port of Tools/build_nfpro.ps1 and
    /// Tests/Integration/NFPro/run.ps1): source assembly, stamp, kept-project rules, safety
    /// inventory, link result, startup checks and the validation record. No TwinCAT is needed.</summary>
    public class NativeMachineTests
    {
        static readonly MachineSources.CommandRunner NoCommand = (c, w) => throw new InvalidOperationException("no command expected");

        const string Config =
            "<NativeConfiguration schemaVersion=\"1\" PlcPrefix=\"TIPC^Legacy^Legacy Instance\"><DataTypes /><ImageDatas /><Io />" +
            "<Motion><NC><Axis Id=\"1\" AxisType=\"1\" /><Axis Id=\"2\" AxisType=\"1\" /></NC></Motion><Safety />" +
            "<Mappings><OwnerA Name=\"TIPC^Legacy^Legacy Instance\"><OwnerB Name=\"TIID^D^T\"><Link VarA=\"PlcTask Inputs^GVL.b\" VarB=\"In\" />" +
            "<Link VarA=\"PlcTask Inputs^GVL.c\" VarB=\"In 2\" /></OwnerB></OwnerA></Mappings></NativeConfiguration>";

        /// <summary>The st fixture plus a native family 'Nat' with one machine 'Nat'.</summary>
        static MachineRepoFixture Repo()
        {
            var repo = new MachineRepoFixture();
            string m = MachineRepoFixture.Manifest(repo.Work);
            string nat = "{ \"kind\": \"native\", \"plc\": \"Nat\", \"platform\": \"TwinCAT OS (x64)\", \"sources\": [\"Lib\", \"App/Nat\"], " +
                         "\"shared\": \"Machines/Nat\", \"buildInfo\": \"App/Nat/Version/GVL_BuildInfo.st\", \"tests\": \"Tests/Integration/Nat\", " +
                         "\"machines\": [\"Nat\"], \"native\": { \"configuration\": \"Machines/Nat/native/configuration.xml\", " +
                         "\"safety\": \"Safety/Nat/S\", \"runtimeLicenses\": [\"TC3 NC PTP\", \"TC3 NC PTP Axis\"] } }";
            File.WriteAllText(repo.ManifestFile, m.Substring(0, m.LastIndexOf("} }", StringComparison.Ordinal)) + ", \"Nat\": " + nat + " } }");
            repo.Write("App/Nat/MAIN.st", "PROGRAM MAIN\nEND_PROGRAM\n");
            repo.Write("App/Nat/Version/GVL_BuildInfo.st", MachineRepoFixture.GvlBuildInfo);
            repo.Write("Machines/Nat/libraries.xml", "<Libraries />");
            repo.Write("Machines/Nat/events.xml", "<Events />");
            repo.Write("Machines/Nat/tasks.xml", "<Tasks><Task Name=\"PlcTask\" CycleTimeUs=\"10000\" Priority=\"20\"><Call>MAIN</Call></Task></Tasks>");
            repo.Write("Machines/Nat/native/configuration.xml", Config);
            repo.Write("Safety/Nat/S/S.splcproj", "safety é\r\n");
            repo.Write("Safety/Nat/S/sub/a.sal", "a");
            repo.Write("Tests/Integration/Nat/one.json", "{}");
            return repo;
        }

        [Fact]
        public void Assemble_NormalBuild_HasConfigurationLinksAndSafety()
        {
            var repo = Repo();
            MachineLayout l = repo.Layout("nat");
            Assert.Equal(Path.Combine(repo.Work, "Nat", "Nat"), l.Dir);
            Assert.Equal(repo.Path_("Machines/Nat"), l.MachineFolder);
            MachineSources.Assemble(l, NoCommand);
            string src = l.Src;
            Assert.Equal(File.ReadAllBytes(repo.Path_("Machines/Nat/native/configuration.xml")), File.ReadAllBytes(Path.Combine(src, "native-configuration.xml")));
            XDocument links = XDocument.Load(Path.Combine(src, "links.xml"));
            Assert.Equal("TIPC^Nat^Nat Instance", (string)links.Root.Element("OwnerA").Attribute("Name"));
            Assert.Equal(File.ReadAllBytes(repo.Path_("Safety/Nat/S/S.splcproj")), File.ReadAllBytes(Path.Combine(src, @"native-safety\S\S.splcproj")));
            Assert.True(File.Exists(Path.Combine(src, @"native-safety\S\sub\a.sal")));
            foreach (string f in new[] { "libraries.xml", "events.xml", "tasks.xml", @"App\Nat\MAIN.st", @"Lib\Math\F_Add.st" })
                Assert.True(File.Exists(Path.Combine(src, f)), f);
            foreach (string f in new[] { "variants.xml", "io-devices.xml", "safety.xml" })
                Assert.False(File.Exists(Path.Combine(src, f)), f);
            Assert.Equal(2, MachineChecks.DeclaredFileLinks(Path.Combine(src, "links.xml")));
        }

        [Fact]
        public void Assemble_SimulationBuild_IsTheSameProject_WithSimulatedNcAxes()
        {
            var repo = Repo();
            MachineLayout sim = repo.Layout("Nat", simulation: true), normal = repo.Layout("Nat");
            Assert.EndsWith("Nat-sim", sim.Dir);
            MachineSources.Assemble(sim, NoCommand);
            MachineSources.Assemble(normal, NoCommand);
            XDocument simulated = NativeConfiguration.Load(Path.Combine(sim.Src, "native-configuration.xml"));
            Assert.Equal(new[] { "1", "1" }, NativeConfiguration.Axes(simulated).Select(a => (string)a.Attribute("SimulationMode")));
            Assert.Equal(0, NativeConfiguration.SimulatedAxes(NativeConfiguration.Load(Path.Combine(normal.Src, "native-configuration.xml"))));
            // Only the two attributes differ; links and safety are the same.
            foreach (XElement axis in NativeConfiguration.Axes(simulated))
                axis.Attribute("SimulationMode").Remove();
            Assert.Equal(NativeConfiguration.Text(NativeConfiguration.Load(repo.Path_("Machines/Nat/native/configuration.xml"))), NativeConfiguration.Text(simulated));
            Assert.Equal(File.ReadAllBytes(Path.Combine(normal.Src, "links.xml")), File.ReadAllBytes(Path.Combine(sim.Src, "links.xml")));
            Assert.True(File.Exists(Path.Combine(sim.Src, @"native-safety\S\sub\a.sal")));
            Assert.NotEqual(MachineSources.Stamp(normal.Src), MachineSources.Stamp(sim.Src));
        }

        [Fact]
        public void Assemble_RefusesARetainedConfigurationWithSimulatedAxes()
        {
            var repo = Repo();
            repo.Write("Machines/Nat/native/configuration.xml", Config.Replace("<Axis Id=\"2\"", "<Axis SimulationMode=\"0\" Id=\"2\""));
            Assert.Contains("SimulationMode", Assert.Throws<InvalidOperationException>(() => MachineSources.Assemble(repo.Layout("Nat"), NoCommand)).Message);
        }

        [Theory]
        [InlineData(false, "", null)]
        [InlineData(false, " SimulationMode=\"1\"", "not a simulation build")]
        [InlineData(true, " SimulationMode=\"1\"", null)]
        [InlineData(true, "", "only 0 of 2")]
        [InlineData(true, " SimulationMode=\"true\"", null)] // as XAE saves it
        [InlineData(false, " SimulationMode=\"true\"", "not a simulation build")]
        public void AxesProblem_SimulatedAxesOnlyInTheSimulationProject(bool simulation, string attribute, string message)
        {
            string file = Path.Combine(TestProjects.NewTempDir("bai_axes"), "P.tsproj");
            File.WriteAllText(file, "<TcSmProject><Project><Motion><NC><Axis Id=\"1\"" + attribute + " /><Axis Id=\"2\"" + attribute + " /></NC></Motion></Project></TcSmProject>");
            string problem = NativeProject.AxesProblem(file, simulation);
            if (message == null)
                Assert.Null(problem);
            else
                Assert.Contains(message, problem);
        }

        const string License =
            "<TcLicenseInfo><LicenseInfo><ExpireTime>{0}</ExpireTime>" +
            "<License><Name>TC3 NC PTP Axis</Name></License><License><Name>TC3 NC PTP</Name></License><License><Name>TC3 PLC</Name></License>" +
            "</LicenseInfo></TcLicenseInfo>";

        [Fact]
        public void LicenseProblem_NamesTheMissingOrExpiredLicenseAndTheFolder()
        {
            string folder = TestProjects.NewTempDir("bai_license");
            string[] nc = { "TC3 NC PTP", "TC3 NC PTP Axis" };
            var now = new DateTime(2026, 10, 20);
            Assert.Contains("'TC3 NC PTP' is missing; 'TC3 NC PTP Axis' is missing. Folder: " + folder, NativeProject.LicenseProblem(nc, folder, now));
            File.WriteAllText(Path.Combine(folder, "Trial.tclrs"), string.Format(License, "2026-10-27T00:00:00"));
            Assert.Null(NativeProject.LicenseProblem(nc, folder, now));
            Assert.Contains("'TC3 NC PTP Axis' expired 2026-10-27 00:00 (Trial.tclrs)", NativeProject.LicenseProblem(nc, folder, new DateTime(2026, 10, 28)));
            File.WriteAllText(Path.Combine(folder, "Old.tclrs"), "<TcLicenseInfo><LicenseInfo><ExpireTime>2026-10-01T00:00:00</ExpireTime><License><Name>TC3 NC PTP</Name></License></LicenseInfo></TcLicenseInfo>");
            Assert.Null(NativeProject.LicenseProblem(nc, folder, now)); // the newest file of a license counts
        }

        [Fact]
        public void Stamp_ChangesWithTheConfigurationAndTheSafetyFiles()
        {
            var repo = Repo();
            MachineLayout l = repo.Layout("Nat");
            MachineSources.Assemble(l, NoCommand);
            string first = MachineSources.Stamp(l.Src);
            repo.Write("App/Nat/MAIN.st", "PROGRAM MAIN\nVAR\nEND_VAR\nEND_PROGRAM\n");
            MachineSources.Assemble(l, NoCommand);
            Assert.Equal(first, MachineSources.Stamp(l.Src));

            repo.Write("Machines/Nat/native/configuration.xml", Config.Replace("In 2", "In 3"));
            MachineSources.Assemble(l, NoCommand);
            string second = MachineSources.Stamp(l.Src);
            Assert.NotEqual(first, second);

            repo.Write("Safety/Nat/S/sub/a.sal", "b");
            MachineSources.Assemble(l, NoCommand);
            Assert.NotEqual(second, MachineSources.Stamp(l.Src));
        }

        [Fact]
        public void Layouts_NativeMachine_NeedsNoVariants()
        {
            var repo = Repo();
            MachineManifest m = repo.Load();
            Assert.Equal(new[] { "M1", "M2", "Nat" }, MachineCommands.Layouts(m, MachineCommands.Parse(new[] { "machine", "build" })).Select(x => x.Machine));
            MachineLayout test = Assert.Single(MachineCommands.Layouts(m, MachineCommands.Parse(new[] { "machine", "test", "Nat" })));
            Assert.True(test.Simulation);
            MachineChecks.AssertReleaseManifests(test.MachineFolder, native: true); // no variants.xml, no io-devices.xml
        }

        [Fact]
        public void SafetyInventory_FindsEveryChangedAddedOrMissingFile()
        {
            var repo = Repo();
            MachineLayout l = repo.Layout("Nat");
            List<NativeProject.FileEntry> before = NativeProject.Inventory(NativeProject.SafetySource(l));
            Assert.Equal(new[] { "S.splcproj", "sub/a.sal" }, before.Select(f => f.Path));
            Assert.Equal(NativeConfiguration.Sha256(repo.Path_("Safety/Nat/S/sub/a.sal")), before[1].Sha256);
            Assert.EndsWith(@"Nat\Nat\Nat\S", NativeProject.SafetyTarget(l));

            Assert.Contains("missing", NativeProject.SafetyProblem(l, before));
            NativeProject.CopySafety(l);
            Assert.Null(NativeProject.SafetyProblem(l, before));

            File.WriteAllText(Path.Combine(NativeProject.SafetyTarget(l), @"sub\a.sal"), "changed");
            Assert.Contains("not byte-equal to the source (changed sub/a.sal)", NativeProject.SafetyProblem(l, before));
            NativeProject.CopySafety(l);
            File.WriteAllText(Path.Combine(NativeProject.SafetyTarget(l), "new.sal"), "x");
            Assert.Contains("added or missing new.sal", NativeProject.SafetyProblem(l, before));
            NativeProject.CopySafety(l);
            repo.Write("Safety/Nat/S/sub/a.sal", "edited during the build");
            NativeProject.CopySafety(l);
            Assert.Contains("copied safety files", NativeProject.SafetyProblem(l, before));
        }

        [Fact]
        public void NativeIoWarnings_AreNoDrift()
        {
            Assert.False(MachineBuilder.IsDrift(true, new PipelineOutcome { IoWarnings = 1 }, ioFromManifest: false));
            Assert.True(MachineBuilder.IsDrift(true, new PipelineOutcome { StaleNames = 1 }, ioFromManifest: false));
        }

        [Theory]
        [InlineData(436, 436, "PASS (436/436)")]
        [InlineData(436, 0, "FAIL (0/436 linked, 0 unresolved)")]
        public void LinkResult_CountsTheLinksOfLinksXml(int declared, int fileLinks, string expected) =>
            Assert.Equal(expected, MachineChecks.LinkResult(declared, new PipelineOutcome { LinkSyncRan = true, FileLinks = fileLinks }));

        [Fact]
        public void IdentityExpectations_OnlyTheCompiledIdentity()
        {
            var checks = new List<Dictionary<string, object>> { new Dictionary<string, object> { ["symbol"] = "GVL_BuildInfo.sMachine", ["type"] = "STRING(31)", ["equals"] = "Nat" } };
            Assert.Equal("{\"variants\":{\"Nat\":[{\"symbol\":\"GVL_BuildInfo.sMachine\",\"type\":\"STRING(31)\",\"equals\":\"Nat\"}]}}",
                MachineChecks.IdentityExpectations("Nat", checks));
        }

        [Fact]
        public void ValidationRecord_HasTheHashesAndItsOwnSha256()
        {
            var repo = Repo();
            MachineLayout l = repo.Layout("Nat", simulation: true);
            Directory.CreateDirectory(l.Dir);
            string tmc = Path.Combine(l.Dir, "x.tmc"), bai = Path.Combine(l.Dir, "bai.exe");
            File.WriteAllText(tmc, "<TcModuleClass />");
            File.WriteAllText(bai, "exe");
            List<NativeProject.FileEntry> scenarios = NativeProject.Inventory(repo.Path_("Tests/Integration/Nat"));
            string file = MachineChecks.WriteValidationRecord(l.ValidationRecord, l, null, 1, scenarios, new List<Dictionary<string, object>>(), tmc, bai);
            string record = File.ReadAllText(file);
            Assert.StartsWith("{\"status\":\"Passed\"", record);
            Assert.Contains("\"completeScenarioSuite\":true", record);
            Assert.Contains("\"physicalDeployment\":false", record);
            Assert.Contains("\"compiledTmcSha256\":\"" + NativeConfiguration.Sha256(tmc).ToUpperInvariant() + "\"", record);
            Assert.Contains("\"path\":\"one.json\"", record);
            Assert.Equal(NativeConfiguration.Sha256(file).ToUpperInvariant(), File.ReadAllText(file + ".sha256").Trim());
        }
    }
}
