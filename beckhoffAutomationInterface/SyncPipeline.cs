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

        /// <summary>What this run found (drift, I/O warnings, links, build), as values.</summary>
        public PipelineOutcome Outcome { get; } = new PipelineOutcome();

        /// <summary>Optional: runs in the same open session right after a passed build stage,
        /// before the safety files are checked again (bai machine: the activation, so the
        /// project is not opened and compiled a second time).</summary>
        public Action<TwinCatSession> AfterBuild { get; set; }

        /// <summary>
        /// Executes the selected stages (see SyncStages; all of them when no stage flag
        /// was given) in a fixed three-phase order regardless of flag order:
        ///   Phase A (VS open):    variants -> code -> libraries -> tasks -> io-tree
        ///   Phase B (VS CLOSED):  direct .tsproj edits (io's Create-PLC-Data-Type
        ///                         templates, events' Event Classes)
        ///   Safety:               ids + copy (VS closed, only if needed), then the
        ///                         safety project and its channel check (VS open)
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
            // Reverse export (regenerate --source FROM the existing project) — checked
            // before the plain --export-links branch below because --export-all sets BOTH
            // IsReverseExport and ExportLinks, and RunReverseExport handles links itself.
            if (_options.IsReverseExport)
            {
                RunReverseExport();
                return;
            }
            if (_options.ExportLinks)
            {
                _session.EnsureOpen();
                ExportLinks();
                return;
            }

            SyncStages stages = _options.Stages;

            // Phase A — stages that work through the open project's Automation Interface.
            // Variants first: the build at the end needs the variant defines in place.
            if (stages.HasFlag(SyncStages.Variants))
            {
                _session.EnsureOpen();
                SyncVariants();
            }
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
            // Tasks after the code (a task calls programs that must exist) and before the I/O:
            // a link names the process image of a task ("<Task> Inputs").
            if (stages.HasFlag(SyncStages.Tasks))
                SyncTasks();
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

            // Safety: its own .tsproj edit (box/module ids) + copy with VS closed, then adds the
            // safety project through the open project. Before the links: they may use its channels.
            SafetyPlan safety = stages.HasFlag(SyncStages.Safety) ? SyncSafety() : null;

            // Phase C — back through the Automation Interface, reopening VS lazily.
            if (stages.HasFlag(SyncStages.Io))
            {
                if (desiredIoDevices != null)
                {
                    _session.EnsureOpen();
                    RetryOnBusy(() => IoSyncEngine.ApplyConfiguration(_session.SysManager, desiredIoDevices), "applying PDO and CoE configuration");
                    _session.Project.Save();
                }
                SyncLinks();
            }
            if (stages.HasFlag(SyncStages.Build))
            {
                _session.EnsureOpen();
                RunBuild();
                if (AfterBuild != null && Outcome.BuildPassed)
                    AfterBuild(_session);
            }

            if (safety != null)
                CheckSafetyFilesAfterClose(safety);
        }

        /// <summary>--export: write the named live PLC object's text back to its mirrored
        /// .st file. Uses Environment.ExitCode (not Environment.Exit) on error paths so
        /// Main's `using` still disposes/closes Visual Studio properly.</summary>
        void ExportObject()
        {
            // Only code objects count: a task's call of a PROGRAM (e.g. PlcTask^MAIN) has the same name.
            List<ITcSmTreeItem> matches = PlcObjectExporter.FindByName(_session.SysManager.LookupTreeItem(_options.ProjectRootPath), _options.ExportObjectName)
                .Where(PlcObjectExporter.IsExportableKind).ToList();
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

            string text = PlcObjectExporter.Export(item, _options.ProjectRootPath, _options.PlcProjectDiskFolder);
            string relativeFolder = PlcObjectExporter.GetRelativeFolder(item, _options.ProjectRootPath);
            string folderPath = Path.Combine(_options.SourceFolder, relativeFolder.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(folderPath);
            string filePath = Path.Combine(folderPath, item.Name + ".st");
            File.WriteAllText(filePath, text);

            Console.WriteLine("{0}: Exported '{1}' -> '{2}'.", Now(), item.Name, filePath);
        }

        /// <summary>--export-links: write ALL currently-linked PLC-variable-to-IO-channel
        /// mappings out to links.xml (see Sync/VarLinksFile.cs), via
        /// ITcSysManager.ProduceMappingInfo — the way to capture links made by hand in the
        /// TwinCAT IDE. Overwrites any existing links.xml, same convention --export already
        /// uses for its target .st file.</summary>
        void ExportLinks()
        {
            string xml = ((ITcSysManager3)_session.SysManager).ProduceMappingInfo();
            Directory.CreateDirectory(Path.GetDirectoryName(_options.VarLinksManifestPath));
            File.WriteAllText(_options.VarLinksManifestPath, xml);
            Console.WriteLine("{0}: Exported current variable links -> '{1}'.", Now(), _options.VarLinksManifestPath);
        }

        /// <summary>
        /// Reverse export: regenerate the --source tree FROM the existing project — the
        /// read-side mirror of the forward pipeline (see ReverseExports). A one-time
        /// bootstrap so a team can adopt the tool on a project that already exists, then
        /// manage it as .st source thereafter. Runs the selected artifacts in a fixed
        /// order — events first because it needs NO Visual Studio (pure .tsproj read), then
        /// the Automation-Interface reads (code/libs/io) with VS opened lazily, then
        /// links.xml. The overwrite guard already ran in Program.Main.
        /// </summary>
        void RunReverseExport()
        {
            ReverseExports exports = _options.ReverseExports;
            Directory.CreateDirectory(_options.SourceFolder);

            if (exports.HasFlag(ReverseExports.Native))
            {
                NativeProjectExport.Write(_options.TsprojFilePath, _options.SourceFolder);
                Console.WriteLine("{0}: Native project snapshot written and every archived file verified.", Now());
            }
            if (exports.HasFlag(ReverseExports.Safety))
            {
                ConfigurationExporter.Safety(_options.TsprojFilePath, _options.SourceFolder);
                Console.WriteLine("{0}: Safety export complete (byte-exact project files).", Now());
            }
            if (exports.HasFlag(ReverseExports.Tasks))
            {
                _session.EnsureOpen();
                RetryOnBusy(() =>
                {
                    var tasks = TaskSyncEngine.ReadState(_session.SysManager, _options.ProjectRootPath);
                    ConfigurationExporter.Tasks(tasks.RtTasks, tasks.TaskRefs).Save(_options.TaskManifestPath);
                }, "exporting task configuration");
                Console.WriteLine("{0}: Exported PLC task periods, priorities and ordered calls.", Now());
            }
            if (exports.HasFlag(ReverseExports.Variants))
            {
                _session.EnsureOpen();
                RetryOnBusy(() =>
                {
                    var variants = VariantManifest.FromProjectVariantConfigXml(((ITcSysManager14)_session.SysManager).ProjectVariantConfig);
                    if (variants.Variants.Count > 0)
                    {
                        if (!VariantSyncEngine.DefinesEnabled(_session.SysManager, _options.PlcItemPath))
                            throw new InvalidOperationException("Variant defines are not implicit; use the native snapshot to retain this configuration.");
                        ConfigurationExporter.Variants(variants).Save(_options.VariantManifestPath);
                    }
                }, "exporting variants");
            }

            // Events: pure .tsproj read, no VS — do it first, while VS is still closed.
            if (exports.HasFlag(ReverseExports.Events))
            {
                EventExportReport report = EventManifestWriter.Export(_options.TsprojFilePath, _options.EventManifestPath, _options.EventClassesFolder);
                if (report.EventsXmlWritten)
                    Console.WriteLine("{0}: Exported {1} event class(es) -> '{2}' (+ {1} template(s) in event-classes/).",
                        Now(), report.WrittenTemplates.Count, _options.EventManifestPath);
                else
                    Console.WriteLine("{0}: No event classes found in the project — events.xml not written.", Now());
            }

            if (exports.HasFlag(ReverseExports.Code))
            {
                _session.EnsureOpen();
                Console.WriteLine("{0}: Exporting all PLC objects -> '{1}'...", Now(), _options.SourceFolder);
                ProjectExportReport report = null;
                RetryOnBusy(() => report = ProjectCodeExporter.ExportAll(
                    _session.SysManager.LookupTreeItem(_options.ProjectRootPath), _options.ProjectRootPath, _options.PlcProjectDiskFolder, _options.SourceFolder), "exporting PLC objects");
                PrintLines("+ wrote    ", report.Written);
                if (report.Unsupported.Count > 0)
                {
                    Console.WriteLine("{0}: {1} object(s) could not be exported (unsupported shape):", Now(), report.Unsupported.Count);
                    PrintLines("! skipped  ", report.Unsupported);
                }
                Console.WriteLine("{0}: Code export complete ({1} .st file(s) written).", Now(), report.Written.Count);
            }

            if (exports.HasFlag(ReverseExports.Libraries))
            {
                _session.EnsureOpen();
                List<LibraryReference> refs = null;
                List<string> unparseable = null;
                RetryOnBusy(() =>
                {
                    ITcPlcLibraryManager libManager = (ITcPlcLibraryManager)_session.SysManager.LookupTreeItem(_options.ReferencesTreePath);
                    refs = LibrarySyncEngine.ReadReferences(libManager, out unparseable);
                }, "reading library references");
                LibraryManifestWriter.Write(_options.LibraryManifestPath, refs, unparseable);
                Console.WriteLine("{0}: Exported {1} library reference(s) -> '{2}'.", Now(), refs.Count, _options.LibraryManifestPath);
                if (unparseable.Count > 0)
                    Console.WriteLine("{0}: {1} reference(s) had an unrecognized display-name format (left as comments for review).", Now(), unparseable.Count);
            }

            if (exports.HasFlag(ReverseExports.Io))
            {
                _session.EnsureOpen();
                var ioReport = new IoExportReport();
                RetryOnBusy(() =>
                {
                    // Per-variant items (Variants="A,B") from one ProjectVariantItems read.
                    Dictionary<string, List<string>> variantsByPath = IoVariantSync.VariantsByPath(
                        IoVariantSync.ReadState(((ITcSysManager14)_session.SysManager).ProjectVariantItems),
                        VariantSyncEngine.ProjectVariants(_session.SysManager));
                    var ioManifest = IoManifestWriter.Build(_session.SysManager.LookupTreeItem("TIID"), ioReport, variantsByPath);
                    ConfigurationExporter.PlcDataTypes(_options.TsprojFilePath, ioManifest, _options.SourceFolder);
                    ioManifest.Save(_options.IoManifestPath);
                    if (variantsByPath.Count > 0)
                        Console.WriteLine("{0}: {1} I/O item(s) exist in some variants only (Variants attribute).", Now(), variantsByPath.Count);
                }, "reading IO tree");
                Console.WriteLine("{0}: Exported IO tree ({1} device(s), {2} box/terminal node(s)) -> '{3}'.",
                    Now(), ioReport.DeviceCount, ioReport.NodeCount, _options.IoManifestPath);
                if (ioReport.ProductsToVerify.Count > 0)
                {
                    Console.WriteLine("{0}: !! VERIFY {1} Product value(s) against the real hardware (derived from ItemSubTypeName; see IoManifestWriter):",
                        Now(), ioReport.ProductsToVerify.Count);
                    PrintLines("! verify   ", ioReport.ProductsToVerify);
                }
                PrintLines("!! WARNING ", ioReport.Warnings);
            }

            // links.xml — variable links (turned on by --export-links or --export-all).
            if (_options.ExportLinks)
            {
                _session.EnsureOpen();
                ExportLinks();
            }
        }

        /// <summary>Code stage: .st files -> PLC POUs (create/update/delete), plus the
        /// warn-only drift detection, the naming lint, and the known-names baseline.</summary>
        void SyncCode()
        {
            Console.WriteLine("{0}: Parsing .st sources from '{1}'...", Now(), _options.SourceFolder);
            List<StPouSource> desiredPous = StFileParser.ParseFolder(_options.SourceFolder, _ignore);

            // Two files for one object: refuse before anything is written (see DuplicateObjects).
            List<string> duplicates = DuplicateObjects.Find(desiredPous);
            if (duplicates.Count > 0)
                throw new InvalidOperationException(string.Format(
                    "{0} object(s) are defined in more than one .st file; nothing was synced. Delete or rename the stale file:\n    {1}",
                    duplicates.Count, string.Join("\n    ", duplicates)));

            // Warn-only drift detection: a previously-synced name missing from the current
            // parse means its .st source was renamed/deleted, but the PLC object still
            // compiles. Report it, never delete it.
            List<string> currentNames = KnownNamesTracker.CollectNames(desiredPous);
            List<string> previousNames = KnownNamesTracker.Read(_options.KnownNamesPath);
            if (previousNames != null)
            {
                List<string> staleNames = KnownNamesTracker.DiffFull(previousNames, currentNames);
                Outcome.StaleNames = staleNames.Count;
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
            Console.WriteLine("{0}: Sync complete ({1} created, {2} updated, {3} deleted, {4} unchanged).",
                Now(), syncReport.Created.Count, syncReport.Updated.Count, syncReport.Deleted.Count, syncReport.Unchanged.Count);

            // Record the known-names drift baseline (see the [drift] warning block above).
            KnownNamesTracker.Write(_options.KnownNamesPath, currentNames);
        }

        /// <summary>Libraries stage: libraries.xml -> PLC library references (config
        /// data, not .st source).</summary>
        /// <summary>Variants stage: apply variants.xml (see VariantSyncEngine). No manifest =
        /// nothing to do, so projects without variants are untouched.</summary>
        void SyncVariants()
        {
            VariantManifest desired;
            try
            {
                desired = VariantManifestParser.Parse(_options.VariantManifestPath);
            }
            catch (VariantManifestException ex)
            {
                Console.WriteLine("{0}: VARIANTS FAILED — {1}", Now(), ex.Message);
                Environment.ExitCode = 1;
                return;
            }
            if (desired == null)
            {
                Console.WriteLine("{0}: No variant manifest '{1}' — variants stage skipped.", Now(), _options.VariantManifestPath);
                return;
            }

            Console.WriteLine("{0}: Syncing {1} variant(s), {2} group(s)...", Now(), desired.Variants.Count, desired.Groups.Count);
            VariantSyncReport report = null;
            RetryOnBusy(() => report = VariantSyncEngine.Sync(_session.SysManager, _options.PlcItemPath, desired), "syncing variants");

            PrintLines("+ variant  ", report.Added);
            PrintLines("- variant  ", report.Removed);
            if (report.DefinesSwitchedOn)
                Console.WriteLine("    ~ variant defines switched on for '{0}' (PvDefine + implicit defines)", _options.PlcItemPath);
            if (report.NoChange)
            {
                Console.WriteLine("{0}: Variants already up to date (no change).", Now());
                return;
            }
            _session.Project.Save();
            Console.WriteLine("{0}: Variant sync complete ({1} added, {2} removed).", Now(), report.Added.Count, report.Removed.Count);
        }

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

        /// <summary>Tasks stage: tasks.xml -> real-time tasks and PLC task references (see
        /// Sync/TaskSyncEngine). No manifest = nothing to do: the project keeps the PLC template's
        /// PlcTask. Every call must name a PROGRAM of the source (checked before VS opens).
        /// Extra tasks and calls are warnings, never deleted.</summary>
        void SyncTasks()
        {
            TaskManifest desired;
            try
            {
                desired = TaskManifestParser.Parse(_options.TaskManifestPath);
            }
            catch (TaskManifestException ex)
            {
                throw new InvalidOperationException("TASKS FAILED — " + ex.Message);
            }
            if (desired == null)
            {
                Console.WriteLine("{0}: No task manifest '{1}' — tasks stage skipped.", Now(), _options.TaskManifestPath);
                return;
            }

            IEnumerable<string> programs = StFileParser.ParseFolder(_options.SourceFolder, _ignore)
                .Where(p => p.Kind == PouKind.Program).Select(p => p.Name);
            List<string> unknown = TaskManifestParser.CheckPrograms(desired, programs, _options.TaskManifestPath);
            if (unknown.Count > 0)
                throw new InvalidOperationException("TASKS FAILED — " + string.Join("\n    ", unknown));

            _session.EnsureOpen();
            Console.WriteLine("{0}: Syncing {1} task(s): {2}...", Now(), desired.Tasks.Count,
                string.Join(", ", desired.Tasks.Select(t => string.Format("{0} ({1} µs, priority {2}: {3})", t.Name, t.CycleTimeUs, t.Priority, string.Join(", ", t.Calls)))));
            TaskSyncPlan plan = null;
            RetryOnBusy(() => plan = TaskSyncEngine.Sync(_session.SysManager, _options.ProjectRootPath, desired), "syncing tasks");

            foreach (TaskAction action in plan.Actions)
                Console.WriteLine("    {0}", action.Description);
            PrintLines("!! WARNING ", plan.Warnings);
            _session.Project.Save();
            var cachedTiming = PlcTaskTimingEditor.Changes(_options.TsprojFilePath, _options.PlcProjectName, desired);
            if (cachedTiming.Count > 0)
            {
                _session.EnsureClosed();
                foreach (var file in cachedTiming) file.Value.Save(file.Key);
                if (PlcTaskTimingEditor.Changes(_options.TsprojFilePath, _options.PlcProjectName, desired).Count > 0)
                    throw new InvalidOperationException("PLC task timing did not persist.");
                Console.WriteLine("{0}: Repaired saved timing in {1} PLC task reference(s).", Now(), cachedTiming.Count);
            }
            Console.WriteLine("{0}: Task sync complete ({1} change(s), {2} warning(s)).", Now(), plan.Actions.Count, plan.Warnings.Count);
        }

        /// <summary>Io stage, part 1: reconcile the I/O hardware tree (Device -> Box ->
        /// Terminal) from io-devices.xml. Idempotent; orphans are only WARNED about
        /// unless --confirm-delete-io (see IoSyncEngine). Returns the parsed manifest so
        /// the tsproj-edit step can collect Create-PLC-Data-Type targets from it.</summary>
        List<IoDeviceSpec> SyncIoTree()
        {
            Console.WriteLine("{0}: Parsing IO manifest '{1}'...", Now(), _options.IoManifestPath);
            IoManifest manifest = IoManifestParser.ParseManifest(_options.IoManifestPath);
            var desiredIoDevices = manifest.Devices;

            Console.WriteLine("{0}: Syncing {1} IO device(s){2}...", Now(), desiredIoDevices.Count,
                manifest.Adapters.Count > 0 ? string.Format(" on {0} RT-Ethernet adapter(s)", manifest.Adapters.Count) : "");
            IoSyncReport ioReport = null;
            RetryOnBusy(() => ioReport = IoSyncEngine.Sync(_session.SysManager, manifest.Adapters, desiredIoDevices, _options.ConfirmDeleteIo), "syncing IO tree");

            PrintLines("+ created  ", ioReport.Created);
            PrintLines("- deleted  ", ioReport.Deleted);
            PrintLines("~ state    ", ioReport.StateChanged);
            PrintLines("!! WARNING ", ioReport.Warnings);
            PrintLines("!! ERROR   ", ioReport.Errors);
            Outcome.IoWarnings = ioReport.Warnings.Count;
            Outcome.IoErrors = ioReport.Errors.Count;
            if (ioReport.Errors.Count > 0)
            {
                Console.WriteLine("{0}: IO SYNC FAILED — {1} item(s) have the wrong kind (see above).", Now(), ioReport.Errors.Count);
                Environment.ExitCode = 1;
            }

            SyncIoVariants(desiredIoDevices);

            _session.Project.Save();
            Console.WriteLine("{0}: IO sync complete ({1} created, {2} deleted, {3} state change(s), {4} warning(s)).",
                Now(), ioReport.Created.Count, ioReport.Deleted.Count, ioReport.StateChanged.Count, ioReport.Warnings.Count);
            return desiredIoDevices;
        }

        /// <summary>Io stage, per-variant part: devices/boxes/terminals with a Variants
        /// attribute are enabled in those project variants and disabled in the others
        /// (see IoVariantSync). Nothing to do when no item has the attribute.</summary>
        void SyncIoVariants(List<IoDeviceSpec> desiredIoDevices)
        {
            List<IoVariantItem> items = IoVariantRules.ItemsWithVariants(desiredIoDevices);
            if (items.Count == 0)
                return;

            VariantManifest variants;
            try
            {
                variants = VariantManifestParser.Parse(_options.VariantManifestPath);
            }
            catch (VariantManifestException ex)
            {
                Console.WriteLine("{0}: IO VARIANTS FAILED — {1}", Now(), ex.Message);
                Environment.ExitCode = 1;
                return;
            }
            List<string> problems = IoVariantRules.Validate(desiredIoDevices, variants);
            IReadOnlyList<string> projectVariants = VariantSyncEngine.ProjectVariants(_session.SysManager);
            if (problems.Count == 0)
                problems.AddRange(variants.Variants.Where(v => !projectVariants.Contains(v))
                    .Select(v => string.Format("variant '{0}' is not in the project yet — run 'sync variants' first.", v)));
            if (problems.Count > 0)
            {
                Console.WriteLine("{0}: IO VARIANTS FAILED — io-devices.xml:", Now());
                foreach (string problem in problems)
                    Console.WriteLine("    ! {0}", problem);
                Environment.ExitCode = 1;
                return;
            }

            IoVariantPlan plan = null;
            RetryOnBusy(() => plan = IoVariantSync.Sync(_session.SysManager, items, projectVariants), "syncing per-variant IO");
            foreach (IoVariantState change in plan.Changes)
                Console.WriteLine("    ~ variants {0}: enabled in {1}", change.TreePath,
                    string.Join(", ", change.DisabledByVariant.Where(v => !v.Value).Select(v => v.Key)));
            PrintLines("!! WARNING ", plan.Warnings);
            Console.WriteLine("{0}: Per-variant IO: {1} item(s), {2} changed.", Now(), items.Count, plan.Changes.Count);
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

        /// <summary>Safety stage: safety.xml -> the TwinSAFE project (see Sync/SafetyProjectSync).
        /// No manifest = nothing to do. VS is closed only when the .tsproj ids or the copied
        /// files must change. Any problem stops the run: without the right ids TwinCAT adds the
        /// project silently with no Standard In/Out channels.</summary>
        SafetyPlan SyncSafety()
        {
            SafetyManifest manifest;
            try
            {
                manifest = SafetyManifestParser.Parse(_options.SafetyManifestPath, _options.SourceFolder);
            }
            catch (SafetyManifestException ex)
            {
                throw new InvalidOperationException("SAFETY FAILED — " + ex.Message);
            }
            if (manifest == null)
            {
                Console.WriteLine("{0}: No safety manifest '{1}' — safety stage skipped.", Now(), _options.SafetyManifestPath);
                return null;
            }

            SafetyPlan plan;
            try
            {
                plan = SafetyProjectSync.Plan(manifest, _options.TsprojFilePath);
            }
            catch (InvalidOperationException ex)
            {
                throw new InvalidOperationException("SAFETY FAILED — " + ex.Message);
            }
            Console.WriteLine("{0}: Safety project '{1}': logic terminal '{2}' (Box Id {3}), {4} object id(s), {5} file(s).",
                Now(), manifest.Name, plan.LogicTerminal.BoxName, plan.LogicTerminal.Id, plan.ObjectIds.Count, plan.Files.Count);
            if (plan.IdChanges.Problems.Count > 0)
                throw new InvalidOperationException("SAFETY FAILED — the project does not match the safety files:\n    "
                    + string.Join("\n    ", plan.IdChanges.Problems));

            if (plan.NeedsFileWork)
            {
                if (_session.IsOpen)
                {
                    Console.WriteLine("{0}: Closing Visual Studio to set the safety object ids and copy the safety project...", Now());
                    _session.EnsureClosed();
                }
                ObjectIdEditResult ids = TsprojObjectIdEditor.Apply(_options.TsprojFilePath, plan.ObjectIds);
                PrintLines("~ id       ", ids.Applied);
                if (ids.Problems.Count > 0)
                    throw new InvalidOperationException("SAFETY FAILED — " + string.Join("\n    ", ids.Problems));
                List<string> copied = SafetyProjectSync.Copy(plan);
                Console.WriteLine("{0}: Safety ids set ({1} change(s)); {2} safety file(s) copied to '{3}'.",
                    Now(), ids.Applied.Count, copied.Count, plan.ProjectFolder);
            }
            List<string> different = SafetyProjectSync.Differences(plan);
            if (different.Count > 0)
                throw new InvalidOperationException("SAFETY FAILED — copied safety file(s) are not byte-equal to the source:\n    "
                    + string.Join("\n    ", different));
            Console.WriteLine("{0}: Safety files: {1} file(s) byte-equal to the source.", Now(), plan.Files.Count);

            _session.EnsureOpen();
            bool added = false;
            List<string> otherProjects = null;
            RetryOnBusy(() => added = SafetyProjectSync.AddProject(_session.SysManager, manifest.Name, plan.SplcprojPath, out otherProjects), "adding the safety project");
            Console.WriteLine(added ? "    + safety   TISC^{0}" : "    = safety   TISC^{0} (already in the project)", manifest.Name);
            PrintLines("!! WARNING safety project not in safety.xml: ", otherProjects);

            string boxXml = null;
            RetryOnBusy(() => boxXml = SafetyProjectSync.ProduceBoxXml(_session.SysManager, plan.LogicTerminal.BoxName), "reading the safety logic terminal");
            if (boxXml == null)
                throw new InvalidOperationException(string.Format("SAFETY FAILED — no box '{0}' in the I/O tree.", plan.LogicTerminal.BoxName));
            List<string> problems = SafetyProjectSync.CheckChannels(boxXml, plan.LogicTerminal, plan.Channels, out List<string> found);
            PrintLines("  channels ", found);
            if (problems.Count > 0)
                throw new InvalidOperationException("SAFETY FAILED — the safety logic terminal has not got its Standard In/Out channels "
                    + "(object ids of the .tsproj and the safety files do not agree?):\n    " + string.Join("\n    ", problems));

            if (added)
                _session.Project.Save();
            Console.WriteLine("{0}: Safety sync complete ('{1}' {2}, Standard In/Out channels on '{3}').",
                Now(), manifest.Name, added ? "added" : "already present", plan.LogicTerminal.BoxName);
            return plan;
        }

        /// <summary>After the last stage: close VS (it can write files when it saves or closes the
        /// project) and check again that the copied safety files are byte-equal to the source.</summary>
        void CheckSafetyFilesAfterClose(SafetyPlan plan)
        {
            _session.EnsureClosed();
            List<string> different = SafetyProjectSync.Differences(plan);
            if (different.Count > 0)
            {
                Console.WriteLine("{0}: SAFETY FAILED — TwinCAT changed copied safety file(s); they are not byte-equal to the source:", Now());
                PrintLines("! changed  ", different);
                Environment.ExitCode = 1;
                return;
            }
            Console.WriteLine("{0}: Safety files after close: {1} file(s) byte-equal to the source.", Now(), plan.Files.Count);
        }

        /// <summary>Io stage, part 2: PLC-variable to IO-channel links declared in the
        /// Links section of io-devices.xml. If any declared link can't be resolved (the
        /// PLC instance image / EtherCAT channels only materialize after Activate
        /// Configuration on a real or simulated target), all masters are disabled as a
        /// fallback so the build stays green and unattended.</summary>
        void SyncLinks()
        {
            var desiredLinks = IoManifestParser.ParseLinks(_options.IoManifestPath);
            bool hasVarLinksFile = File.Exists(_options.VarLinksManifestPath);
            if (desiredLinks.Count == 0 && !hasVarLinksFile)
                return;

            _session.EnsureOpen(); // lazily reopens after the .tsproj edits closed VS

            // links.xml (native <VarLinks>, see Sync/VarLinksFile.cs) applies FIRST, in one
            // bulk COM call — then io-devices.xml's own <Links> apply on top, unchanged.
            // Both coexist rather than one replacing the other.
            bool varLinksApplied = false;
            RetryOnBusy(() => varLinksApplied = VariableLinkEngine.ApplyFromFile(_session.SysManager, _options.PlcProjectName, _options.VarLinksManifestPath), "applying links.xml");
            if (varLinksApplied)
                Console.WriteLine("{0}: Applied links.xml.", Now());

            VariableLinkReport linkReport = new VariableLinkReport();
            if (desiredLinks.Count > 0)
            {
                Console.WriteLine("{0}: Syncing {1} variable link(s) from io-devices.xml...", Now(), desiredLinks.Count);
                RetryOnBusy(() => linkReport = VariableLinkEngine.Sync(_session.SysManager, _options.PlcProjectName, desiredLinks), "linking variables");

                PrintLines("+ linked   ", linkReport.Linked);
                PrintLines("x unlinked ", linkReport.Failed);
            }

            if (!linkReport.AllLinked)
            {
                Console.WriteLine("{0}: Some links unresolved (the PLC instance image / EtherCAT channels", Now());
                Console.WriteLine("        require Activate Configuration against a real or simulated target).");
                List<string> disabled = IoSyncEngine.DisableAllMasters(_session.SysManager);
                foreach (string name in disabled)
                    Console.WriteLine("        ~ disabled master '{0}' to keep the build green.", name);
                Outcome.DisabledMasters.AddRange(disabled);
            }
            _session.Project.Save();
            Outcome.LinkSyncRan = true;
            Outcome.LinksLinked = linkReport.Linked.Count;
            Outcome.LinksUnresolved = linkReport.Failed.Count;
            Console.WriteLine("{0}: Variable link sync complete ({1} linked, {2} unresolved).",
                Now(), linkReport.Linked.Count, linkReport.Failed.Count);
        }

        /// <summary>Build stage: compile and report, with error locations translated back
        /// to the original .st path/line (see Sync/ErrorLocationResolver.cs). Unmapped
        /// locations print raw, clearly labeled — never silently dropped.</summary>
        void RunBuild()
        {
            List<string> variants;
            try
            {
                variants = VariantBuildPlan.Select(VariantSyncEngine.ProjectVariants(_session.SysManager), _options.Variant, _options.AllVariants);
            }
            catch (ArgumentException ex)
            {
                Console.WriteLine("{0}: BUILD FAILED — {1}", Now(), ex.Message);
                Environment.ExitCode = 1;
                return;
            }

            if (variants.Count == 0)
            {
                BuildOnce(_options.Clean); // project without variants: one plain build, as before
                return;
            }

            if (!VariantSyncEngine.DefinesEnabled(_session.SysManager, _options.PlcItemPath))
            {
                Console.WriteLine("{0}: BUILD FAILED — variant defines are off for '{1}', so the PLC would get no variant defines. Run 'sync variants' first.",
                    Now(), _options.PlcItemPath);
                Environment.ExitCode = 1;
                return;
            }

            var sys14 = (ITcSysManager14)_session.SysManager;
            string original = sys14.CurrentProjectVariant;
            var results = new List<VariantBuildResult>();
            try
            {
                foreach (string variant in variants)
                {
                    Console.WriteLine("{0}: ===== Variant '{1}' =====", Now(), variant);
                    sys14.CurrentProjectVariant = variant;
                    // Always clean: the previous variant's output must not pass for this one.
                    results.Add(BuildOnce(clean: true, variant: variant));
                }
            }
            finally
            {
                if (!string.IsNullOrEmpty(original))
                {
                    try { sys14.CurrentProjectVariant = original; }
                    catch (COMException) { /* best effort; the project is not saved by a build */ }
                }
            }

            Console.WriteLine("{0}: Variant build summary:{1}{2}", Now(), Environment.NewLine, VariantBuildPlan.Summary(results));
            if (results.Any(r => !r.Passed))
                Environment.ExitCode = 1;
        }

        /// <summary>One clean/build of the current variant (or of a project without variants),
        /// with errors, warnings and the PLC's message pragmas printed.</summary>
        VariantBuildResult BuildOnce(bool clean, string variant = null)
        {
            VariantBuildResult result = BuildOnceCore(clean, variant);
            Outcome.Builds++;
            if (!result.Passed)
                Outcome.BuildFailures++;
            if (result.TimedOut)
                Outcome.BuildTimedOut = true;
            return result;
        }

        VariantBuildResult BuildOnceCore(bool clean, string variant)
        {
            var result = new VariantBuildResult { Variant = variant };
            if (_options.Defines.Count > 0)
            {
                // After the variant is selected: TwinCAT keeps the variant's defines in the same field.
                string defines = null;
                RetryOnBusy(() => defines = CompilerDefines.Add(_session.SysManager, _options.ProjectRootPath, _options.Defines), "adding compiler defines");
                Console.WriteLine("{0}: Compiler defines for this build: {1}", Now(), defines);
            }
            BuildReport buildReport = null;
            try
            {
                using (_session.Vs.Watchdog("Build", BuildRunner.HangLimit))
                    RetryOnBusy(() => buildReport = BuildRunner.Build(_session.Dte, clean), "building solution");
            }
            catch (BuildTimeoutException ex)
            {
                Console.WriteLine("{0}: BUILD TIMED OUT — {1}", Now(), ex.Message);
                Environment.ExitCode = 1; // CI contract: anything but BUILD PASSED is non-zero
                result.TimedOut = true;
                return result; // Main's using will force-close VS (dialog is still up)
            }

            if (_session.Shadow != null) // report locations in the adopted project, not in its temp copy
                foreach (BuildError e in buildReport.Errors.Concat(buildReport.Warnings))
                    e.FileName = _session.Shadow.ToOriginalPath(e.FileName);
            foreach (string message in buildReport.PragmaMessages)
                Console.WriteLine("    [PLC MESSAGE] {0}", message);
            result.Passed = buildReport.Success;
            result.ErrorCount = buildReport.Errors.Count;

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
                if (buildReport.OutputTail.Count > 0)
                {
                    try
                    {
                        string nativeErrors = ((ITcSysManager2)_session.SysManager).GetLastErrorMessages();
                        if (!string.IsNullOrWhiteSpace(nativeErrors))
                            Console.WriteLine("    TwinCAT System Manager: {0}", nativeErrors);
                    }
                    catch (COMException ex)
                    {
                        Console.WriteLine("    TwinCAT diagnostics unavailable: {0}", ex.Message);
                    }
                    Console.WriteLine("    No PLC error: the cause is outside the PLC code. End of the Build output:");
                    foreach (string line in buildReport.OutputTail)
                        Console.WriteLine("      | {0}", line);
                }

                if (buildReport.Warnings.Count > 0)
                {
                    Console.WriteLine("{0}: {1} warning(s):", Now(), buildReport.Warnings.Count);
                    foreach (BuildError warning in buildReport.Warnings)
                        Console.WriteLine("    [WARN] {0} {1}", warning.Description, FormatErrorLocation(warning, provenance));
                }
                return result;
            }

            if (buildReport.Warnings.Count > 0)
            {
                Dictionary<string, StPouSource> provenance = TryBuildProvenanceIndex();
                Console.WriteLine("{0}: {1} warning(s):", Now(), buildReport.Warnings.Count);
                foreach (BuildError warning in buildReport.Warnings)
                    Console.WriteLine("    [WARN] {0} {1}", warning.Description, FormatErrorLocation(warning, provenance));
            }
            return result;
        }

        /// <summary>Fresh full parse of the source folder for error mapping — independent
        /// of the sync's own (possibly incremental/partial) parse so errors in unchanged
        /// files still map. Best-effort: a parse failure just means raw locations.</summary>
        Dictionary<string, StPouSource> TryBuildProvenanceIndex()
        {
            if (!MapsErrorsToSource(_options))
                return new Dictionary<string, StPouSource>(StringComparer.OrdinalIgnoreCase);
            try
            {
                return ErrorLocationResolver.BuildIndex(StFileParser.ParseFolder(_options.SourceFolder, _ignore));
            }
            catch (Exception)
            {
                return new Dictionary<string, StPouSource>(StringComparer.OrdinalIgnoreCase);
            }
        }

        /// <summary>`build &lt;path&gt;` (an existing .tsproj) has no source folder of its own:
        /// the default "." is just the caller's current directory, and mapping errors
        /// through unrelated .st files there points at the wrong file. Map only when the
        /// project was not adopted from a path, or a source folder was given explicitly.</summary>
        internal static bool MapsErrorsToSource(RunOptions options) =>
            options.ExistingTsprojPath == null || options.SourceFolderExplicit;

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
