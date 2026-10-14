using BeckhoffAutomationInterface;
using Xunit;

namespace BeckhoffAutomationInterface.Tests
{
    /// <summary>`build &lt;path&gt;` must not map PLC errors to .st files of the caller's
    /// current directory (tasks/2026-10-14-variants-closed-loop, Task 4b).</summary>
    public class ErrorMappingSourceTests : System.IDisposable
    {
        readonly string _dir;

        public ErrorMappingSourceTests()
        {
            _dir = Path.Combine(Path.GetTempPath(), "ErrorMappingSourceTests_" + Guid.NewGuid());
            Directory.CreateDirectory(_dir);
        }

        public void Dispose() => Directory.Delete(_dir, recursive: true);

        [Fact]
        public void BuildOfAdoptedTsproj_WithoutSource_DoesNotMap()
        {
            // What `build <path>` translates to: --tsproj, no --source.
            RunOptions options = RunOptions.Parse(
                new[] { "--tsproj", Path.Combine(_dir, "P.tsproj"), "--plc-name", "P", "--build", "--no-config" }, cwd: _dir);

            Assert.False(options.SourceFolderExplicit);
            Assert.False(SyncPipeline.MapsErrorsToSource(options));
        }

        [Fact]
        public void BuildOfAdoptedTsproj_WithExplicitSource_Maps()
        {
            RunOptions options = RunOptions.Parse(
                new[] { "--tsproj", Path.Combine(_dir, "P.tsproj"), "--source", _dir, "--build", "--no-config" });

            Assert.True(options.SourceFolderExplicit);
            Assert.True(SyncPipeline.MapsErrorsToSource(options));
        }

        [Fact]
        public void BuildOfAdoptedTsproj_WithSourceFromStConfig_Maps()
        {
            File.WriteAllText(Path.Combine(_dir, ".stconfig"), "source=" + _dir + "\n");

            RunOptions options = RunOptions.Parse(
                new[] { "--tsproj", Path.Combine(_dir, "P.tsproj"), "--build" }, cwd: _dir);

            Assert.True(SyncPipeline.MapsErrorsToSource(options));
        }

        [Fact]
        public void LegacyBuildFromInsideTheSourceFolder_StillMaps()
        {
            // `cd ST\Shark; tool --build` — the current directory IS the source folder.
            RunOptions options = RunOptions.Parse(new[] { "--build", "--no-config" }, cwd: _dir);

            Assert.True(SyncPipeline.MapsErrorsToSource(options));
        }
    }
}
