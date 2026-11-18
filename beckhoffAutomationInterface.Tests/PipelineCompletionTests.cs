using System;
using System.Collections.Generic;
using Xunit;

namespace BeckhoffAutomationInterface.Tests
{
    public class PipelineCompletionTests
    {
        [Theory]
        [InlineData("io")]
        [InlineData("variants")]
        [InlineData("links")]
        public void SyncFailureStillCompilesButNeverRecordsSyncedOrContinues(string stage)
        {
            var outcome = new PipelineOutcome();
            if (stage == "io") outcome.IoErrors = 1;
            else if (stage == "links") outcome.LinksUnresolved = 1;
            else outcome.Fail(PipelinePhase.Sync, "invalid variants");
            var calls = new List<string>();
            new PipelineCompletion(outcome).Finish(
                () => { calls.Add("build"); outcome.Builds++; },
                () => calls.Add("validate"), () => calls.Add("synced"), () => calls.Add("activate"));
            Assert.Equal(new[] { "build", "validate" }, calls);
            Assert.True(outcome.BuildPassed);
            Assert.False(outcome.SyncCompleted);
            Assert.False(outcome.Succeeded);
        }

        [Fact]
        public void FinalValidationPrecedesContinuationAndCanVetoIt()
        {
            var outcome = new PipelineOutcome();
            var calls = new List<string>();
            new PipelineCompletion(outcome).Finish(
                () => { calls.Add("build"); outcome.Builds++; },
                () => { calls.Add("safety"); outcome.Fail(PipelinePhase.Validation, "safety files changed"); },
                () => calls.Add("synced"), () => calls.Add("activate"));
            Assert.Equal(new[] { "synced", "build", "safety" }, calls);
            Assert.True(outcome.BuildPassed);
            Assert.False(outcome.Succeeded);
        }

        [Fact]
        public void CompileFailureKeepsCompletedSyncAndSuccessfulRunContinuesAfterValidation()
        {
            var failed = new PipelineOutcome();
            new PipelineCompletion(failed).Finish(() => { failed.Builds++; failed.BuildFailures++; }, () => { });
            Assert.True(failed.SyncCompleted);
            Assert.False(failed.Succeeded);
            var passed = new PipelineOutcome();
            var calls = new List<string>();
            new PipelineCompletion(passed).Finish(() => { passed.Builds++; calls.Add("build"); },
                () => calls.Add("validate"), () => calls.Add("synced"), () => calls.Add("continue"));
            Assert.Equal(new[] { "synced", "build", "validate", "continue" }, calls);
            Assert.True(passed.Succeeded);
        }

        [Fact]
        public void ValidationExceptionCannotLeaveAnOutcomeEligibleForContinuation()
        {
            var outcome = new PipelineOutcome();
            bool continued = false;
            Assert.Throws<InvalidOperationException>(() => new PipelineCompletion(outcome).Finish(
                () => outcome.Builds++, () => throw new InvalidOperationException("read failed"),
                continuation: () => continued = true));
            Assert.False(continued);
            Assert.False(outcome.Succeeded);
            Assert.True(outcome.ValidationFailed);
        }

        [Fact]
        public void MissingDeclaredLinkResultPreventsCompletedSyncAndContinuation()
        {
            var outcome = new PipelineOutcome { ExpectedLinks = 10 };
            bool continued = false;
            new PipelineCompletion(outcome).Finish(() => outcome.Builds++, () => { },
                continuation: () => continued = true);
            Assert.False(outcome.SyncCompleted);
            Assert.True(outcome.HasErrors);
            Assert.False(continued);
        }

        [Fact]
        public void BuildExceptionKeepsSyncButCannotPassAndDoesNotRunFinalContinuation()
        {
            var outcome = new PipelineOutcome();
            bool continued = false;
            Assert.Throws<InvalidOperationException>(() => new PipelineCompletion(outcome).Finish(
                () => throw new InvalidOperationException("compiler disconnected"), () => { },
                continuation: () => continued = true));
            Assert.True(outcome.SyncCompleted);
            Assert.True(outcome.HasErrors);
            Assert.False(continued);
        }

        [Fact]
        public void LateSyncFailureInvalidatesTheRecordedOutcome()
        {
            var outcome = new PipelineOutcome();
            new PipelineCompletion(outcome).Finish(null, null);
            Assert.True(outcome.SyncCompleted);
            outcome.Fail(PipelinePhase.Sync, "sync bookkeeping failed");
            Assert.False(outcome.SyncCompleted);
        }
    }
}
