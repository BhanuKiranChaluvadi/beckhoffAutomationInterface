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
        readonly Dictionary<string, List<string>> _options = new Dictionary<string, List<string>>(StringComparer.Ordinal);
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
                    if (i + 1 >= args.Length || args[i + 1].StartsWith("--") || args[i + 1] == "-h" || string.IsNullOrWhiteSpace(args[i + 1]))
                        throw new ArgumentException(a + " needs a value.");
                    if (!result._options.TryGetValue(a, out List<string> values))
                        result._options[a] = values = new List<string>();
                    values.Add(args[++i]);
                }
                else if (switches.Contains(a))
                    result._switches.Add(a);
                else if (!a.StartsWith("-") && result.Positional == null)
                    result.Positional = a;
                else
                    throw new ArgumentException("unrecognized argument: " + a);
            }
            return result;
        }

        public string Get(string option) => _options.TryGetValue(option, out List<string> v) ? v[v.Count - 1] : null;
        public IReadOnlyList<string> GetAll(string option) => _options.TryGetValue(option, out List<string> v) ? v : new List<string>();
        public bool Has(string flag) => _switches.Contains(flag);
    }

    static class RuntimeCommands
    {
        // ------------------------------------------------------------------ verify

        internal const string VerifyUsage =
            "Usage: beckhoffAutomationInterface verify --target umrt|local|<AMS-Net-ID> --expect <expect.json> [--variant <name>] [--timeout <s>]";

        public static int Verify(string[] args)
        {
            CommandArgs a;
            try
            {
                a = CommandArgs.Parse(args, new[] { "--target", "--expect", "--variant", "--timeout" }, new string[0]);
                if (a.Get("--target") == null || a.Get("--expect") == null)
                    throw new ArgumentException("'verify' needs --target umrt|local|<AMS-Net-ID> and --expect <expect.json>.");
            }
            catch (ArgumentException ex)
            {
                Cli.CliHelp.WriteError(Console.Error, args, ex.Message);
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

        // ------------------------------------------------------------------ test

        internal static string TestUsage => "Usage: beckhoffAutomationInterface " + Cli.CommandLine.TestUsage;

        /// <summary>
        /// Runs scenario files (Scenario) against the running PLC over ADS. Exit code: 0 = every
        /// selected scenario passed (skipped ones do not count), 1 = a scenario failed or the PLC
        /// is not in Run, 2 = a wrong command line or an invalid scenario file (nothing runs).
        /// </summary>
        public static int Test(string[] args)
        {
            CommandArgs a;
            string target, netId, variant;
            List<string> groups = null, tags;
            List<Scenario> selected;
            int timeoutS;
            try
            {
                a = CommandArgs.Parse(args,
                    new[] { "--target", "--scenarios", "--variant", "--variants", "--groups", "--tags", "--junit", "--timeout" }, new string[0]);
                if (a.Get("--target") == null || a.Get("--scenarios") == null)
                    throw new ArgumentException("'test' needs --target umrt|local|<AMS-Net-ID> and --scenarios <file or folder>.");
                if (a.Get("--groups") != null && a.Get("--variants") != null)
                    throw new ArgumentException("use --groups or --variants, not both.");
                timeoutS = 60;
                if (a.Get("--timeout") != null && (!int.TryParse(a.Get("--timeout"), out timeoutS) || timeoutS <= 0))
                    throw new ArgumentException("--timeout needs a number of seconds (more than 0).");
                target = RuntimeTargets.Normalize(a.Get("--target"));

                variant = a.Get("--variant");
                tags = SplitList(a.Get("--tags"));
                if (a.Get("--groups") != null)
                    groups = SplitList(a.Get("--groups"));
                else if (a.Get("--variants") != null)
                    groups = GroupsOfVariant(Sync.VariantManifestParser.Parse(a.Get("--variants")), a.Get("--variants"), ref variant);
            }
            catch (Exception ex) when (ex is ArgumentException || ex is Sync.VariantManifestException
                                       || ex is System.IO.IOException || ex is System.Xml.XmlException)
            {
                Cli.CliHelp.WriteError(Console.Error, args, ex.Message);
                return 2;
            }

            var problems = new List<string>();
            try
            {
                List<Scenario> all = ScenarioFile.LoadAll(a.Get("--scenarios"), problems);
                selected = ScenarioFilter.ByTags(all, tags);
                if (problems.Count == 0 && selected.Count == 0)
                    problems.Add(all.Count == 0
                        ? "no scenario file (*.json) in " + a.Get("--scenarios")
                        : string.Format("no scenario has the tag(s) {0} ({1} scenario(s) found)", string.Join(", ", tags), all.Count));
            }
            catch (Exception ex) when (ex is ArgumentException || ex is System.IO.IOException || ex is UnauthorizedAccessException)
            {
                problems.Add(ex.Message);
                selected = null;
            }
            if (problems.Count > 0)
            {
                foreach (string p in problems)
                    Console.Error.WriteLine("ERROR: " + p);
                Console.Error.WriteLine("{0}: TEST NOT STARTED — {1} problem(s). Nothing was sent to the PLC.", Now(), problems.Count);
                return 2;
            }

            try
            {
                netId = RuntimeTargets.NetId(target);
            }
            catch (Exception ex) when (ex is InvalidOperationException || ex is FormatException || ex is System.IO.IOException)
            {
                Console.Error.WriteLine("ERROR: " + ex.Message);
                return 1;
            }

            DateTime started = DateTime.Now;
            Console.WriteLine("{0}: Test on {1} ({2}), boot project: {3}", Now(), target, netId,
                RuntimeTargets.CurrentBootProject(target) ?? "(none)");
            Console.WriteLine("{0}: {1} scenario(s) from {2}; feature groups: {3}", Now(), selected.Count, a.Get("--scenarios"),
                groups == null ? "not given (scenarios with \"requires\" are skipped)" : groups.Count == 0 ? "(none)" : string.Join(", ", groups));

            List<ScenarioResult> results;
            using (var plc = new AdsClient(netId, AdsClient.PortPlc1))
            {
                if (!WaitForPlcRun(target, netId, plc, timeoutS, "TEST FAILED"))
                    return 1;

                var guard = new SymbolSizeGuard(plc.TrySymbolInfo);
                var clock = Stopwatch.StartNew();
                var runner = new ScenarioRunner(guard.Reader(plc.TryRead), guard.Writer(plc.TryWrite), Thread.Sleep,
                    () => clock.Elapsed.TotalSeconds, ScenarioRunner.IndexSeed(DateTime.UtcNow)) { PlcTypeNote = guard.Note };
                string root = a.Get("--scenarios");
                results = runner.RunAll(selected, groups, timeoutS, r => PrintResult(r, root));
            }

            int failed = results.Count(r => r.Status == ScenarioStatus.Failed);
            int skipped = results.Count(r => r.Status == ScenarioStatus.Skipped);
            int passed = results.Count - failed - skipped;
            string label = variant ?? target;
            Console.WriteLine(ScenarioExitCode(results) == 0
                ? string.Format("{0}: TEST PASSED — {1}: {2} passed, {3} skipped, in {4}.", Now(), label, passed, skipped, ScenarioNumbers.Seconds(results.Sum(r => r.Seconds)))
                : string.Format("{0}: TEST FAILED — {1}: {2} of {3} failed ({4} passed, {5} skipped).", Now(), label, failed, results.Count, passed, skipped));

            if (passed == 0 && failed == 0)
                Console.Error.WriteLine("TEST FAILED: no applicable scenario executed (all skipped).");

            string junit = a.Get("--junit");
            if (junit != null)
            {
                try
                {
                    JUnitReport.Write(junit, JUnitReport.Build("bai test " + label, results, a.Get("--scenarios"), started));
                    Console.WriteLine("{0}: JUnit XML: {1}", Now(), junit);
                }
                catch (Exception ex) when (ex is System.IO.IOException || ex is UnauthorizedAccessException)
                {
                    Console.Error.WriteLine("ERROR: cannot write {0}: {1}", junit, ex.Message);
                    return 1;
                }
            }
            return ScenarioExitCode(results);
        }

        internal static int ScenarioExitCode(IReadOnlyCollection<ScenarioResult> results) =>
            results.Any(r => r.Status == ScenarioStatus.Passed) &&
            results.All(r => r.Status != ScenarioStatus.Failed) ? 0 : 1;

        static void PrintResult(ScenarioResult r, string root)
        {
            string id = JUnitReport.ClassName(root, r.Scenario.File);
            string status = r.Status == ScenarioStatus.Passed ? "PASS" : r.Status == ScenarioStatus.Failed ? "FAIL" : "SKIP";
            Console.WriteLine("  {0}  {1}  {2}  ({3})", status, id, r.Scenario.Name,
                r.Status == ScenarioStatus.Skipped ? r.Message : ScenarioNumbers.Seconds(r.Seconds));
            if (r.Status == ScenarioStatus.Failed)
            {
                Console.WriteLine("        {0}", r.Message);
                foreach (string line in r.Details)
                    Console.WriteLine("        {0}", line);
            }
        }

        /// <summary>"a, b,,c" -> [a, b, c]; null -> empty.</summary>
        internal static List<string> SplitList(string value) =>
            (value ?? "").Split(',').Select(s => s.Trim()).Where(s => s.Length > 0).ToList();

        /// <summary>The feature groups of a variant in variants.xml. Without --variant, a file with one
        /// variant gives that variant.</summary>
        internal static List<string> GroupsOfVariant(Sync.VariantManifest manifest, string path, ref string variant)
        {
            if (manifest == null)
                throw new ArgumentException("variants file not found: " + path);
            if (variant == null)
            {
                if (manifest.Variants.Count != 1)
                    throw new ArgumentException(string.Format("choose --variant: {0} lists {1}.", path, string.Join(", ", manifest.Variants)));
                variant = manifest.Variants[0];
            }
            string name = variant;
            string found = manifest.Variants.FirstOrDefault(v => v.Equals(name, StringComparison.OrdinalIgnoreCase));
            if (found == null)
                throw new ArgumentException(string.Format("{0} has no variant '{1}'. It has: {2}.", path, variant, string.Join(", ", manifest.Variants)));
            variant = found;
            return manifest.Groups.Where(g => g.Value.Contains(found, StringComparer.OrdinalIgnoreCase)).Select(g => g.Key).ToList();
        }

        // ------------------------------------------------------------------ activate

        internal const string ActivateUsage =
            "Usage: beckhoffAutomationInterface activate <project> --target umrt|local|<AMS-Net-ID> [--variant <name>] [--plc <name>] [--define <NAME>]... [--timeout <s>] [--yes]";

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
                a = CommandArgs.Parse(args, new[] { "--target", "--variant", "--plc", "--timeout", "--define" }, new[] { "--yes" });
                if (a.Positional == null || a.Get("--target") == null)
                    throw new ArgumentException("'activate' needs a project path and --target umrt|local|<AMS-Net-ID>.");
                string defineProblem = Sync.CompilerDefines.Problem(a.GetAll("--define"));
                if (defineProblem != null)
                    throw new ArgumentException(defineProblem);
                target = RuntimeTargets.Normalize(a.Get("--target"));
                netId = RuntimeTargets.NetId(target);
                tsprojPath = ProjectLocator.ResolveTsprojPath(a.Positional);
                if (a.GetAll("--define").Contains("SIMULATION", StringComparer.OrdinalIgnoreCase) && !RuntimeTargets.IsUmRt(target))
                    throw new ArgumentException("SIMULATION activation is restricted to the known local UmRT instance.");
            }
            catch (Exception ex) when (ex is ArgumentException || ex is InvalidOperationException || ex is FormatException
                                       || ex is System.IO.IOException)
            {
                Cli.CliHelp.WriteError(Console.Error, args, ex.Message);
                return 1;
            }

            Console.WriteLine("{0}: Target {1} ({2}): TwinCAT {3}; boot project now: {4}", Now(), target, netId,
                TwinCatState(netId), RuntimeTargets.CurrentBootProject(target) ?? "(none)");
            if (!a.Has("--yes"))
            {
                Console.WriteLine("{0}: NOT ACTIVATED — activation replaces that boot project. Run again with --yes to do it.", Now());
                return 1;
            }

            string plcName = ProjectLocator.ResolvePlcName(tsprojPath, a.Get("--plc"));
            int timeoutS = int.TryParse(a.Get("--timeout"), out int t) ? t : 60;
            bool running = false;
            ProjectShadowCopy shadow = null;
            try
            {
                using (VisualStudioSession vs = VisualStudioSession.Start(shellName))
                {
                    // A shadow copy: TwinCAT saves the project during build and activation by itself.
                    var opened = TwinCatProjectOpener.OpenExistingReadOnly(vs.Dte, tsprojPath);
                    shadow = opened.Shadow;
                    running = ActivateOpenProject(vs, opened.SysManager, plcName, a.Get("--variant"), a.GetAll("--define"),
                        target, netId, timeoutS, shadow.ToOriginalPath);
                    if (!running)
                        return 1;
                }
            }
            catch (Exception ex) when (ex is System.Runtime.InteropServices.COMException || ex is UnauthorizedAccessException)
            {
                Console.Error.WriteLine("ACTIVATE FAILED: TwinCAT rejected the request: " + ex.Message);
                return 1;
            }
            finally
            {
                shadow?.Dispose(); // after the shell is closed
            }

            if (!running)
                return 1;
            Console.WriteLine("{0}: ACTIVATED — boot project now: {1}", Now(), RuntimeTargets.CurrentBootProject(target) ?? "(none)");
            return 0;
        }

        /// <summary>
        /// The activation steps on a project that is open in 'vs': select the one variant, add the
        /// defines, set the target, disable the I/O devices on UmRT, set the boot autostart, build
        /// (no Clean), activate, restart TwinCAT and wait for the PLC to run. Returns true when the
        /// PLC runs. A build in the same session before this call makes this build incremental
        /// (bai machine test); 'activate' calls it on a fresh shadow copy. mapPath maps an error
        /// location back to the user's project. COM errors go to the caller.
        /// </summary>
        internal static bool ActivateOpenProject(VisualStudioSession vs, Interop.TCatSysManager.ITcSysManager sys, string plcName,
            string variant, IReadOnlyList<string> defines, string target, string netId, int timeoutS, Func<string, string> mapPath)
        {
            var sys14 = (Interop.TCatSysManager.ITcSysManager14)sys;
            string plcItem = "TIPC^" + plcName;

            // Exactly one variant runs on a target.
            List<string> selected;
            try
            {
                selected = Sync.VariantBuildPlan.Select(Sync.VariantSyncEngine.ProjectVariants(sys), variant, false);
                if (selected.Count > 1)
                    throw new ArgumentException(string.Format("the project has variants ({0}); choose one with --variant.", string.Join(", ", selected)));
            }
            catch (ArgumentException ex)
            {
                Console.WriteLine("{0}: ACTIVATE FAILED — {1}", Now(), ex.Message);
                return false;
            }
            if (selected.Count == 1)
            {
                if (!Sync.VariantSyncEngine.DefinesEnabled(sys, plcItem))
                {
                    Console.WriteLine("{0}: ACTIVATE FAILED — variant defines are off for '{1}'. Run 'sync variants' first.", Now(), plcItem);
                    return false;
                }
                sys14.CurrentProjectVariant = selected[0];
                Console.WriteLine("{0}: Variant: {1}", Now(), sys14.CurrentProjectVariant);
            }
            if (defines.Count > 0)
                Console.WriteLine("{0}: Compiler defines for this activation: {1}", Now(),
                    Sync.CompilerDefines.Add(sys, string.Format("TIPC^{0}^{0} Project", plcName), defines));

            sys14.SetTargetNetId(netId);
            Console.WriteLine("{0}: Target set to {1}.", Now(), sys14.GetTargetNetId());

            if (RuntimeTargets.IsUmRt(target))
            {
                // UmRT has no EtherCAT adapter: an enabled master keeps TwinCAT in Config.
                // Disable the I/O devices for this activation only: 'activate' works on a shadow
                // copy, and 'machine' sets them again from io-devices.xml on its next sync.
                int disabled = DisableIoDevices(sys);
                if (disabled > 0)
                    Console.WriteLine("{0}: UmRT has no EtherCAT adapter: {1} I/O device(s) disabled for this activation.", Now(), disabled);
            }
            // The PLC must start by itself after the restart, or nothing can be verified.
            sys.LookupTreeItem(plcItem).ConsumeXml(
                "<TreeItem><PlcProjectDef><BootProjectAutostart>true</BootProjectAutostart></PlcProjectDef></TreeItem>");

            Sync.BuildReport build;
            try
            {
                // No Clean: the variant, define, device and boot-flag changes above start a
                // background PLC precompile, and a Clean sent while it runs can deadlock
                // the shell (seen 4 times on Google, 2026-10-15; Build alone passed). The
                // project was built before (init/sync), and the build applies the changed
                // defines (verify checks GVL_Simulation.bActive from --define SIMULATION).
                using (vs.Watchdog("Build", Sync.BuildRunner.HangLimit))
                    build = Sync.BuildRunner.Build(vs.Dte, clean: false);
            }
            catch (Sync.BuildTimeoutException ex)
            {
                Console.WriteLine("{0}: ACTIVATE FAILED — build timed out: {1}", Now(), ex.Message);
                return false;
            }
            foreach (string message in build.PragmaMessages)
                Console.WriteLine("    [PLC MESSAGE] {0}", message);
            if (!build.Success)
            {
                Console.WriteLine("{0}: ACTIVATE FAILED — build failed, nothing was activated ({1} error(s)):", Now(), build.Errors.Count);
                foreach (Sync.BuildError e in build.Errors)
                    Console.WriteLine("    [ERROR] {0} ({1}:{2})", e.Description, mapPath(e.FileName), e.Line);
                foreach (string line in build.OutputTail)
                    Console.WriteLine("      | {0}", line);
                return false;
            }
            Console.WriteLine("{0}: Build passed.", Now());

            sys.ActivateConfiguration();
            Console.WriteLine("{0}: Configuration activated on {1}.", Now(), netId);
            sys.StartRestartTwinCAT();
            Console.WriteLine("{0}: TwinCAT restart requested.", Now());

            using (var plc = new AdsClient(netId, AdsClient.PortPlc1))
                return WaitForPlcRun(target, netId, plc, timeoutS);
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
                + ", but it may not hold every license this project needs: licensed libraries (for example"
                + " Tc2_TempController, Tc2_ControllerToolbox, Tc2_ModbusRTU) need their own. Make a new 7-day trial license in"
                + " XAE with THIS project open (SYSTEM > License). Other causes: a device the target cannot start (for example"
                + " an EtherCAT master on UmRT, which has no EtherCAT adapter) or another configuration error.";
        }

        /// <summary>Waits for the PLC (port 851) to report Run. On timeout, says why TwinCAT may
        /// have stayed in Config mode (see ConfigModeHint).</summary>
        static bool WaitForPlcRun(string target, string netId, AdsClient plc, int timeoutS, string failure = "VERIFY FAILED")
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
                Console.WriteLine("{0}: {4} — PLC not in Run after {1} s. TwinCAT: {2}. PLC: {3}.", Now(), timeoutS, sysText, plcText, failure);
                if (sysErr == 0 && sysState == AdsClient.StateConfig)
                    Console.WriteLine("    TwinCAT stays in Config mode: the runtime did not start. " +
                                      ConfigModeHint(RuntimeTargets.LicenseExpiry(target), DateTime.Now));
            }
            return false;
        }
    }
}
