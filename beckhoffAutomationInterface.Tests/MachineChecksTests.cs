using BeckhoffAutomationInterface.Machines;
using Xunit;

namespace BeckhoffAutomationInterface.Tests
{
    /// <summary>Release manifests, link result, compiled build identity, startup checks and the JUnit check.</summary>
    public class MachineChecksTests
    {
        [Fact]
        public void ReleaseManifests_RealAdaptersAndNoSimulation_Pass()
        {
            var repo = new MachineRepoFixture();
            MachineChecks.AssertReleaseManifests(repo.Path_("Machines/Fam/M1"));
        }

        [Theory]
        [InlineData("<Variants><Variant Name=\"M1\" /><Group Name=\"simulation\"><Member>M1</Member></Group></Variants>", null, "SIMULATION")]
        [InlineData(null, "02000000AA01", "test adapter address")]
        [InlineData(null, "000000000000", "test adapter address")]
        [InlineData(null, "FF-FF-FF-FF-FF-FF", "test adapter address")]
        [InlineData(null, "00010", "test adapter address")]
        public void ReleaseManifests_AreRefused(string variants, string mac, string message)
        {
            var repo = new MachineRepoFixture();
            if (variants != null)
                repo.Write("Machines/Fam/M1/variants.xml", variants);
            if (mac != null)
                repo.Write("Machines/Fam/Platform/io-devices.xml", "<IoTree><Adapter Name=\"A1\" Mac=\"" + mac + "\" Ports=\"8\" /></IoTree>");
            var ex = Assert.Throws<InvalidOperationException>(() => MachineChecks.AssertReleaseManifests(repo.Path_("Machines/Fam/M1")));
            Assert.Contains(message, ex.Message);
        }

        static PipelineOutcome Outcome(bool ran, int linked, int unresolved, params string[] disabled)
        {
            var o = new PipelineOutcome { LinkSyncRan = ran, LinksLinked = linked, LinksUnresolved = unresolved };
            o.DisabledMasters.AddRange(disabled);
            return o;
        }

        [Theory]
        [InlineData(0, false, 0, 0, false, "none")]
        [InlineData(3, true, 3, 0, false, "PASS (3/3)")]
        [InlineData(3, true, 2, 1, false, "FAIL (2/3 linked, 1 unresolved)")]
        [InlineData(3, false, 0, 0, false, "FAIL (no link result)")]
        [InlineData(3, true, 2, 1, true, "FAIL (master disabled)")]
        [InlineData(0, true, 0, 0, true, "FAIL (master disabled)")]
        public void LinkResult_EveryDeclaredLinkMustResolve(int declared, bool ran, int linked, int unresolved, bool disabled, string expected)
        {
            Assert.Equal(expected, MachineChecks.LinkResult(declared, disabled ? Outcome(ran, linked, unresolved, "Device 1") : Outcome(ran, linked, unresolved)));
        }

        [Fact]
        public void DeclaredLinks_CountsTheLinksOfTheResolvedTree()
        {
            var repo = new MachineRepoFixture();
            MachineLayout l = repo.Layout("M1");
            MachineSources.Assemble(l, (c, w) => 0);
            Assert.Equal(1, MachineChecks.DeclaredLinks(Path.Combine(l.Src, "io-devices.xml")));
            Assert.Equal(0, MachineChecks.DeclaredLinks(Path.Combine(l.Src, "none.xml")));
        }

        static string Tmc(string tag = "v1.0.0", string commit = "0123456789abcdef0123456789abcdef01234567", string dirty = "false",
                          string machine = "Google", string time = "2026-10-20T08:15:00Z", string commitType = "STRING(40)") =>
            "<TcModuleClass xmlns:xsi=\"http://www.w3.org/2001/XMLSchema-instance\"><DataTypes><DataType><Name>GVL_BuildInfo</Name>" +
            Sub("sGitTag", "STRING(31)", "String", tag) + Sub("sGitCommit", commitType, "String", commit) + Sub("bDirty", "BOOL", "Bool", dirty) +
            Sub("sMachine", "STRING(31)", "String", machine) + Sub("sBuildTime", "STRING(31)", "String", time) +
            "</DataType></DataTypes></TcModuleClass>";

        static string Sub(string name, string type, string kind, string value) =>
            "<SubItem><Name>" + name + "</Name><Type>" + type + "</Type><Default><" + kind + ">" + value + "</" + kind + "></Default></SubItem>";

        [Fact]
        public void CompiledBuildExpectations_ComeFromTheTmc()
        {
            List<Dictionary<string, object>> checks = MachineChecks.CompiledBuildExpectations(Tmc(), "Google");
            Assert.Equal(new[] { "GVL_BuildInfo.sGitTag", "GVL_BuildInfo.sGitCommit", "GVL_BuildInfo.bDirty", "GVL_BuildInfo.sMachine", "GVL_BuildInfo.sBuildTime" },
                checks.Select(c => (string)c["symbol"]));
            Assert.Equal(false, checks[2]["equals"]);
            Assert.Equal("v1.0.0", checks[0]["equals"]);
            Assert.Equal("STRING(40)", checks[1]["type"]);
        }

        [Theory]
        [InlineData("Shark", null, null, null, "different machine")]
        [InlineData("Google", "ABC", null, null, "full git SHA1")]
        [InlineData("Google", null, "maybe", null, "dirty flag")]
        [InlineData("Google", null, null, "yesterday", "UTC build time")]
        public void CompiledBuildExpectations_RefuseWrongValues(string machine, string commit, string dirty, string time, string message)
        {
            var ex = Assert.Throws<InvalidOperationException>(() => MachineChecks.CompiledBuildExpectations(
                Tmc(commit: commit ?? "0123456789abcdef0123456789abcdef01234567", dirty: dirty ?? "false", time: time ?? "2026-10-20T08:15:00Z"), machine));
            Assert.Contains(message, ex.Message);
        }

        [Fact]
        public void CompiledBuildExpectations_NeedTheDeclaredTypes()
        {
            var ex = Assert.Throws<InvalidOperationException>(() => MachineChecks.CompiledBuildExpectations(Tmc(commitType: "STRING(80)"), "Google"));
            Assert.Contains("sGitCommit", ex.Message);
            Assert.Throws<InvalidOperationException>(() => MachineChecks.CompiledBuildExpectations("<TcModuleClass />", "Google"));
        }

        [Fact]
        public void StartupExpectations_AppendTheBuildIdentity_AndLoadInVerify()
        {
            string expect = "{ \"variants\": { \"Google\": [ { \"symbol\": \"GVL_Simulation.bActive\", \"type\": \"BOOL\", \"equals\": true } ], \"SAS\": [] } }";
            string json = MachineChecks.StartupExpectations(expect, "Google", MachineChecks.CompiledBuildExpectations(Tmc(), "Google"));
            var parsed = Runtime.ExpectationFile.Parse(json);
            Assert.Equal(new[] { "Google" }, parsed.Keys);
            Assert.Equal(6, parsed["Google"].Count);
            Assert.Equal("GVL_Simulation.bActive", parsed["Google"][0].Symbol);
            Assert.Equal("GVL_BuildInfo.sBuildTime", parsed["Google"][5].Symbol);
        }

        [Theory]
        [InlineData("{ \"variants\": { \"SAS\": [ { \"symbol\": \"a\", \"type\": \"BOOL\", \"equals\": true } ] } }", "No startup checks for Google")]
        [InlineData("{ \"variants\": { \"Google\": [] } }", "No startup checks for Google")]
        [InlineData("{ \"variants\": { \"Google\": [ { \"symbol\": \"GVL_BuildInfo.sGitTag\", \"type\": \"STRING(31)\", \"equals\": \"x\" } ] } }", "must not hard-code build identity")]
        public void StartupExpectations_AreRefused(string expect, string message)
        {
            var ex = Assert.Throws<InvalidOperationException>(() => MachineChecks.StartupExpectations(expect, "Google", new List<Dictionary<string, object>>()));
            Assert.Contains(message, ex.Message);
        }

        static string JUnit(string body, string failures = "0") =>
            "<testsuites failures=\"" + failures + "\" errors=\"0\"><testsuite name=\"s\" failures=\"" + failures + "\" errors=\"0\">" + body + "</testsuite></testsuites>";

        [Theory]
        [InlineData("<testcase name=\"a\" /><testcase name=\"b\"><skipped /></testcase>", "0", null, 1)]
        [InlineData("<testcase name=\"a\"><failure message=\"x\" /></testcase>", "1", "failed scenarios", 0)]
        [InlineData("<testcase name=\"a\" />", "2", "failures=2", 0)]
        [InlineData("<testcase name=\"b\"><skipped /></testcase>", "0", "No scenario executed", 0)]
        public void ScenarioResults_AreChecked(string body, string failures, string message, int executed)
        {
            string file = Path.Combine(TestProjects.NewTempDir("bai_junit"), "r.xml");
            File.WriteAllText(file, JUnit(body, failures));
            if (message == null)
                Assert.Equal(executed, MachineChecks.AssertScenarioResults(file));
            else
                Assert.Contains(message, Assert.Throws<InvalidOperationException>(() => MachineChecks.AssertScenarioResults(file)).Message);
        }

        [Fact]
        public void ScenarioResults_MissingFile_Fails()
        {
            Assert.Contains("No JUnit", Assert.Throws<InvalidOperationException>(() => MachineChecks.AssertScenarioResults(@"C:\none\r.xml")).Message);
        }

        [Fact]
        public void JUnitFile_GetsTheMachineName_ForSeveralMachines()
        {
            Assert.Equal(@"C:\r\results.xml", MachineChecks.JUnitFor(@"C:\r\results.xml", "Google", several: false));
            Assert.Equal(@"C:\r\results-Google.xml", MachineChecks.JUnitFor(@"C:\r\results.xml", "Google", several: true));
        }

        [Fact]
        public void CompiledTmc_IsNextToTheOnePlcProject()
        {
            string dir = TestProjects.NewTempDir("bai_tmc");
            string tsproj = TestProjects.Adopted(dir, "Google");
            Assert.Throws<InvalidOperationException>(() => MachineChecks.CompiledTmc(tsproj));
            string plc = Path.Combine(Path.GetDirectoryName(tsproj), "NFLite");
            Directory.CreateDirectory(plc);
            File.WriteAllText(Path.Combine(plc, "NFLite.plcproj"), "");
            Assert.Equal(Path.Combine(plc, "NFLite.tmc"), MachineChecks.CompiledTmc(tsproj));
        }

        [Fact]
        public void MissingEsi_ListsFilesInNoEsiFolder()
        {
            var repo = new MachineRepoFixture();
            repo.Write("Machines/Fam/esi/A.xml", "<a/>");
            repo.Write("Machines/Fam/esi/B.xml", "<b/>");
            string installed = TestProjects.NewTempDir("bai_esi");
            File.WriteAllText(Path.Combine(installed, "A.xml"), "<a/>");
            Assert.Equal(new[] { "B.xml" }, MachineChecks.MissingEsi(repo.Path_("Machines/Fam"), new[] { installed, @"C:\none" }));
        }
    }
}
