using BeckhoffAutomationInterface;
using Xunit;

namespace BeckhoffAutomationInterface.Tests
{
    /// <summary>`build &lt;path&gt;` must not map PLC errors to .st files of the caller's
    /// current directory.</summary>
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
            string tsproj = TestProjects.Adopted(_dir, "P");

            RunOptions options = TestProjects.Parse(_dir, "build", tsproj, "--no-config");

            Assert.False(options.SourceFolderExplicit);
            Assert.False(SyncPipeline.MapsErrorsToSource(options));
        }

        [Fact]
        public void BuildOfAdoptedTsproj_WithSourceFromStConfig_Maps()
        {
            string tsproj = TestProjects.Adopted(_dir, "P");
            File.WriteAllText(Path.Combine(_dir, ".stconfig"), "source=" + _dir + "\n");

            RunOptions options = TestProjects.Parse(_dir, "build", tsproj);

            Assert.True(options.SourceFolderExplicit);
            Assert.True(SyncPipeline.MapsErrorsToSource(options));
        }

        [Fact]
        public void SyncOfAToolCreatedProject_Maps()
        {
            string sln = TestProjects.Owned(_dir, "Shark");
            string source = TestProjects.Source(_dir, "src");

            RunOptions options = TestProjects.Parse(_dir, "sync", "all", source, "--project", sln, "--no-config");

            Assert.True(SyncPipeline.MapsErrorsToSource(options));
        }
    }
}
