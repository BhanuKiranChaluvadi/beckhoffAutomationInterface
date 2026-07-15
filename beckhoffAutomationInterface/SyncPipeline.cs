using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using BeckhoffAutomationInterface.Sync;
using Interop.TCatSysManager;
using static BeckhoffAutomationInterface.Clock;
using static BeckhoffAutomationInterface.ConsoleReport;

namespace BeckhoffAutomationInterface
{
    /// <summary>
    /// Runs the selected pipeline stages (see SyncStages) against a TwinCAT project: .st
    /// source -> POUs, libraries.xml -> library references, io-devices.xml -> IO tree +
    /// Create-PLC-Data-Type + links, events.xml -> Event Classes, and the final build.
    ///
    /// Extracted from Program so "parse args and preflight" (Program.Main) is a separate
    /// concern from "what actually gets synced" — this class owns all per-run state
    /// (session/options/ignore) as constructor-injected fields instead of threading them
    /// through every stage method's parameter list, the way the original monolithic
    /// Program.RunSync did.
    /// </summary>
    class SyncPipeline
    {
        const int RETRY_TIMEOUT_MS = 30000;
        const int RETRY_INTERVAL_MS = 1000;

        readonly TwinCatSession _session;
        readonly RunOptions _options;
        readonly IgnoreRules _ignore;

        public SyncPipeline(TwinCatSession session, RunOptions options, IgnoreRules ignore)
        {
            _session = session;
            _options = options;
            _ignore = ignore;
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
        public void Run()
        {
            if (_options.ExportObjectName != null)
            {
                _session.EnsureOpen();
                ExportObject();
                return;
            }

            SyncStages stages = _options.Stages;

            // Phase A — stages that work through the open project's Automation Interface.
            if (stages.HasFlag(SyncStages.Code))
            {
                _session.EnsureOpen();
                SyncCode();
            }
            if (stages.HasFlag(SyncStages.Libraries))
            {
                _session.EnsureOpen();
                SyncLibraries();
            }
            List<IoDeviceSpec> desiredIoDevices = null;
            if (stages.HasFlag(SyncStages.Io))
            {
                _session.EnsureOpen();
                desiredIoDevices = SyncIoTree();
            }

            // Phase B — direct .tsproj file edits, which need VS closed (no DTE holding
            // the file). Contributes nothing (and touches nothing) unless the Io stage
            // declared Create-PLC-Data-Type targets and/or the Events stage found
            // missing Event Classes.
            ApplyTsprojEdits(desiredIoDevices, includeEvents: stages.HasFlag(SyncStages.Events));

            // Phase C — back through the Automation Interface, reopening VS lazily.
            if (stages.HasFlag(SyncStages.Io))
                SyncLinks();
            if (stages.HasFlag(SyncStages.Build))
            {
                _session.EnsureOpen();
                RunBuild();
            }
        }

        /// <summary>--export: write the named live PLC object's text back to its mirrored
        /// .st file. Uses Environment.ExitCode (not Environment.Exit) on error paths so
        /// Main's `using` still disposes/closes Visual Studio properly.</summary>
        void ExportObject()
        {
            List<ITcSmTreeItem> matches = PlcObjectExporter.FindByName(_session.SysManager.LookupTreeItem(_options.ProjectRootPath), _options.ExportObjectName);
            if (matches.Count == 0)
            {
                Console.Error.WriteLine("ERROR: no PLC object named '{0}' found.", _options.ExportObjectName);
                Environment.ExitCode = 1;
                return;
            }
            if (matches.Count > 1)
            {
                Console.Error.WriteLine("ERROR: {0} object(s) named '{1}' found — ambiguous.", matches.Count, _options.ExportObjectName);
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
            string relativeFolder = PlcObjectExporter.GetRelativeFolder(item, _options.ProjectRootPath);
            string folderPath = Path.Combine(_options.SourceFolder, relativeFolder.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(folderPath);
            string filePath = Path.Combine(folderPath, item.Name + ".st");
            File.WriteAllText(filePath, text);

            Console.WriteLine("{0}: Exported '{1}' -> '{2}'.", Now(), item.Name, filePath);
        }

        /// <summary>Code stage: .st files -> PLC POUs (create/update/delete), plus the
        /// warn-only drift detection, the naming lint, and the sync/known-names
        /// baselines. --incremental narrows the parse to files git says changed/were
        /// deleted since the recorded baseline.</summary>
        void SyncCode()
        {
            List<StPouSource> desiredPous;
            if (_options.Incremental)
            {
                string lastSha = SyncState.Read(_options.SyncStatePath);

                Console.WriteLine("{0}: Computing .st changes since {1}...", Now(), lastSha);
                GitDiffResult diff = GitDiffHelper.GetChangedStFiles(_options.SourceFolder, lastSha);

                desiredPous = new List<StPouSource>();
                foreach (string relativePath in diff.Changed)
                {
                    if (_ignore.IsIgnored(relativePath))
                        continue;
                    string fullPath = Path.Combine(_options.SourceFolder, relativePath.Replace('/', Path.DirectorySeparatorChar));
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
                    if (_options.ConfirmDelete)
                    {
                        List<DeleteResult> deleteResults = IncrementalDeleter.Delete(_session.SysManager, _options.ProjectRootPath, diff.Deleted);
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
                        PrintLines("! ", diff.Deleted);
                    }
                }
            }
            else
            {
                Console.WriteLine("{0}: Parsing .st sources from '{1}'...", Now(), _options.SourceFolder);
                desiredPous = StFileParser.ParseFolder(_options.SourceFolder, _ignore);
            }

            // Warn-only drift detection (see tasks/archive/2026-07-14-post-review-hardening/,
            // Tasks 7-8 decision: warn always, prune opt-in): a previously-synced name
            // missing from the current parse means its .st source was renamed/deleted, but
            // the PLC object still compiles silently. Report it — never delete (deletion
            // stays behind --incremental --confirm-delete for whole top-level objects only).
            List<string> currentNames = KnownNamesTracker.CollectNames(desiredPous);
            List<string> previousNames = KnownNamesTracker.Read(_options.KnownNamesPath);
            var staleNames = new List<string>();
            if (previousNames != null)
            {
                staleNames = _options.Incremental
                    ? KnownNamesTracker.DiffWithinOwners(previousNames, currentNames)
                    : KnownNamesTracker.DiffFull(previousNames, currentNames);
                if (staleNames.Count > 0)
                {
                    Console.WriteLine("{0}: [drift] {1} previously-synced name(s) no longer in .st source (renamed or deleted?) — still compiling in the PLC project:", Now(), staleNames.Count);
                    PrintLines("! stale    ", staleNames);
                }
            }

            List<string> lintIssues = StLinter.Lint(desiredPous);
            if (lintIssues.Count > 0)
            {
                Console.WriteLine("{0}: [lint] {1} naming convention warning(s):", Now(), lintIssues.Count);
                PrintLines("! ", lintIssues);
            }

            Console.WriteLine("{0}: Syncing {1} PLC object(s)...", Now(), desiredPous.Count);
            var syncEngine = new PouSyncEngine(_session.SysManager, _options.ProjectRootPath);
            SyncReport syncReport = syncEngine.Sync(desiredPous);

            PrintLines("+ created  ", syncReport.Created);
            PrintLines("~ updated  ", syncReport.Updated);
            PrintLines("- deleted  ", syncReport.Deleted);

            _session.Project.Save();
            Console.WriteLine("{0}: Sync complete ({1} created, {2} updated, {3} deleted).",
                Now(), syncReport.Created.Count, syncReport.Updated.Count, syncReport.Deleted.Count);

            // Record the new sync baseline (best-effort; silently skipped if SourceFolder
            // isn't inside a git repo, since --incremental is opt-in and needs one anyway).
            string headSha = GitDiffHelper.TryGetHeadSha(_options.SourceFolder);
            if (headSha != null)
            {
                SyncState.Write(_options.SyncStatePath, headSha);
                Console.WriteLine("{0}: Recorded sync baseline {1} in '{2}'.", Now(), headSha, _options.SyncStatePath);
            }

            // Record the known-names drift baseline (see the [drift] warning block above).
            // Full sync: the parse covered everything, so record it verbatim. Incremental:
            // fold the partial parse into the previous record, dropping proven-stale names.
            KnownNamesTracker.Write(_options.KnownNamesPath, _options.Incremental
                ? KnownNamesTracker.Merge(previousNames ?? new List<string>(), currentNames, staleNames)
                : currentNames);
        }

        /// <summary>Libraries stage: libraries.xml -> PLC library references (config
        /// data, not .st source).</summary>
        void SyncLibraries()
        {
            Console.WriteLine("{0}: Parsing library manifest '{1}'...", Now(), _options.LibraryManifestPath);
            var desiredLibraries = LibraryManifestParser.Parse(_options.LibraryManifestPath);

            Console.WriteLine("{0}: Syncing {1} library reference(s)...", Now(), desiredLibraries.Count);
            ITcSmTreeItem referencesItem = _session.SysManager.LookupTreeItem(_options.ReferencesTreePath);
            ITcPlcLibraryManager libManager = (ITcPlcLibraryManager)referencesItem;
            LibrarySyncReport libraryReport = null;
            RetryOnBusy(() => libraryReport = LibrarySyncEngine.Sync(libManager, desiredLibraries), "syncing library references");

            PrintLines("+ added    ", libraryReport.Added);
            PrintLines("- removed  ", libraryReport.Removed);

            _session.Project.Save();
            Console.WriteLine("{0}: Library sync complete ({1} added, {2} removed).",
                Now(), libraryReport.Added.Count, libraryReport.Removed.Count);
        }

        /// <summary>Io stage, part 1: reconcile the I/O hardware tree (Device -> Box ->
        /// Terminal) from io-devices.xml. Idempotent; orphans are only WARNED about
        /// unless --confirm-delete-io (see IoSyncEngine). Returns the parsed manifest so
        /// the tsproj-edit step can collect Create-PLC-Data-Type targets from it.</summary>
        List<IoDeviceSpec> SyncIoTree()
        {
            Console.WriteLine("{0}: Parsing IO manifest '{1}'...", Now(), _options.IoManifestPath);
            var desiredIoDevices = IoManifestParser.Parse(_options.IoManifestPath);

            Console.WriteLine("{0}: Syncing {1} IO device(s)...", Now(), desiredIoDevices.Count);
            IoSyncReport ioReport = null;
            RetryOnBusy(() => ioReport = IoSyncEngine.Sync(_session.SysManager, desiredIoDevices, _options.ConfirmDeleteIo), "syncing IO tree");

            PrintLines("+ created  ", ioReport.Created);
            PrintLines("- deleted  ", ioReport.Deleted);
            PrintLines("~ state    ", ioReport.StateChanged);
            PrintLines("!! WARNING ", ioReport.Warnings);

            _session.Project.Save();
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
        void ApplyTsprojEdits(List<IoDeviceSpec> desiredIoDevices, bool includeEvents)
        {
            List<PlcDataTypeTarget> plcDataTypeTargets = desiredIoDevices != null
                ? PlcDataTypeTarget.CollectFrom(desiredIoDevices)
                : new List<PlcDataTypeTarget>();

            List<string> missingEventClasses = new List<string>();
            if (includeEvents)
            {
                var desiredEventClasses = EventManifestParser.Parse(_options.EventManifestPath);
                if (desiredEventClasses.Count > 0)
                    missingEventClasses = EventClassChecker.Check(_options.TsprojFilePath, desiredEventClasses).Missing;
                if (missingEventClasses.Count == 0)
                    Console.WriteLine("{0}: Event classes: all declared classes already present.", Now());
            }

            if (plcDataTypeTargets.Count == 0 && missingEventClasses.Count == 0)
                return;

            if (_session.IsOpen)
            {
                Console.WriteLine("{0}: Closing Visual Studio to edit the project file directly ({1} terminal(s), {2} event class(es))...",
                    Now(), plcDataTypeTargets.Count, missingEventClasses.Count);
                _session.EnsureClosed();
            }

            if (plcDataTypeTargets.Count > 0)
            {
                PlcDataTypeEditResult editResult = TsprojPlcDataTypeEditor.Apply(_options.TsprojFilePath, plcDataTypeTargets, _options.PlcDataTypesFolder);
                PrintLines("~ set       ", editResult.Applied);
                PrintLines("!! WARNING ", editResult.Warnings);
                Console.WriteLine("{0}: Create PLC Data Type complete ({1} applied, {2} warning(s)).",
                    Now(), editResult.Applied.Count, editResult.Warnings.Count);
            }

            if (missingEventClasses.Count > 0)
            {
                EventClassEditResult eventEditResult = TsprojEventClassEditor.Apply(_options.TsprojFilePath, missingEventClasses, _options.EventClassesFolder);
                PrintLines("~ added     ", eventEditResult.Applied);
                PrintLines("!! WARNING ", eventEditResult.Warnings);
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
        void SyncLinks()
        {
            var desiredLinks = IoManifestParser.ParseLinks(_options.IoManifestPath);
            if (desiredLinks.Count == 0)
                return;

            _session.EnsureOpen(); // lazily reopens after the .tsproj edits closed VS
            Console.WriteLine("{0}: Syncing {1} variable link(s)...", Now(), desiredLinks.Count);
            VariableLinkReport linkReport = null;
            RetryOnBusy(() => linkReport = VariableLinkEngine.Sync(_session.SysManager, _options.ProjectName, desiredLinks), "linking variables");

            PrintLines("+ linked   ", linkReport.Linked);
            PrintLines("x unlinked ", linkReport.Failed);

            if (!linkReport.AllLinked)
            {
                Console.WriteLine("{0}: Some links unresolved (the PLC instance image / EtherCAT channels", Now());
                Console.WriteLine("        require Activate Configuration against a real or simulated target).");
                List<string> disabled = IoSyncEngine.DisableAllMasters(_session.SysManager);
                foreach (string name in disabled)
                    Console.WriteLine("        ~ disabled master '{0}' to keep the build green.", name);
            }
            _session.Project.Save();
            Console.WriteLine("{0}: Variable link sync complete ({1} linked, {2} unresolved).",
                Now(), linkReport.Linked.Count, linkReport.Failed.Count);
        }

        /// <summary>Build stage: compile and report, with error locations translated back
        /// to the original .st path/line (see Sync/ErrorLocationResolver.cs). Unmapped
        /// locations print raw, clearly labeled — never silently dropped.</summary>
        void RunBuild()
        {
            Console.WriteLine("{0}: Building solution...", Now());
            BuildReport buildReport = null;
            try
            {
                RetryOnBusy(() => buildReport = BuildRunner.Build(_session.Dte), "building solution");
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
                Dictionary<string, StPouSource> provenance = TryBuildProvenanceIndex();
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
                Dictionary<string, StPouSource> provenance = TryBuildProvenanceIndex();
                Console.WriteLine("{0}: {1} warning(s):", Now(), buildReport.Warnings.Count);
                foreach (BuildError warning in buildReport.Warnings)
                    Console.WriteLine("    [WARN] {0} {1}", warning.Description, FormatErrorLocation(warning, provenance));
            }
        }

        /// <summary>Fresh full parse of the source folder for error mapping — independent
        /// of the sync's own (possibly incremental/partial) parse so errors in unchanged
        /// files still map. Best-effort: a parse failure just means raw locations.</summary>
        Dictionary<string, StPouSource> TryBuildProvenanceIndex()
        {
            try
            {
                return ErrorLocationResolver.BuildIndex(StFileParser.ParseFolder(_options.SourceFolder, _ignore));
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
                : string.Format("({0}:{1}) [unmapped — raw TwinCAT location]", error.FileName, error.Line);
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
                catch (COMException ex) when ((uint)ex.HResult == ComInterop.ServerCallRetryLater && elapsed < RETRY_TIMEOUT_MS)
                {
                    Console.WriteLine("{0}: Visual Studio is busy ({1}), retrying in 1s... ({2}s elapsed)",
                        Now(), description, elapsed / 1000);
                    Thread.Sleep(RETRY_INTERVAL_MS);
                    elapsed += RETRY_INTERVAL_MS;
                }
            }
        }
    }
}
