using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BeckhoffAutomationInterface.Sync;

namespace BeckhoffAutomationInterface
{
    /// <summary>
    /// The composable pipeline stages a run can execute (see tasks/plan.md). Each
    /// --sync-*/--build CLI flag ORs one stage in; giving NO stage flag selects All
    /// (the original full sync+build behavior). Stages always execute in the fixed
    /// order Code → Libraries → Io(tree) → tsproj edits (Io templates + Events) →
    /// Io links → Build, regardless of flag order.
    /// </summary>
    [Flags]
    enum SyncStages
    {
        None = 0,
        Code = 1,
        Libraries = 2,
        Io = 4,
        Events = 8,
        Build = 16,
        Variants = 32,
        All = Code | Libraries | Io | Events | Build | Variants,
    }

    /// <summary>
    /// The reverse-export artifacts a run can generate FROM an existing PLC project
    /// (see tasks/2026-07-15-reverse-export-scaffold/): the read-side mirror of
    /// SyncStages. Each --export-* CLI flag ORs one in; --export-all selects all four
    /// (and additionally turns on the existing ExportLinks -> links.xml). This is a
    /// one-time bootstrap/adoption direction, NOT the ongoing flow — .st stays the
    /// human-edited source of truth once the tree exists.
    /// </summary>
    [Flags]
    enum ReverseExports
    {
        None = 0,
        Code = 1,
        Libraries = 2,
        Io = 4,
        Events = 8,
        All = Code | Libraries | Io | Events,
    }

    /// <summary>
    /// Resolved configuration for one run, built once from command-line arguments by
    /// <see cref="Parse"/>. Replaces the previously-hardcoded "Shark" paths and the
    /// _buildOnly/_eventsOnly static fields that used to live directly on Program.
    /// </summary>
    class RunOptions
    {
        /// <summary>Folder containing the .st source files.</summary>
        public string SourceFolder { get; }

        /// <summary>Folder under which "&lt;ProjectName&gt;/&lt;ProjectName&gt;.sln" is created/opened.</summary>
        public string DestinationFolder { get; }

        /// <summary>Project/solution name. Defaults to the SourceFolder's own directory name.</summary>
        public string ProjectName { get; }

        /// <summary>Which pipeline stages this run executes (see SyncStages). All when no
        /// stage flag was given — the original full sync+build behavior.</summary>
        public SyncStages Stages { get; }

        /// <summary>Explicit opt-in to CREATE the solution/TwinCAT/PLC project when the
        /// computed SolutionFilePath doesn't exist. Without it a missing solution is a
        /// hard error in every mode — guards against the silent-bootstrap trap where a
        /// mistyped --dest/--name quietly builds a brand-new empty project (real
        /// near-miss 2026-07-14, see tasks/archive/2026-07-14-post-review-hardening/).</summary>
        public bool Init { get; }

        /// <summary>Read-only declared-vs-present Event Class check (no Visual Studio):
        /// prints the report and exits — code 1 if any declared class is missing, else 0.
        /// (--events-only is the deprecated alias.)</summary>
        public bool CheckEvents { get; }

        /// <summary>Read-only declared-vs-linked %I/%Q check (no Visual Studio, see
        /// Sync.LinkChecker): prints the report and exits — code 1 if any declared GVL/
        /// PROGRAM %I/%Q variable has no matching &lt;Link&gt; in io-devices.xml, or any .st
        /// file failed to parse, else 0.</summary>
        public bool CheckLinks { get; }

        /// <summary>True when this run is ONLY the Build stage (--build with no other
        /// stage flags, or the deprecated --build-only alias).</summary>
        public bool BuildOnly => Stages == SyncStages.Build;

        public bool ParseOnly { get; }

        /// <summary>Which reverse-export artifacts this run generates from the existing
        /// project (see ReverseExports). None when no --export-* flag was given — the
        /// run then behaves as a normal forward sync/build.</summary>
        public ReverseExports ReverseExports { get; }

        /// <summary>True when this run is a reverse export (any --export-* flag), so the
        /// pipeline generates source artifacts instead of syncing them into the project.</summary>
        public bool IsReverseExport => ReverseExports != ReverseExports.None;

        /// <summary>Safety gate for reverse export: without it, refusing to overwrite an
        /// already-populated --source (existing .st files or manifests). CLI-only, never
        /// read from .stconfig — same philosophy as --init/--confirm-delete*, so a
        /// destructive regenerate can't happen by accident.</summary>
        public bool Overwrite { get; }

        /// <summary>True when a `.stconfig` defaults file was actually found (and not
        /// suppressed by --no-config) — purely informational, so Program.cs can print a
        /// one-line "loaded defaults from X" note rather than applying it silently.</summary>
        public bool ConfigFileLoaded { get; }

        /// <summary>When set, report (never write) low-risk .st style issues — trailing
        /// whitespace, mixed line endings, EOF newline hygiene — and stop (see
        /// Sync.StFormatter). Deliberately dry-run-only for now; no --format (write) mode
        /// exists yet, since actually rewriting user source is a separate, riskier step.</summary>
        public bool FormatCheck { get; }

        /// <summary>When set, sync only the .st files changed/deleted since the commit
        /// recorded in SyncStatePath (via Sync.GitDiffHelper), instead of the whole source
        /// folder. Requires SourceFolder to be inside a git repo with a prior recorded
        /// baseline (see Sync.SyncState) — refuses to run without one rather than guessing.</summary>
        public bool Incremental { get; }

        /// <summary>Opt-in gate (user's explicit choice of the safer default): with
        /// --incremental alone, deleted .st files are only warned about. Combined with
        /// --confirm-delete, Sync.IncrementalDeleter actually removes the corresponding
        /// PLC object(s), conservatively (exact unambiguous name match only).</summary>
        public bool ConfirmDelete { get; }

        /// <summary>Opt-in gate for IO device orphan deletion (see Sync.IoSyncEngine):
        /// without this flag, IO tree items not declared in io-devices.xml are only
        /// warned about, never deleted. Confirmed necessary (2026-07-14) — a manifest/
        /// reality mismatch or a TreeItemFactory lookup miss can otherwise delete real,
        /// hand-configured EtherCAT hardware with no warning.</summary>
        public bool ConfirmDeleteIo { get; }

        /// <summary>When set, export the named live PLC object's current text back to its
        /// mirrored .st file instead of running a sync (see Sync.PlcObjectExporter). Null
        /// when --export wasn't given.</summary>
        public string ExportObjectName { get; }

        /// <summary>When set, write ALL currently-linked PLC-variable-to-IO-channel
        /// mappings out to VarLinksManifestPath (via ITcSysManager.ProduceMappingInfo)
        /// instead of running a sync — the way to capture links made by hand in the
        /// TwinCAT IDE. Defaultable from .stconfig, same as --export.</summary>
        public bool ExportLinks { get; }

        /// <summary>Extra ignore glob patterns from repeated --ignore &lt;pattern&gt; CLI args,
        /// merged with any ".stignore" file found in SourceFolder (see Sync.IgnoreRules).</summary>
        public IReadOnlyList<string> IgnorePatterns { get; }

        // Fixed OS-install locations — not per-run configuration, so not exposed as CLI flags.
        public string TwinCatTemplate { get; } =
            @"C:\Program Files (x86)\Beckhoff\TwinCAT\3.1\Components\Base\PrjTemplate\TwinCAT Project.tsproj";
        public string StandardPlcProjectTemplate { get; } = "Standard PLC Template.plcproj";

        public string SolutionDirectory => Path.Combine(DestinationFolder, ProjectName);
        public string SolutionFilePath => Path.Combine(SolutionDirectory, ProjectName + ".sln");

        /// <summary>When ExistingTsprojPath is given (see --tsproj), that path is used
        /// verbatim — the read-only "adopt an arbitrary existing project" path for
        /// reverse export (see TwinCatProjectOpener.OpenExistingReadOnly), which bypasses
        /// the dest/name/.sln convention entirely since a real pre-existing project's
        /// on-disk layout (and whether it even HAS a .sln) can't be assumed. Otherwise
        /// unchanged: the conventional nested path this tool's own --init creates.</summary>
        public string TsprojFilePath => ExistingTsprojPath ?? Path.Combine(SolutionDirectory, ProjectName, ProjectName + ".tsproj");

        /// <summary>The live PLC project's own on-disk folder (e.g. ".../PLC_NFL_SHARK/
        /// PLC_NFL_prj"), matching TwinCAT's convention of the PLC project folder sitting
        /// alongside its .tsproj, named after PlcProjectName. Needed ONLY for reverse
        /// export of TREEITEMTYPE_PLCTEXTLISTENUMERATION (.TcTLEO) objects — confirmed
        /// live (2026-07-16) to NOT expose ITcPlcDeclaration via COM at all (E_NOINTERFACE),
        /// unlike every other DUT/GVL kind — so their DeclarationText must be read directly
        /// from their .TcTLEO file on disk instead (see PlcObjectExporter.Export).</summary>
        public string PlcProjectDiskFolder => Path.Combine(Path.GetDirectoryName(TsprojFilePath), PlcProjectName);

        public string LibraryManifestPath => Path.Combine(SourceFolder, "libraries.xml");
        public string EventManifestPath => Path.Combine(SourceFolder, "events.xml");
        public string IoManifestPath => Path.Combine(SourceFolder, "io-devices.xml");
        public string VariantManifestPath => Path.Combine(SourceFolder, "variants.xml");

        /// <summary>The PLC project's System Manager item, e.g. "TIPC^Shark" (variant settings live here).</summary>
        public string PlcItemPath => "TIPC^" + PlcProjectName;

        /// <summary>TwinCAT-native &lt;VarLinks&gt; format (see Sync/VarLinksFile.cs) — the
        /// same schema the XAE IDE's own "Export/Import Variable Mapping" produces/reads.
        /// Applied via ITcSysManager.ConsumeMappingInfo in one bulk call, alongside (not
        /// instead of) io-devices.xml's own &lt;Links&gt; section.</summary>
        public string VarLinksManifestPath => Path.Combine(SourceFolder, "links.xml");

        /// <summary>Already-computed &lt;DataType&gt;/&lt;PlcDataType&gt; XML per terminal
        /// product, e.g. plc-data-types/EL3174.xml — see Sync/PlcDataTypeTemplate.cs.</summary>
        public string PlcDataTypesFolder => Path.Combine(SourceFolder, "plc-data-types");

        /// <summary>Already-computed Event Class &lt;DataType&gt; XML per class name, e.g.
        /// event-classes/BeckhoffLibEvents.xml — see Sync/PlcDataTypeTemplate.cs.</summary>
        public string EventClassesFolder => Path.Combine(SourceFolder, "event-classes");

        /// <summary>Records the last-synced commit SHA (see Sync.SyncState); consulted/updated
        /// by --incremental and updated after every successful full sync too.</summary>
        public string SyncStatePath => Path.Combine(SourceFolder, ".st-sync-state");

        /// <summary>Records every synced object/member's full name (see
        /// Sync.KnownNamesTracker) so renames/deletions in .st source are warned about
        /// on the next run instead of leaving stale PLC code compiling silently.</summary>
        public string KnownNamesPath => Path.Combine(SourceFolder, ".st-known-names");

        public string PousTreePath => TreePath("POUs");
        public string ReferencesTreePath => TreePath("References");

        /// <summary>The real TIPC tree name of the PLC project (TIPC^&lt;PlcProjectName&gt;^
        /// &lt;PlcProjectName&gt; Project^...). For a project this tool bootstrapped itself
        /// (--init), this always equals ProjectName (the XAE/.tsproj name), since
        /// TwinCatProjectOpener.BootstrapNew creates both with the same name. For an
        /// ARBITRARY pre-existing project being reverse-exported (see --plc), the PLC
        /// project can have a different name than the .tsproj file itself — confirmed live
        /// against PLC_NFL_SHARK_V2, whose .tsproj is "PLC_NFL_SHARK" but whose PLC project
        /// (TIPC^PLC_NFL_prj) is "PLC_NFL_prj". Defaults to ProjectName when not given, so
        /// every existing forward-sync/--init scenario is completely unaffected.</summary>
        public string PlcProjectName { get; }

        public string ProjectRootPath => string.Format("TIPC^{0}^{0} Project", PlcProjectName);

        string TreePath(string leaf) => string.Format("TIPC^{0}^{0} Project^{1}", PlcProjectName, leaf);

        /// <summary>When set (via --tsproj), opens THIS exact existing .tsproj file
        /// directly — read-only, never saved — instead of resolving
        /// SolutionFilePath/TsprojFilePath from DestinationFolder/ProjectName. This is the
        /// "adopt an arbitrary pre-existing project" path: such a project may have no .sln
        /// at all (confirmed live against PLC_NFL_SHARK_V2), which the normal dte.Solution.
        /// Open flow requires. CLI-only. Valid either with a --export-* flag (reverse
        /// export) or with --build alone (e.g. CI compiling a project natively hosted on
        /// GitHub, never bootstrapped by this tool) — see TwinCatSession.EnsureOpen.</summary>
        public string ExistingTsprojPath { get; }

        /// <summary>DTE shell to automate: "xae", "vs2022", or null = first usable one
        /// (XAE preferred). See DteShellResolver.</summary>
        public string Shell { get; }

        /// <summary>Clean the solution before the build stage (--clean / .stconfig "clean").</summary>
        public bool Clean { get; }

        /// <summary>True when the source folder came from --source or .stconfig "source",
        /// false when it fell back to the current directory.</summary>
        public bool SourceFolderExplicit { get; }

        /// <summary>Build only this project variant (--variant). Null = all variants when the
        /// project has any (see VariantBuildPlan).</summary>
        public string Variant { get; }

        /// <summary>--all-variants: build every project variant (also the default).</summary>
        public bool AllVariants { get; }

        internal RunOptions(string sourceFolder, string destinationFolder, string projectName,
            SyncStages stages, bool init, bool checkEvents, bool checkLinks, bool parseOnly, IReadOnlyList<string> ignorePatterns, bool incremental, string exportObjectName, bool exportLinks, bool confirmDelete, bool formatCheck, bool confirmDeleteIo, ReverseExports reverseExports, bool overwrite, string plcProjectName, string existingTsprojPath, bool configFileLoaded, string shell, bool clean, bool sourceFolderExplicit, string variant, bool allVariants)
        {
            SourceFolder = sourceFolder;
            DestinationFolder = destinationFolder;
            ProjectName = projectName;
            Stages = stages;
            Init = init;
            CheckEvents = checkEvents;
            CheckLinks = checkLinks;
            ParseOnly = parseOnly;
            IgnorePatterns = ignorePatterns;
            Incremental = incremental;
            ExportObjectName = exportObjectName;
            ExportLinks = exportLinks;
            ConfirmDelete = confirmDelete;
            FormatCheck = formatCheck;
            ConfirmDeleteIo = confirmDeleteIo;
            ReverseExports = reverseExports;
            Overwrite = overwrite;
            PlcProjectName = plcProjectName ?? projectName;
            ExistingTsprojPath = existingTsprojPath;
            ConfigFileLoaded = configFileLoaded;
            Shell = shell;
            Clean = clean;
            SourceFolderExplicit = sourceFolderExplicit;
            Variant = variant;
            AllVariants = allVariants;
        }

        static readonly System.Text.RegularExpressions.Regex PlcIdentifier =
            new System.Text.RegularExpressions.Regex(@"^[A-Za-z_][A-Za-z0-9_]*$");

        /// <summary>Null when TwinCAT accepts the PLC project name, else a message. The name
        /// defaults to the source folder's name, so a folder like "variant-sample" fails.</summary>
        public static string PlcProjectNameProblem(string plcProjectName)
        {
            if (plcProjectName != null && PlcIdentifier.IsMatch(plcProjectName))
                return null;
            return string.Format(
                "PLC project name '{0}' is not valid: TwinCAT needs letters, digits and '_' only, not starting with a digit. " +
                "The name comes from the source folder unless you pass --name <ValidName>.", plcProjectName);
        }

    }
}
