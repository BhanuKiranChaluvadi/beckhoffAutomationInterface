using System;

namespace BeckhoffAutomationInterface.Runtime
{
    /// <summary>One clock and deadline policy for a scenario and its phases. Recovery gets an independent budget.</summary>
    sealed class ScenarioBudget
    {
        public const double LimitMarginS = 30;
        readonly Func<double> _now;
        public double StartedAt { get; }
        public double DurationS { get; }
        public double Deadline { get; }
        public double Elapsed => _now() - StartedAt;
        public bool Expired => _now() >= Deadline;
        public bool Overrun => _now() > Deadline;

        public ScenarioBudget(Func<double> now, double durationS = double.PositiveInfinity,
            double ceiling = double.PositiveInfinity)
        {
            _now = now;
            StartedAt = now();
            DurationS = durationS;
            Deadline = Math.Min(StartedAt + durationS, ceiling);
        }

        public static ScenarioBudget ForScenario(Func<double> now, Scenario scenario, double prepareTimeoutS) =>
            new ScenarioBudget(now, scenario.TimeoutS + 2 * prepareTimeoutS + LimitMarginS);

        public ScenarioBudget Phase(double timeoutS) => new ScenarioBudget(_now, timeoutS, Deadline);
        public ScenarioBudget Recovery(double timeoutS) => new ScenarioBudget(_now, timeoutS);
        public double StepDeadline(double timeoutS) => Math.Min(_now() + timeoutS, Deadline);
    }
}
