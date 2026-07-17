using BeckhoffAutomationInterface;
using Xunit;

namespace BeckhoffAutomationInterface.Tests
{
    /// <summary>
    /// Path-to-.tsproj resolution for the new `build &lt;path&gt;` subcommand (see
    /// docs/ideas/cli-subcommand-redesign.md) — the piece that replaces the legacy
    /// --tsproj/--source/--dest/--name duality with one positional argument.
    /// ProjectLocator is COM-free, so this whole surface is unit-testable without VS/TwinCAT.
    /// </summary>
    public class ProjectLocatorTests : System.IDisposable
    {
        readonly string _dir;

        public ProjectLocatorTests()
        {
            _dir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "ProjectLocatorTests_" + System.Guid.NewGuid());
            Directory.CreateDirectory(_dir);
        }

        public void Dispose() => Directory.Delete(_dir, recursive: true);

        string WriteTsproj(string relativeDir, string fileName)
        {
            string dir = Path.Combine(_dir, relativeDir);
            Directory.CreateDirectory(dir);
            string path = Path.Combine(dir, fileName);
            File.WriteAllText(path, "<TcSmProject/>");
            return path;
        }

        [Fact]
        public void PathIsTsprojFile_ReturnsItDirectly()
        {
            string tsproj = WriteTsproj("PLC_NFL_SHARK", "PLC_NFL_SHARK.tsproj");

            Assert.Equal(tsproj, ProjectLocator.ResolveTsprojPath(tsproj));
        }

        [Fact]
        public void PathIsTsprojFile_ButMissing_Throws()
        {
            string missing = Path.Combine(_dir, "Nope.tsproj");

            Assert.Throws<FileNotFoundException>(() => ProjectLocator.ResolveTsprojPath(missing));
        }

        [Fact]
        public void PathIsFolder_WithExactlyOneTsprojNestedInside_FindsItRecursively()
        {
            // Mirrors PLC_NFL_SHARK_V2's real layout: repo root -> PLC_NFL_SHARK\PLC_NFL_SHARK.tsproj
            string tsproj = WriteTsproj("PLC_NFL_SHARK", "PLC_NFL_SHARK.tsproj");

            Assert.Equal(tsproj, ProjectLocator.ResolveTsprojPath(_dir));
        }

        [Fact]
        public void PathIsFolder_WithNoTsproj_Throws()
        {
            Assert.Throws<FileNotFoundException>(() => ProjectLocator.ResolveTsprojPath(_dir));
        }

        [Fact]
        public void PathIsFolder_WithMultipleTsproj_ThrowsWithBothPathsListed()
        {
            string first = WriteTsproj("First", "First.tsproj");
            string second = WriteTsproj("Second", "Second.tsproj");

            var ex = Assert.Throws<InvalidOperationException>(() => ProjectLocator.ResolveTsprojPath(_dir));
            Assert.Contains(first, ex.Message);
            Assert.Contains(second, ex.Message);
        }

        [Fact]
        public void PathDoesNotExistAtAll_Throws()
        {
            string missing = Path.Combine(_dir, "does-not-exist");

            Assert.Throws<DirectoryNotFoundException>(() => ProjectLocator.ResolveTsprojPath(missing));
        }

        [Fact]
        public void ResolvePlcName_UsesOverride_WhenGiven()
        {
            Assert.Equal("PLC_NFL_prj", ProjectLocator.ResolvePlcName(@"C:\x\PLC_NFL_SHARK.tsproj", "PLC_NFL_prj"));
        }

        [Fact]
        public void ResolvePlcName_DefaultsToTsprojBaseName_WhenNoOverrideGiven()
        {
            Assert.Equal("PLC_NFL_SHARK", ProjectLocator.ResolvePlcName(@"C:\x\PLC_NFL_SHARK.tsproj", null));
        }
    }
}
