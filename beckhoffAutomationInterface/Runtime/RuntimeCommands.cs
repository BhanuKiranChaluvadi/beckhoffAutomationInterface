using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using static BeckhoffAutomationInterface.Clock;

namespace BeckhoffAutomationInterface.Runtime
{
    /// <summary>Small parser for the runtime commands: one optional positional, "--name value"
    /// options and "--flag" switches. Written in the new CLI style (no legacy-flag translation).</summary>
    sealed class CommandArgs
    {
        public string Positional { get; private set; }
        readonly Dictionary<string, string> _options = new Dictionary<string, string>(StringComparer.Ordinal);
        readonly HashSet<string> _switches = new HashSet<string>(StringComparer.Ordinal);

        public static CommandArgs Parse(string[] args, IEnumerable<string> optionNames, IEnumerable<string> switchNames)
        {
            var known = new HashSet<string>(optionNames);
            var switches = new HashSet<string>(switchNames);
            var result = new CommandArgs();
            for (int i = 1; i < args.Length; i++)
            {
                string a = args[i];
                if (known.Contains(a))
                {
                    if (i + 1 >= args.Length)
                        throw new ArgumentException(a + " needs a value.");
                    result._options[a] = args[++i];
                }
                else if (switches.Contains(a))
                    result._switches.Add(a);
                else if (!a.StartsWith("--") && result.Positional == null)
                    result.Positional = a;
                else
                    throw new ArgumentException("unrecognized argument: " + a);
            }
            return result;
        }

        public string Get(string option) => _options.TryGetValue(option, out string v) ? v : null;
        public bool Has(string flag) => _switches.Contains(flag);
    }

    static class RuntimeCommands
    {
        // ------------------------------------------------------------------ verify

        public static int Verify(string[] args)
        {
            CommandArgs a;
            try
            {
                a = CommandArgs.Parse(args, new[] { "--target", "--expect", "--variant", "--timeout" }, new string[0]);
                if (a.Get("--target") == null || a.Get("--expect") == null)
                    throw new ArgumentException("'verify' needs --target umrt|local and --expect <expect.json>.");
            }
            catch (ArgumentException ex)
            {
                Console.Error.WriteLine("ERROR: " + ex.Message);
                Console.Error.WriteLine("Usage: beckhoffAutomationInterface verify --target umrt|local --expect <expect.json> [--variant <name>] [--timeout <s>]");
                return 1;
            }

            Dictionary<string, List<Expectation>> file;
            List<Expectation> expectations;
            string variant = a.Get("--variant");
            try
            {
                file = ExpectationFile.Load(a.Get("--expect"));
                expectations = SelectExpectations(file, variant);
            }
            catch (Exception ex) when (ex is ArgumentException || ex is FormatException || ex is System.IO.IOException)
            {
                Console.Error.WriteLine("ERROR: " + ex.Message);
                return 1;
            }

            string target, netId;
            try
            {
                target = RuntimeTargets.Normalize(a.Get("--target"));
                netId = RuntimeTargets.NetId(target);
            }
            catch (Exception ex) when (ex is ArgumentException || ex is InvalidOperationException || ex is FormatException)
            {
                Console.Error.WriteLine("ERROR: " + ex.Message);
                return 1;
            }

            int timeoutS = int.TryParse(a.Get("--timeout"), out int t) ? t : 60;
            Console.WriteLine("{0}: Verify on {1} ({2}), boot project: {3}", Now(), target, netId,
                RuntimeTargets.CurrentBootProject(target) ?? "(none)");

            using (var plc = new AdsClient(netId, AdsClient.PortPlc1))
            {
                if (!WaitForPlcRun(netId, plc, timeoutS))
                    return 1;

                List<CheckResult> results = ExpectationChecker.Check(expectations, plc.TryRead, Thread.Sleep);
                int width = Math.Max(10, results.Max(r => r.Symbol.Length));
                foreach (CheckResult r in results)
                    Console.WriteLine("  {0} {1}  = {2}  (expected {3})",
                        r.Passed ? "OK  " : "FAIL", r.Symbol.PadRight(width), r.Actual, r.Expected);

                int failed = results.Count(r => !r.Passed);
                string label = variant ?? "default";
                Console.WriteLine(failed == 0
                    ? string.Format("{0}: VERIFY PASSED — {1}: all {2} check(s) as expected.", Now(), label, results.Count)
                    : string.Format("{0}: VERIFY FAILED — {1}: {2} of {3} check(s) failed.", Now(), label, failed, results.Count));
                return failed == 0 ? 0 : 1;
            }
        }

        /// <summary>The expectations for this run: the variant's list, or "default" for a project
        /// without variants.</summary>
        internal static List<Expectation> SelectExpectations(Dictionary<string, List<Expectation>> file, string variant)
        {
            if (variant != null)
            {
                if (!file.TryGetValue(variant, out List<Expectation> list) || variant.Equals(ExpectationFile.DefaultKey, StringComparison.OrdinalIgnoreCase))
                    throw new ArgumentException(string.Format("expect.json has no entry for variant '{0}'. It has: {1}.",
                        variant, string.Join(", ", file.Keys)));
                return list;
            }
            if (file.TryGetValue(ExpectationFile.DefaultKey, out List<Expectation> defaults))
                return defaults;
            throw new ArgumentException(string.Format("choose --variant: expect.json lists {0}.", string.Join(", ", file.Keys)));
        }

        /// <summary>Waits for the PLC (port 851) to report Run. On timeout, names the likely
        /// cause: TwinCAT left in Config mode usually means an expired (trial) license.</summary>
        static bool WaitForPlcRun(string netId, AdsClient plc, int timeoutS)
        {
            var clock = Stopwatch.StartNew();
            ushort plcState = 0;
            int plcErr = -1;
            while (clock.Elapsed.TotalSeconds < timeoutS)
            {
                plcErr = plc.TryReadState(out plcState);
                if (plcErr == 0 && plcState == AdsClient.StateRun)
                {
                    Console.WriteLine("{0}: PLC (port 851) is in Run.", Now());
                    return true;
                }
                Thread.Sleep(500);
            }

            using (var system = new AdsClient(netId, AdsClient.PortSystemService))
            {
                int sysErr = system.TryReadState(out ushort sysState);
                string sysText = sysErr == 0 ? AdsClient.StateName(sysState) : "not reachable (ADS error " + sysErr + ")";
                string plcText = plcErr == 0 ? AdsClient.StateName(plcState) : "not reachable (ADS error " + plcErr + ")";
                Console.WriteLine("{0}: VERIFY FAILED — PLC not in Run after {1} s. TwinCAT: {2}. PLC: {3}.", Now(), timeoutS, sysText, plcText);
                if (sysErr == 0 && sysState == AdsClient.StateConfig)
                    Console.WriteLine("    TwinCAT stays in Config mode: the runtime did not start. The usual cause is an expired" +
                                      " license (7-day trial) — run 'doctor' to see the expiry date.");
            }
            return false;
        }
    }
}
