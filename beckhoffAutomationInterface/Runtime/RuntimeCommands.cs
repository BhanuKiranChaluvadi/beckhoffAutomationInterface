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
                if (!WaitForPlcRun(target, netId, plc, timeoutS))
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

        // ------------------------------------------------------------------ activate

        const string ActivateUsage =
            "Usage: beckhoffAutomationInterface activate <path-to-.tsproj-or-folder> --target umrt|local [--variant <name>] [--plc-name <name>] [--yes]";

        /// <summary>
        /// Builds one variant and activates it on the target, then waits for the PLC to run.
        /// Activation REPLACES the target's boot project, so without --yes it only reports what is
        /// on the target now and changes nothing (no shell is started). The project is attached
        /// read-only and never saved. Steps proven by the VariantDemo prototype (2026-10-14).
        /// </summary>
        public static int Activate(string[] args, string shellName)
        {
            CommandArgs a;
            string target, netId, tsprojPath;
            try
            {
                a = CommandArgs.Parse(args, new[] { "--target", "--variant", "--plc-name", "--timeout" }, new[] { "--yes" });
                if (a.Positional == null || a.Get("--target") == null)
                    throw new ArgumentException("'activate' needs a project path and --target umrt|local.");
                target = RuntimeTargets.Normalize(a.Get("--target"));
                netId = RuntimeTargets.NetId(target);
                tsprojPath = ProjectLocator.ResolveTsprojPath(a.Positional);
            }
            catch (Exception ex) when (ex is ArgumentException || ex is InvalidOperationException || ex is FormatException
                                       || ex is System.IO.IOException)
            {
                Console.Error.WriteLine("ERROR: " + ex.Message);
                Console.Error.WriteLine(ActivateUsage);
                return 1;
            }

            Console.WriteLine("{0}: Target {1} ({2}): TwinCAT {3}; boot project now: {4}", Now(), target, netId,
                TwinCatState(netId), RuntimeTargets.CurrentBootProject(target) ?? "(none)");
            if (!a.Has("--yes"))
            {
                Console.WriteLine("{0}: NOT ACTIVATED — activation replaces that boot project. Run again with --yes to do it.", Now());
                return 1;
            }

            string plcName = ProjectLocator.ResolvePlcName(tsprojPath, a.Get("--plc-name"));
            ProjectShadowCopy shadow = null;
            try
            {
                using (VisualStudioSession vs = VisualStudioSession.Start(shellName))
                {
                    // A shadow copy: TwinCAT saves the project during build and activation by itself.
                    var opened = TwinCatProjectOpener.OpenExistingReadOnly(vs.Dte, tsprojPath);
                    shadow = opened.Shadow;
                    Interop.TCatSysManager.ITcSysManager sys = opened.SysManager;
                    var sys14 = (Interop.TCatSysManager.ITcSysManager14)sys;
                    string plcItem = "TIPC^" + plcName;

                    // Exactly one variant runs on a target.
                    List<string> selected;
                    try
                    {
                        selected = Sync.VariantBuildPlan.Select(Sync.VariantSyncEngine.ProjectVariants(sys), a.Get("--variant"), false);
                        if (selected.Count > 1)
                            throw new ArgumentException(string.Format("the project has variants ({0}); choose one with --variant.", string.Join(", ", selected)));
                    }
                    catch (ArgumentException ex)
                    {
                        Console.WriteLine("{0}: ACTIVATE FAILED — {1}", Now(), ex.Message);
                        return 1;
                    }
                    if (selected.Count == 1)
                    {
                        if (!Sync.VariantSyncEngine.DefinesEnabled(sys, plcItem))
                        {
                            Console.WriteLine("{0}: ACTIVATE FAILED — variant defines are off for '{1}'. Run 'sync variants' first.", Now(), plcItem);
                            return 1;
                        }
                        sys14.CurrentProjectVariant = selected[0];
                        Console.WriteLine("{0}: Variant: {1}", Now(), sys14.CurrentProjectVariant);
                    }

                    sys14.SetTargetNetId(netId);
                    Console.WriteLine("{0}: Target set to {1}.", Now(), sys14.GetTargetNetId());

                    if (target == RuntimeTargets.UmRt)
                    {
                        // UmRT has no EtherCAT adapter: an enabled master keeps TwinCAT in Config.
                        // Disable the I/O devices for this activation only — the project is attached
                        // read-only and never saved, so the files on disk do not change.
                        int disabled = DisableIoDevices(sys);
                        if (disabled > 0)
                            Console.WriteLine("{0}: UmRT has no EtherCAT adapter: {1} I/O device(s) disabled for this activation (project not saved).", Now(), disabled);
                    }
                    // The PLC must start by itself after the restart, or nothing can be verified.
                    sys.LookupTreeItem(plcItem).ConsumeXml(
                        "<TreeItem><PlcProjectDef><BootProjectAutostart>true</BootProjectAutostart></PlcProjectDef></TreeItem>");

                    Sync.BuildReport build;
                    try
                    {
                        build = Sync.BuildRunner.Build(vs.Dte, clean: true);
                    }
                    catch (Sync.BuildTimeoutException ex)
                    {
                        Console.WriteLine("{0}: ACTIVATE FAILED — build timed out: {1}", Now(), ex.Message);
                        return 1;
                    }
                    foreach (string message in build.PragmaMessages)
                        Console.WriteLine("    [PLC MESSAGE] {0}", message);
                    if (!build.Success)
                    {
                        Console.WriteLine("{0}: ACTIVATE FAILED — build failed, nothing was activated ({1} error(s)):", Now(), build.Errors.Count);
                        foreach (Sync.BuildError e in build.Errors)
                            Console.WriteLine("    [ERROR] {0} ({1}:{2})", e.Description, shadow.ToOriginalPath(e.FileName), e.Line);
                        return 1;
                    }
                    Console.WriteLine("{0}: Build passed.", Now());

                    sys.ActivateConfiguration();
                    Console.WriteLine("{0}: Configuration activated on {1}.", Now(), netId);
                    sys.StartRestartTwinCAT();
                    Console.WriteLine("{0}: TwinCAT restart requested.", Now());
                }
            }
            finally
            {
                shadow?.Dispose(); // after the shell is closed
            }

            int timeoutS = int.TryParse(a.Get("--timeout"), out int t) ? t : 60;
            using (var plc = new AdsClient(netId, AdsClient.PortPlc1))
            {
                if (!WaitForPlcRun(target, netId, plc, timeoutS))
                    return 1;
            }
            Console.WriteLine("{0}: ACTIVATED — boot project now: {1}", Now(), RuntimeTargets.CurrentBootProject(target) ?? "(none)");
            return 0;
        }

        static int DisableIoDevices(Interop.TCatSysManager.ITcSysManager sys)
        {
            Interop.TCatSysManager.ITcSmTreeItem io = sys.LookupTreeItem("TIID");
            int count = 0;
            for (int i = 1; i <= io.ChildCount; i++)
            {
                Interop.TCatSysManager.ITcSmTreeItem device = io.Child[i];
                if (device.Disabled == Interop.TCatSysManager.DISABLED_STATE.SMDS_NOT_DISABLED)
                {
                    device.Disabled = Interop.TCatSysManager.DISABLED_STATE.SMDS_DISABLED;
                    count++;
                }
            }
            return count;
        }

        static string TwinCatState(string netId)
        {
            using (var system = new AdsClient(netId, AdsClient.PortSystemService))
            {
                int err = system.TryReadState(out ushort state);
                return err == 0 ? AdsClient.StateName(state) : "not reachable (ADS error " + err + ")";
            }
        }

        /// <summary>Why TwinCAT may stay in Config mode, based on the target's license (pure, unit-tested).
        /// licenseExpiry: null = no license file; DateTime.MaxValue = no expiry.</summary>
        internal static string ConfigModeHint(DateTime? licenseExpiry, DateTime now)
        {
            if (licenseExpiry == null)
                return "No license file was found on the target. Create a 7-day trial license in XAE (SYSTEM > License).";
            if (licenseExpiry.Value <= now)
                return string.Format("The license expired {0:yyyy-MM-dd HH:mm}. Renew it in XAE (SYSTEM > License > 7 Days Trial License).", licenseExpiry.Value);
            return (licenseExpiry.Value == DateTime.MaxValue ? "The license has no expiry" : string.Format("The license is valid until {0:yyyy-MM-dd}", licenseExpiry.Value))
                + ", so the likely cause is a device the target cannot start (for example an EtherCAT master on UmRT, which"
                + " has no EtherCAT adapter) or another configuration error. Check the target's TwinCAT event log.";
        }

        /// <summary>Waits for the PLC (port 851) to report Run. On timeout, says why TwinCAT may
        /// have stayed in Config mode (see ConfigModeHint).</summary>
        static bool WaitForPlcRun(string target, string netId, AdsClient plc, int timeoutS)
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
                    Console.WriteLine("    TwinCAT stays in Config mode: the runtime did not start. " +
                                      ConfigModeHint(RuntimeTargets.LicenseExpiry(target), DateTime.Now));
            }
            return false;
        }
    }
}
