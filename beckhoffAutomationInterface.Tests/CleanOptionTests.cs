using BeckhoffAutomationInterface;
using Xunit;

namespace BeckhoffAutomationInterface.Tests
{
    /// <summary>--clean / .stconfig "clean" (tasks/2026-10-14-variants-closed-loop, Task 3).</summary>
    public class CleanOptionTests : System.IDisposable
    {
        readonly string _dir;

        public CleanOptionTests()
        {
            _dir = Path.Combine(Path.GetTempPath(), "CleanOptionTests_" + Guid.NewGuid());
            Directory.CreateDirectory(_dir);
        }

        public void Dispose() => Directory.Delete(_dir, recursive: true);

        [Fact]
        public void NoFlag_DoesNotClean()
        {
            Assert.False(RunOptions.Parse(new[] { "--source", _dir, "--build" }).Clean);
        }

        [Fact]
        public void Flag_Cleans()
        {
            Assert.True(RunOptions.Parse(new[] { "--source", _dir, "--build", "--clean" }).Clean);
        }

        [Fact]
        public void StConfig_Cleans_WhenNotOnCommandLine()
        {
            File.WriteAllText(Path.Combine(_dir, ".stconfig"), "clean=true\n");

            Assert.True(RunOptions.Parse(new[] { "--source", _dir, "--build" }).Clean);
        }
    }
}
