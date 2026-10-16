using System;
using System.Collections.Generic;

namespace BeckhoffAutomationInterface.Runtime
{
    enum ScenarioStatus { Passed, Failed, Skipped }

    sealed class ScenarioResult
    {
        public Scenario Scenario { get; set; }
        public ScenarioStatus Status { get; set; }
        public string Message { get; set; }        // failure: "step 3 (waitUntil) ..."; skip: the reason
        public double Seconds { get; set; }
        public List<string> Details { get; } = new List<string>();   // failure: sStatus, stLastError, ...
    }

    /// <summary>
    /// Runs scenario steps against a PLC over ADS. The reader, writer, sleep and clock are
    /// delegates, so the logic is testable without a runtime (same pattern as ExpectationChecker).
    /// A command goes in as JSON text into the command receiver's AdsStream, as the client sends
    /// it; the receiver answers by setting nLastCommandIndex (accepted) or
    /// nLastRejectedCommandIndex (rejected) to the command's index (Lib/Telemetry/FB_CommandReceiverBase.st).
    /// </summary>
    sealed class ScenarioRunner
    {
        public delegate int Writer(string symbol, byte[] data);

        public const string StreamSymbol = "PRG_Telemetry.fbCommandReceiver.AdsStream";                  // ARRAY[0..4095] OF BYTE
        public const string AcceptedSymbol = "PRG_Telemetry.fbCommandReceiver.nLastCommandIndex";          // DINT
        public const string RejectedSymbol = "PRG_Telemetry.fbCommandReceiver.nLastRejectedCommandIndex";  // DINT
        public const string ActiveSequenceSymbol = "PRG_Sequence.fbOrchestrator.eActiveSequence";          // E_Sequence (INT)
        public const string StatusSymbol = "PRG_Sequence.fbOrchestrator.sStatus";                          // STRING(255)
        public const string LastErrorSourceSymbol = "PRG_Sequence.fbOrchestrator.stLastError.sSource";     // STRING(80)
        public const string LastErrorMessageSymbol = "PRG_Sequence.fbOrchestrator.stLastError.sMessage";   // STRING(255)
        public const string SimulationResetSymbol = "GVL_Simulation.bReset";                               // BOOL, optional

        public const int ErrorAcknowledgeCommand = 9999;
        const int SequenceNone = 0, SequenceError = 99;   // E_Sequence.None, E_Sequence.Error
        public const int PollMs = 100;

        readonly ExpectationChecker.Reader _read;
        readonly Writer _write;
        readonly Action<int> _sleepMs;
        readonly Func<double> _nowS;
        int _nextIndex;

        /// <summary>Optional: a note on the PLC type of a symbol, for an ADS size error (for
        /// example " (the PLC symbol is DINT, 4 bytes)").</summary>
        public Func<string, string> PlcTypeNote { get; set; }

        public ScenarioRunner(ExpectationChecker.Reader read, Writer write, Action<int> sleepMs, Func<double> nowS, int firstIndex)
        {
            _read = read;
            _write = write;
            _sleepMs = sleepMs;
            _nowS = nowS;
            _nextIndex = Math.Max(1, firstIndex);
        }

        /// <summary>First command index of a run: from the clock, so that a new run does not use the
        /// index of an earlier run (the PLC keeps the last one). 1000 indexes per second; the cycle
        /// repeats after about 23 days and stays below DINT max.</summary>
        public static int IndexSeed(DateTime utcNow)
        {
            long seconds = (long)(utcNow - new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc)).TotalSeconds;
            return (int)(((seconds % 2000000) + 2000000) % 2000000 * 1000 + 1);
        }

        // ------------------------------------------------------------------ whole run

        /// <summary>Runs each scenario in turn. Before each one, Prepare; a failure does not stop the
        /// next scenario. groups = the machine's feature groups (null = not known).</summary>
        public List<ScenarioResult> RunAll(IEnumerable<Scenario> scenarios, IReadOnlyCollection<string> groups,
            double prepareTimeoutS, Action<ScenarioResult> onResult)
        {
            var results = new List<ScenarioResult>();
            foreach (Scenario s in scenarios)
            {
                ScenarioResult r;
                string skip = ScenarioFilter.SkipReason(s, groups);
                if (skip != null)
                    r = new ScenarioResult { Scenario = s, Status = ScenarioStatus.Skipped, Message = skip };
                else
                {
                    double start = _nowS();
                    string problem = Prepare(prepareTimeoutS);
                    r = problem == null
                        ? Run(s)
                        : new ScenarioResult { Scenario = s, Status = ScenarioStatus.Failed, Message = "before the scenario: " + problem };
                    r.Seconds = _nowS() - start;
                    if (r.Status == ScenarioStatus.Failed)
                        r.Details.AddRange(Diagnostics());
                }
                results.Add(r);
                onResult?.Invoke(r);
            }
            return results;
        }

        /// <summary>Puts the machine in its start state: no sequence runs (None, or stopped in Error),
        /// errors acknowledged (9999), the simulation reset (GVL_Simulation.bReset, when the program
        /// has it), and then no sequence at all. Returns null, or what went wrong.</summary>
        public string Prepare(double timeoutS)
        {
            string problem = WaitForSequence(new[] { SequenceNone, SequenceError }, timeoutS);
            if (problem != null)
                return problem;

            problem = Send(ErrorAcknowledgeCommand, null, _nowS() + ScenarioStep.DefaultSendTimeoutS, out bool accepted, out int index);
            if (problem != null)
                return "ErrorAcknowledge (9999): " + problem;
            if (!accepted)
                return string.Format("ErrorAcknowledge (9999, index {0}) was rejected", index);

            int err = _write(SimulationResetSymbol, new byte[] { 1 });
            if (err != 0 && err != AdsClient.ErrorSymbolNotFound)
                return "cannot write " + SimulationResetSymbol + ": " + AdsError(err, SimulationResetSymbol);

            return WaitForSequence(new[] { SequenceNone }, timeoutS);
        }

        string WaitForSequence(int[] wanted, double timeoutS)
        {
            double start = _nowS();
            while (true)
            {
                int err = _read(ActiveSequenceSymbol, 2, out byte[] data);
                if (err != 0)
                    return "cannot read " + ActiveSequenceSymbol + ": " + AdsError(err, ActiveSequenceSymbol);
                int value = BitConverter.ToInt16(data, 0);
                if (Array.IndexOf(wanted, value) >= 0)
                    return null;
                if (_nowS() - start >= timeoutS)
                    return string.Format("{0} = {1} after {2} (expected {3})", ActiveSequenceSymbol, value,
                        ScenarioNumbers.Seconds(timeoutS), string.Join(" or ", wanted));
                _sleepMs(PollMs);
            }
        }

        /// <summary>Lines that help to find the cause of a failure: what the orchestrator reports.</summary>
        public List<string> Diagnostics()
        {
            var lines = new List<string>
            {
                ActiveSequenceSymbol + " = " + ReadText(ActiveSequenceSymbol, "INT"),
                StatusSymbol + " = " + ReadText(StatusSymbol, "STRING(255)"),
            };
            string source = ReadText(LastErrorSourceSymbol, "STRING(80)");
            string message = ReadText(LastErrorMessageSymbol, "STRING(255)");
            lines.Add("PRG_Sequence.fbOrchestrator.stLastError = " + (source == "''" && message == "''" ? "(none)" : source + ": " + message));
            return lines;
        }

        string ReadText(string symbol, string type)
        {
            int err = _read(symbol, PlcValue.SizeOf(type), out byte[] data);
            return err != 0 ? "(" + AdsError(err, symbol) + ")" : Comparison.Quote(PlcValue.Decode(type, data));
        }

        // ------------------------------------------------------------------ one scenario

        /// <summary>Runs the steps in order and stops at the first failed step.</summary>
        public ScenarioResult Run(Scenario scenario)
        {
            double start = _nowS();
            double deadline = start + scenario.TimeoutS;
            var result = new ScenarioResult { Scenario = scenario, Status = ScenarioStatus.Passed };
            foreach (ScenarioStep step in scenario.Steps)
            {
                string failure = Execute(step, deadline, scenario.TimeoutS);
                if (failure != null)
                {
                    result.Status = ScenarioStatus.Failed;
                    result.Message = string.Format("step {0} ({1}) {2}", step.Number, step.KindName, failure);
                    break;
                }
            }
            result.Seconds = _nowS() - start;
            return result;
        }

        /// <summary>Null when the step passed, else what went wrong (symbol, expected, actual).</summary>
        string Execute(ScenarioStep step, double scenarioDeadline, double scenarioTimeoutS)
        {
            double now = _nowS();
            switch (step.Kind)
            {
                case StepKind.Send:
                {
                    double deadline = Math.Min(now + (step.TimeoutS ?? ScenarioStep.DefaultSendTimeoutS), scenarioDeadline);
                    string problem = Send(step.Command, step.Parameters, deadline, out bool accepted, out int index);
                    string what = string.Format("command {0} (index {1}): ", step.Command, index);
                    if (problem != null)
                        return what + problem;
                    if (accepted != step.ExpectAccepted)
                        return what + string.Format("expected {0}, actual {1}", Answer(step.ExpectAccepted), Answer(accepted));
                    return null;
                }

                case StepKind.WaitUntil:
                {
                    bool scenarioLimit = step.TimeoutS == null || now + step.TimeoutS.Value >= scenarioDeadline;
                    double deadline = scenarioLimit ? scenarioDeadline : now + step.TimeoutS.Value;
                    while (true)
                    {
                        string problem = ReadValue(step, out object actual);
                        if (problem != null)
                            return problem;
                        if (step.Condition.Matches(actual))
                            return null;
                        if (_nowS() >= deadline)
                            return string.Format("{0}: expected {1}, actual {2} (after {3}{4})", step.Symbol, step.Condition.Text,
                                Comparison.Quote(actual), ScenarioNumbers.Seconds(deadline - now),
                                scenarioLimit ? ", scenario timeout " + ScenarioNumbers.Seconds(scenarioTimeoutS) : "");
                        _sleepMs(PollMs);
                    }
                }

                case StepKind.Check:
                {
                    string problem = ReadValue(step, out object actual);
                    if (problem != null)
                        return problem;
                    return step.Condition.Matches(actual)
                        ? null
                        : string.Format("{0}: expected {1}, actual {2}", step.Symbol, step.Condition.Text, Comparison.Quote(actual));
                }

                case StepKind.Write:
                {
                    int err = _write(step.Symbol, PlcValue.Encode(step.Type, step.Value));
                    return err == 0 ? null
                        : string.Format("{0} := {1}: {2}", step.Symbol, Comparison.Quote(step.Value), AdsError(err, step.Symbol));
                }

                case StepKind.Wait:
                {
                    double left = scenarioDeadline - now;
                    if (step.Seconds > left)
                    {
                        _sleepMs((int)Math.Max(0, left * 1000));
                        return string.Format("{0}: scenario timeout {1}", ScenarioNumbers.Seconds(step.Seconds), ScenarioNumbers.Seconds(scenarioTimeoutS));
                    }
                    _sleepMs((int)(step.Seconds * 1000));
                    return null;
                }

                default:
                    return "unknown step";
            }
        }

        string ReadValue(ScenarioStep step, out object value)
        {
            value = null;
            int err = _read(step.Symbol, PlcValue.SizeOf(step.Type), out byte[] data);
            if (err != 0)
                return string.Format("{0}: expected {1}, actual {2}", step.Symbol, step.Condition.Text, AdsError(err, step.Symbol));
            value = PlcValue.Decode(step.Type, data);
            return null;
        }

        /// <summary>Sends one command and waits for the receiver's answer. Returns null when an answer
        /// came (accepted tells which), else what went wrong.</summary>
        internal string Send(int command, Dictionary<string, object> parameters, double deadline, out bool accepted, out int index)
        {
            accepted = false;
            index = 0;
            string problem = ReadIndexes(out int lastAccepted, out int lastRejected);
            if (problem != null)
                return problem;
            do
                index = NextIndex();
            while (index == lastAccepted || index == lastRejected);

            byte[] stream = CommandJson.ToStream(CommandJson.Build(command, index, parameters));
            int err = _write(StreamSymbol, stream);
            if (err != 0)
                return "cannot write " + StreamSymbol + ": " + AdsError(err, StreamSymbol);

            double start = _nowS();
            while (true)
            {
                problem = ReadIndexes(out lastAccepted, out lastRejected);
                if (problem != null)
                    return problem;
                if (lastAccepted == index)
                {
                    accepted = true;
                    return null;
                }
                if (lastRejected == index)
                    return null;
                if (_nowS() >= deadline)
                    return string.Format("no answer after {0}: neither nLastCommandIndex nor nLastRejectedCommandIndex became {1}" +
                        " (an unknown command id, or the command receiver does not run)", ScenarioNumbers.Seconds(deadline - start), index);
                _sleepMs(PollMs);
            }
        }

        string ReadIndexes(out int accepted, out int rejected)
        {
            rejected = 0;
            return ReadIndex(AcceptedSymbol, out accepted) ?? ReadIndex(RejectedSymbol, out rejected);
        }

        string ReadIndex(string symbol, out int value)
        {
            value = 0;
            int err = _read(symbol, 4, out byte[] data);
            if (err != 0)
                return "cannot read " + symbol + ": " + AdsError(err, symbol);
            value = BitConverter.ToInt32(data, 0);
            return null;
        }

        int NextIndex()
        {
            int index = _nextIndex;
            _nextIndex = _nextIndex == int.MaxValue ? 1 : _nextIndex + 1;
            return index;
        }

        static string Answer(bool accepted) => accepted ? "accepted" : "rejected";

        string AdsError(int err, string symbol)
        {
            switch (err)
            {
                case AdsClient.ErrorSymbolNotFound:
                    return "not in the program (ADS error 1808)";
                case AdsClient.ErrorInvalidSize:
                    return "the \"type\" does not match the PLC symbol (ADS error 1797)" + (PlcTypeNote?.Invoke(symbol) ?? "");
                default:
                    return "ADS error " + err;
            }
        }
    }

    /// <summary>Wraps an ADS reader and writer: before the first access to a symbol, it reads the
    /// symbol's size from the PLC. A size that does not match the scenario's "type" gives
    /// ErrorInvalidSize, so a wrong type never reads part of a value or writes part of a symbol.</summary>
    sealed class SymbolSizeGuard
    {
        public delegate int InfoReader(string symbol, out int size, out string typeName);

        readonly InfoReader _info;
        readonly Dictionary<string, KeyValuePair<int, string>> _known = new Dictionary<string, KeyValuePair<int, string>>(StringComparer.OrdinalIgnoreCase);

        public SymbolSizeGuard(InfoReader info) { _info = info; }

        public int Check(string symbol, int size)
        {
            if (!_known.TryGetValue(symbol, out KeyValuePair<int, string> entry))
            {
                int err = _info(symbol, out int plcSize, out string typeName);
                if (err != 0)
                    return err;
                _known[symbol] = entry = new KeyValuePair<int, string>(plcSize, typeName);
            }
            return entry.Key == size ? 0 : AdsClient.ErrorInvalidSize;
        }

        public string Note(string symbol) =>
            _known.TryGetValue(symbol, out KeyValuePair<int, string> e)
                ? string.Format(" (the PLC symbol is {0}, {1} bytes)", e.Value, e.Key)
                : null;

        public ExpectationChecker.Reader Reader(ExpectationChecker.Reader read) =>
            (string symbol, int size, out byte[] data) =>
            {
                data = null;
                int err = Check(symbol, size);
                return err != 0 ? err : read(symbol, size, out data);
            };

        public ScenarioRunner.Writer Writer(ScenarioRunner.Writer write) =>
            (symbol, data) =>
            {
                int err = Check(symbol, data.Length);
                return err != 0 ? err : write(symbol, data);
            };
    }
}
