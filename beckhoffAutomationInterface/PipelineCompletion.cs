using System;

namespace BeckhoffAutomationInterface
{
    /// <summary>Orders sync persistence, diagnostic compilation, final checks, and successful continuation.</summary>
    sealed class PipelineCompletion
    {
        readonly PipelineOutcome _outcome;
        public PipelineCompletion(PipelineOutcome outcome) { _outcome = outcome; }

        public void Finish(Action build, Action validate, Action synced = null, Action continuation = null)
        {
            _outcome.CompleteSync();
            if (_outcome.SyncCompleted) RunStage(synced, PipelinePhase.Sync);
            RunStage(build, PipelinePhase.Build);
            RunStage(validate, PipelinePhase.Validation);
            _outcome.ValidationCompleted = true;
            if (_outcome.Succeeded) continuation?.Invoke();
        }

        void RunStage(Action action, PipelinePhase phase)
        {
            try { action?.Invoke(); }
            catch (Exception ex)
            {
                _outcome.Fail(phase, ex.Message);
                throw;
            }
        }
    }
}
