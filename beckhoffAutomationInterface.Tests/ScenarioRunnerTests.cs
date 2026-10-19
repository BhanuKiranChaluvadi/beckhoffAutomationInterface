using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using BeckhoffAutomationInterface.Runtime;
using Xunit;

namespace BeckhoffAutomationInterface.Tests
{
    /// <summary>Step execution of 'test' against a fake PLC: no ADS, and a fake clock, so a
    /// timeout costs no real time (BeckhoffLib tasks/2026-10-16-plc-release, T1.2 and T1.3).</summary>
    public class ScenarioRunnerTests
    {
        /// <summary>A fake PLC: symbols with raw values, a command receiver and a clock that moves
        /// only when the runner sleeps.</summary>
        sealed class FakePlc
        {
            public double Time;
            public bool CompleteReset = true;
            public Func<string, int> WriteError = symbol => 0;
            public readonly Dictionary<string, byte[]> Values = new Dictionary<string, byte[]>();
            public readonly Dictionary<string, Func<double, byte[]>> ByTime = new Dictionary<string, Func<double, byte[]>>();
            public readonly List<string> Commands = new List<string>();
            public readonly List<string> Writes = new List<string>();

            /// <summary>The receiver's answer to a command id: true accepted, false rejected, null none.</summary>
            public Func<int, bool?> Receiver = command => true;

            public FakePlc()
            {
                Values[ScenarioRunner.StreamSymbol] = new byte[4096];
                Values[ScenarioRunner.SimulationActiveSymbol] = new byte[] { 1 };
                Values[ScenarioRunner.SimulationResetSymbol] = new byte[] { 0 };
                SetDint(ScenarioRunner.AcceptedSymbol, 0);
                SetDint(ScenarioRunner.RejectedSymbol, 0);
                SetInt(ScenarioRunner.ActiveSequenceSymbol, 0);
                Values[ScenarioRunner.StatusSymbol] = Str("Ready");
                Values[ScenarioRunner.LastErrorSourceSymbol] = Str("");
                Values[ScenarioRunner.LastErrorMessageSymbol] = Str("");
            }

            public void SetInt(string symbol, short v) => Values[symbol] = BitConverter.GetBytes(v);
            public void SetDint(string symbol, int v) => Values[symbol] = BitConverter.GetBytes(v);
            public int Dint(string symbol) => BitConverter.ToInt32(Values[symbol], 0);
            public static byte[] Str(string s) => Encoding.ASCII.GetBytes(s + "\0");

            public int Read(string symbol, int size, out byte[] data)
            {
                data = null;
                byte[] v;
                if (ByTime.TryGetValue(symbol, out Func<double, byte[]> f))
                    v = f(Time);
                else if (!Values.TryGetValue(symbol, out v))
                    return AdsClient.ErrorSymbolNotFound;
                data = new byte[size];
                Array.Copy(v, data, Math.Min(v.Length, size));
                return 0;
            }

            public int Write(string symbol, byte[] data)
            {
                if (!Values.ContainsKey(symbol))
                    return AdsClient.ErrorSymbolNotFound;
                int error = WriteError(symbol);
                if (error != 0) return error;
                Writes.Add(symbol);
                if (symbol != ScenarioRunner.StreamSymbol)
                {
                    Values[symbol] = data;
                    return 0;
                }
                Assert.Equal(4096, data.Length);
                string json = Encoding.ASCII.GetString(data, 0, Array.IndexOf(data, (byte)0));
                Commands.Add(json);
                int index = int.Parse(Regex.Match(json, @"""index"":(\d+)").Groups[1].Value);
                bool? answer = Receiver(int.Parse(Regex.Match(json, @"""command"":(\d+)").Groups[1].Value));
                if (answer == true)
                    SetDint(ScenarioRunner.AcceptedSymbol, index);
                else if (answer == false)
                    SetDint(ScenarioRunner.RejectedSymbol, index);
                return 0;
            }

            public void Sleep(int ms)
            {
                Time += ms / 1000.0;
                if (CompleteReset && Values.ContainsKey(ScenarioRunner.SimulationResetSymbol))
                    Values[ScenarioRunner.SimulationResetSymbol] = new byte[] { 0 };
            }

            public ScenarioRunner Runner(int firstIndex = 100) => new ScenarioRunner(Read, Write, Sleep, () => Time, firstIndex);
        }

        static Scenario Load(string steps, string extra = "") =>
            ScenarioFile.Parse(@"{ ""name"": ""s"", " + extra + @" ""steps"": [ " + steps + " ] }", "s.json");

        const string Seq = ScenarioRunner.ActiveSequenceSymbol;

        // ------------------------------------------------------------------ send

        [Fact]
        public void Send_Accepted_Passes_AndWritesTheClientJson()
        {
            var plc = new FakePlc();

            ScenarioResult r = plc.Runner().Run(Load(@"{ ""do"": ""send"", ""command"": 200, ""parameters"": { ""mode"": 1, ""side"": 1 } }"));

            Assert.Equal(ScenarioStatus.Passed, r.Status);
            Assert.Equal(new[] { "{\"command\":200,\"index\":100,\"parameters\":{\"mode\":1,\"side\":1}}" }, plc.Commands);
            Assert.Equal(100, plc.Dint(ScenarioRunner.AcceptedSymbol));
        }

        [Fact]
        public void Send_RejectedAsExpected_Passes()
        {
            var plc = new FakePlc { Receiver = command => false };

            Assert.Equal(ScenarioStatus.Passed, plc.Runner().Run(Load(@"{ ""do"": ""send"", ""command"": 51, ""expect"": ""rejected"" }")).Status);
        }

        [Fact]
        public void Send_RejectedButAcceptedExpected_Fails()
        {
            var plc = new FakePlc { Receiver = command => false };

            ScenarioResult r = plc.Runner().Run(Load(@"{ ""do"": ""send"", ""command"": 51 }"));

            Assert.Equal(ScenarioStatus.Failed, r.Status);
            Assert.Equal("step 1 (send) command 51 (index 100): expected accepted, actual rejected", r.Message);
        }

        [Fact]
        public void Send_AcceptedButRejectedExpected_Fails()
        {
            var plc = new FakePlc();

            ScenarioResult r = plc.Runner().Run(Load(@"{ ""do"": ""send"", ""command"": 51, ""expect"": ""rejected"" }"));

            Assert.Equal("step 1 (send) command 51 (index 100): expected rejected, actual accepted", r.Message);
        }

        [Fact]
        public void Send_NoAnswer_TimesOut()   // an unknown command id changes neither index
        {
            var plc = new FakePlc { Receiver = command => null };

            ScenarioResult r = plc.Runner().Run(Load(@"{ ""do"": ""send"", ""command"": 4242, ""timeoutS"": 3 }"));

            Assert.Equal(ScenarioStatus.Failed, r.Status);
            Assert.StartsWith("step 1 (send) command 4242 (index 100): no answer after 3.0 s", r.Message);
            Assert.InRange(plc.Time, 3.0, 3.2);
        }

        [Fact]
        public void Send_DefaultTimeout_Is10Seconds()
        {
            var plc = new FakePlc { Receiver = command => null };

            plc.Runner().Run(Load(@"{ ""do"": ""send"", ""command"": 1 }"));

            Assert.InRange(plc.Time, 10.0, 10.2);
        }

        [Fact]
        public void Send_NeverReusesTheIndexThePlcAlreadyShows()
        {
            var plc = new FakePlc();
            plc.SetDint(ScenarioRunner.AcceptedSymbol, 100);   // from an earlier run
            plc.SetDint(ScenarioRunner.RejectedSymbol, 101);

            plc.Runner(100).Run(Load(@"{ ""do"": ""send"", ""command"": 1 }, { ""do"": ""send"", ""command"": 2 }"));

            Assert.Contains("\"index\":102", plc.Commands[0]);
            Assert.Contains("\"index\":103", plc.Commands[1]);
        }

        [Fact]
        public void Send_ReceiverNotInProgram_Fails()
        {
            var plc = new FakePlc();
            plc.Values.Remove(ScenarioRunner.StreamSymbol);

            ScenarioResult r = plc.Runner().Run(Load(@"{ ""do"": ""send"", ""command"": 1 }"));

            Assert.Contains("cannot write PRG_Telemetry.fbCommandReceiver.AdsStream: not in the program (ADS error 1808)", r.Message);
        }

        [Fact]
        public void IndexSeed_IsPositive_AndMovesWithTheClock()
        {
            var t = new DateTime(2026, 10, 16, 8, 0, 0, DateTimeKind.Utc);
            int a = ScenarioRunner.IndexSeed(t), b = ScenarioRunner.IndexSeed(t.AddSeconds(1));

            Assert.InRange(a, 1, int.MaxValue - 1000000);
            Assert.Equal(1000, b - a);
            Assert.InRange(ScenarioRunner.IndexSeed(new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc)), 1, int.MaxValue);
        }

        // ------------------------------------------------------------------ waitUntil, check

        [Fact]
        public void WaitUntil_PassesWhenTheValueComes()
        {
            var plc = new FakePlc();
            plc.ByTime[Seq] = t => BitConverter.GetBytes((short)(t >= 2 ? 7 : 0));

            ScenarioResult r = plc.Runner().Run(Load(@"{ ""do"": ""waitUntil"", ""symbol"": """ + Seq + @""", ""type"": ""INT"", ""equals"": 7, ""timeoutS"": 5 }"));

            Assert.Equal(ScenarioStatus.Passed, r.Status);
            Assert.InRange(plc.Time, 2.0, 2.2);
        }

        [Fact]
        public void WaitUntil_Timeout_ReportsSymbolExpectedAndActual()
        {
            var plc = new FakePlc();

            ScenarioResult r = plc.Runner().Run(Load(
                @"{ ""do"": ""wait"", ""seconds"": 1 }, { ""do"": ""waitUntil"", ""symbol"": """ + Seq + @""", ""type"": ""INT"", ""equals"": 7, ""timeoutS"": 5 }"));

            Assert.Equal(ScenarioStatus.Failed, r.Status);
            Assert.Equal("step 2 (waitUntil) PRG_Sequence.fbOrchestrator.eActiveSequence: expected = 7, actual 0 (after 5.0 s)", r.Message);
        }

        [Fact]
        public void WaitUntil_WithoutTimeout_UsesTheScenarioTimeout()
        {
            var plc = new FakePlc();
            plc.Values["PRG_Sequence.fbOrchestrator.bDone"] = new byte[] { 0 };

            ScenarioResult r = plc.Runner().Run(Load(
                @"{ ""do"": ""wait"", ""seconds"": 4 }, { ""do"": ""waitUntil"", ""symbol"": ""PRG_Sequence.fbOrchestrator.bDone"", ""equals"": true }",
                @"""timeoutS"": 30,"));

            Assert.Equal("step 2 (waitUntil) PRG_Sequence.fbOrchestrator.bDone: expected = TRUE, actual FALSE (after 26.0 s, scenario timeout 30.0 s)",
                r.Message);
            Assert.InRange(plc.Time, 30.0, 30.2);
        }

        [Fact]
        public void WaitUntil_SymbolNotInProgram_FailsAtOnce()
        {
            var plc = new FakePlc();

            ScenarioResult r = plc.Runner().Run(Load(@"{ ""do"": ""waitUntil"", ""symbol"": ""GVL_X.bNone"", ""equals"": true, ""timeoutS"": 60 }"));

            Assert.Equal("step 1 (waitUntil) GVL_X.bNone: expected = TRUE, actual not in the program (ADS error 1808)", r.Message);
            Assert.Equal(0, plc.Time);
        }

        [Theory]
        [InlineData(@"""type"": ""INT"", ""equals"": 5", true)]
        [InlineData(@"""type"": ""INT"", ""equals"": 6", false)]
        [InlineData(@"""type"": ""INT"", ""notEquals"": 0", true)]
        [InlineData(@"""type"": ""INT"", ""notEquals"": 5", false)]
        [InlineData(@"""type"": ""INT"", ""greaterThan"": 4, ""lessThan"": 6", true)]
        [InlineData(@"""type"": ""INT"", ""greaterThan"": 5", false)]
        [InlineData(@"""type"": ""INT"", ""lessThan"": 5", false)]
        [InlineData(@"""type"": ""INT"", ""equals"": 6, ""tolerance"": 1", true)]
        public void Check_Comparisons(string condition, bool passes)
        {
            var plc = new FakePlc();
            plc.SetInt("MAIN.n", 5);

            ScenarioResult r = plc.Runner().Run(Load(@"{ ""do"": ""check"", ""symbol"": ""MAIN.n"", " + condition + " }"));

            Assert.Equal(passes ? ScenarioStatus.Passed : ScenarioStatus.Failed, r.Status);
        }

        [Fact]
        public void Check_Failure_ReportsSymbolExpectedAndActual()
        {
            var plc = new FakePlc();
            plc.Values["PRG_Sequence.fbOrchestrator.sStatus"] = FakePlc.Str("Invalid purge mode");
            plc.Values["MAIN.r"] = BitConverter.GetBytes(2.5f);

            ScenarioResult a = plc.Runner().Run(Load(@"{ ""do"": ""check"", ""symbol"": ""PRG_Sequence.fbOrchestrator.sStatus"", ""type"": ""STRING(255)"", ""equals"": ""Ready"" }"));
            ScenarioResult b = plc.Runner().Run(Load(@"{ ""do"": ""check"", ""symbol"": ""MAIN.r"", ""type"": ""REAL"", ""greaterThan"": 3, ""lessThan"": 9 }"));

            Assert.Equal("step 1 (check) PRG_Sequence.fbOrchestrator.sStatus: expected = 'Ready', actual 'Invalid purge mode'", a.Message);
            Assert.Equal("step 1 (check) MAIN.r: expected > 3 and < 9, actual 2.5", b.Message);
        }

        // ------------------------------------------------------------------ write, wait

        [Fact]
        public void Write_SetsTheValue()
        {
            var plc = new FakePlc();
            plc.Values["GVL_Simulation.bSafetyOk"] = new byte[] { 1 };
            plc.Values["GVL_Simulation.rLeakRate"] = new byte[4];

            ScenarioResult r = plc.Runner().Run(Load(
                @"{ ""do"": ""write"", ""symbol"": ""GVL_Simulation.bSafetyOk"", ""value"": false }," +
                @"{ ""do"": ""write"", ""symbol"": ""GVL_Simulation.rLeakRate"", ""type"": ""REAL"", ""value"": 0.25 }"));

            Assert.Equal(ScenarioStatus.Passed, r.Status);
            Assert.Equal(new byte[] { 0 }, plc.Values["GVL_Simulation.bSafetyOk"]);
            Assert.Equal(0.25f, BitConverter.ToSingle(plc.Values["GVL_Simulation.rLeakRate"], 0));
        }

        [Fact]
        public void Write_SymbolNotInProgram_Fails()
        {
            var plc = new FakePlc();

            ScenarioResult r = plc.Runner().Run(Load(@"{ ""do"": ""write"", ""symbol"": ""GVL_X.n"", ""type"": ""INT"", ""value"": 3 }"));

            Assert.Equal("step 1 (write) GVL_X.n := 3: not in the program (ADS error 1808)", r.Message);
        }

        [Fact]
        public void Wait_Sleeps_AndStopsAtTheScenarioTimeout()
        {
            var plc = new FakePlc();
            Assert.Equal(ScenarioStatus.Passed, plc.Runner().Run(Load(@"{ ""do"": ""wait"", ""seconds"": 2 }")).Status);
            Assert.Equal(2, plc.Time, 3);

            plc.Time = 0;
            ScenarioResult r = plc.Runner().Run(Load(@"{ ""do"": ""wait"", ""seconds"": 20 }", @"""timeoutS"": 5,"));
            Assert.Equal("step 1 (wait) 20.0 s: scenario timeout 5.0 s", r.Message);
            Assert.Equal(5, plc.Time, 3);
        }

        [Fact]
        public void Scenario_StopsAtTheFirstFailedStep()
        {
            var plc = new FakePlc();
            plc.Values["MAIN.n"] = BitConverter.GetBytes((short)0);

            ScenarioResult r = plc.Runner().Run(Load(
                @"{ ""do"": ""check"", ""symbol"": ""MAIN.n"", ""type"": ""INT"", ""equals"": 0 }," +
                @"{ ""do"": ""check"", ""symbol"": ""MAIN.n"", ""type"": ""INT"", ""equals"": 1 }," +
                @"{ ""do"": ""write"", ""symbol"": ""MAIN.n"", ""type"": ""INT"", ""value"": 9 }"));

            Assert.StartsWith("step 2 (check)", r.Message);
            Assert.DoesNotContain("MAIN.n", plc.Writes);
        }

        // ------------------------------------------------------------------ prepare and the whole run

        [Fact]
        public void Prepare_AcknowledgesErrors_AndResetsTheSimulation()
        {
            var plc = new FakePlc();
            plc.SetInt(Seq, 99);   // stopped in Error: only ErrorAcknowledge brings it back
            plc.Values[ScenarioRunner.SimulationResetSymbol] = new byte[] { 0 };
            plc.Receiver = command =>
            {
                if (command == 9999)
                    plc.SetInt(Seq, 0);
                return true;
            };

            Assert.Null(plc.Runner().Prepare(5));
            Assert.Equal(new[] { "{\"command\":9999,\"index\":100,\"parameters\":{}}" }, plc.Commands);
            Assert.Equal(new byte[] { 0 }, plc.Values[ScenarioRunner.SimulationResetSymbol]);
            Assert.True(plc.Time > 0);
        }

        [Fact]
        public void Prepare_WithoutResetInput_RefusesBeforeWriting()
        {
            var plc = new FakePlc();

            plc.Values.Remove(ScenarioRunner.SimulationResetSymbol);
            Assert.Contains("cannot read " + ScenarioRunner.SimulationResetSymbol, plc.Runner().Prepare(5));
            Assert.Empty(plc.Writes);
        }

        [Fact]
        public void Prepare_WaitsForTheRunningSequence()
        {
            var plc = new FakePlc();
            plc.ByTime[Seq] = t => BitConverter.GetBytes((short)(t >= 3 ? 0 : 5));

            Assert.Null(plc.Runner().Prepare(10));
            Assert.InRange(plc.Time, 3.0, 3.2);

            plc.Time = 0;
            plc.ByTime[Seq] = t => BitConverter.GetBytes((short)5);
            Assert.Equal("PRG_Sequence.fbOrchestrator.eActiveSequence = 5 after 10.0 s (expected 0 or 99)", plc.Runner().Prepare(10));
        }

        [Fact]
        public void RunAll_AFailureDoesNotStopTheNextScenario_AndShowsTheOrchestrator()
        {
            var plc = new FakePlc();
            plc.Values[ScenarioRunner.StatusSymbol] = FakePlc.Str("Invalid purge mode");
            plc.Values[ScenarioRunner.LastErrorSourceSymbol] = FakePlc.Str("FB_PurgeSequence");
            plc.Values[ScenarioRunner.LastErrorMessageSymbol] = FakePlc.Str("PCV timeout");
            Scenario failing = Load(@"{ ""do"": ""send"", ""command"": 200, ""expect"": ""rejected"" }");
            Scenario passing = Load(@"{ ""do"": ""wait"", ""seconds"": 1 }");
            Scenario other = Load(@"{ ""do"": ""wait"", ""seconds"": 1 }", @"""requires"": [""PCV_ETHERCAT""],");
            var seen = new List<ScenarioStatus>();

            List<ScenarioResult> results = plc.Runner().RunAll(new[] { failing, passing, other }, new[] { "PCV_MODBUS" }, 5, r => seen.Add(r.Status));

            Assert.Equal(new[] { ScenarioStatus.Failed, ScenarioStatus.Passed, ScenarioStatus.Skipped }, seen);
            Assert.Equal(new[]
            {
                "PRG_Sequence.fbOrchestrator.eActiveSequence = 0",
                "PRG_Sequence.fbOrchestrator.sStatus = 'Invalid purge mode'",
                "PRG_Sequence.fbOrchestrator.stLastError = 'FB_PurgeSequence': 'PCV timeout'",
            }, results[0].Details);
            Assert.Empty(results[1].Details);
            Assert.Equal("requires PCV_ETHERCAT (not a feature group of this machine)", results[2].Message);
            // Acknowledge twice, failing command, and cleanup CancelProcess.
            Assert.Equal(4, plc.Commands.Count);
            Assert.Contains("\"command\":9999", plc.Commands[0]);
            Assert.Contains("\"command\":200", plc.Commands[1]);
            Assert.Contains("\"command\":1,", plc.Commands[2]);
            Assert.Contains("\"command\":9999", plc.Commands[3]);
        }

        [Fact]
        public void RunAll_PrepareFailure_FailsTheScenario()
        {
            var plc = new FakePlc();
            plc.SetInt(Seq, 5);

            List<ScenarioResult> results = plc.Runner().RunAll(new[] { Load(@"{ ""do"": ""wait"", ""seconds"": 1 }") }, null, 2, null);

            Assert.Equal(ScenarioStatus.Failed, results[0].Status);
            Assert.StartsWith("before the scenario: PRG_Sequence.fbOrchestrator.eActiveSequence = 5", results[0].Message);
            Assert.Equal("PRG_Sequence.fbOrchestrator.stLastError = (none)", results[0].Details[2]);
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void Prepare_RequiresActiveSimulationBeforeWriting(bool missing)
        {
            var plc = new FakePlc();
            if (missing) plc.Values.Remove(ScenarioRunner.SimulationActiveSymbol);
            else plc.Values[ScenarioRunner.SimulationActiveSymbol] = new byte[] { 0 };
            Assert.Contains(ScenarioRunner.SimulationActiveSymbol, plc.Runner().Prepare(2));
            Assert.Empty(plc.Writes);
        }

        [Fact]
        public void Prepare_IdleSequenceDoesNotHideMissingResetAcknowledgement()
        {
            var plc = new FakePlc { CompleteReset = false };
            Assert.Contains("did not clear", plc.Runner().Prepare(2));
            Assert.InRange(plc.Time, 2, 2.2);
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void RunAll_RestoresOriginalFaultInjectionAfterSuccessOrFailure(bool fail)
        {
            var plc = new FakePlc();
            plc.SetDint("MAIN.override", 7);
            Scenario first = Load(@"{ ""do"": ""write"", ""symbol"": ""MAIN.override"", ""type"": ""DINT"", ""value"": 99 },
                { ""do"": ""check"", ""symbol"": ""MAIN.override"", ""type"": ""DINT"", ""equals"": " + (fail ? "8" : "99") + " }");
            Scenario second = Load(@"{ ""do"": ""check"", ""symbol"": ""MAIN.override"", ""type"": ""DINT"", ""equals"": 7 }");
            var results = plc.Runner().RunAll(new[] { first, second }, null, 2, null);
            Assert.Equal(fail ? ScenarioStatus.Failed : ScenarioStatus.Passed, results[0].Status);
            Assert.Equal(ScenarioStatus.Passed, results[1].Status);
            Assert.Equal(7, plc.Dint("MAIN.override"));
        }

        [Fact]
        public void RunAll_FailedActiveSequenceIsCancelledBeforeNextScenario()
        {
            var plc = new FakePlc();
            plc.Receiver = command => { if (command == 200) plc.SetInt(Seq, 3); if (command == 1) plc.SetInt(Seq, 0); return true; };
            var results = plc.Runner().RunAll(new[] {
                Load(@"{ ""do"": ""send"", ""command"": 200, ""expect"": ""rejected"" }"),
                Load(@"{ ""do"": ""check"", ""symbol"": """ + Seq + @""", ""type"": ""INT"", ""equals"": 0 }")
            }, null, 2, null);
            Assert.Equal(ScenarioStatus.Failed, results[0].Status);
            Assert.Equal(ScenarioStatus.Passed, results[1].Status);
            Assert.Contains(plc.Commands, command => command.Contains("\"command\":1,"));
        }

        [Fact]
        public void RunAll_CleanupFailureBlocksFurtherCommands()
        {
            var plc = new FakePlc { Receiver = command => command != 1 };
            var results = plc.Runner().RunAll(new[] {
                Load(@"{ ""do"": ""send"", ""command"": 200, ""expect"": ""rejected"" }"),
                Load(@"{ ""do"": ""send"", ""command"": 300 }")
            }, null, 2, null);
            Assert.All(results, r => Assert.Equal(ScenarioStatus.Failed, r.Status));
            Assert.Contains("cleanup failed", results[1].Message);
            Assert.DoesNotContain(plc.Commands, command => command.Contains("\"command\":300,"));
        }

        [Fact]
        public void RunAll_RestoreFailureFailsAnOtherwisePassingScenarioAndBlocksTheNext()
        {
            var plc = new FakePlc();
            plc.SetDint("MAIN.override", 7);
            int writes = 0;
            plc.WriteError = symbol => symbol == "MAIN.override" && ++writes > 1 ? 1793 : 0;
            var results = plc.Runner().RunAll(new[] {
                Load(@"{ ""do"": ""write"", ""symbol"": ""MAIN.override"", ""type"": ""DINT"", ""value"": 99 }"),
                Load(@"{ ""do"": ""send"", ""command"": 300 }")
            }, null, 2, null);
            Assert.All(results, r => Assert.Equal(ScenarioStatus.Failed, r.Status));
            Assert.Contains("cannot restore MAIN.override", results[0].Message);
            Assert.DoesNotContain(plc.Commands, command => command.Contains("\"command\":300,"));
        }

        [Fact]
        public void RunAll_RepeatedWriteRestoresTheOriginalValueNotAnIntermediateValue()
        {
            var plc = new FakePlc();
            plc.SetDint("MAIN.override", 7);
            var scenario = Load(@"{ ""do"": ""write"", ""symbol"": ""MAIN.override"", ""type"": ""DINT"", ""value"": 8 },
                { ""do"": ""write"", ""symbol"": ""MAIN.override"", ""type"": ""DINT"", ""value"": 9 }");
            var runner = plc.Runner();
            for (int i = 0; i < 2; i++)
            {
                Assert.Equal(ScenarioStatus.Passed, runner.RunAll(new[] { scenario }, null, 2, null)[0].Status);
                Assert.Equal(7, plc.Dint("MAIN.override"));
            }
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void RunAll_ExplicitCleanupRunsAfterSuccessOrFailure(bool fail)
        {
            var plc = new FakePlc();
            plc.SetDint("MAIN.manual", 0);
            var scenario = Load(@"{ ""do"": ""check"", ""symbol"": ""MAIN.manual"", ""type"": ""DINT"", ""equals"": " + (fail ? "1" : "0") + " }",
                @"""cleanup"": [{ ""do"": ""write"", ""symbol"": ""MAIN.manual"", ""type"": ""DINT"", ""value"": 7 }],");
            Assert.Equal(fail ? ScenarioStatus.Failed : ScenarioStatus.Passed, plc.Runner().RunAll(new[] { scenario }, null, 2, null)[0].Status);
            Assert.Equal(7, plc.Dint("MAIN.manual"));
        }

        [Fact]
        public void RunAll_ExplicitCleanupFailureStillAttemptsRemainingStepsAndFailsScenario()
        {
            var plc = new FakePlc();
            plc.SetDint("MAIN.manual", 1);
            var scenario = Load(@"{ ""do"": ""wait"", ""seconds"": 0 }", @"""cleanup"": [
                { ""do"": ""check"", ""symbol"": ""MAIN.manual"", ""type"": ""DINT"", ""equals"": 0 },
                { ""do"": ""write"", ""symbol"": ""MAIN.manual"", ""type"": ""DINT"", ""value"": 0 }],");
            var result = plc.Runner().RunAll(new[] { scenario }, null, 2, null)[0];
            Assert.Equal(ScenarioStatus.Failed, result.Status);
            Assert.Contains("cleanup step 1", result.Message);
            Assert.Equal(0, plc.Dint("MAIN.manual"));
        }

        [Fact]
        public void RunAll_ExplicitCleanupHasABoundedTimeout()
        {
            var plc = new FakePlc();
            var scenario = Load(@"{ ""do"": ""wait"", ""seconds"": 0 }", @"""cleanup"": [
                { ""do"": ""wait"", ""seconds"": 100 }, { ""do"": ""send"", ""command"": 300 }],");
            Assert.Equal(ScenarioStatus.Failed, plc.Runner().RunAll(new[] { scenario }, null, 2, null)[0].Status);
            Assert.InRange(plc.Time, 2, 2.3);
            Assert.DoesNotContain(plc.Commands, command => command.Contains("\"command\":300,"));
        }

        [Fact]
        public void CleanupSchemaIsValidatedBeforeExecution()
        {
            Assert.Throws<ScenarioFormatException>(() => Load(@"{ ""do"": ""wait"", ""seconds"": 0 }", @"""cleanup"": {},"));
            Assert.Throws<ScenarioFormatException>(() => Load(@"{ ""do"": ""wait"", ""seconds"": 0 }", @"""cleanup"": [{ ""do"": ""send"" }],"));
        }

        [Fact]
        public void ScenarioExitCode_RequiresAtLeastOnePassAndNoFailure()
        {
            var pass = new ScenarioResult { Status = ScenarioStatus.Passed };
            var skip = new ScenarioResult { Status = ScenarioStatus.Skipped };
            var fail = new ScenarioResult { Status = ScenarioStatus.Failed };
            Assert.Equal(1, RuntimeCommands.ScenarioExitCode(new ScenarioResult[0]));
            Assert.Equal(1, RuntimeCommands.ScenarioExitCode(new[] { skip }));
            Assert.Equal(1, RuntimeCommands.ScenarioExitCode(new[] { pass, fail }));
            Assert.Equal(0, RuntimeCommands.ScenarioExitCode(new[] { pass, skip }));
        }

        // ------------------------------------------------------------------ size guard

        [Fact]
        public void SizeGuard_RejectsAWrongType_AndNamesThePlcType()
        {
            var plc = new FakePlc();
            plc.SetDint("MAIN.n", 7);
            var guard = new SymbolSizeGuard((string symbol, out int size, out string type) =>
            {
                size = 0;
                type = null;
                if (symbol != "MAIN.n")
                    return AdsClient.ErrorSymbolNotFound;
                size = 4;
                type = "DINT";
                return 0;
            });
            var runner = new ScenarioRunner(guard.Reader(plc.Read), guard.Writer(plc.Write), plc.Sleep, () => plc.Time, 1) { PlcTypeNote = guard.Note };

            Assert.Equal(ScenarioStatus.Passed, runner.Run(Load(@"{ ""do"": ""check"", ""symbol"": ""MAIN.n"", ""type"": ""DINT"", ""equals"": 7 }")).Status);
            Assert.Equal("step 1 (check) MAIN.n: expected = 7, actual the \"type\" does not match the PLC symbol (ADS error 1797) (the PLC symbol is DINT, 4 bytes)",
                runner.Run(Load(@"{ ""do"": ""check"", ""symbol"": ""MAIN.n"", ""type"": ""INT"", ""equals"": 7 }")).Message);
            Assert.Equal("step 1 (write) MAIN.n := 3: the \"type\" does not match the PLC symbol (ADS error 1797) (the PLC symbol is DINT, 4 bytes)",
                runner.Run(Load(@"{ ""do"": ""write"", ""symbol"": ""MAIN.n"", ""type"": ""INT"", ""value"": 3 }")).Message);
            Assert.Equal(7, plc.Dint("MAIN.n"));   // nothing was written
            Assert.Contains("not in the program", runner.Run(Load(@"{ ""do"": ""check"", ""symbol"": ""MAIN.x"", ""type"": ""INT"", ""equals"": 7 }")).Message);
        }
    }
}
