using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Web.Script.Serialization;

namespace BeckhoffAutomationInterface.Runtime
{
    /// <summary>
    /// One test scenario file (BeckhoffLib Documentation/PLC_RELEASE_PLAN.md, section 2.8):
    /// <code>
    /// { "name": "Purge precursor, change material",
    ///   "tags": ["sequence", "smoke"],
    ///   "requires": ["PCV_ETHERCAT"],          // feature groups (variants.xml), never a machine
    ///   "timeoutS": 300,                       // whole scenario; default 600
    ///   "steps": [
    ///     { "do": "send", "command": 200, "parameters": { "mode": 1 }, "expect": "accepted" },
    ///     { "do": "waitUntil", "symbol": "PRG_Sequence.fbOrchestrator.eActiveSequence", "type": "INT", "equals": 7, "timeoutS": 5 },
    ///     { "do": "check", "symbol": "PRG_Sequence.fbOrchestrator.bError", "equals": false },
    ///     { "do": "write", "symbol": "GVL_Simulation.bSafetyOk", "value": false },
    ///     { "do": "wait", "seconds": 2 } ] }
    /// </code>
    /// </summary>
    sealed class Scenario
    {
        public const double DefaultTimeoutS = 600;

        public string File { get; set; }         // path as found; used in messages
        public string Name { get; set; }
        public List<string> Tags { get; set; } = new List<string>();
        public List<string> Requires { get; set; } = new List<string>();
        public double TimeoutS { get; set; } = DefaultTimeoutS;
        public List<ScenarioStep> Steps { get; set; } = new List<ScenarioStep>();
        public List<ScenarioStep> CleanupSteps { get; set; } = new List<ScenarioStep>();

        // The intent for the user; required by 'acceptance' (purpose, estimateS), optional for 'test'.
        public string Purpose { get; set; }       // what the scenario proves, and why
        public List<string> Prepare { get; set; } = new List<string>();   // what the user does before it
        public double? EstimateS { get; set; }    // expected duration, for the run plan

        public SimulationFault SimulationFault { get; set; }   // null = none
    }

    /// <summary>Proof that a scenario can fail: in simulation the scenario runs healthy (must pass),
    /// then with these writes (must fail at FailsAtStep); the values are restored. Ignored on hardware.
    /// <code>"simulationFault": { "write": [ { "symbol": "GVL_Simulation.rInertFlowFraction", "type": "LREAL", "value": 0.5 } ], "failsAtStep": 4 }</code></summary>
    sealed class SimulationFault
    {
        public List<ScenarioStep> Writes { get; set; } = new List<ScenarioStep>();
        public int FailsAtStep { get; set; }
    }

    enum StepKind { Send, WaitUntil, Check, Write, Wait, Instruct, Confirm }

    /// <summary>One step. Only the fields of its kind are set.</summary>
    sealed class ScenarioStep
    {
        public const double DefaultSendTimeoutS = 10;

        public int Number { get; set; }          // 1-based, for messages
        public StepKind Kind { get; set; }

        // send
        public int Command { get; set; }
        public string CommandName { get; set; }  // the E_Command member as written; null = a number
        /// <summary>For messages: "SetValve (50)", or "50".</summary>
        public string CommandText => CommandName == null ? Command.ToString(CultureInfo.InvariantCulture) : string.Format("{0} ({1})", CommandName, Command);
        public Dictionary<string, object> Parameters { get; set; }   // null = none
        /// <summary>The parameters as written in the file (with names), for the run plan:
        /// "valveId E_ValveIdentifier.VALV_EX_PUMP, enabled true"; null = none.</summary>
        public string ParametersAsWritten { get; set; }

        /// <summary>"key value, ..." in the order of the file: a name as written, any other value as JSON.</summary>
        public static string ParametersText(Dictionary<string, object> parameters) =>
            string.Join(", ", parameters.Select(p => p.Key + " " + (ScenarioNames.IsName(p.Value) ? (string)p.Value : new JavaScriptSerializer().Serialize(p.Value))));

        public bool ExpectAccepted { get; set; } = true;

        // waitUntil, check, write
        public string Symbol { get; set; }        // as the PLC knows it: every enum name replaced by its number
        string _symbolAsWritten;
        /// <summary>The symbol as written in the file (with names), for messages.</summary>
        public string SymbolAsWritten { get => _symbolAsWritten ?? Symbol; set => _symbolAsWritten = value; }
        public string Type { get; set; }
        public Comparison Condition { get; set; } // waitUntil, check
        public object Value { get; set; }          // write: bool, double or string

        // send, waitUntil: null = the default (send) or the rest of the scenario time (waitUntil)
        public double? TimeoutS { get; set; }

        // wait
        public double Seconds { get; set; }

        // instruct (the user does something, then Enter), confirm (the user answers yes or no)
        public string Text { get; set; }

        public string KindName => Names[Kind];

        internal static readonly IReadOnlyDictionary<StepKind, string> Names = new Dictionary<StepKind, string>
        {
            [StepKind.Send] = "send",
            [StepKind.WaitUntil] = "waitUntil",
            [StepKind.Check] = "check",
            [StepKind.Write] = "write",
            [StepKind.Wait] = "wait",
            [StepKind.Instruct] = "instruct",
            [StepKind.Confirm] = "confirm",
        };
    }

    /// <summary>The condition of a waitUntil or check step. Values are bool, double or string
    /// (as PlcValue.Decode returns them). lessThan and greaterThan may be used together (a range);
    /// equals and notEquals stand alone.</summary>
    sealed class Comparison
    {
        public object EqualsValue { get; set; }
        public object NotEqualsValue { get; set; }
        public double? LessThan { get; set; }
        public double? GreaterThan { get; set; }
        public double? Tolerance { get; set; }
        // The enum member names as written (e.g. "E_Sequence.None"); null = a value
        public string EqualsName { get; set; }
        public string NotEqualsName { get; set; }
        public string LessThanName { get; set; }
        public string GreaterThanName { get; set; }

        const double DefaultTolerance = 1e-6;

        public bool Matches(object actual)
        {
            if (EqualsValue != null)
                return Same(actual, EqualsValue);
            if (NotEqualsValue != null)
                return !Same(actual, NotEqualsValue);
            if (!(actual is double d))
                return false;
            return (LessThan == null || d < LessThan.Value) && (GreaterThan == null || d > GreaterThan.Value);
        }

        bool Same(object actual, object expected)
        {
            if (actual is double a && expected is double e)
                return Math.Abs(a - e) <= (Tolerance ?? DefaultTolerance);
            return Equals(actual, expected);
        }

        public string Text
        {
            get
            {
                string tol = Tolerance != null ? " ± " + PlcValue.Format(Tolerance.Value) : "";
                if (EqualsValue != null)
                    return "= " + Show(EqualsValue, EqualsName) + tol;
                if (NotEqualsValue != null)
                    return "<> " + Show(NotEqualsValue, NotEqualsName) + tol;
                var parts = new List<string>();
                if (GreaterThan != null)
                    parts.Add("> " + Show(GreaterThan.Value, GreaterThanName));
                if (LessThan != null)
                    parts.Add("< " + Show(LessThan.Value, LessThanName));
                return string.Join(" and ", parts);
            }
        }

        /// <summary>"E_Sequence.None (0)" for a name, else the value.</summary>
        static string Show(object value, string name) => name == null ? Quote(value) : string.Format("{0} ({1})", name, Quote(value));

        internal static string Quote(object value) =>
            value is string s ? "'" + s + "'" : PlcValue.Format(value);
    }

    /// <summary>Selects the scenarios to run: by tags (any one of them) and by feature groups
    /// (all of a scenario's "requires" must be in the machine's groups).</summary>
    static class ScenarioFilter
    {
        /// <summary>Scenarios with at least one of the tags; all when no tag is given.</summary>
        public static List<Scenario> ByTags(IEnumerable<Scenario> scenarios, IReadOnlyCollection<string> tags)
        {
            if (tags == null || tags.Count == 0)
                return scenarios.ToList();
            var wanted = new HashSet<string>(tags, StringComparer.OrdinalIgnoreCase);
            return scenarios.Where(s => s.Tags.Any(wanted.Contains)).ToList();
        }

        /// <summary>Why the scenario cannot run on a machine with these feature groups, or null when
        /// it can. groups = null: the groups are not known, so a scenario with "requires" is skipped.</summary>
        public static string SkipReason(Scenario scenario, IReadOnlyCollection<string> groups)
        {
            if (scenario.Requires.Count == 0)
                return null;
            if (groups == null)
                return "requires " + string.Join(", ", scenario.Requires) + "; the feature groups are not known (use --groups or --variants)";
            var have = new HashSet<string>(groups, StringComparer.OrdinalIgnoreCase);
            List<string> missing = scenario.Requires.Where(r => !have.Contains(r)).ToList();
            return missing.Count == 0 ? null : "requires " + string.Join(", ", missing) + " (not a feature group of this machine)";
        }
    }

    static class ScenarioNumbers
    {
        public static string Seconds(double s) => s.ToString("0.0", CultureInfo.InvariantCulture) + " s";
    }
}
