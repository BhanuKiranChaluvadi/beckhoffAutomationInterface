using BeckhoffAutomationInterface;
using Xunit;

namespace BeckhoffAutomationInterface.Tests
{
    /// <summary>init/sync reject a PLC project name TwinCAT refuses, before the shell starts
    /// (tasks/2026-10-14-variants-closed-loop, Task 7b).</summary>
    public class PlcProjectNameTests
    {
        [Theory]
        [InlineData("VariantSample")]
        [InlineData("sample")]
        [InlineData("PLC_NFL_prj")]
        [InlineData("_x1")]
        public void ValidNames_Pass(string name)
        {
            Assert.Null(RunOptions.PlcProjectNameProblem(name));
        }

        [Theory]
        [InlineData("variant-sample")]
        [InlineData("1Shark")]
        [InlineData("my project")]
        [InlineData("")]
        [InlineData(null)]
        public void InvalidNames_AreRejectedWithAHint(string name)
        {
            string problem = RunOptions.PlcProjectNameProblem(name);

            Assert.NotNull(problem);
            Assert.Contains("--name", problem);
        }

        [Fact]
        public void NameDefaultsToTheSourceFolder_SoAHyphenatedFolderIsCaught()
        {
            string dir = Path.Combine(Path.GetTempPath(), "variant-sample-" + Guid.NewGuid().ToString("N").Substring(0, 6));
            Directory.CreateDirectory(dir);
            try
            {
                RunOptions o = Cli.CommandLine.Parse(new[] { "init", dir, "--into", Path.GetTempPath(), "--no-config" });

                Assert.NotNull(RunOptions.PlcProjectNameProblem(o.PlcProjectName));
                Assert.Null(RunOptions.PlcProjectNameProblem(
                    Cli.CommandLine.Parse(new[] { "init", dir, "--into", Path.GetTempPath(), "--name", "VariantSample", "--no-config" }).PlcProjectName));
            }
            finally
            {
                Directory.Delete(dir);
            }
        }
    }
}
