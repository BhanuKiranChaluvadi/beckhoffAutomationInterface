namespace BeckhoffAutomationInterface.Runtime
{
    enum ScenarioFailureKind { Expectation, CommandAnswer, Deadline, Transport, InvalidOperation, Operator, Preparation, Cleanup, FaultProof }

    /// <summary>Execution evidence used by proof decisions before a human-readable message is rendered.</summary>
    sealed class ScenarioFailure
    {
        public ScenarioFailureKind Kind { get; }
        public string Message { get; }
        public int? Step { get; }
        public bool ProvesFault => Kind == ScenarioFailureKind.Expectation || Kind == ScenarioFailureKind.CommandAnswer;

        public ScenarioFailure(ScenarioFailureKind kind, string message, int? step = null)
        {
            Kind = kind;
            Message = message;
            Step = step;
        }

        public ScenarioFailure AtStep(ScenarioStep step) => new ScenarioFailure(Kind,
            string.Format("step {0} ({1}) {2}", step.Number, step.KindName, Message), step.Number);
        public ScenarioFailure WithMessage(string message) => new ScenarioFailure(Kind, message, Step);
    }
}
