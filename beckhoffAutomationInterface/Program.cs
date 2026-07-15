using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using BeckhoffAutomationInterface.Sync;
using Interop.TCatSysManager;

namespace BeckhoffAutomationInterface
{
    /// <summary>
    /// Syncs ST/Shark/*.st files into a persistent "Shark" TwinCAT PLC project via
    /// the Automation Interface: opens the project if it already exists (instead of
    /// recreating it every run), reconciles POUs (create/update/delete) using
    /// Sync.PouSyncEngine, then builds and reports pass/fail via Sync.BuildRunner.
    ///
    /// This is the MVP engine described in docs/ideas/st-source-twincat-sync.md,
    /// assembled from spikes validated end-to-end on 2026-07-04.
    /// </summary>
    class Program
    {
        const uint RPC_E_SERVERCALL_RETRYLATER = 0x8001010A;
        const int RETRY_TIMEOUT_MS = 30000;
        const int RETRY_INTERVAL_MS = 1000;

        static string Now() => DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");

        /// <summary>Parses every .st file under the source folder without opening Visual
        /// Studio, aggregating and printing all parser failures. Returns a process exit code
        /// (0 = all parsed, 1 = one or more failed). Used by the --parse-only preflight.</summary>
        static int ParseOnly(string stSourceFolder, IgnoreRules ignore)
        {
            var failures = new List<string>();
            var parsed = new List<Sync.StPouSource>();
            int ok = 0;
            foreach (string file in Sync.StFileParser.GetStFiles(stSourceFolder, ignore))
            {
                try
                {
                    parsed.AddRange(Sync.StFileParser.ParseFile(file));
                    ok++;
                }
                catch (Exception ex)
                {
                    string rel = file.Substring(stSourceFolder.Length).TrimStart('\\', '/');
                    failures.Add($"  {rel}\n      {ex.Message}");
                }
            }

            Console.WriteLine("{0}: [parse-only] {1} file(s) parsed OK ({2} PLC objects), {3} failed.",
                Now(), ok, parsed.Count, failures.Count);
            foreach (string f in failures)
                Console.WriteLine(f);

            List<string> lintIssues = Sync.StLinter.Lint(parsed);
            if (lintIssues.Count > 0)
            {
                Console.WriteLine("{0}: [lint] {1} naming convention warning(s):", Now(), lintIssues.Count);
                foreach (string issue in lintIssues)
                    Console.WriteLine("    ! {0}", issue);
            }

            return failures.Count == 0 ? 0 : 1;
        }

        /// <summary>
        /// Retries a COM call that can transiently fail with RPC_E_SERVERCALL_RETRYLATER
        /// (0x8001010A) when Visual Studio's background compiler/IntelliSense is still
        /// busy right after a large batch of tree changes (observed after syncing 16+
        /// PLC objects in one pass, immediately followed by a library reference call).
        /// </summary>
        static void RetryOnBusy(Action action, string description)
        {
            int elapsed = 0;
            while (true)
            {
                try
                {
                    action();
                    return;
                }
                catch (COMException ex) when ((uint)ex.HResult == RPC_E_SERVERCALL_RETRYLATER && elapsed < RETRY_TIMEOUT_MS)
                {
                    Console.WriteLine("{0}: Visual Studio is busy ({1}), retrying in 1s... ({2}s elapsed)",
                        Now(), description, elapsed / 1000);
                    Thread.Sleep(RETRY_INTERVAL_MS);
                    elapsed += RETRY_INTERVAL_MS;
                }
            }
        }

        [STAThread]
        static void Main(string[] args)
        {
            RunOptions options = RunOptions.Parse(args);
            Console.WriteLine("{0}: Source='{1}'  Dest='{2}'  Project='{3}'", Now(), options.SourceFolder, options.DestinationFolder, options.ProjectName);
            IgnoreRules ignore = IgnoreRules.Load(options.SourceFolder, options.IgnorePatterns);

            // Fast preflight: parse all .st files WITHOUT opening Visual Studio, so parser
            // issues surface in seconds (not after a ~40s VS round-trip). Run with --parse-only.
            if (options.ParseOnly)
            {
                Environment.Exit(ParseOnly(options.SourceFolder, ignore));
            }

            // Read-only style check, also no VS needed. Dry-run only — see Sync/StFormatter.cs.
            if (options.FormatCheck)
            {
                List<FormatIssue> formatIssues = StFormatter.CheckFolder(options.SourceFolder, ignore);
                Console.WriteLine("{0}: [format-check] {1} issue(s) found.", Now(), formatIssues.Count);
                foreach (FormatIssue issue in formatIssues)
                    Console.WriteLine("    ! {0}: {1}", issue.RelativePath, issue.Description);
                Environment.Exit(0);
            }

            // Read-only "declared vs actual" Event Class preflight (reads the .tsproj
            // directly, no VS session needed). Informational on a normal run — the Events
            // stage creates anything missing via TsprojEventClassEditor later. Under
            // --check-events it's the whole run: report and exit, code 1 if anything is
            // missing (usable as a fast CI/pipeline gate).
            int missingEventClassCount = 0;
            if (File.Exists(options.TsprojFilePath))
            {
                var desiredEventClasses = EventManifestParser.Parse(options.EventManifestPath);
                if (desiredEventClasses.Count > 0)
                {
                    EventClassCheckReport eventReport = EventClassChecker.Check(options.TsprojFilePath, desiredEventClasses);
                    missingEventClassCount = eventReport.Missing.Count;
                    Console.WriteLine("{0}: Event class check: {1} declared, {2} present, {3} missing.",
                        Now(), desiredEventClasses.Count, eventReport.Present.Count, eventReport.Missing.Count);
                    foreach (string name in eventReport.Missing)
                        Console.WriteLine("    ! MISSING '{0}' — will be created by the events stage (--sync-events or a full run).", name);
                }
            }
            if (options.CheckEvents)
            {
                Console.WriteLine("{0}: --check-events: event class check complete, skipping Visual Studio.", Now());
                Environment.Exit(missingEventClassCount > 0 ? 1 : 0);
            }

            // Fail fast (before opening Visual Studio) if --incremental has nothing to diff
            // against — no point paying the ~30-40s VS round-trip just to refuse.
            // Only relevant when the Code stage will actually run.
            if (options.Incremental && options.Stages.HasFlag(SyncStages.Code) && SyncState.Read(options.SyncStatePath) == null)
            {
                Console.Error.WriteLine("ERROR: --incremental requested but no baseline found at '{0}'.", options.SyncStatePath);
                Console.Error.WriteLine("Run a full sync (without --incremental) first to establish one.");
                Environment.Exit(1);
            }

            // Pre-flight checks
            if (!File.Exists(options.TwinCatTemplate))
            {
                Console.Error.WriteLine("ERROR: TwinCAT project template not found at:");
                Console.Error.WriteLine("  {0}", options.TwinCatTemplate);
                Console.Error.WriteLine("Ensure TwinCAT 3.1 XAE is installed.");
                Environment.Exit(1);
            }

            // Creating a NEW project is explicit (--init), never a silent fallback: a
            // mistyped --dest/--name used to quietly bootstrap a fresh empty project
            // (and once planted one inside the real project's own folder tree — see
            // tasks/archive/2026-07-14-post-review-hardening/). In CI, a wrong path now
            // fails loudly instead of green-building an empty project.
            if (!File.Exists(options.SolutionFilePath) && !options.Init)
            {
                Console.Error.WriteLine("ERROR: solution not found at:");
                Console.Error.WriteLine("  {0}", options.SolutionFilePath);
                Console.Error.WriteLine("Check --source/--dest/--name point at the intended project, or pass --init");
                Console.Error.WriteLine("to create a brand-new solution/TwinCAT/PLC project at that path.");
                Environment.Exit(1);
            }

            // The Visual Studio session is owned by TwinCatSession: stages open it
            // lazily and the pipeline legitimately closes/reopens it mid-run for the
            // direct .tsproj edits — `using` here just guarantees final disposal.
            using (var session = new TwinCatSession(options))
            {
                RunSync(session, options, ignore);
            }
        }

        /// <summary>
        /// Executes the selected stages (see SyncStages; all of them when no stage flag
        /// was given) in a fixed three-phase order regardless of flag order:
        ///   Phase A (VS open):    code -> libraries -> io-tree
        ///   Phase B (VS CLOSED):  direct .tsproj edits (io's Create-PLC-Data-Type
        ///                         templates, events' Event Classes)
        ///   Phase C (VS open):    io's variable links, build
        /// Visual Studio is opened lazily by whichever stage first needs it — a run
        /// selecting only the Events stage is a pure file edit and never launches VS.
        /// </summary>
        static void RunSync(TwinCatSession session, RunOptions options, IgnoreRules ignore)
        {
            if (options.ExportObjectName != null)
            {
                session.EnsureOpen();
                ExportObject(session, options);
                return;
            }

            SyncStages stages = options.Stages;

            // Phase A — stages that work through the open project's Automation Interface.
            if (stages.HasFlag(SyncStages.Code))
            {
                session.EnsureOpen();
                SyncCode(session, options, ignore);
            }
            if (stages.HasFlag(SyncStages.Libraries))
            {
                session.EnsureOpen();
                SyncLibraries(session, options);
            }
            List<IoDeviceSpec> desiredIoDevices = null;
            if (stages.HasFlag(SyncStages.Io))
            {
                session.EnsureOpen();
                desiredIoDevices = SyncIoTree(session, options);
            }

            // Phase B — direct .tsproj file edits, which need VS closed (no DTE holding
            // the file). Contributes nothing (and touches nothing) unless the Io stage
            // declared Create-PLC-Data-Type targets and/or the Events stage found
            // missing Event Classes.
            ApplyTsprojEdits(session, options, desiredIoDevices, includeEvents: stages.HasFlag(SyncStages.Events));

            // Phase C — back through the Automation Interface, reopening VS lazily.
            if (stages.HasFlag(SyncStages.Io))
                SyncLinks(session, options);
            if (stages.HasFlag(SyncStages.Build))
            {
                session.EnsureOpen();
                RunBuild(session, options, ignore);
            }
        }

        /// <summary>--export: write the named live PLC object's text back to its mirrored
        /// .st file. Uses Environment.ExitCode (not Environment.Exit) on error paths so
        /// Main's `using` still disposes/closes Visual Studio properly.</summary>
        static void ExportObject(TwinCatSession session, RunOptions options)
        {
            List<ITcSmTreeItem> matches = PlcObjectExporter.FindByName(session.SysManager.LookupTreeItem(options.ProjectRootPath), options.ExportObjectName);
            if (matches.Count == 0)
            {
                Console.Error.WriteLine("ERROR: no PLC object named '{0}' found.", options.ExportObjectName);
                Environment.ExitCode = 1;
                return;
            }
            if (matches.Count > 1)
            {
                Console.Error.WriteLine("ERROR: {0} object(s) named '{1}' found — ambiguous.", matches.Count, options.ExportObjectName);
                Environment.ExitCode = 1;
                return;
            }

            ITcSmTreeItem item = matches[0];
            if (!PlcObjectExporter.IsSupported(item))
            {
                Console.Error.WriteLine("ERROR: export of '{0}' ({1}) is not yet supported.", item.Name, item.ItemSubTypeName);
                Environment.ExitCode = 1;
                return;
            }

            string text = PlcObjectExporter.Export(item);
            string relativeFolder = PlcObjectExporter.GetRelativeFolder(item, options.ProjectRootPath);
            string folderPath = Path.Combine(options.SourceFolder, relativeFolder.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(folderPath);
            string filePath = Path.Combine(folderPath, item.Name + ".st");
            File.WriteAllText(filePath, text);

            Console.WriteLine("{0}: Exported '{1}' -> '{2}'.", Now(), item.Name, filePath);
        }

        /// <summary>Code stage: .st files -> PLC POUs (create/update/delete), plus the
        /// warn-only drift detection, the naming lint, and the sync/known-names
        /// baselines. --incremental narrows the parse to files git says changed/were
        /// deleted since the recorded baseline.</summary>
        static void SyncCode(TwinCatSession session, RunOptions options, IgnoreRules ignore)
        {
            List<StPouSource> desiredPous;
            if (options.Incremental)
            {
                string lastSha = SyncState.Read(options.SyncStatePath);

                Console.WriteLine("{0}: Computing .st changes since {1}...", Now(), lastSha);
                GitDiffResult diff = GitDiffHelper.GetChangedStFiles(options.SourceFolder, lastSha);

                desiredPous = new List<StPouSource>();
                foreach (string relativePath in diff.Changed)
                {
                    if (ignore.IsIgnored(relativePath))
                        continue;
                    string fullPath = Path.Combine(options.SourceFolder, relativePath.Replace('/', Path.DirectorySeparatorChar));
                    string relativeFolder = Path.GetDirectoryName(relativePath)?.Replace('\\', '/') ?? "";
                    foreach (StPouSource source in StFileParser.ParseFile(fullPath))
                    {
                        source.RelativeFolder = relativeFolder;
                        desiredPous.Add(source);
                    }
                }

                Console.WriteLine("{0}: {1} file(s) changed, {2} file(s) deleted since last sync.",
                    Now(), diff.Changed.Count, diff.Deleted.Count);
                if (diff.Deleted.Count > 0)
                {
                    if (options.ConfirmDelete)
                    {
                        List<DeleteResult> deleteResults = IncrementalDeleter.Delete(session.SysManager, options.ProjectRootPath, diff.Deleted);
                        Console.WriteLine("{0}: --confirm-delete: {1} of {2} deleted PLC object(s) removed.",
                            Now(), deleteResults.Count(r => r.Deleted), deleteResults.Count);
                        foreach (DeleteResult r in deleteResults)
                        {
                            if (r.Deleted)
                                Console.WriteLine("    - deleted  {0}", r.ObjectName);
                            else
                                Console.WriteLine("    ! skipped  {0} ({1})", r.ObjectName, r.SkipReason);
                        }
                    }
                    else
                    {
                        Console.WriteLine("{0}: NOTE: the following PLC object(s) still exist — remove manually, or re-run with --confirm-delete:", Now());
                        foreach (string f in diff.Deleted) Console.WriteLine("    ! {0}", f);
                    }
                }
            }
            else
            {
                Console.WriteLine("{0}: Parsing .st sources from '{1}'...", Now(), options.SourceFolder);
                desiredPous = StFileParser.ParseFolder(options.SourceFolder, ignore);
            }

            // Warn-only drift detection (see tasks/archive/2026-07-14-post-review-hardening/,
            // Tasks 7-8 decision: warn always, prune opt-in): a previously-synced name
            // missing from the current parse means its .st source was renamed/deleted, but
            // the PLC object still compiles silently. Report it — never delete (deletion
            // stays behind --incremental --confirm-delete for whole top-level objects only).
            List<string> currentNames = KnownNamesTracker.CollectNames(desiredPous);
            List<string> previousNames = KnownNamesTracker.Read(options.KnownNamesPath);
            var staleNames = new List<string>();
            if (previousNames != null)
            {
                staleNames = options.Incremental
                    ? KnownNamesTracker.DiffWithinOwners(previousNames, currentNames)
                    : KnownNamesTracker.DiffFull(previousNames, currentNames);
                if (staleNames.Count > 0)
                {
                    Console.WriteLine("{0}: [drift] {1} previously-synced name(s) no longer in .st source (renamed or deleted?) — still compiling in the PLC project:", Now(), staleNames.Count);
                    foreach (string name in staleNames)
                        Console.WriteLine("    ! stale    {0}", name);
                }
            }

            List<string> lintIssues = StLinter.Lint(desiredPous);
            if (lintIssues.Count > 0)
            {
                Console.WriteLine("{0}: [lint] {1} naming convention warning(s):", Now(), lintIssues.Count);
                foreach (string issue in lintIssues)
                    Console.WriteLine("    ! {0}", issue);
            }

            Console.WriteLine("{0}: Syncing {1} PLC object(s)...", Now(), desiredPous.Count);
            var syncEngine = new PouSyncEngine(session.SysManager, options.ProjectRootPath);
            SyncReport syncReport = syncEngine.Sync(desiredPous);

            foreach (string name in syncReport.Created) Console.WriteLine("    + created  {0}", name);
            foreach (string name in syncReport.Updated) Console.WriteLine("    ~ updated  {0}", name);
            foreach (string name in syncReport.Deleted) Console.WriteLine("    - deleted  {0}", name);

            session.Project.Save();
            Console.WriteLine("{0}: Sync complete ({1} created, {2} updated, {3} deleted).",
                Now(), syncReport.Created.Count, syncReport.Updated.Count, syncReport.Deleted.Count);

            // Record the new sync baseline (best-effort; silently skipped if SourceFolder
            // isn't inside a git repo, since --incremental is opt-in and needs one anyway).
            string headSha = GitDiffHelper.TryGetHeadSha(options.SourceFolder);
            if (headSha != null)
            {
                SyncState.Write(options.SyncStatePath, headSha);
                Console.WriteLine("{0}: Recorded sync baseline {1} in '{2}'.", Now(), headSha, options.SyncStatePath);
            }

            // Record the known-names drift baseline (see the [drift] warning block above).
            // Full sync: the parse covered everything, so record it verbatim. Incremental:
            // fold the partial parse into the previous record, dropping proven-stale names.
            KnownNamesTracker.Write(options.KnownNamesPath, options.Incremental
                ? KnownNamesTracker.Merge(previousNames ?? new List<string>(), currentNames, staleNames)
                : currentNames);
        }

        /// <summary>Libraries stage: libraries.xml -> PLC library references (config
        /// data, not .st source).</summary>
        static void SyncLibraries(TwinCatSession session, RunOptions options)
        {
            Console.WriteLine("{0}: Parsing library manifest '{1}'...", Now(), options.LibraryManifestPath);
            var desiredLibraries = LibraryManifestParser.Parse(options.LibraryManifestPath);

            Console.WriteLine("{0}: Syncing {1} library reference(s)...", Now(), desiredLibraries.Count);
            ITcSmTreeItem referencesItem = session.SysManager.LookupTreeItem(options.ReferencesTreePath);
            ITcPlcLibraryManager libManager = (ITcPlcLibraryManager)referencesItem;
            LibrarySyncReport libraryReport = null;
            RetryOnBusy(() => libraryReport = LibrarySyncEngine.Sync(libManager, desiredLibraries), "syncing library references");

            foreach (string name in libraryReport.Added) Console.WriteLine("    + added    {0}", name);
            foreach (string name in libraryReport.Removed) Console.WriteLine("    - removed  {0}", name);

            session.Project.Save();
            Console.WriteLine("{0}: Library sync complete ({1} added, {2} removed).",
                Now(), libraryReport.Added.Count, libraryReport.Removed.Count);
        }

        /// <summary>Io stage, part 1: reconcile the I/O hardware tree (Device -> Box ->
        /// Terminal) from io-devices.xml. Idempotent; orphans are only WARNED about
        /// unless --confirm-delete-io (see IoSyncEngine). Returns the parsed manifest so
        /// the tsproj-edit step can collect Create-PLC-Data-Type targets from it.</summary>
        static List<IoDeviceSpec> SyncIoTree(TwinCatSession session, RunOptions options)
        {
            Console.WriteLine("{0}: Parsing IO manifest '{1}'...", Now(), options.IoManifestPath);
            var desiredIoDevices = IoManifestParser.Parse(options.IoManifestPath);

            Console.WriteLine("{0}: Syncing {1} IO device(s)...", Now(), desiredIoDevices.Count);
            IoSyncReport ioReport = null;
            RetryOnBusy(() => ioReport = IoSyncEngine.Sync(session.SysManager, desiredIoDevices, options.ConfirmDeleteIo), "syncing IO tree");

            foreach (string name in ioReport.Created) Console.WriteLine("    + created  {0}", name);
            foreach (string name in ioReport.Deleted) Console.WriteLine("    - deleted  {0}", name);
            foreach (string change in ioReport.StateChanged) Console.WriteLine("    ~ state    {0}", change);
            foreach (string warning in ioReport.Warnings) Console.WriteLine("    !! WARNING {0}", warning);

            session.Project.Save();
            Console.WriteLine("{0}: IO sync complete ({1} created, {2} deleted, {3} state change(s), {4} warning(s)).",
                Now(), ioReport.Created.Count, ioReport.Deleted.Count, ioReport.StateChanged.Count, ioReport.Warnings.Count);
            return desiredIoDevices;
        }

        /// <summary>Direct .tsproj file edits — the two settings with no Automation
        /// Interface path (confirmed; see TsprojPlcDataTypeEditor/TsprojEventClassEditor):
        /// "Create PLC Data Type" for terminals declaring CreatePlcType (Io stage), and
        /// missing Event Classes from events.xml (Events stage, gated on includeEvents).
        /// Both MUST happen while the project is NOT open in Visual Studio, hence
        /// closing the session around them; later stages reopen it lazily when they
        /// need it. desiredIoDevices is null when the Io stage didn't run.</summary>
        static void ApplyTsprojEdits(TwinCatSession session, RunOptions options, List<IoDeviceSpec> desiredIoDevices, bool includeEvents)
        {
            List<PlcDataTypeTarget> plcDataTypeTargets = desiredIoDevices != null
                ? PlcDataTypeTarget.CollectFrom(desiredIoDevices)
                : new List<PlcDataTypeTarget>();

            List<string> missingEventClasses = new List<string>();
            if (includeEvents)
            {
                var desiredEventClasses = EventManifestParser.Parse(options.EventManifestPath);
                if (desiredEventClasses.Count > 0)
                    missingEventClasses = EventClassChecker.Check(options.TsprojFilePath, desiredEventClasses).Missing;
                if (missingEventClasses.Count == 0)
                    Console.WriteLine("{0}: Event classes: all declared classes already present.", Now());
            }

            if (plcDataTypeTargets.Count == 0 && missingEventClasses.Count == 0)
                return;

            if (session.IsOpen)
            {
                Console.WriteLine("{0}: Closing Visual Studio to edit the project file directly ({1} terminal(s), {2} event class(es))...",
                    Now(), plcDataTypeTargets.Count, missingEventClasses.Count);
                session.EnsureClosed();
            }

            if (plcDataTypeTargets.Count > 0)
            {
                PlcDataTypeEditResult editResult = TsprojPlcDataTypeEditor.Apply(options.TsprojFilePath, plcDataTypeTargets, options.PlcDataTypesFolder);
                foreach (string name in editResult.Applied) Console.WriteLine("    ~ set       {0}", name);
                foreach (string warning in editResult.Warnings) Console.WriteLine("    !! WARNING {0}", warning);
                Console.WriteLine("{0}: Create PLC Data Type complete ({1} applied, {2} warning(s)).",
                    Now(), editResult.Applied.Count, editResult.Warnings.Count);
            }

            if (missingEventClasses.Count > 0)
            {
                EventClassEditResult eventEditResult = TsprojEventClassEditor.Apply(options.TsprojFilePath, missingEventClasses, options.EventClassesFolder);
                foreach (string name in eventEditResult.Applied) Console.WriteLine("    ~ added     {0}", name);
                foreach (string warning in eventEditResult.Warnings) Console.WriteLine("    !! WARNING {0}", warning);
                Console.WriteLine("{0}: Event Class sync complete ({1} applied, {2} warning(s)).",
                    Now(), eventEditResult.Applied.Count, eventEditResult.Warnings.Count);
            }
            // No eager reopen: whichever later stage needs Visual Studio (links, build)
            // calls session.EnsureOpen() itself, and an events-only run just ends here.
        }

        /// <summary>Io stage, part 2: PLC-variable to IO-channel links declared in the
        /// Links section of io-devices.xml. If any declared link can't be resolved (the
        /// PLC instance image / EtherCAT channels only materialize after Activate
        /// Configuration on a real or simulated target), all masters are disabled as a
        /// fallback so the build stays green and unattended.</summary>
        static void SyncLinks(TwinCatSession session, RunOptions options)
        {
            var desiredLinks = IoManifestParser.ParseLinks(options.IoManifestPath);
            if (desiredLinks.Count == 0)
                return;

            session.EnsureOpen(); // lazily reopens after the .tsproj edits closed VS
            Console.WriteLine("{0}: Syncing {1} variable link(s)...", Now(), desiredLinks.Count);
            VariableLinkReport linkReport = null;
            RetryOnBusy(() => linkReport = VariableLinkEngine.Sync(session.SysManager, options.ProjectName, desiredLinks), "linking variables");

            foreach (string s in linkReport.Linked) Console.WriteLine("    + linked   {0}", s);
            foreach (string s in linkReport.Failed) Console.WriteLine("    x unlinked {0}", s);

            if (!linkReport.AllLinked)
            {
                Console.WriteLine("{0}: Some links unresolved (the PLC instance image / EtherCAT channels", Now());
                Console.WriteLine("        require Activate Configuration against a real or simulated target).");
                List<string> disabled = IoSyncEngine.DisableAllMasters(session.SysManager);
                foreach (string name in disabled)
                    Console.WriteLine("        ~ disabled master '{0}' to keep the build green.", name);
            }
            session.Project.Save();
            Console.WriteLine("{0}: Variable link sync complete ({1} linked, {2} unresolved).",
                Now(), linkReport.Linked.Count, linkReport.Failed.Count);
        }

        /// <summary>Build stage: compile and report, with error locations translated back
        /// to the original .st path/line (see Sync/ErrorLocationResolver.cs). Unmapped
        /// locations print raw, clearly labeled — never silently dropped.</summary>
        static void RunBuild(TwinCatSession session, RunOptions options, IgnoreRules ignore)
        {
            Console.WriteLine("{0}: Building solution...", Now());
            BuildReport buildReport = null;
            try
            {
                RetryOnBusy(() => buildReport = BuildRunner.Build(session.Dte), "building solution");
            }
            catch (BuildTimeoutException ex)
            {
                Console.WriteLine("{0}: BUILD TIMED OUT — {1}", Now(), ex.Message);
                Environment.ExitCode = 1; // CI contract: anything but BUILD PASSED is non-zero
                return; // Main's using will force-close VS (dialog is still up)
            }

            if (buildReport.Success)
            {
                Console.WriteLine("{0}: BUILD PASSED — project compiled cleanly with no errors.", Now());
            }
            else
            {
                Environment.ExitCode = 1; // CI contract: build failure = exit code 1
                Dictionary<string, StPouSource> provenance = TryBuildProvenanceIndex(options, ignore);
                Console.WriteLine("{0}: BUILD FAILED — {1} error(s):", Now(), buildReport.Errors.Count);
                foreach (BuildError error in buildReport.Errors)
                    Console.WriteLine("    [ERROR] {0} {1}", error.Description, FormatErrorLocation(error, provenance));

                if (buildReport.Warnings.Count > 0)
                {
                    Console.WriteLine("{0}: {1} warning(s):", Now(), buildReport.Warnings.Count);
                    foreach (BuildError warning in buildReport.Warnings)
                        Console.WriteLine("    [WARN] {0} {1}", warning.Description, FormatErrorLocation(warning, provenance));
                }
                return;
            }

            if (buildReport.Warnings.Count > 0)
            {
                Dictionary<string, StPouSource> provenance = TryBuildProvenanceIndex(options, ignore);
                Console.WriteLine("{0}: {1} warning(s):", Now(), buildReport.Warnings.Count);
                foreach (BuildError warning in buildReport.Warnings)
                    Console.WriteLine("    [WARN] {0} {1}", warning.Description, FormatErrorLocation(warning, provenance));
            }
        }

        /// <summary>Fresh full parse of the source folder for error mapping \u2014 independent
        /// of the sync's own (possibly incremental/partial) parse so errors in unchanged
        /// files still map. Best-effort: a parse failure just means raw locations.</summary>
        static Dictionary<string, StPouSource> TryBuildProvenanceIndex(RunOptions options, IgnoreRules ignore)
        {
            try
            {
                return ErrorLocationResolver.BuildIndex(StFileParser.ParseFolder(options.SourceFolder, ignore));
            }
            catch (Exception)
            {
                return new Dictionary<string, StPouSource>(StringComparer.OrdinalIgnoreCase);
            }
        }

        static string FormatErrorLocation(BuildError error, Dictionary<string, StPouSource> provenance)
        {
            ResolvedErrorLocation loc = ErrorLocationResolver.Resolve(error.FileName, error.Line, provenance);
            return loc.Mapped
                ? string.Format("({0}:{1})", loc.Path, loc.Line)
                : string.Format("({0}:{1}) [unmapped \u2014 raw TwinCAT location]", error.FileName, error.Line);
        }
    }
}
