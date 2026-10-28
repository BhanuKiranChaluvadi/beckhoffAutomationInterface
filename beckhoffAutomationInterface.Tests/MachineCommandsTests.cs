using BeckhoffAutomationInterface.Machines;
using Xunit;

namespace BeckhoffAutomationInterface.Tests
{
    /// <summary>'machine build' / 'machine test': command line, exit codes, release refusal, lock,
    /// kept-project decisions. Nothing here starts TwinCAT.</summary>
    public class MachineCommandsTests
    {
        static MachineCommands.Request Parse(params string[] args) => MachineCommands.Parse(new[] { "machine" }.Concat(args).ToArray());

        [Fact]
        public void Build_TakesMachinesAndFlags()
        {
            MachineCommands.Request q = Parse("build", "Shark,SAS", "Google", "--fresh", "--simulation", "--parallel", "--platform", "TwinCAT OS (x64)", "--work", @"C:\w");
            Assert.Equal("build", q.Command);
            Assert.Equal(new[] { "Shark", "SAS", "Google" }, q.Machines);
            Assert.True(q.Fresh && q.Simulation && q.Parallel);
            Assert.False(q.Release);
            Assert.Equal("TwinCAT OS (x64)", q.Platform);
            Assert.Equal(@"C:\w", q.Work);
        }

        [Fact]
        public void Test_TakesTagsJUnitAndTimeout()
        {
            MachineCommands.Request q = Parse("test", "Google", "--tags", "smoke,heaters", "--junit", @"C:\r.xml", "--timeout-seconds", "90");
            Assert.Equal("test", q.Command);
            Assert.Equal("smoke,heaters", q.Tags);
            Assert.Equal(@"C:\r.xml", q.JUnit);
            Assert.Equal(90, q.TimeoutS);
            Assert.Empty(Parse("test").Machines); // all machines
        }

        [Theory]
        [InlineData(new string[0], "needs a mode")]
        [InlineData(new[] { "run" }, "needs a mode")]
        [InlineData(new[] { "build", "--release", "--simulation" }, "never has SIMULATION")]
        [InlineData(new[] { "build", "--tags", "x" }, "does not take --tags")]
        [InlineData(new[] { "build", "--junit", "x" }, "does not take --junit")]
        [InlineData(new[] { "test", "--release" }, "does not take --release")]
        [InlineData(new[] { "test", "--simulation" }, "does not take --simulation")]
        [InlineData(new[] { "test", "--parallel" }, "does not take --parallel")]
        [InlineData(new[] { "test", "--timeout-seconds", "0" }, "--timeout-seconds needs")]
        [InlineData(new[] { "build", "--platform", "x86" }, "--platform must be one of")]
        [InlineData(new[] { "build", "--work" }, "--work needs a value")]
        [InlineData(new[] { "build", "--clean" }, "unknown option --clean")]
        [InlineData(new[] { "build", "SAS", "sas" }, "more than once")]
        public void BadCommandLines_AreRefused(string[] args, string message)
        {
            var ex = Assert.Throws<ArgumentException>(() => Parse(args));
            Assert.Contains(message, ex.Message);
        }

        [Fact]
        public void Run_BadCommandLine_IsUsageExit2()
        {
            Assert.Equal(MachineCommands.Exit.Usage, MachineCommands.Run(new[] { "machine", "build", "--bad" }, null, @"C:\"));
            Assert.Equal(2, (int)MachineCommands.Exit.Usage);
        }

        [Fact]
        public void Run_UnknownMachineOrNoManifest_IsUsageExit2()
        {
            var repo = new MachineRepoFixture();
            Assert.Equal(MachineCommands.Exit.Usage, MachineCommands.Run(new[] { "machine", "build", "Nope" }, null, repo.Root));
            Assert.Equal(MachineCommands.Exit.Usage, MachineCommands.Run(new[] { "machine", "build", "M1" }, null, TestProjects.NewTempDir("bai_nomanifest")));
        }

        [Fact]
        public void Run_WorkInsideTheRepository_IsUsageExit2()
        {
            var repo = new MachineRepoFixture();
            Assert.Equal(MachineCommands.Exit.Usage, MachineCommands.Run(new[] { "machine", "build", "M1", "--work", Path.Combine(repo.Root, "work") }, null, repo.Root));
        }

        [Fact]
        public void Layouts_DefaultToAllMachines_AndTestNeedsATestFolder()
        {
            var repo = new MachineRepoFixture();
            MachineManifest m = repo.Load();
            List<MachineLayout> all = MachineCommands.Layouts(m, Parse("test"));
            Assert.Equal(new[] { "M1", "M2" }, all.Select(l => l.Machine));
            Assert.All(all, l => Assert.EndsWith("-sim", l.Dir));
            Assert.False(MachineCommands.Layouts(m, Parse("build", "M2"))[0].Dir.EndsWith("-sim"));
            Assert.EndsWith("-sim", MachineCommands.Layouts(m, Parse("build", "M2", "--simulation"))[0].Dir);

            Directory.Delete(repo.Path_("Tests/Integration/Fam"), recursive: true);
            Assert.Throws<ArgumentException>(() => MachineCommands.Layouts(m, Parse("test", "M1")));
        }

        static (int, List<string>) Git(string status, string tag, params string[] args) =>
            args[0] == "status" ? (0, status == null ? new List<string>() : new List<string> { status })
            : args[0] == "describe" ? (tag == null ? 128 : 0, tag == null ? new List<string>() : new List<string> { tag })
            : (0, new List<string>());

        [Fact]
        public void Release_NeedsCleanTreeTagAndRealManifests()
        {
            var repo = new MachineRepoFixture();
            MachineManifest m = repo.Load();
            var m1 = new[] { new MachineLayout(m, "M1", false) };
            Assert.Null(MachineCommands.ReleaseProblem(m.Root, m.BuildInputs, m1, (r, a) => Git(null, "v1.0.0", a), out string tag));
            Assert.Equal("v1.0.0", tag);

            Assert.Contains("uncommitted changes", MachineCommands.ReleaseProblem(m.Root, m.BuildInputs, m1, (r, a) => Git(" M App/x.st", "v1.0.0", a), out _));
            Assert.Contains("HEAD has no tag", MachineCommands.ReleaseProblem(m.Root, m.BuildInputs, m1, (r, a) => Git(null, null, a), out _));
            Assert.Contains("git status failed", MachineCommands.ReleaseProblem(m.Root, m.BuildInputs, m1, (r, a) => (128, new List<string>()), out _));
            // M2 uses a test adapter address (02:00:00:00:xx:xx).
            Assert.StartsWith("M2: ", MachineCommands.ReleaseProblem(m.Root, m.BuildInputs, new[] { new MachineLayout(m, "M2", false) }, (r, a) => Git(null, "v1", a), out _));
        }

        [Fact]
        public void ExitCode_IsZeroOnlyWhenEveryMachinePasses()
        {
            var pass = new MachineRow("A") { Parse = "PASS", Build = "PASS (retry)", Links = "PASS (3/3)" };
            var noLinks = new MachineRow("B") { Parse = "PASS", Build = "PASS", Links = "none" };
            var badLinks = new MachineRow("C") { Parse = "PASS", Build = "PASS", Links = "FAIL (master disabled)" };
            var parseFail = new MachineRow("D") { Parse = "FAIL" };
            Assert.Equal(MachineCommands.Exit.Passed, MachineCommands.ExitCode(new[] { pass, noLinks }, test: false));
            Assert.Equal(MachineCommands.Exit.Failed, MachineCommands.ExitCode(new[] { pass, badLinks }, test: false));
            Assert.Equal(MachineCommands.Exit.Failed, MachineCommands.ExitCode(new[] { parseFail }, test: false));
            Assert.Equal(MachineCommands.Exit.Failed, MachineCommands.ExitCode(new MachineRow[0], test: false));

            var tested = new MachineRow("E") { Parse = "PASS", Build = "PASS", Links = "none", Activate = "PASS", Verify = "PASS", Tests = "PASS (21 executed)" };
            Assert.Equal(MachineCommands.Exit.Passed, MachineCommands.ExitCode(new[] { tested }, test: true));
            Assert.Equal(MachineCommands.Exit.Failed, MachineCommands.ExitCode(new[] { pass }, test: true)); // built, not tested
            tested.Tests = "FAIL (JUnit)";
            Assert.Equal(MachineCommands.Exit.Failed, MachineCommands.ExitCode(new[] { tested }, test: true));
            Assert.Equal(1, (int)MachineCommands.Exit.Failed);
            Assert.Equal(3, (int)MachineCommands.Exit.Busy);
        }

        [Fact]
        public void Table_HasTheTestColumnsOnlyForTest()
        {
            var row = new MachineRow("Google") { Mode = "kept", Parse = "PASS", Build = "PASS", Links = "PASS (154/154)", Time = "03:10" };
            string build = MachineCommands.Table(new[] { row }, test: false);
            Assert.StartsWith("Machine  Mode  Parse  Build  Links           Time", build);
            Assert.DoesNotContain("Verify", build);
            Assert.Contains("Verify", MachineCommands.Table(new[] { row }, test: true));
        }

        [Fact]
        public void ResultRow_RoundTrips()
        {
            var row = new MachineRow("Shark") { Mode = "fresh (drift)", Parse = "PASS", Build = "PASS (retry)", Links = "none", Time = "09:59" };
            MachineRow back = MachineCommands.RowFromDictionary(MachineCommands.RowToDictionary(row), "Shark");
            Assert.Equal(MachineCommands.Table(new[] { row }, true), MachineCommands.Table(new[] { back }, true));
            Assert.Equal("FAIL (no result)", MachineCommands.RowFromDictionary(null, "Shark").Parse);
        }

        [Fact]
        public void Lock_AllowsOneRunOnly()
        {
            string file = Path.Combine(TestProjects.NewTempDir("bai_lock"), "sub", "umrt.lock");
            using (FileStream first = MachineCommands.TryLock(file))
            {
                Assert.NotNull(first);
                Assert.Null(MachineCommands.TryLock(file));
            }
            using (FileStream again = MachineCommands.TryLock(file))
                Assert.NotNull(again);
        }

        [Theory]
        [InlineData(null, false, false)]
        [InlineData("passed", false, true)]
        [InlineData("failed", false, true)]
        [InlineData("passed", true, false)]   // other build mode
        [InlineData("crashed", false, false)]
        [InlineData("running", false, false)] // the process ended during the build
        public void KeptProject_IsUsedOnlyAfterACompleteRunOfTheSameMode(string result, bool otherMode, bool usable)
        {
            MachineState state = result == null ? null : new MachineState { Stamp = "s", Simulation = otherMode, Result = result };
            Assert.Equal(usable, MachineState.Usable(state, simulation: false));
        }

        [Fact]
        public void State_RoundTrips()
        {
            string file = Path.Combine(TestProjects.NewTempDir("bai_state"), "bai-machine.json");
            new MachineState { Stamp = "abc", Simulation = true, Result = "passed" }.Write(file);
            MachineState back = MachineState.Read(file);
            Assert.Equal("abc", back.Stamp);
            Assert.True(back.Simulation);
            Assert.Equal("passed", back.Result);
            File.WriteAllText(file, "not json");
            Assert.Null(MachineState.Read(file));
        }

        [Theory]
        [InlineData(unchecked((int)0x800706BA), true)]  // RPC server unavailable
        [InlineData(unchecked((int)0x800706BE), true)]  // remote procedure call failed
        [InlineData(unchecked((int)0x80010108), true)]  // disconnected
        [InlineData(unchecked((int)0x80080005), true)]  // CO_E_SERVER_EXEC_FAILURE
        [InlineData(unchecked((int)0x8001010A), false)] // busy: retried in place, not a crash
        [InlineData(unchecked((int)0x80004005), false)]
        public void ShellCrash_IsRecognized(int hresult, bool crash)
        {
            Assert.Equal(crash, MachineBuilder.IsShellCrash(hresult));
        }

        [Fact]
        public void Drift_OnlyForAKeptProject()
        {
            Assert.True(MachineBuilder.IsDrift(true, new PipelineOutcome { IoWarnings = 1 }));
            Assert.True(MachineBuilder.IsDrift(true, new PipelineOutcome { StaleNames = 2 }));
            Assert.False(MachineBuilder.IsDrift(false, new PipelineOutcome { IoWarnings = 1 }));
            Assert.False(MachineBuilder.IsDrift(true, new PipelineOutcome()));
            Assert.False(MachineBuilder.IsDrift(true, null));
        }

        [Theory]
        [InlineData("Google,PCV_ETHERCAT,SIMULATION", true)]
        [InlineData("Google, simulation ,", true)]
        [InlineData("Google,SIMULATION_X", false)]
        [InlineData("", false)]
        [InlineData(null, false)]
        public void SimulationDefine_IsFoundInTheDefineList(string defines, bool found)
        {
            Assert.Equal(found, MachineBuilder.HasDefine(defines, "SIMULATION"));
        }

        [Fact]
        public void PipelineOutcome_BuildPassedNeedsABuild()
        {
            Assert.False(new PipelineOutcome().BuildPassed);
            Assert.True(new PipelineOutcome { Builds = 1 }.BuildPassed);
            Assert.False(new PipelineOutcome { Builds = 2, BuildFailures = 1 }.BuildPassed);
        }
    }
}
