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
    }
}
