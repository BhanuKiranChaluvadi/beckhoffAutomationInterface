using System;
using System.Diagnostics;
using System.Threading;

namespace BeckhoffAutomationInterface.Runtime
{
    // System-service control without XAE or a compiler. Used by boot-file updates.
    static class RuntimeStateCommand
    {
        internal const string Usage = "Usage: beckhoffAutomationInterface runtime-state --target umrt|local|<AMS-Net-ID> [--expect config|run] [--state config|run --yes]";

        // System service (10000) restarts into Run with Reset, not the PLC's Run command.
        internal static ushort Command(string state) => state == "config" ? (ushort)16 : state == "run" ? (ushort)2
            : throw new ArgumentException("State must be config or run.");

        public static int Run(string[] args)
        {
            try
            {
                var options = CommandArgs.Parse(args, new[] { "--target", "--expect", "--state" }, new[] { "--yes" });
                string target = RuntimeTargets.Normalize(options.Get("--target"));
                string requested = options.Get("--state");
                string expected = options.Get("--expect");
                if (requested != null && expected != null) throw new ArgumentException("Choose --state or --expect.");
                ushort desired = requested != null || expected != null ? Command(requested ?? expected) : (ushort)0;
                ushort reached = desired == 16 ? AdsClient.StateConfig : AdsClient.StateRun;
                using (var system = new AdsClient(RuntimeTargets.NetId(target), AdsClient.PortSystemService))
                {
                    int error = system.TryReadState(out ushort state);
                    if (error != 0) throw new InvalidOperationException("Runtime is unreachable (ADS " + error + ").");
                    Console.WriteLine("Target {0}: {1}", target, AdsClient.StateName(state));
                    if (requested == null) return expected == null || state == reached ? 0 : 1;
                    if (!options.Has("--yes")) { Console.WriteLine("State unchanged. Repeat with --yes to change the runtime state."); return 1; }
                    if (state == reached) { Console.WriteLine("Already in {0}.", AdsClient.StateName(state)); return 0; }
                    error = system.TryWriteControl(desired);
                    if (error != 0) throw new InvalidOperationException("State request failed (ADS " + error + ").");
                    var clock = Stopwatch.StartNew();
                    do
                    {
                        Thread.Sleep(250);
                        if (system.TryReadState(out state) == 0 && state == reached)
                        { Console.WriteLine("Reached {0}.", AdsClient.StateName(state)); return 0; }
                    } while (clock.Elapsed.TotalSeconds < 45);
                    throw new InvalidOperationException("Runtime did not reach the requested state within 45 s.");
                }
            }
            catch (Exception ex) when (ex is ArgumentException || ex is InvalidOperationException || ex is System.IO.IOException)
            { Console.Error.WriteLine("ERROR: " + ex.Message); Console.Error.WriteLine(Usage); return 1; }
        }
    }
}
