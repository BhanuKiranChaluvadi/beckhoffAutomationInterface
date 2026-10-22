using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Web.Script.Serialization;
using BeckhoffAutomationInterface.Sync;
using static BeckhoffAutomationInterface.Clock;

namespace BeckhoffAutomationInterface.Machines
{
    /// <summary>One line of the summary table.</summary>
    sealed class MachineRow
    {
        public string Machine;
        public string Mode = "-";
        public string Parse = "-";
        public string Build = "-";
        public string Links = "-";
        public string Activate = "-";
        public string Verify = "-";
        public string Tests = "-";
        public string Time = "-";
        /// <summary>The shell crashed or hung (RPC error or timeout): retried once, fresh.</summary>
        public bool Crashed;

        public MachineRow(string machine) { Machine = machine; }

        public bool BuildPassed => Parse == "PASS" && Build.StartsWith("PASS") && !Links.StartsWith("FAIL");
        public bool TestPassed => BuildPassed && Activate == "PASS" && Verify == "PASS" && Tests.StartsWith("PASS");
    }

    /// <summary>What a machine build must do.</summary>
    sealed class MachineBuildRequest
    {
        public MachineLayout Layout;
        public bool Fresh;
        public bool Release;
        public bool Simulation;
        public string Platform;
        public string Shell;
        public IReadOnlyList<string> BuildInputs = MachineManifest.DefaultBuildInputs;
        /// <summary>Activate on UmRT in the same session after the build (machine test).</summary>
        public bool Activate;
        public string ActivateNetId;
        public int TimeoutS = 60;
    }

    /// <summary>The saved state of a kept project: what it was built from and how it ended.</summary>
    sealed class MachineState
    {
        public string Stamp;
        public bool Simulation;
        /// <summary>running (the process ended during the build), passed, failed or crashed.</summary>
        public string Result;

        public static MachineState Read(string file)
        {
            try
            {
                if (!File.Exists(file))
                    return null;
                var d = new JavaScriptSerializer().DeserializeObject(File.ReadAllText(file)) as Dictionary<string, object>;
                return d == null ? null : new MachineState
                {
                    Stamp = d.TryGetValue("stamp", out object s) ? s as string : null,
                    Simulation = d.TryGetValue("simulation", out object m) && m is bool b && b,
                    Result = d.TryGetValue("result", out object r) ? r as string : null,
                };
            }
            catch (ArgumentException) { return null; }
        }

        public void Write(string file) =>
            File.WriteAllText(file, new JavaScriptSerializer().Serialize(new Dictionary<string, object>
                { ["stamp"] = Stamp, ["simulation"] = Simulation, ["result"] = Result }));

        /// <summary>A kept project can be used: same build mode and the last run did not end in a crash or in the middle.</summary>
        internal static bool Usable(MachineState s, bool simulation) =>
            s != null && s.Stamp != null && s.Simulation == simulation && (s.Result == "passed" || s.Result == "failed");
    }

    /// <summary>
    /// Builds one machine: assemble the source folder, write the build identity, parse (no
    /// TwinCAT), then init a fresh project or sync the kept one and build it, in ONE TwinCAT
    /// session. With Activate, the same session also activates the project on UmRT (no second
    /// shell, no second full compile). The results are values (PipelineOutcome), not log text.
    /// </summary>
    static class MachineBuilder
    {
        const string SimulationDefine = "SIMULATION";

        public static MachineRow Build(MachineBuildRequest r) => Build(r, retried: false, forceFresh: false, freshReason: null);

        static MachineRow Build(MachineBuildRequest r, bool retried, bool forceFresh, string freshReason)
        {
            MachineLayout l = r.Layout;
            var row = new MachineRow(l.Machine);
            BuildLocation.Assert(l.RepoRoot, l.Dir);
            Console.WriteLine();
            // A native family: every build is the full native project (I/O, NC axes, safety). The
            // simulation copy (-sim) has simulated NC axes and SIMULATION.
            bool native = l.Family.IsNative;
            bool nativeConfig = native;
            ConsoleReport.Heading(l.Machine +
                (r.Simulation ? (native ? " (native, simulation: SIMULATION and simulated NC axes)" : " (simulation)") : native ? " (native)" : ""));
            List<NativeProject.FileEntry> safetyBefore = nativeConfig ? NativeProject.Inventory(NativeProject.SafetySource(l)) : null;

            // 1. Kept or fresh. All checks before TwinCAT starts.
            MachineState state = MachineState.Read(l.StateFile);
            bool keep = !r.Fresh && !r.Release && !forceFresh && File.Exists(l.Sln) && Directory.Exists(l.Src) && MachineState.Usable(state, r.Simulation);
            row.Mode = keep ? "kept" : freshReason ?? "fresh";
            if (keep)
            {
                List<string> gone = MachineSources.GoneStFiles(l.RepoRoot, l.Src, l.Family.Sources);
                if (gone.Count > 0)
                {
                    Console.WriteLine("{0}: {1} .st file(s) deleted or renamed since the last build (for example {2}): building fresh.", Now(), gone.Count, gone[0]);
                    keep = false;
                    row.Mode = "fresh (file gone)";
                }
            }
            if (keep)
            {
                MachineSources.Assemble(l, RunCommand);
                if (MachineSources.Stamp(l.Src) != state.Stamp)
                {
                    Console.WriteLine("{0}: A manifest (I/O tree, links, variants, libraries, events, tasks, safety) changed: building fresh.", Now());
                    keep = false;
                    row.Mode = "fresh (manifest changed)";
                }
            }
            List<StPouSource> parsed = null;
            if (keep)
            {
                parsed = Parse(l.Src, row);
                if (parsed == null)
                    return row;
                List<string> previous = KnownNamesTracker.Read(Path.Combine(l.Src, ".st-known-names"));
                List<string> stale = previous == null ? new List<string>() : KnownNamesTracker.DiffFull(previous, KnownNamesTracker.CollectNames(parsed));
                if (stale.Count > 0)
                {
                    Console.WriteLine("{0}: {1} object(s) are in the kept project but no longer in the source (for example {2}): building fresh.", Now(), stale.Count, stale[0]);
                    keep = false;
                    row.Mode = "fresh (drift)";
                }
            }
            if (!keep)
            {
                if (Directory.Exists(l.Dir))
                {
                    if (state != null && state.Result != "passed")
                        Console.WriteLine("{0}: Previous failed build kept: {1}", Now(), BuildLocation.SaveFailedEvidence(l.FamilyWork, l.Dir, l.Machine));
                    BuildLocation.Delete(l.Dir);
                }
                MachineSources.Assemble(l, RunCommand);
                parsed = Parse(l.Src, row);
                if (parsed == null)
                    return row;
            }

            // 2. The build identity, in the build copy only.
            string buildInfo = MachineSources.RepoPath(l.Src, l.Family.BuildInfo);
            BuildInfo.Write(buildInfo, BuildInfo.FromGit(l.RepoRoot, l.Machine, r.BuildInputs, BuildInfo.RunGit, DateTime.UtcNow));

            // 3. One TwinCAT session: init or sync, build, (activate).
            new MachineState { Stamp = MachineSources.Stamp(l.Src), Simulation = r.Simulation, Result = "running" }.Write(l.StateFile);
            // A fresh native build bootstraps the project and applies the native configuration
            // first: the main session then syncs into that project (not 'init').
            var options = Options(r, init: !keep && !nativeConfig);
            int declaredLinks = MachineChecks.DeclaredLinks(options.IoManifestPath) + MachineChecks.DeclaredFileLinks(options.VarLinksManifestPath);
            string phase = "build";
            PipelineOutcome outcome = null;
            Environment.ExitCode = 0;
            try
            {
                if (nativeConfig && !keep)
                {
                    phase = "bootstrap";
                    NativeProject.Bootstrap(l, r.Shell, r.Platform ?? l.Family.Platform);
                    phase = "build";
                }
                else if (nativeConfig)
                    NativeProject.CopySafety(l); // kept: only files whose bytes differ (XAE is closed)
                using (var session = new TwinCatSession(options))
                {
                    var pipeline = new SyncPipeline(session, options, IgnoreRules.Load(options.SourceFolder, options.IgnorePatterns));
                    outcome = pipeline.Outcome;
                    pipeline.AfterBuild = s =>
                    {
                        // A kept project with I/O items the manifest no longer has: build fresh, activate that one.
                        if (IsDrift(keep, outcome, ioFromManifest: !native))
                            return;
                        if (!r.Simulation && !CheckNoSimulationDefine(s, options))
                        {
                            Environment.ExitCode = 1;
                            return;
                        }
                        if (!r.Activate)
                            return;
                        string links = MachineChecks.LinkResult(declaredLinks, outcome);
                        if (links.StartsWith("FAIL"))
                        {
                            Console.WriteLine("{0}: Not activated: links {1}.", Now(), links);
                            return;
                        }
                        phase = "activate";
                        Console.WriteLine("{0}: Activate on UmRT_Default (SIMULATION, I/O disabled), same session.", Now());
                        row.Activate = Runtime.RuntimeCommands.ActivateOpenProject(s.Vs, s.SysManager, options.PlcProjectName, options.Variant,
                            new[] { SimulationDefine }, Runtime.RuntimeTargets.UmRt, r.ActivateNetId, r.TimeoutS, p => p) ? "PASS" : "FAIL";
                    };
                    pipeline.Run();
                }
            }
            catch (COMException ex) when (IsShellCrash(ex.HResult))
            {
                Console.WriteLine("{0}: ERROR: the TwinCAT shell crashed or stopped answering ({1}): {2}", Now(), phase, ex.Message);
                row.Crashed = true;
            }
            catch (Exception ex) when (ex is InvalidOperationException || ex is IOException || ex is UnauthorizedAccessException
                                       || ex is FormatException || ex is ArgumentException || ex is System.Xml.XmlException || ex is COMException)
            {
                Console.WriteLine("{0}: ERROR ({1}): {2}", Now(), phase, ex.Data[TreeItemFactory.CreationContextKey] ?? ex.Message);
                if (phase == "activate")
                    row.Activate = "FAIL";
                else
                    Environment.ExitCode = 1;
            }
            if (outcome != null && outcome.BuildTimedOut)
                row.Crashed = true;
            bool failed = Environment.ExitCode != 0 || outcome == null || !outcome.BuildPassed;
            Environment.ExitCode = 0;
            string safetyProblem = nativeConfig && !row.Crashed && outcome != null ? NativeProject.SafetyProblem(l, safetyBefore) : null;
            if (safetyProblem != null)
            {
                Console.WriteLine("{0}: SAFETY FAILED — {1}", Now(), safetyProblem);
                failed = true;
            }
            // Simulated NC axes only in the -sim project, never in a normal (release) project.
            string axesProblem = nativeConfig && !row.Crashed && outcome != null ? NativeProject.AxesProblem(l.Tsproj, r.Simulation) : null;
            if (axesProblem != null)
            {
                Console.WriteLine("{0}: NC AXES FAILED — {1}", Now(), axesProblem);
                failed = true;
            }

            new MachineState { Stamp = MachineSources.Stamp(l.Src), Simulation = r.Simulation, Result = row.Crashed ? "crashed" : failed ? "failed" : "passed" }.Write(l.StateFile);

            if (!row.Crashed && IsDrift(keep, outcome, ioFromManifest: !native))
            {
                Console.WriteLine("{0}: The kept project has objects the source no longer has: building it fresh.", Now());
                return Build(r, retried, forceFresh: true, freshReason: "fresh (drift)");
            }
            if (row.Crashed)
            {
                if (!retried)
                {
                    // The fresh build keeps the failed one in _failed (its state is 'crashed') before it deletes it.
                    Console.WriteLine("{0}: {1}: the shell crashed or hung; building once more, fresh.", Now(), l.Machine);
                    MachineRow again = Build(r, retried: true, forceFresh: true, freshReason: "fresh (retry)");
                    if (again.Build.StartsWith("PASS"))
                        again.Build += " (retry)";
                    return again;
                }
                row.Build = "FAIL (shell crashed)";
                return row;
            }

            row.Build = safetyProblem != null ? "FAIL (safety changed)" : axesProblem != null ? "FAIL (NC axes)" : failed ? "FAIL" : "PASS";
            row.Links = outcome == null ? "-" : MachineChecks.LinkResult(declaredLinks, outcome);
            if (r.Release && row.BuildPassed && !BuildInfo.ShowsCleanTag(File.ReadAllText(buildInfo), ReleaseTag ?? ""))
            {
                Console.WriteLine("{0}: {1}: GVL_BuildInfo does not show tag {2} with bDirty FALSE.", Now(), l.Machine, ReleaseTag);
                row.Build = "FAIL (build info)";
            }
            if (r.Release && row.BuildPassed)
                Console.WriteLine("{0}: {1}: release project {2} (compiled boot files: {3})", Now(), l.Machine, Path.GetDirectoryName(l.Tsproj),
                    Path.Combine(Path.GetDirectoryName(l.Tsproj), "_Boot", r.Platform ?? l.Family.Platform));
            return row;
        }

        /// <summary>The release tag (set by the command after its release checks).</summary>
        internal static string ReleaseTag { get; set; }

        static RunOptions Options(MachineBuildRequest r, bool init)
        {
            MachineLayout l = r.Layout;
            return new RunOptions(
                sourceFolder: l.Src, destinationFolder: l.Dir, projectName: l.Machine, stages: SyncStages.All,
                init: init, checkEvents: false, parseOnly: false, ignorePatterns: new string[0], exportObjectName: null,
                exportLinks: false, formatCheck: false, confirmDeleteIo: false, reverseExports: ReverseExports.None,
                overwrite: false, plcProjectName: l.Family.Plc, existingTsprojPath: null, configFileLoaded: false,
                shell: r.Shell, clean: false, sourceFolderExplicit: true, variant: l.Family.IsNative ? null : l.Machine, allVariants: false,
                defines: r.Simulation ? new[] { SimulationDefine } : new string[0], targetPlatform: r.Platform ?? l.Family.Platform);
        }

        /// <summary>A build that is not a simulation build must not have SIMULATION. (A simulation
        /// build adds it before the compile; 'verify' then checks GVL_Simulation.bActive.)</summary>
        static bool CheckNoSimulationDefine(TwinCatSession s, RunOptions options)
        {
            string defines = CompilerDefines.Read(s.SysManager.LookupTreeItem(options.ProjectRootPath).ProduceXml());
            if (!HasDefine(defines, SimulationDefine))
                return true;
            Console.WriteLine("{0}: BUILD FAILED — compiler defines '{1}' contain SIMULATION, but this is not a simulation build.", Now(), defines);
            return false;
        }

        /// <summary>A kept project that the sync found different from the source: objects whose
        /// source is gone ([drift]) or I/O items no longer in io-devices.xml (I/O warnings). The
        /// I/O of a native family is not in io-devices.xml: its I/O warnings are no drift.</summary>
        internal static bool IsDrift(bool kept, PipelineOutcome o, bool ioFromManifest = true) =>
            kept && o != null && (o.StaleNames > 0 || (ioFromManifest && o.IoWarnings > 0));

        internal static bool HasDefine(string defines, string name) =>
            (defines ?? "").Split(',').Select(d => d.Trim()).Contains(name, StringComparer.OrdinalIgnoreCase);

        /// <summary>The shell is gone or could not start: retry once, fresh (a compile error is never retried).</summary>
        internal static bool IsShellCrash(int hresult) =>
            BuildRunner.IsShellGone(hresult) || (uint)hresult == 0x80080005; // CO_E_SERVER_EXEC_FAILURE

        /// <summary>'check parse' without TwinCAT: null (row.Parse = FAIL) on a parse error or a duplicate object.</summary>
        static List<StPouSource> Parse(string src, MachineRow row)
        {
            List<StPouSource> parsed = Program.ParseAllStFiles(src, IgnoreRules.Load(src, new string[0]), out List<string> failures, out int filesOk);
            List<string> duplicates = DuplicateObjects.Find(parsed);
            Console.WriteLine("{0}: Parse: {1} file(s) OK ({2} PLC objects), {3} failed, {4} duplicate(s).", Now(), filesOk, parsed.Count, failures.Count, duplicates.Count);
            foreach (string f in failures)
                Console.WriteLine(f);
            foreach (string d in duplicates)
                Console.WriteLine("    ! DUPLICATE {0}", d);
            row.Parse = failures.Count == 0 && duplicates.Count == 0 ? "PASS" : "FAIL";
            return row.Parse == "PASS" ? parsed : null;
        }

        /// <summary>Runs a command in a folder; its output goes through Console (and so into the log).</summary>
        internal static int RunCommand(IReadOnlyList<string> command, string workingFolder)
        {
            var psi = new ProcessStartInfo(command[0], string.Join(" ", command.Skip(1).Select(BuildInfo.Quote)))
            {
                UseShellExecute = false,
                WorkingDirectory = workingFolder,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
            };
            Console.WriteLine("{0}: > {1}", Now(), string.Join(" ", command));
            try
            {
                using (Process p = Process.Start(psi))
                {
                    object gate = new object();
                    p.OutputDataReceived += (s, e) => { if (e.Data != null) lock (gate) Console.WriteLine("    {0}", e.Data); };
                    p.ErrorDataReceived += (s, e) => { if (e.Data != null) lock (gate) Console.WriteLine("    {0}", e.Data); };
                    p.BeginOutputReadLine();
                    p.BeginErrorReadLine();
                    p.WaitForExit();
                    return p.ExitCode;
                }
            }
            catch (System.ComponentModel.Win32Exception ex)
            {
                Console.WriteLine("{0}: ERROR: cannot start '{1}': {2}", Now(), command[0], ex.Message);
                return -1;
            }
        }
    }

    /// <summary>Writes to two writers (the console and the machine's run.log).</summary>
    sealed class TeeWriter : TextWriter
    {
        readonly TextWriter _a, _b;
        public TeeWriter(TextWriter a, TextWriter b) { _a = a; _b = b; }
        public override Encoding Encoding => _a.Encoding;
        public override void Write(char value) { _a.Write(value); _b.Write(value); }
        public override void Write(string value) { _a.Write(value); _b.Write(value); }
        public override void WriteLine(string value) { _a.WriteLine(value); _b.WriteLine(value); }
        public override void Flush() { _a.Flush(); _b.Flush(); }

        /// <summary>Console output and errors also go to 'file' until the result is disposed. An
        /// existing file becomes &lt;name&gt;.previous.log.</summary>
        public static IDisposable Capture(string file)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(file));
            if (File.Exists(file))
            {
                string previous = Path.ChangeExtension(file, ".previous.log");
                if (File.Exists(previous))
                    File.Delete(previous);
                File.Move(file, previous);
            }
            var log = new StreamWriter(file, append: false, encoding: new UTF8Encoding(false)) { AutoFlush = true };
            TextWriter output = Console.Out, error = Console.Error;
            Console.SetOut(TextWriter.Synchronized(new TeeWriter(output, log)));
            Console.SetError(TextWriter.Synchronized(new TeeWriter(error, log)));
            return new Restore(() =>
            {
                Console.SetOut(output);
                Console.SetError(error);
                log.Dispose();
            });
        }

        sealed class Restore : IDisposable
        {
            readonly Action _action;
            public Restore(Action action) { _action = action; }
            public void Dispose() => _action();
        }
    }
}
