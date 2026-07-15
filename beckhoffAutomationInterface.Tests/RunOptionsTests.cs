using BeckhoffAutomationInterface;
using Xunit;

namespace BeckhoffAutomationInterface.Tests
{
    /// <summary>
    /// Stage-flag parsing (tasks/plan.md: composable stage-based CLI). RunOptions.Parse
    /// is COM-free, so the whole CLI surface is unit-testable. Every invocation needs at
    /// least one argument (zero args prints usage and exits the process).
    /// </summary>
    public class RunOptionsTests
    {
        static RunOptions Parse(params string[] args) => RunOptions.Parse(args);

        [Fact]
        public void NoStageFlags_SelectsAllStages()
        {
            RunOptions options = Parse("--source", ".");

            Assert.Equal(SyncStages.All, options.Stages);
            Assert.False(options.BuildOnly);
        }

        [Fact]
        public void SingleStageFlag_SelectsExactlyThatStage()
        {
            Assert.Equal(SyncStages.Code, Parse("--sync-code").Stages);
            Assert.Equal(SyncStages.Libraries, Parse("--sync-libs").Stages);
            Assert.Equal(SyncStages.Io, Parse("--sync-io").Stages);
            Assert.Equal(SyncStages.Events, Parse("--sync-events").Stages);
            Assert.Equal(SyncStages.Build, Parse("--build").Stages);
        }

        [Fact]
        public void StageFlags_Compose()
        {
            RunOptions options = Parse("--sync-io", "--sync-events");

            Assert.Equal(SyncStages.Io | SyncStages.Events, options.Stages);
        }

        [Fact]
        public void BuildOnlyAlias_MapsToBuildStageOnly()
        {
            RunOptions options = Parse("--build-only");

            Assert.Equal(SyncStages.Build, options.Stages);
            Assert.True(options.BuildOnly);
        }

        [Fact]
        public void BuildOnly_IsFalse_WhenBuildCombinedWithOtherStages()
        {
            Assert.False(Parse("--sync-code", "--build").BuildOnly);
        }

        [Fact]
        public void EventsOnlyAlias_MapsToCheckEvents()
        {
            Assert.True(Parse("--events-only").CheckEvents);
            Assert.True(Parse("--check-events").CheckEvents);
            Assert.False(Parse("--sync-events").CheckEvents);
        }

        [Fact]
        public void Init_ParsedOnlyWhenGiven()
        {
            Assert.True(Parse("--init").Init);
            Assert.False(Parse("--sync-code").Init);
        }

        [Fact]
        public void CheckLinks_ParsedOnlyWhenGiven()
        {
            Assert.True(Parse("--check-links").CheckLinks);
            Assert.False(Parse("--sync-code").CheckLinks);
        }

        [Fact]
        public void ExportLinks_ParsedOnlyWhenGiven()
        {
            Assert.True(Parse("--export-links").ExportLinks);
            Assert.False(Parse("--sync-code").ExportLinks);
        }

        [Fact]
        public void NoReverseFlags_MeansNotAReverseExport()
        {
            RunOptions options = Parse("--source", ".");

            Assert.Equal(ReverseExports.None, options.ReverseExports);
            Assert.False(options.IsReverseExport);
        }

        [Fact]
        public void SingleReverseFlag_SelectsExactlyThatArtifact()
        {
            Assert.Equal(ReverseExports.Code, Parse("--export-code").ReverseExports);
            Assert.Equal(ReverseExports.Libraries, Parse("--export-libs").ReverseExports);
            Assert.Equal(ReverseExports.Io, Parse("--export-io").ReverseExports);
            Assert.Equal(ReverseExports.Events, Parse("--export-events").ReverseExports);
        }

        [Fact]
        public void ReverseFlags_Compose()
        {
            RunOptions options = Parse("--export-code", "--export-libs");

            Assert.Equal(ReverseExports.Code | ReverseExports.Libraries, options.ReverseExports);
            Assert.True(options.IsReverseExport);
        }

        [Fact]
        public void ExportAll_SelectsEveryArtifact_AndTurnsOnLinks()
        {
            RunOptions options = Parse("--export-all");

            Assert.Equal(ReverseExports.All, options.ReverseExports);
            Assert.True(options.ReverseExports.HasFlag(ReverseExports.Code));
            Assert.True(options.ReverseExports.HasFlag(ReverseExports.Libraries));
            Assert.True(options.ReverseExports.HasFlag(ReverseExports.Io));
            Assert.True(options.ReverseExports.HasFlag(ReverseExports.Events));
            // --export-all also generates links.xml (the variable-links artifact).
            Assert.True(options.ExportLinks);
        }

        [Fact]
        public void Overwrite_ParsedOnlyWhenGiven()
        {
            Assert.True(Parse("--export-all", "--overwrite").Overwrite);
            Assert.False(Parse("--export-all").Overwrite);
        }
    }
}
