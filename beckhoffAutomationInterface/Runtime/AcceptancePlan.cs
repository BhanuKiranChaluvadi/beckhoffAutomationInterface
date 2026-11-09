using System;
using System.Collections.Generic;

namespace BeckhoffAutomationInterface.Runtime
{
    /// <summary>The run plan that 'acceptance' shows before it starts: each scenario in run order with
    /// what it proves, how long it takes and what the user prepares; skipped ones with the reason.</summary>
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
            }
            var lines = new List<string> { string.Format("Acceptance plan: {0} scenario(s) to run, about {1}", count, Minutes(totalS)) };
            lines.AddRange(body);
            return lines;
        }

        static string Minutes(double seconds) => Math.Max(1, (int)Math.Ceiling(seconds / 60)) + " min";
    }
}
