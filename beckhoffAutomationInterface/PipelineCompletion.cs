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
            if (_outcome.SyncCompleted) synced?.Invoke();
            build?.Invoke();
            validate?.Invoke();
            _outcome.ValidationCompleted = true;
            if (_outcome.Succeeded) continuation?.Invoke();
        }
    }
}
