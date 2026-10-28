using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Web.Script.Serialization;
using static BeckhoffAutomationInterface.Clock;

namespace BeckhoffAutomationInterface.Machines
{
    /// <summary>
    /// 'machine build' and 'machine test': build (and test) the machines of a PLC repository
    /// described by its bai.machines.json. Replaces the repository's build_nflite_machine.ps1,
    /// run_integration_tests.ps1, build_info.ps1 and the generic parts of build_checks.ps1, and for a
    /// native family build_nfpro.ps1, native_configuration.py and Tests/Integration/NFPro/run.ps1.
    /// Exit codes: see <see cref="Exit"/>.
    /// </summary>
    static class MachineCommands
    {
        public const string Usage =
            "machine build [<Machine>...] [--fresh] [--release] [--simulation] [--parallel] [--platform <name>]\n" +
            "                [--manifest <file>] [--work <folder>]\n" +
            "  machine test  [<Machine>...] [--fresh] [--tags <a,b>] [--junit <file>] [--timeout-seconds <s>]\n" +
            "                [--manifest <file>] [--work <folder>]";

        /// <summary>The exit codes of 'machine'.</summary>
        internal enum Exit
        {
            /// <summary>Every machine passed every step.</summary>
            Passed = 0,
            /// <summary>A step failed: source check, parse, build, links, activate, verify or a scenario.</summary>
            Failed = 1,
            /// <summary>Wrong command line, manifest or machine name, or a release build was refused. Nothing was built.</summary>
            Usage = 2,
            /// <summary>'machine test': another run holds the UmRT lock. Nothing was done.</summary>
            Busy = 3,
        }

        internal sealed class Request
        {
            public string Command;
            public List<string> Machines = new List<string>();
            public bool Fresh, Release, Simulation, Parallel;
            public string Platform, Manifest, Work, Tags, JUnit, ResultFile;
            public int? TimeoutS;
        }

        static readonly string[] Options = { "--platform", "--manifest", "--work", "--tags", "--junit", "--timeout-seconds", "--result" };
        static readonly string[] Switches = { "--fresh", "--release", "--simulation", "--parallel" };

        /// <summary>args[0] = "machine". Throws ArgumentException on a command-line problem (pure).</summary>
        internal static Request Parse(string[] args)
        {
            if (args.Length < 2 || (args[1] != "build" && args[1] != "test"))
                throw new ArgumentException("'machine' needs a mode: build or test.");
            var q = new Request { Command = args[1] };
            for (int i = 2; i < args.Length; i++)
            {
                string a = args[i];
                if (Options.Contains(a))
                {
                    if (i + 1 >= args.Length || args[i + 1].StartsWith("--") || args[i + 1] == "-h" || string.IsNullOrWhiteSpace(args[i + 1]))
                        throw new ArgumentException(a + " needs a value.");
                    string v = args[++i];
                    switch (a)
                    {
                        case "--platform": q.Platform = v; break;
                        case "--manifest": q.Manifest = v; break;
                        case "--work": q.Work = v; break;
                        case "--tags": q.Tags = v; break;
                        case "--junit": q.JUnit = v; break;
                        case "--result": q.ResultFile = v; break;
                        case "--timeout-seconds":
                            if (!int.TryParse(v, out int t) || t <= 0)
                                throw new ArgumentException("--timeout-seconds needs a number of seconds (more than 0).");
                            q.TimeoutS = t;
                            break;
                    }
                }
                else if (Switches.Contains(a))
                {
                    if (a == "--fresh") q.Fresh = true;
                    if (a == "--release") q.Release = true;
                    if (a == "--simulation") q.Simulation = true;
                    if (a == "--parallel") q.Parallel = true;
                }
                else if (a.StartsWith("-"))
                    throw new ArgumentException("unknown option " + a + ".");
                else
                    q.Machines.AddRange(a.Split(',').Select(s => s.Trim()).Where(s => s.Length > 0));
            }

            string[] buildOnly = { "--release", "--simulation", "--parallel", "--platform", "--result" };
            string[] testOnly = { "--tags", "--junit", "--timeout-seconds" };
            string wrong = (q.Command == "build" ? testOnly : buildOnly).FirstOrDefault(o => args.Skip(2).Contains(o));
            if (wrong != null)
                throw new ArgumentException(string.Format("'machine {0}' does not take {1}.", q.Command, wrong));
            if (q.Release && q.Simulation)
                throw new ArgumentException("--release and --simulation: a release build never has SIMULATION.");
            if (q.Platform != null && !MachineFamily.Platforms.Contains(q.Platform))
                throw new ArgumentException("--platform must be one of: " + string.Join(", ", MachineFamily.Platforms) + ".");
            string duplicate = q.Machines.GroupBy(m => m, StringComparer.OrdinalIgnoreCase).Where(g => g.Count() > 1).Select(g => g.Key).FirstOrDefault();
            if (duplicate != null)
                throw new ArgumentException("machine '" + duplicate + "' is given more than once.");
            return q;
        }

        public static int Run(string[] args, string shell) => (int)Run(args, shell, Directory.GetCurrentDirectory());

        internal static Exit Run(string[] args, string shell, string cwd)
        {
            Request q;
            MachineManifest manifest;
            List<MachineLayout> layouts;
            try
            {
                q = Parse(args);
                manifest = MachineManifest.Load(MachineManifest.Find(q.Manifest, cwd));
                layouts = Layouts(manifest, q);
            }
            catch (Exception ex) when (ex is ArgumentException || ex is FormatException || ex is IOException
                                       || ex is InvalidOperationException || ex is UnauthorizedAccessException)
            {
                Cli.CliHelp.WriteError(Console.Error, args, ex.Message);
                return Exit.Usage;
            }
            Console.WriteLine("{0}: Machines: {1} (manifest {2})", Now(), string.Join(", ", layouts.Select(l => l.Machine)), manifest.File);
            WarnMissingEsi(layouts);
            return q.Command == "build" ? RunBuild(q, manifest, layouts, shell) : RunTest(q, manifest, layouts, shell);
        }

        /// <summary>The machines asked for (default: all), each with a valid work folder.</summary>
        internal static List<MachineLayout> Layouts(MachineManifest manifest, Request q)
        {
            List<string> machines = q.Machines.Count > 0 ? q.Machines : manifest.AllMachines.ToList();
            var layouts = new List<MachineLayout>();
            foreach (string machine in machines)
            {
                if (manifest.FamilyOf(machine) == null)
                    throw new ArgumentException(string.Format("unknown machine '{0}'. {1} lists: {2}.", machine, MachineManifest.FileName, string.Join(", ", manifest.AllMachines)));
                var layout = new MachineLayout(manifest, machine, simulation: q.Command == "test" || q.Simulation, work: q.Work);
                string problem = BuildLocation.Problem(manifest.Root, layout.Dir);
                if (problem != null)
                    throw new ArgumentException(problem);
                if (!layout.Family.IsNative && !File.Exists(Path.Combine(layout.MachineFolder, "variants.xml")))
                    throw new ArgumentException("no variants.xml in " + layout.MachineFolder);
                if (q.Command == "test" && (layout.Family.Tests == null || !Directory.Exists(MachineSources.RepoPath(manifest.Root, layout.Family.Tests))))
                    throw new ArgumentException(string.Format("family '{0}' has no test folder (\"tests\" in {1}).", layout.Family.Name, MachineManifest.FileName));
                layouts.Add(layout);
            }
            return layouts;
        }

        // ------------------------------------------------------------------ build

        static Exit RunBuild(Request q, MachineManifest manifest, List<MachineLayout> layouts, string shell)
        {
            if (q.Release)
            {
                string problem = ReleaseProblem(manifest.Root, manifest.BuildInputs, layouts, BuildInfo.RunGit, out string tag);
                if (problem != null)
                {
                    Console.WriteLine("RELEASE REFUSED: " + problem);
                    return Exit.Usage;
                }
                MachineBuilder.ReleaseTag = tag;
                Console.WriteLine("{0}: Release build of {1} from tag {2}", Now(), string.Join(", ", layouts.Select(l => l.Machine)), tag);
            }

            List<MachineRow> rows = q.Parallel && layouts.Count > 1
                ? BuildParallel(q, manifest, layouts, shell)
                : layouts.Select(l => BuildOne(BuildRequest(q, manifest, l, shell, activate: false, netId: null))).ToList();

            if (q.ResultFile != null && rows.Count == 1)
                File.WriteAllText(q.ResultFile, new JavaScriptSerializer().Serialize(RowToDictionary(rows[0])));
            return Summary(rows, test: false);
        }

        static MachineBuildRequest BuildRequest(Request q, MachineManifest manifest, MachineLayout l, string shell, bool activate, string netId) =>
            new MachineBuildRequest
            {
                Layout = l,
                Fresh = q.Fresh || q.Release,
                Release = q.Release,
                Simulation = activate || q.Simulation,
                Platform = q.Platform,
                Shell = shell,
                BuildInputs = manifest.BuildInputs,
                Activate = activate,
                ActivateNetId = netId,
                TimeoutS = q.TimeoutS ?? 60,
            };

        /// <summary>One machine build, its output also in its log.</summary>
        static MachineRow BuildOne(MachineBuildRequest r)
        {
            var clock = Stopwatch.StartNew();
            MachineRow row;
            using (TeeWriter.Capture(r.Layout.Log))
            {
                try
                {
                    row = MachineBuilder.Build(r);
                }
                catch (Exception ex) when (ex is InvalidOperationException || ex is IOException || ex is UnauthorizedAccessException
                                           || ex is FormatException || ex is ArgumentException || ex is System.Xml.XmlException)
                {
                    Console.WriteLine("{0}: ERROR: {1}", Now(), ex.Message);
                    row = new MachineRow(r.Layout.Machine) { Build = "FAIL (sources)" };
                }
                row.Time = Minutes(clock.Elapsed);
                Console.WriteLine("{0}: Full log: {1}", Now(), r.Layout.Log);
            }
            return row;
        }

        /// <summary>Several machines at the same time: one bai process (and one shell) per machine,
        /// at most two at once (each shell needs about 1 GB of RAM).</summary>
        static List<MachineRow> BuildParallel(Request q, MachineManifest manifest, List<MachineLayout> layouts, string shell)
        {
            const int MaxParallel = 2;
            string folder = Path.Combine(layouts[0].FamilyWork, "_parallel");
            Directory.CreateDirectory(folder);
            string self = Process.GetCurrentProcess().MainModule.FileName;
            var running = new List<(MachineLayout Layout, Process Process, StreamWriter Out)>();
            Console.WriteLine("{0}: Building {1} machines in parallel (at most {2} at once)...", Now(), layouts.Count, MaxParallel);
            foreach (MachineLayout l in layouts)
            {
                while (running.Count(p => !p.Process.HasExited) >= MaxParallel)
                    System.Threading.Thread.Sleep(2000);
                var argv = new List<string> { "machine", "build", l.Machine, "--manifest", manifest.File, "--result", Path.Combine(folder, l.Machine + ".json") };
                if (q.Fresh) argv.Add("--fresh");
                if (q.Release) argv.Add("--release");
                if (q.Simulation) argv.Add("--simulation");
                if (q.Platform != null) argv.AddRange(new[] { "--platform", q.Platform });
                if (q.Work != null) argv.AddRange(new[] { "--work", Path.GetFullPath(q.Work) });
                if (shell != null) argv.AddRange(new[] { "--shell", shell });
                File.Delete(Path.Combine(folder, l.Machine + ".json"));
                var output = new StreamWriter(Path.Combine(folder, l.Machine + ".out"), false, new UTF8Encoding(false)) { AutoFlush = true };
                var psi = new ProcessStartInfo(self, string.Join(" ", argv.Select(BuildInfo.Quote)))
                {
                    UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true,
                };
                Process p = Process.Start(psi);
                p.OutputDataReceived += (s, e) => { if (e.Data != null) lock (output) output.WriteLine(e.Data); };
                p.ErrorDataReceived += (s, e) => { if (e.Data != null) lock (output) output.WriteLine(e.Data); };
                p.BeginOutputReadLine();
                p.BeginErrorReadLine();
                running.Add((l, p, output));
                Console.WriteLine("{0}:   started build: {1} (pid {2})", Now(), l.Machine, p.Id);
            }
            var rows = new List<MachineRow>();
            foreach (var (l, p, output) in running)
            {
                p.WaitForExit();
                output.Dispose();
                Console.WriteLine(File.ReadAllText(Path.Combine(folder, l.Machine + ".out")));
                string result = Path.Combine(folder, l.Machine + ".json");
                rows.Add(File.Exists(result)
                    ? RowFromDictionary(new JavaScriptSerializer().DeserializeObject(File.ReadAllText(result)) as Dictionary<string, object>, l.Machine)
                    : new MachineRow(l.Machine) { Parse = "FAIL (no result)" });
            }
            return rows;
        }

        /// <summary>Null when a release build may start; else why not. A release comes from a clean
        /// tree with a tag on HEAD, and its manifests have no SIMULATION and no test adapter.</summary>
        internal static string ReleaseProblem(string repoRoot, IReadOnlyList<string> buildInputs, IEnumerable<MachineLayout> layouts,
            BuildInfo.GitRunner git, out string tag)
        {
            tag = null;
            var status = git(repoRoot, new[] { "status", "--porcelain", "--" }.Concat(buildInputs).ToArray());
            if (status.Code != 0)
                return "git status failed (is this a git work tree?).";
            string dirty = status.Lines.FirstOrDefault(s => s.Trim().Length > 0);
            if (dirty != null)
                return string.Format("uncommitted changes in {0} (for example: {1}).", string.Join(", ", buildInputs), dirty.Trim());
            var describe = git(repoRoot, "describe", "--exact-match", "--tags", "HEAD");
            if (describe.Code != 0 || describe.Lines.Count == 0)
                return "HEAD has no tag. Tag the commit first (git tag -a vX.Y.Z).";
            tag = describe.Lines[0].Trim();
            foreach (MachineLayout l in layouts)
            {
                try { MachineChecks.AssertReleaseManifests(l.MachineFolder, l.Family.IsNative); }
                catch (Exception ex) when (ex is InvalidOperationException || ex is IOException || ex is System.Xml.XmlException)
                {
                    return l.Machine + ": " + ex.Message;
                }
            }
            return null;
        }

        // ------------------------------------------------------------------ test

        static Exit RunTest(Request q, MachineManifest manifest, List<MachineLayout> layouts, string shell)
        {
            string netId;
            try
            {
                netId = Runtime.RuntimeTargets.NetId(Runtime.RuntimeTargets.UmRt);
            }
            catch (Exception ex) when (ex is InvalidOperationException || ex is FormatException || ex is IOException)
            {
                Console.Error.WriteLine("ERROR: " + ex.Message);
                return Exit.Failed;
            }

            // One run on UmRT_Default at a time: hold the lock file open with no sharing until the end.
            using (FileStream lockFile = TryLock(manifest.Lock))
            {
                if (lockFile == null)
                {
                    Console.Error.WriteLine("UmRT_Default is in use by another run (lock: {0}). Try again later.", manifest.Lock);
                    return Exit.Busy;
                }
                if (manifest.PreCheck.Count > 0)
                {
                    ConsoleReport.Heading("Source check");
                    if (MachineBuilder.RunCommand(manifest.PreCheck, manifest.Root) != 0)
                    {
                        Console.WriteLine("Source check FAILED; no build or runtime activation performed.");
                        return Exit.Failed;
                    }
                    Console.WriteLine("Source check PASSED.");
                }
                var rows = new List<MachineRow>();
                foreach (MachineLayout l in layouts)
                    rows.Add(TestOne(q, manifest, l, shell, netId, several: layouts.Count > 1));
                return Summary(rows, test: true);
            }
        }

        /// <summary>Build with SIMULATION (kept project by default) and activate on UmRT in the same
        /// session, then verify expect.json and run the scenarios.</summary>
        static MachineRow TestOne(Request q, MachineManifest manifest, MachineLayout l, string shell, string netId, bool several)
        {
            var clock = Stopwatch.StartNew();
            MachineRow row = null;
            using (TeeWriter.Capture(l.Log))
            {
                try
                {
                    // A native family with NC axes needs its runtime licenses on UmRT_Default (a 7-day
                    // trial): check before the long build, not after a failed activation.
                    string license = l.Family.IsNative && l.Family.Native.RuntimeLicenses.Count > 0
                        ? NativeProject.LicenseProblem(l.Family.Native.RuntimeLicenses, NativeProject.UmRtLicenseFolder(), DateTime.Now) : null;
                    if (license != null)
                    {
                        Console.WriteLine("{0}: ERROR: {1}", Now(), license);
                        row = new MachineRow(l.Machine) { Activate = "FAIL (license)" };
                        row.Time = Minutes(clock.Elapsed);
                        return row;
                    }
                    row = MachineBuilder.Build(BuildRequest(q, manifest, l, shell, activate: true, netId: netId));
                    if (row.Activate == "PASS")
                    {
                        Console.WriteLine();
                        ConsoleReport.Heading(l.Machine + " - verification and test scenarios");
                        string tmc = MachineChecks.CompiledTmc(l.Tsproj);
                        List<Dictionary<string, object>> identity = MachineChecks.CompiledBuildExpectations(File.ReadAllText(tmc), l.Machine);
                        // A native family may have no expect.json: then only the compiled build identity is checked.
                        string expect = Path.Combine(l.MachineFolder, "expect.json");
                        File.WriteAllText(l.StartupExpect, l.Family.IsNative && !File.Exists(expect)
                            ? MachineChecks.IdentityExpectations(l.Machine, identity)
                            : MachineChecks.StartupExpectations(File.ReadAllText(expect), l.Machine, identity));
                        row.Verify = Runtime.RuntimeCommands.Verify(new[] { "verify", "--target", "umrt", "--expect", l.StartupExpect, "--variant", l.Machine }) == 0 ? "PASS" : "FAIL";
                        if (row.Verify == "PASS")
                        {
                            string tests = MachineSources.RepoPath(manifest.Root, l.Family.Tests);
                            List<NativeProject.FileEntry> scenarios = NativeProject.Inventory(tests);
                            row.Tests = RunScenarios(q, manifest, l, several, out int executed);
                            if (NativeProject.Difference(scenarios, NativeProject.Inventory(tests)) != null)
                                row.Tests = "FAIL (scenarios changed during the run)";
                            if (row.Tests.StartsWith("PASS"))
                                Console.WriteLine("{0}: Validation record: {1}", Now(), MachineChecks.WriteValidationRecord(l.ValidationRecord,
                                    l, q.Tags, executed, scenarios, identity, tmc, Process.GetCurrentProcess().MainModule.FileName));
                        }
                    }
                }
                catch (Exception ex) when (ex is InvalidOperationException || ex is IOException || ex is UnauthorizedAccessException
                                           || ex is FormatException || ex is ArgumentException || ex is System.Xml.XmlException)
                {
                    Console.WriteLine("{0}: ERROR: {1}", Now(), ex.Message);
                    if (row == null)
                        row = new MachineRow(l.Machine) { Build = "FAIL (sources)" };
                    else
                        row.Verify = "FAIL (startup checks)";
                }
                row.Time = Minutes(clock.Elapsed);
                Console.WriteLine("{0}: Full log: {1}", Now(), l.Log);
            }
            return row;
        }

        static string RunScenarios(Request q, MachineManifest manifest, MachineLayout l, bool several, out int executed)
        {
            executed = 0;
            string junit = q.JUnit == null ? l.DefaultJUnit : MachineChecks.JUnitFor(q.JUnit, l.Machine, several);
            // Never accept the result of an earlier run when the tool writes no report.
            if (File.Exists(junit))
                File.Delete(junit);
            var argv = new List<string> { "test", "--target", "umrt", "--scenarios", MachineSources.RepoPath(manifest.Root, l.Family.Tests) };
            // A native family has no variants: every scenario applies.
            if (!l.Family.IsNative)
                argv.AddRange(new[] { "--variants-file", Path.Combine(l.MachineFolder, "variants.xml"), "--variant", l.Machine });
            argv.AddRange(new[] { "--junit", junit });
            if (q.Tags != null) argv.AddRange(new[] { "--tags", q.Tags });
            if (q.TimeoutS != null) argv.AddRange(new[] { "--timeout-seconds", q.TimeoutS.Value.ToString() });
            int code = Runtime.RuntimeCommands.Test(argv.ToArray());
            if (code != 0)
                return code == 1 ? "FAIL" : "ERROR (" + code + ")";
            try
            {
                executed = MachineChecks.AssertScenarioResults(junit);
                return "PASS (" + executed + " executed)";
            }
            catch (InvalidOperationException ex)
            {
                Console.WriteLine(ex.Message);
                return "FAIL (JUnit)";
            }
        }

        /// <summary>The UmRT lock: null when another run holds it.</summary>
        internal static FileStream TryLock(string path)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            try { return new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None); }
            catch (IOException) { return null; }
        }

        // ------------------------------------------------------------------ summary

        internal static Exit ExitCode(IReadOnlyCollection<MachineRow> rows, bool test) =>
            rows.Count > 0 && rows.All(r => test ? r.TestPassed : r.BuildPassed) ? Exit.Passed : Exit.Failed;

        static Exit Summary(List<MachineRow> rows, bool test)
        {
            Console.WriteLine();
            ConsoleReport.Heading("Machine summary");
            Console.WriteLine(Table(rows, test));
            Exit exit = ExitCode(rows, test);
            if (exit == Exit.Passed)
                Console.WriteLine(test ? "ALL INTEGRATION TESTS PASSED" : "ALL MACHINES PASSED");
            else
                Console.WriteLine("FAILED: " + string.Join(", ", rows.Where(r => !(test ? r.TestPassed : r.BuildPassed)).Select(r => r.Machine)));
            return exit;
        }

        internal static string Table(IReadOnlyList<MachineRow> rows, bool test)
        {
            var header = new List<string> { "Machine", "Mode", "Parse", "Build", "Links" };
            if (test) header.AddRange(new[] { "Activate", "Verify", "Tests" });
            header.Add("Time");
            List<List<string>> lines = rows.Select(r =>
            {
                var cells = new List<string> { r.Machine, r.Mode, r.Parse, r.Build, r.Links };
                if (test) cells.AddRange(new[] { r.Activate, r.Verify, r.Tests });
                cells.Add(r.Time);
                return cells;
            }).ToList();
            int[] widths = header.Select((h, i) => Math.Max(h.Length, lines.Select(c => c[i].Length).DefaultIfEmpty(0).Max())).ToArray();
            string Line(IList<string> cells) => string.Join("  ", cells.Select((c, i) => c.PadRight(widths[i]))).TrimEnd();
            var sb = new StringBuilder();
            sb.AppendLine(Line(header));
            sb.AppendLine(Line(widths.Select(w => new string('-', w)).ToList()));
            foreach (List<string> l in lines)
                sb.AppendLine(Line(l));
            return sb.ToString().TrimEnd();
        }

        static string Minutes(TimeSpan t) => string.Format("{0:00}:{1:00}", (int)t.TotalMinutes, t.Seconds);

        internal static Dictionary<string, object> RowToDictionary(MachineRow r) => new Dictionary<string, object>
        {
            ["machine"] = r.Machine, ["mode"] = r.Mode, ["parse"] = r.Parse, ["build"] = r.Build, ["links"] = r.Links,
            ["activate"] = r.Activate, ["verify"] = r.Verify, ["tests"] = r.Tests, ["time"] = r.Time,
        };

        internal static MachineRow RowFromDictionary(Dictionary<string, object> d, string machine)
        {
            if (d == null)
                return new MachineRow(machine) { Parse = "FAIL (no result)" };
            string Get(string key) => d.TryGetValue(key, out object v) && v is string s ? s : "-";
            return new MachineRow(machine)
            {
                Mode = Get("mode"), Parse = Get("parse"), Build = Get("build"), Links = Get("links"),
                Activate = Get("activate"), Verify = Get("verify"), Tests = Get("tests"), Time = Get("time"),
            };
        }

        /// <summary>Third-party EtherCAT devices need their ESI file in TwinCAT's ESI folder, or the
        /// I/O stage cannot make their boxes (Machines/&lt;family&gt;/esi/).</summary>
        static void WarnMissingEsi(IReadOnlyList<MachineLayout> layouts)
        {
            string system = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), @"Beckhoff\TwinCAT\3.1\Config\Io\EtherCAT");
            string user = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), @"Beckhoff\TwinCAT\3.1\Config\IO\EtherCAT");
            foreach (MachineFamily family in layouts.Select(l => l.Family).Distinct())
            {
                List<string> missing = MachineChecks.MissingEsi(MachineSources.RepoPath(layouts[0].RepoRoot, family.Shared), new[] { system, user });
                if (missing.Count > 0)
                    Console.WriteLine("WARNING: ESI file(s) of {0} not installed in {1}: {2}. Install them first (bai esi install).",
                        family.Name, system, string.Join(", ", missing));
            }
        }
    }
}
