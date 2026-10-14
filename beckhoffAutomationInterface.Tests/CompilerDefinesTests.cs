using BeckhoffAutomationInterface.Cli;
using BeckhoffAutomationInterface.Sync;
using Xunit;

namespace BeckhoffAutomationInterface.Tests
{
    /// <summary>--define (build/activate only): extra PLC compiler defines, e.g. SIMULATION for a
    /// test run on UmRT. The COM-free parts.</summary>
    public class CompilerDefinesTests
    {
        [Theory]
        [InlineData("<CompilerSettings><CompilerDefines>DOOR_SENSORS,MachineB,PCV_ETHERCAT,</CompilerDefines></CompilerSettings>", "DOOR_SENSORS,MachineB,PCV_ETHERCAT,")]
        [InlineData("<CompilerSettings><CompilerDefines /></CompilerSettings>", "")]
        [InlineData("<CompilerSettings></CompilerSettings>", "")]
        public void Read_TheCompilerDefinesOfTheProjectXml(string xml, string expected)
        {
            Assert.Equal(expected, CompilerDefines.Read(xml));
        }

        [Fact]
        public void Merge_KeepsTheVariantDefines_AddsOnlyNewOnes()
        {
            Assert.Equal("DOOR_SENSORS,SAS,PCV_ETHERCAT,SIMULATION",
                CompilerDefines.Merge("DOOR_SENSORS,SAS,PCV_ETHERCAT,", new[] { "SIMULATION", "SAS" }));
            Assert.Equal("SIMULATION", CompilerDefines.Merge("", new[] { "SIMULATION" }));
        }

        [Fact]
        public void Problem_RejectsANameThatIsNotADefine()
        {
            Assert.Null(CompilerDefines.Problem(new[] { "SIMULATION", "PCV_2" }));
            Assert.Contains("'2FAST'", CompilerDefines.Problem(new[] { "2FAST" }));
            Assert.Contains("'A-B'", CompilerDefines.Problem(new[] { "A-B" }));
        }

        [Fact]
        public void Build_TakesDefines_SyncDoesNot()   // sync saves the project; a test define must never be saved
        {
            string dir = TestProjects.NewTempDir("Defines");
            try
            {
                string tsproj = System.IO.Path.Combine(dir, "P", "P.tsproj");
                System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(tsproj));
                System.IO.File.WriteAllText(tsproj, "<TcSmProject/>");

                RunOptions o = CommandLine.Parse(new[] { "build", tsproj, "--define", "SIMULATION", "--define", "TRACE" }, cwd: dir);

                Assert.Equal(new[] { "SIMULATION", "TRACE" }, o.Defines);
                Assert.Throws<UsageException>(() => CommandLine.Parse(new[] { "build", tsproj, "--define", "2FAST" }, cwd: dir));
                string sln = TestProjects.Owned(dir, "Shark");
                var ex = Assert.Throws<UsageException>(() => CommandLine.Parse(
                    new[] { "sync", "all", TestProjects.Source(dir, "src"), "--project", sln, "--define", "SIMULATION", "--no-config" }, cwd: dir));
                Assert.Contains("unknown option --define", ex.Message);
            }
            finally
            {
                System.IO.Directory.Delete(dir, recursive: true);
            }
        }
    }
}
