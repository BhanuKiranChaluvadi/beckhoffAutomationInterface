using System;
using System.Collections.Generic;

namespace BeckhoffAutomationInterface.Runtime
{
    enum ScenarioStatus { Passed, Failed, Skipped }

    /// <summary>Simulation: 'bai test' on a simulation build. Hardware: 'bai acceptance' on a real
    /// machine (no writes, no automatic acknowledge or reset, stop at the first failure).</summary>
    enum ScenarioMode { Simulation, Hardware }

    sealed class ScenarioResult
    {
        public Scenario Scenario { get; set; }
        public ScenarioStatus Status { get; set; }
        public string Message { get; set; }        // failure: "step 3 (waitUntil) ..."; skip: the reason
        public double Seconds { get; set; }
        public ScenarioFailure Failure { get; private set; }
        public void Fail(ScenarioFailure failure)
        {
            Failure = failure;
            Status = ScenarioStatus.Failed;
            Message = failure.Message;
        }
        public List<string> Details { get; } = new List<string>();   // failure: sStatus, stLastError, ...
        public List<Measurement> Measurements { get; } = new List<Measurement>();   // check/waitUntil, passed or failed
    }

    /// <summary>The last value a check or waitUntil step read, with its limit: the evidence of an
    /// acceptance report ("step 4: MFCT_[3].Measure = 99.6 (expected > 95 and < 105)").</summary>
    sealed class Measurement
    {
        public int Step { get; set; }
        public string Symbol { get; set; }
        public string Value { get; set; }      // as shown: 99.6, TRUE, 'text'
        public string Expected { get; set; }   // the condition: "= 7", "> 190"

        public override string ToString() => string.Format("step {0}: {1} = {2} (expected {3})", Step, Symbol, Value, Expected);
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
        public const string SimulationResetSymbol = "GVL_Simulation.bReset";                               // BOOL, required
        public const string SimulationActiveSymbol = "GVL_Simulation.bActive";                             // BOOL, required
        public const int CancelProcessCommand = 1;

        public const int ErrorAcknowledgeCommand = 9999;
        const int SequenceNone = 0, SequenceError = 99;   // E_Sequence.None, E_Sequence.Error
        /// <summary>Wait between two reads of the same symbol (send, waitUntil, reset). 20 ms: a
        /// step ends at most 20 ms after the PLC value changes (100 ms cost about 25 s per machine).</summary>
        public const int PollMs = 20;

        readonly ExpectationChecker.Reader _read;
        readonly Writer _write;
        readonly Action<int> _sleepMs;
        readonly Func<double> _nowS;
        ScenarioBudget _budget;
        int _nextIndex;
        RecoveryState _recovery;

        sealed class RecoveryState
        {
            public bool Required;
            public bool ResetPending;
            public bool CaptureWrites = true;
            public readonly Dictionary<string, byte[]> Originals = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
        }
        List<Measurement> _measurements;   // the running scenario's steps only (not prepare or cleanup)

        /// <summary>Optional: a note on the PLC type of a symbol, for an ADS size error (for
        /// example " (the PLC symbol is DINT, 4 bytes)").</summary>
        public Func<string, string> PlcTypeNote { get; set; }

        public ScenarioMode Mode { get; set; }

        /// <summary>Hardware: shows a prompt to the user and returns the typed line (null = no user).</summary>
        public Func<string, string> Ask { get; set; }

        /// <summary>Called before each scenario that runs (not for skipped ones): to show its intent.</summary>
        public Action<Scenario> OnStart { get; set; }

        /// <summary>On a real machine the PLC interlocks stay the only authority: a scenario changes the
        /// machine only through client commands, never by writing PLC symbols.</summary>
        const string WriteNotAllowed = "is not allowed on a real machine: acceptance scenarios only send, wait and check";

        /// <summary>Each write step (also in cleanup) of the scenarios, checked before anything is sent.</summary>
        public static List<string> HardwareProblems(IEnumerable<Scenario> scenarios)
        {
            var problems = new List<string>();
            foreach (Scenario s in scenarios)
            {
                if (s.Purpose == null)
                    problems.Add(s.File + ": no \"purpose\": an acceptance scenario says what it proves");
                if (s.EstimateS == null)
                    problems.Add(s.File + ": no \"estimateS\": an acceptance scenario says how long it takes");
                foreach (ScenarioStep step in s.Steps)
                    if (step.Kind == StepKind.Write)
                        problems.Add(string.Format("{0}: step {1} (write) {2}", s.File, step.Number, WriteNotAllowed));
                foreach (ScenarioStep step in s.CleanupSteps)
                    if (step.Kind == StepKind.Write)
                        problems.Add(string.Format("{0}: cleanup step {1} (write) {2}", s.File, step.Number, WriteNotAllowed));
            }
            return problems;
        }

        public ScenarioRunner(ExpectationChecker.Reader read, Writer write, Action<int> sleepMs, Func<double> nowS, int firstIndex)
        {
            // Each ADS call is timed: a starved runtime or PC makes calls take seconds (TcAdsDll
            // waits about 5 s per call), which stretches a scenario far beyond its timeoutS.
            _read = (string symbol, int size, out byte[] data) =>
            {
                double t = nowS();
                int err = read(symbol, size, out data);
                CountCall(nowS() - t);
                return err;
            };
            _write = (symbol, data) =>
            {
                double t = nowS();
                int err = write(symbol, data);
                CountCall(nowS() - t);
                return err;
            };
            _sleepMs = sleepMs;
            _nowS = nowS;
            _budget = new ScenarioBudget(nowS);
            _nextIndex = Math.Max(1, firstIndex);
        }

        /// <summary>An ADS call longer than this is slow (counted and reported with the result).</summary>
        public const double SlowCallS = 1.0;
        int _slowCalls;
        double _slowMaxS;

        void CountCall(double seconds)
        {
            if (seconds <= SlowCallS)
                return;
            _slowCalls++;
            _slowMaxS = Math.Max(_slowMaxS, seconds);
        }

        string SlowAds() => string.Format("slow ADS: {0} call{1} > {2} (max {3})", _slowCalls, _slowCalls == 1 ? "" : "s",
            SlowCallS.ToString("0.#", System.Globalization.CultureInfo.InvariantCulture) + " s", ScenarioNumbers.Seconds(_slowMaxS));

        /// <summary>First command index of a run: from the clock, so that a new run does not use the
        /// index of an earlier run (the PLC keeps the last one). 1000 indexes per second; the cycle
        /// repeats after about 23 days and stays below DINT max.</summary>
        public static int IndexSeed(DateTime utcNow)
        {
            long seconds = (long)(utcNow - new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc)).TotalSeconds;
            return (int)(((seconds % 2000000) + 2000000) % 2000000 * 1000 + 1);
        }

        // ------------------------------------------------------------------ whole run

        /// <summary>Runs each scenario in turn. Before each one, Prepare. In simulation a failure does not
        /// stop the next scenario; on hardware the run stops at the first failure (the rest is "not run").
        /// groups = the machine's feature groups (null = not known).</summary>
        public List<ScenarioResult> RunAll(IEnumerable<Scenario> scenarios, IReadOnlyCollection<string> groups,
            double prepareTimeoutS, Action<ScenarioResult> onResult)
        {
            var results = new List<ScenarioResult>();
            string cleanupFailure = null;
            bool stopped = false;
            foreach (Scenario s in scenarios)
            {
                ScenarioResult r;
                string skip = stopped
                    ? "not run: an earlier scenario failed (acceptance stops at the first failure)"
                    : ScenarioFilter.SkipReason(s, groups);
                if (skip != null)
                    r = new ScenarioResult { Scenario = s, Status = ScenarioStatus.Skipped, Message = skip };
                else
                {
                    OnStart?.Invoke(s);
                    double start = _nowS();
                    _slowCalls = 0;
                    _slowMaxS = 0;
                    r = RunOnce(s, prepareTimeoutS, null, ref cleanupFailure);
                    if (Mode == ScenarioMode.Simulation && s.SimulationFault != null && r.Status == ScenarioStatus.Passed)
                        ProveFault(s, r, prepareTimeoutS, ref cleanupFailure);
                    r.Seconds = _nowS() - start;
                    if (_slowCalls > 0)
                        r.Details.Add(SlowAds() + "; the runtime or the PC was overloaded");
                }
                stopped |= Mode == ScenarioMode.Hardware && r.Status == ScenarioStatus.Failed;
                results.Add(r);
                onResult?.Invoke(r);
            }
            return results;
        }

        /// <summary>Prepare, the steps, then cleanup (also after a failure). A cleanup failure blocks
        /// every later run: cleanupFailure stays set and the next RunOnce fails before it sends.</summary>
        ScenarioResult RunOnce(Scenario s, double prepareTimeoutS, IReadOnlyList<ScenarioStep> inject, ref string cleanupFailure)
        {
            ScenarioBudget previous = _budget;
            _budget = ScenarioBudget.ForScenario(_nowS, s, prepareTimeoutS);
            try
            {
                ScenarioResult r = RunLifecycle(s, prepareTimeoutS, inject, ref cleanupFailure);
                double took = _budget.Elapsed;
                if (_budget.Overrun && !(r.Message ?? "").StartsWith(TookPrefix, StringComparison.Ordinal))
                {
                    string prior = r.Status == ScenarioStatus.Failed ? r.Message : null;
                    r.Fail(new ScenarioFailure(ScenarioFailureKind.Deadline,
                        LimitMessage(took, _budget.DurationS) + (prior != null ? "; " + prior : "")));
                }
                return r;
            }
            finally { _budget = previous; }
        }

        const string TookPrefix = "scenario took ";

        string LimitMessage(double tookS, double limitS) =>
            string.Format("{0}{1}, more than its limit {2} ({3})", TookPrefix, ScenarioNumbers.Seconds(tookS), ScenarioNumbers.Seconds(limitS),
                _slowCalls > 0 ? SlowAds() : "no slow ADS call");

        ScenarioResult RunLifecycle(Scenario s, double prepareTimeoutS, IReadOnlyList<ScenarioStep> inject, ref string cleanupFailure)
        {
            var result = new ScenarioResult { Scenario = s, Status = ScenarioStatus.Passed };
            if (cleanupFailure != null)
            {
                result.Fail(new ScenarioFailure(ScenarioFailureKind.Cleanup, "before the scenario: " + cleanupFailure));
                CaptureDiagnostics(result);
                return result;
            }
            RecoveryState previous = _recovery;
            var recovery = new RecoveryState();
            _recovery = recovery;
            bool completed = false;
            try
            {
                string problem = Prepare(prepareTimeoutS);
                if (problem != null)
                    result.Fail(new ScenarioFailure(ScenarioFailureKind.Preparation, "before the scenario: " + problem));
                else
                {
                    recovery.Required = true;
                    result = RunPrepared(s, prepareTimeoutS, inject);
                }
                completed = true;
            }
            catch (Exception ex) when (Recoverable(ex))
            {
                result.Fail(new ScenarioFailure(ScenarioFailureKind.Transport, "scenario execution failed: " + ex.Message));
            }
            finally
            {
                try
                {
                    if (!completed || result.Status == ScenarioStatus.Failed) CaptureDiagnostics(result);
                }
                finally
                {
                    try
                    {
                        if (recovery.Required)
                            cleanupFailure = Cleanup(!completed || result.Status == ScenarioStatus.Failed, prepareTimeoutS, s.CleanupSteps);
                    }
                    finally { _recovery = previous; }
                }
            }
            if (cleanupFailure != null)
                result.Fail(new ScenarioFailure(ScenarioFailureKind.Cleanup,
                    (result.Message == null ? "" : result.Message + "; ") + cleanupFailure));
            return result;
        }

        ScenarioResult RunPrepared(Scenario s, double prepareTimeoutS, IReadOnlyList<ScenarioStep> inject)
        {
            ScenarioBudget injection = _budget.Phase(prepareTimeoutS);
            foreach (ScenarioStep write in inject ?? new ScenarioStep[0])
            {
                ScenarioFailure problem = Execute(write, injection);
                if (problem == null) continue;
                var failed = new ScenarioResult { Scenario = s };
                failed.Fail(problem.WithMessage("cannot inject the fault: " + problem.Message));
                return failed;
            }
            return RunSteps(s);
        }

        static bool Recoverable(Exception ex) => ex is System.IO.IOException ||
            ex is InvalidOperationException || ex is TimeoutException;

        void CaptureDiagnostics(ScenarioResult result)
        {
            try { result.Details.AddRange(Diagnostics()); }
            catch (Exception ex) when (Recoverable(ex)) { result.Details.Add("diagnostics unavailable: " + ex.Message); }
        }
        /// <summary>The scenario passed healthy; with its simulationFault written it must fail at the
        /// named step. Otherwise the scenario fails: a check that cannot fail proves nothing.</summary>
        void ProveFault(Scenario s, ScenarioResult healthy, double prepareTimeoutS, ref string cleanupFailure)
        {
            int n = s.SimulationFault.FailsAtStep;
            ScenarioResult faulted = RunOnce(s, prepareTimeoutS, s.SimulationFault.Writes, ref cleanupFailure);
            string verdict =
                faulted.Status == ScenarioStatus.Passed
                    ? "fault proof: the scenario passed with the fault injected, so its checks cannot see the fault"
                : faulted.Failure?.Step == n && faulted.Failure.ProvesFault && cleanupFailure == null
                    ? null
                    : "fault proof: with the fault it failed at " + faulted.Message + "; expected step " + n;
            if (verdict == null)
            {
                healthy.Details.Add(string.Format("fault proof: with the fault it fails at step {0} ({1}), as expected", n, s.Steps[n - 1].KindName));
                return;
            }
            healthy.Fail((faulted.Failure ?? new ScenarioFailure(ScenarioFailureKind.FaultProof, verdict)).WithMessage(verdict));
            healthy.Details.AddRange(faulted.Details);
        }

        /// <summary>Simulation: puts the machine in its start state: no sequence runs (None, or stopped in
        /// Error), errors acknowledged (9999), the active simulation reset and its acknowledgement
        /// awaited, and then no sequence at all. Hardware: changes nothing; the machine must already be
        /// idle with no error (an acknowledge could hide a real fault). Returns null, or what went wrong.</summary>
        public string Prepare(double timeoutS)
        {
            ScenarioBudget phase = _budget.Phase(timeoutS);
            string problem = RequireMode();
            if (problem != null)
                return problem;
            if (phase.Expired) return "prepare timeout after " + ScenarioNumbers.Seconds(timeoutS);
            if (Mode == ScenarioMode.Hardware)
            {
                problem = RequireIdle();
                return problem ?? (phase.Expired ? "prepare timeout after " + ScenarioNumbers.Seconds(timeoutS) : null);
            }
            // Check the reset symbol before sending even ErrorAcknowledge.
            int resetError = _read(SimulationResetSymbol, 1, out byte[] unused);
            if (resetError != 0)
                return "cannot read " + SimulationResetSymbol + ": " + AdsError(resetError, SimulationResetSymbol);
            problem = WaitForSequence(new[] { SequenceNone, SequenceError }, phase);
            if (problem != null)
                return problem;

            ScenarioFailure sendFailure = Send(ErrorAcknowledgeCommand, null, phase.Phase(ScenarioStep.DefaultSendTimeoutS), out bool accepted, out int index);
            if (sendFailure != null)
                return "ErrorAcknowledge (9999): " + sendFailure.Message;
            if (!accepted)
                return string.Format("ErrorAcknowledge (9999, index {0}) was rejected", index);

            if (phase.Expired) return "prepare timeout after " + ScenarioNumbers.Seconds(timeoutS);

            if (_recovery != null) { _recovery.Required = true; _recovery.ResetPending = true; }
            int err = _write(SimulationResetSymbol, new byte[] { 1 });
            if (err != 0)
                return "cannot write " + SimulationResetSymbol + ": " + AdsError(err, SimulationResetSymbol);

            while (true)
            {
                err = _read(SimulationResetSymbol, 1, out byte[] reset);
                if (err != 0)
                    return "cannot read " + SimulationResetSymbol + ": " + AdsError(err, SimulationResetSymbol);
                if (reset[0] == 0)
                {
                    if (_recovery != null) _recovery.ResetPending = false;
                    break;
                }
                if (phase.Expired)
                    return SimulationResetSymbol + " did not clear after " + ScenarioNumbers.Seconds(timeoutS);
                _sleepMs(PollMs);
            }
            return WaitForSequence(new[] { SequenceNone }, phase);
        }

        /// <summary>The one place that matches the build to the mode: simulation needs bActive TRUE,
        /// a real machine needs it FALSE.</summary>
        string RequireMode()
        {
            int err = _read(SimulationActiveSymbol, 1, out byte[] active);
            if (err != 0)
                return "cannot read " + SimulationActiveSymbol + ": " + AdsError(err, SimulationActiveSymbol);
            bool simulation = active[0] == 1;
            if (Mode == ScenarioMode.Hardware)
                return simulation ? "acceptance needs a real machine (" + SimulationActiveSymbol + " = FALSE); no commands sent" : null;
            return simulation ? null : "tests require " + SimulationActiveSymbol + " = TRUE; no commands sent";
        }

        /// <summary>Hardware: no waiting, because a running sequence may be someone's process.</summary>
        string RequireIdle()
        {
            int err = _read(ActiveSequenceSymbol, 2, out byte[] data);
            if (err != 0)
                return "cannot read " + ActiveSequenceSymbol + ": " + AdsError(err, ActiveSequenceSymbol);
            int value = BitConverter.ToInt16(data, 0);
            return value == SequenceNone ? null : string.Format("{0} = {1}; the machine must be idle (0) with no error. " +
                "Stop or acknowledge it on the machine first; no commands sent", ActiveSequenceSymbol, value);
        }

        string Cleanup(bool failed, double timeoutS, IReadOnlyList<ScenarioStep> steps)
        {
            ScenarioBudget phase = _budget.Recovery(timeoutS);
            string guard;
            try { guard = RequireMode(); }
            catch (Exception ex) when (Recoverable(ex)) { guard = ex.Message; }
            if (guard != null) return "cleanup stopped: " + guard + "; subsequent scenarios blocked";

            var problems = new List<string>();
            _recovery.CaptureWrites = false;
            AttemptRecovery(problems, "clear pending reset", ClearPendingReset);
            // Restore injected values before cancellation so an injected interlock cannot prevent it.
            foreach (var entry in _recovery.Originals)
                AttemptRecovery(problems, "restore " + entry.Key, () =>
                {
                    int err = _write(entry.Key, entry.Value);
                    return err == 0 ? null : "cannot restore " + entry.Key + ": " + AdsError(err, entry.Key);
                });

            bool cancel = true;
            AttemptRecovery(problems, "read active sequence", () =>
            {
                int err = _read(ActiveSequenceSymbol, 2, out byte[] sequence);
                if (err != 0) { cancel = true; return "cannot read " + ActiveSequenceSymbol + ": " + AdsError(err, ActiveSequenceSymbol); }
                int value = BitConverter.ToInt16(sequence, 0);
                cancel = failed || (value != SequenceNone && value != SequenceError);
                return null;
            });
            // Cancellation gets an acknowledgment window even when restoration exhausted the shared cleanup budget.
            if (cancel)
                AttemptRecovery(problems, "cancel process", () => CancelAndWait(_budget.Recovery(timeoutS)));

            foreach (ScenarioStep step in steps)
            {
                if (phase.Expired)
                {
                    problems.Add("explicit cleanup timeout after " + ScenarioNumbers.Seconds(timeoutS));
                    break;
                }
                AttemptRecovery(problems, "cleanup step " + step.Number, () =>
                {
                    ScenarioFailure problem = Execute(step, phase);
                    return problem == null ? null : "cleanup step " + step.Number + ": " + problem.Message;
                });
            }
            if (phase.Overrun) problems.Add("cleanup timeout after " + ScenarioNumbers.Seconds(timeoutS));
            return problems.Count == 0 ? null : "cleanup failed; subsequent scenarios blocked: " + string.Join("; ", problems);
        }

        string ClearPendingReset()
        {
            if (!_recovery.ResetPending) return null;
            int err = _read(SimulationResetSymbol, 1, out byte[] reset);
            if (err != 0) return "cannot read pending " + SimulationResetSymbol + ": " + AdsError(err, SimulationResetSymbol);
            if (reset[0] != 0)
            {
                err = _write(SimulationResetSymbol, new byte[] { 0 });
                if (err != 0) return "cannot clear " + SimulationResetSymbol + ": " + AdsError(err, SimulationResetSymbol);
            }
            _recovery.ResetPending = false;
            return null;
        }

        string CancelAndWait(ScenarioBudget budget)
        {
            ScenarioFailure problem = Send(CancelProcessCommand, null, budget, out bool accepted, out int index);
            if (problem != null) return problem.Message;
            return !accepted ? "CancelProcess was rejected" : WaitForSequence(new[] { SequenceNone, SequenceError }, budget);
        }

        static void AttemptRecovery(List<string> problems, string action, Func<string> attempt)
        {
            try
            {
                string problem = attempt();
                if (problem != null) problems.Add(problem);
            }
            catch (Exception ex) when (Recoverable(ex)) { problems.Add(action + ": " + ex.Message); }
        }
        string WaitForSequence(int[] wanted, ScenarioBudget phase)
        {
            while (true)
            {
                int err = _read(ActiveSequenceSymbol, 2, out byte[] data);
                if (err != 0)
                    return "cannot read " + ActiveSequenceSymbol + ": " + AdsError(err, ActiveSequenceSymbol);
                int value = BitConverter.ToInt16(data, 0);
                if (Array.IndexOf(wanted, value) >= 0 && !phase.Expired)
                    return null;
                if (phase.Expired)
                    return string.Format("{0} = {1} after {2} (expected {3})", ActiveSequenceSymbol, value,
                        ScenarioNumbers.Seconds(phase.DurationS), string.Join(" or ", wanted));
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

        /// <summary>Step execution only. Production callers use RunAll for preparation, recovery and reporting.</summary>
        internal ScenarioResult RunSteps(Scenario scenario)
        {
            double start = _nowS();
            ScenarioBudget phase = _budget.Phase(scenario.TimeoutS);
            var result = new ScenarioResult { Scenario = scenario, Status = ScenarioStatus.Passed };
            _measurements = result.Measurements;
            try
            {
                foreach (ScenarioStep step in scenario.Steps)
                {
                    // Slow ADS calls can stretch the steps past every deadline: stop at the scenario
                    // limit (RunOnce); the cleanup still runs.
                    if (_budget.Expired)
                    {
                        result.Fail(new ScenarioFailure(ScenarioFailureKind.Deadline,
                            LimitMessage(_budget.Elapsed, _budget.DurationS) + string.Format("; step {0} not run", step.Number)));
                        break;
                    }
                    ScenarioFailure failure = Execute(step, phase);
                    if (failure != null)
                    {
                        result.Fail(failure.AtStep(step));
                        break;
                    }
                }
            }
            finally { _measurements = null; }
            result.Seconds = _nowS() - start;
            return result;
        }

        /// <summary>Null when the step passed; otherwise typed execution evidence.</summary>
        ScenarioFailure Execute(ScenarioStep step, ScenarioBudget phase)
        {
            if (phase.Expired) return PhaseTimeout(phase);
            ScenarioFailure failure = ExecuteStep(step, phase);
            return failure ?? (phase.Overrun ? PhaseTimeout(phase) : null);
        }

        ScenarioFailure ExecuteStep(ScenarioStep step, ScenarioBudget phase)
        {
            switch (step.Kind)
            {
                case StepKind.Send: return SendStep(step, phase);
                case StepKind.WaitUntil: return WaitUntil(step, phase);
                case StepKind.Check: return Check(step, phase);
                case StepKind.Write: return WriteStep(step, phase);
                case StepKind.Wait: return WaitStep(step, phase);
                case StepKind.Instruct:
                case StepKind.Confirm: return Operator(step);
                default: return new ScenarioFailure(ScenarioFailureKind.InvalidOperation, "unknown step");
            }
        }

        static ScenarioFailure PhaseTimeout(ScenarioBudget phase) =>
            new ScenarioFailure(ScenarioFailureKind.Deadline, "scenario timeout " + ScenarioNumbers.Seconds(phase.DurationS));

        ScenarioFailure SendStep(ScenarioStep step, ScenarioBudget phase)
        {
            ScenarioFailure problem = Send(step.Command, step.Parameters,
                phase.Phase(step.TimeoutS ?? ScenarioStep.DefaultSendTimeoutS), out bool accepted, out int index);
            string what = string.Format("command {0} (index {1}): ", step.CommandText, index);
            if (problem != null) return problem.WithMessage(what + problem.Message);
            return accepted == step.ExpectAccepted ? null : new ScenarioFailure(ScenarioFailureKind.CommandAnswer,
                what + string.Format("expected {0}, actual {1}", Answer(step.ExpectAccepted), Answer(accepted)));
        }

        ScenarioFailure WaitUntil(ScenarioStep step, ScenarioBudget phase)
        {
            ScenarioBudget wait = phase.Phase(step.TimeoutS ?? phase.DurationS);
            bool scenarioLimit = wait.Deadline == phase.Deadline;
            object last = null;
            bool observed = false;
            // The window ended without a match: the last value read in time is the evidence.
            ScenarioFailure NotReached()
            {
                Measured(step, last);
                return new ScenarioFailure(ScenarioFailureKind.Expectation,
                    string.Format("{0}: expected {1}, actual {2} (after {3}{4})", step.SymbolAsWritten,
                        step.Condition.Text, Comparison.Quote(last), ScenarioNumbers.Seconds(wait.Deadline - wait.StartedAt),
                        scenarioLimit ? ", scenario timeout " + ScenarioNumbers.Seconds(phase.DurationS) : ""));
            }
            while (true)
            {
                if (wait.Expired) return observed ? NotReached() : PhaseTimeout(wait);
                ScenarioFailure problem = ReadValue(step, out object actual);
                if (problem != null) return problem;
                // A read that ended after the deadline is no evidence, neither for a pass nor for the
                // failure. With an earlier read in time the window has its answer; without one, the
                // step ran out of time.
                if (wait.Overrun) return observed ? NotReached() : new ScenarioFailure(ScenarioFailureKind.Deadline,
                    "waitUntil timeout " + ScenarioNumbers.Seconds(wait.Deadline - wait.StartedAt));
                if (step.Condition.Matches(actual)) return Measured(step, actual);
                last = actual;
                observed = true;
                if (!wait.Expired)
                    _sleepMs((int)Math.Ceiling(Math.Min(PollMs, wait.RemainingS * 1000)));
            }
        }

        ScenarioFailure Check(ScenarioStep step, ScenarioBudget phase)
        {
            ScenarioFailure problem = ReadValue(step, out object actual);
            if (problem != null) return problem;
            if (phase.Overrun) return PhaseTimeout(phase);
            Measured(step, actual);
            return step.Condition.Matches(actual) ? null : new ScenarioFailure(ScenarioFailureKind.Expectation,
                string.Format("{0}: expected {1}, actual {2}", step.SymbolAsWritten, step.Condition.Text, Comparison.Quote(actual)));
        }

        ScenarioFailure WriteStep(ScenarioStep step, ScenarioBudget phase)
        {
            if (Mode == ScenarioMode.Hardware)
                return new ScenarioFailure(ScenarioFailureKind.InvalidOperation, WriteNotAllowed);
            if (string.Equals(step.Symbol, SimulationActiveSymbol, StringComparison.OrdinalIgnoreCase))
                return new ScenarioFailure(ScenarioFailureKind.InvalidOperation, "scenario cannot change " + SimulationActiveSymbol);
            if (_recovery != null && _recovery.CaptureWrites && !_recovery.Originals.ContainsKey(step.Symbol))
            {
                int readError = _read(step.Symbol, PlcValue.SizeOf(step.Type), out byte[] original);
                if (readError != 0) return new ScenarioFailure(ScenarioFailureKind.Transport,
                    "cannot save " + step.SymbolAsWritten + " before write: " + AdsError(readError, step.Symbol));
                if (phase.Overrun) return PhaseTimeout(phase);
                _recovery.Originals.Add(step.Symbol, (byte[])original.Clone());
            }
            int err = _write(step.Symbol, PlcValue.Encode(step.Type, step.Value));
            return err == 0 ? null : new ScenarioFailure(ScenarioFailureKind.Transport,
                string.Format("{0} := {1}: {2}", step.SymbolAsWritten, Comparison.Quote(step.Value), AdsError(err, step.Symbol)));
        }

        ScenarioFailure WaitStep(ScenarioStep step, ScenarioBudget phase)
        {
            double left = phase.RemainingS;
            if (step.Seconds > left)
            {
                _sleepMs((int)Math.Max(0, left * 1000));
                return new ScenarioFailure(ScenarioFailureKind.Deadline,
                    string.Format("{0}: scenario timeout {1}", ScenarioNumbers.Seconds(step.Seconds), ScenarioNumbers.Seconds(phase.DurationS)));
            }
            _sleepMs((int)(step.Seconds * 1000));
            return null;
        }
        /// <summary>The one rule for a yes: "y" or "yes", any case; anything else (also no input) is no.</summary>
        internal static bool IsYes(string line)
        {
            string typed = (line ?? "").Trim().ToLowerInvariant();
            return typed == "y" || typed == "yes";
        }

        /// <summary>An instruct or confirm step. Hardware: the user answers (no user = failure).
        /// Simulation: answered automatically, and the report says so.</summary>
        ScenarioFailure Operator(ScenarioStep step)
        {
            bool confirm = step.Kind == StepKind.Confirm;
            string answer = "auto (simulation)";
            if (Mode == ScenarioMode.Hardware)
            {
                string line = Ask?.Invoke(confirm ? step.Text + " [y/n]" : step.Text.TrimEnd('.') + ". Press Enter when done.");
                if (line == null)
                    return new ScenarioFailure(ScenarioFailureKind.Operator, "no operator input (the run is not interactive)");
                answer = !confirm ? "done" : IsYes(line) ? "yes" : "no";
            }
            _measurements?.Add(new Measurement
            {
                Step = step.Number, Symbol = "operator: " + step.Text, Value = answer, Expected = confirm ? "yes" : "done",
            });
            return answer == "no" ? new ScenarioFailure(ScenarioFailureKind.Operator, "the operator answered no: " + step.Text) : null;
        }

        /// <summary>Records the value a scenario step read (not in prepare or cleanup). Always null, so
        /// "return Measured(...) ?? failure" keeps the step's own result.</summary>
        ScenarioFailure Measured(ScenarioStep step, object actual)
        {
            _measurements?.Add(new Measurement
            {
                Step = step.Number, Symbol = step.SymbolAsWritten, Value = Comparison.Quote(actual), Expected = step.Condition.Text,
            });
            return null;
        }

        ScenarioFailure ReadValue(ScenarioStep step, out object value)
        {
            value = null;
            int err = _read(step.Symbol, PlcValue.SizeOf(step.Type), out byte[] data);
            if (err != 0)
                return new ScenarioFailure(ScenarioFailureKind.Transport, string.Format("{0}: expected {1}, actual {2}", step.SymbolAsWritten, step.Condition.Text, AdsError(err, step.Symbol)));
            value = PlcValue.Decode(step.Type, data);
            return null;
        }

        /// <summary>Sends one command and waits for the receiver's answer. Returns null when an answer
        /// came (accepted tells which), else what went wrong.</summary>
        ScenarioFailure Send(int command, Dictionary<string, object> parameters, ScenarioBudget budget, out bool accepted, out int index)
        {
            accepted = false;
            index = 0;
            if (budget.Expired) return new ScenarioFailure(ScenarioFailureKind.Deadline, "command timeout before send");
            ScenarioFailure problem = ReadIndexes(out int lastAccepted, out int lastRejected);
            if (problem != null)
                return problem;
            if (budget.Expired) return new ScenarioFailure(ScenarioFailureKind.Deadline, "command timeout before send");
            do
                index = NextIndex();
            while (index == lastAccepted || index == lastRejected);

            byte[] stream = CommandJson.ToStream(CommandJson.Build(command, index, parameters));
            if (_recovery != null) _recovery.Required = true;
            int err = _write(StreamSymbol, stream);
            if (err != 0)
                return new ScenarioFailure(ScenarioFailureKind.Transport, "cannot write " + StreamSymbol + ": " + AdsError(err, StreamSymbol));

            while (true)
            {
                if (budget.Expired) return CommandTimeout(budget, index);
                problem = ReadIndexes(out lastAccepted, out lastRejected);
                if (problem != null)
                    return problem;
                if (budget.Overrun) return CommandTimeout(budget, index);
                if (lastAccepted == index)
                {
                    accepted = true;
                    return null;
                }
                if (lastRejected == index)
                    return null;
                _sleepMs(PollMs);
            }
        }

        static ScenarioFailure CommandTimeout(ScenarioBudget budget, int index) => new ScenarioFailure(ScenarioFailureKind.Deadline,
            string.Format("no answer after {0}: neither nLastCommandIndex nor nLastRejectedCommandIndex became {1}" +
                " (an unknown command id, or the command receiver does not run)", ScenarioNumbers.Seconds(budget.Deadline - budget.StartedAt), index));

        ScenarioFailure ReadIndexes(out int accepted, out int rejected)
        {
            rejected = 0;
            return ReadIndex(AcceptedSymbol, out accepted) ?? ReadIndex(RejectedSymbol, out rejected);
        }

        ScenarioFailure ReadIndex(string symbol, out int value)
        {
            value = 0;
            int err = _read(symbol, 4, out byte[] data);
            if (err != 0)
                return new ScenarioFailure(ScenarioFailureKind.Transport, "cannot read " + symbol + ": " + AdsError(err, symbol));
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
