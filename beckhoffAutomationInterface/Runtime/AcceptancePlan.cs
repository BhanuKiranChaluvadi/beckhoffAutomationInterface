using System;
using System.Collections.Generic;

namespace BeckhoffAutomationInterface.Runtime
{
    /// <summary>The run plan that 'acceptance' shows before it starts: each scenario in run order with
    /// what it proves, how long it takes, what the user prepares and each step that acts on or reads
    /// the machine (names as written, so the user sees which valve or output a step moves);
    /// skipped ones with the reason.</summary>
    static class AcceptancePlan
    {
        public static List<string> Lines(IReadOnlyList<Scenario> scenarios, IReadOnlyCollection<string> groups)
        {
            var body = new List<string>();
            int count = 0;
            double totalS = 0;
            foreach (Scenario s in scenarios)
            {
                string skip = ScenarioFilter.SkipReason(s, groups);
                if (skip != null)
                {
                    body.Add(string.Format("  -  {0}: skipped, {1}", s.Name, skip));
                    continue;
                }
                totalS += s.EstimateS ?? 0;
                body.Add(string.Format("  {0}. {1} (about {2})", ++count, s.Name, Minutes(s.EstimateS ?? 0)));
                if (s.Purpose != null)
                    body.Add("     Why: " + s.Purpose);
                if (s.Prepare.Count > 0)
                    body.Add("     Prepare: " + string.Join("; ", s.Prepare));
                foreach (ScenarioStep step in s.Steps)
                {
                    string text = StepText(step);
                    if (text != null)
                        body.Add(string.Format("     Step {0}: {1}", step.Number, text));
                }
            }
            var lines = new List<string> { string.Format("Acceptance plan: {0} scenario(s) to run, about {1}", count, Minutes(totalS)) };
            lines.AddRange(body);
            return lines;
        }

        /// <summary>One line for a step that acts on or reads the machine; null for the others
        /// (wait, instruct, confirm).</summary>
        static string StepText(ScenarioStep step)
        {
            switch (step.Kind)
            {
                case StepKind.Send:
                    return "send " + step.CommandText
                        + (!string.IsNullOrEmpty(step.ParametersAsWritten) ? " " + step.ParametersAsWritten : "")
                        + (step.ExpectAccepted ? "" : ", expect rejected")
                        + Timeout(step);
                case StepKind.WaitUntil:
                case StepKind.Check:
                    return string.Format("{0} {1} {2}{3}", step.KindName, step.SymbolAsWritten, step.Condition.Text, Timeout(step));
                case StepKind.Write:
                    return string.Format("write {0} := {1}", step.SymbolAsWritten, Comparison.Quote(step.Value));
                default:
                    return null;
            }
        }

        static string Timeout(ScenarioStep step) => step.TimeoutS == null ? "" : " (" + PlcValue.Format(step.TimeoutS.Value) + " s)";

        static string Minutes(double seconds) => Math.Max(1, (int)Math.Ceiling(seconds / 60)) + " min";
    }
}
