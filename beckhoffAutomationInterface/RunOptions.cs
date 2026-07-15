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
        All = Code | Libraries | Io | Events | Build,
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
        public string TsprojFilePath => Path.Combine(SolutionDirectory, ProjectName, ProjectName + ".tsproj");

        public string LibraryManifestPath => Path.Combine(SourceFolder, "libraries.xml");
        public string EventManifestPath => Path.Combine(SourceFolder, "events.xml");
        public string IoManifestPath => Path.Combine(SourceFolder, "io-devices.xml");

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
        public string ProjectRootPath => string.Format("TIPC^{0}^{0} Project", ProjectName);

        string TreePath(string leaf) => string.Format("TIPC^{0}^{0} Project^{1}", ProjectName, leaf);

        RunOptions(string sourceFolder, string destinationFolder, string projectName,
            SyncStages stages, bool init, bool checkEvents, bool checkLinks, bool parseOnly, IReadOnlyList<string> ignorePatterns, bool incremental, string exportObjectName, bool exportLinks, bool confirmDelete, bool formatCheck, bool confirmDeleteIo, bool configFileLoaded)
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
            ConfigFileLoaded = configFileLoaded;
        }

        /// <summary>
        /// Parses command-line arguments into a RunOptions. Prints usage and exits the
        /// process if invoked with no arguments at all, or with --help/-h. Any other
        /// invocation (even just a mode flag like --build-only) resolves --source/--dest
        /// to "." if not given explicitly.
        /// </summary>
        /// <summary>cwd overrides the process's current directory for the CWD fallback
        /// case below (default: the real process CWD) — exists so tests can point it at
        /// a temp folder instead of touching the actual working directory.</summary>
        public static RunOptions Parse(string[] args, string cwd = null)
        {
            if (args.Length == 0 || args.Contains("--help") || args.Contains("-h"))
            {
                PrintUsage();
                Environment.Exit(args.Length == 0 ? 1 : 0);
            }

            string cliSource = GetOption(args, "--source", "--src");
            string configOverride = GetOption(args, "--config");
            bool noConfig = args.Contains("--no-config");

            // .stconfig defaults (see Sync/StConfigFile.cs) are discovered at the top
            // level of the source project by default — same place ".stignore" already
            // lives (see IgnoreRules.Load) — so it just works once --source points at a
            // project, no separate launch folder needed. --config <path> looks somewhere
            // else entirely instead; with neither given, falls back to the process's
            // current directory. --no-config skips loading entirely for one invocation.
            // Deliberately never consulted for the three safety-gated flags below.
            string configSearchDir = Path.GetFullPath(configOverride ?? cliSource ?? cwd ?? Directory.GetCurrentDirectory());
            IReadOnlyDictionary<string, string> config = noConfig
                ? new Dictionary<string, string>()
                : StConfigFile.Load(configSearchDir);
            bool configFileLoaded = !noConfig && File.Exists(Path.Combine(configSearchDir, ".stconfig"));

            string sourceFolder = Path.GetFullPath(cliSource ?? config.GetString("source") ?? ".");
            string destinationFolder = Path.GetFullPath(GetOption(args, "--dest", "--dst") ?? config.GetString("dest") ?? ".");
            string projectName = GetOption(args, "--name") ?? config.GetString("name") ?? new DirectoryInfo(sourceFolder).Name;

            SyncStages cliStages = SyncStages.None;
            if (args.Contains("--sync-code")) cliStages |= SyncStages.Code;
            if (args.Contains("--sync-libs")) cliStages |= SyncStages.Libraries;
            if (args.Contains("--sync-io")) cliStages |= SyncStages.Io;
            if (args.Contains("--sync-events")) cliStages |= SyncStages.Events;
            if (args.Contains("--build")) cliStages |= SyncStages.Build;
            if (args.Contains("--build-only")) cliStages |= SyncStages.Build; // deprecated alias of --build

            SyncStages configStages = SyncStages.None;
            if (config.GetBool("sync-code")) configStages |= SyncStages.Code;
            if (config.GetBool("sync-libs")) configStages |= SyncStages.Libraries;
            if (config.GetBool("sync-io")) configStages |= SyncStages.Io;
            if (config.GetBool("sync-events")) configStages |= SyncStages.Events;
            if (config.GetBool("build")) configStages |= SyncStages.Build;

            // Stage flags are a GROUP, not five independent merges: if the CLI names any
            // stage at all, that exact set is used and .stconfig's stage keys are ignored
            // entirely for this run — otherwise a config default (e.g. build=true) could
            // silently run an EXTRA stage on top of a one-off "--sync-code only"
            // invocation, with no way to say "just this" short of deleting the file.
            SyncStages stages = cliStages != SyncStages.None ? cliStages
                : configStages != SyncStages.None ? configStages
                : SyncStages.All; // neither specifies any stage = the original full sync+build run

            return new RunOptions(
                sourceFolder, destinationFolder, projectName,
                stages: stages,
                // Safety-gated: --init/--confirm-delete/--confirm-delete-io are ALWAYS
                // CLI-only, on purpose — `config` is never consulted here. These flags
                // exist specifically so their effect can't happen by accident; reading
                // them from a defaults file would defeat that. Do not "fix" this.
                init: args.Contains("--init"),
                checkEvents: args.Contains("--check-events") || args.Contains("--events-only") || config.GetBool("check-events"), // --events-only: deprecated alias
                checkLinks: args.Contains("--check-links") || config.GetBool("check-links"),
                parseOnly: args.Contains("--parse-only") || config.GetBool("parse-only"),
                ignorePatterns: GetOptions(args, "--ignore"),
                incremental: args.Contains("--incremental") || config.GetBool("incremental"),
                exportObjectName: GetOption(args, "--export") ?? config.GetString("export"),
                exportLinks: args.Contains("--export-links") || config.GetBool("export-links"),
                confirmDelete: args.Contains("--confirm-delete"),
                formatCheck: args.Contains("--format-check") || config.GetBool("format-check"),
                confirmDeleteIo: args.Contains("--confirm-delete-io"),
                configFileLoaded: configFileLoaded);
        }

        static string GetOption(string[] args, params string[] flags)
        {
            foreach (string flag in flags)
            {
                int i = Array.IndexOf(args, flag);
                if (i >= 0 && i + 1 < args.Length)
                    return args[i + 1];
            }
            return null;
        }

        /// <summary>Collects every value following a repeated flag, e.g. "--ignore a --ignore b" -> [a, b].</summary>
        static List<string> GetOptions(string[] args, string flag)
        {
            var values = new List<string>();
            for (int i = 0; i < args.Length - 1; i++)
                if (args[i] == flag)
                    values.Add(args[i + 1]);
            return values;
        }

        static void PrintUsage()
        {
            Console.WriteLine("Usage: beckhoffAutomationInterface [stage flags] [options]");
            Console.WriteLine();
            Console.WriteLine("Stage flags (composable — any combination runs exactly those stages, in a");
            Console.WriteLine("fixed order, then exits; giving NONE of them runs the full pipeline:");
            Console.WriteLine("code + libs + io + events + build):");
            Console.WriteLine("  --sync-code       ST -> PLC POUs only (parse, lint, drift warnings, sync, save)");
            Console.WriteLine("  --sync-libs       libraries.xml -> PLC library references only");
            Console.WriteLine("  --sync-io         io-devices.xml -> device tree + Create-PLC-Data-Type .tsproj");
            Console.WriteLine("                    templates + <Links>. Orphans are WARNED about only, unless");
            Console.WriteLine("                    --confirm-delete-io is also given");
            Console.WriteLine("  --sync-events     events.xml + event-classes/*.xml -> missing Event Classes");
            Console.WriteLine("                    written into the .tsproj. Alone, needs NO Visual Studio");
            Console.WriteLine("  --build           Open, compile, report errors mapped to .st file:line.");
            Console.WriteLine("                    Exit code 0 = BUILD PASSED, 1 = failed/timeout. For CI.");
            Console.WriteLine("                    (alias: --build-only, deprecated)");
            Console.WriteLine();
            Console.WriteLine("Project lifecycle:");
            Console.WriteLine("  --init            Allow creating the solution/TwinCAT/PLC project when missing.");
            Console.WriteLine("                    Without it, a missing solution is a hard error in every mode.");
            Console.WriteLine();
            Console.WriteLine("Checks (read-only, no Visual Studio):");
            Console.WriteLine("  --check-events    Report declared-vs-present Event Classes and exit; code 1 if");
            Console.WriteLine("                    any declared class is missing (alias: --events-only, deprecated)");
            Console.WriteLine("  --check-links     Report declared-vs-linked %I/%Q GVL/PROGRAM variables and exit;");
            Console.WriteLine("                    code 1 if any is missing a <Link> in io-devices.xml (also");
            Console.WriteLine("                    surfaced, non-blocking, by --parse-only)");
            Console.WriteLine("  --parse-only      Parse all .st files without opening Visual Studio");
            Console.WriteLine("  --format-check    Report (never write) .st style issues — trailing whitespace,");
            Console.WriteLine("                    mixed line endings, EOF newline hygiene — without opening VS");
            Console.WriteLine();
            Console.WriteLine("Options:");
            Console.WriteLine("  --source <path>   Folder containing the .st source files (default: .)");
            Console.WriteLine("                    (alias: --src)");
            Console.WriteLine("  --dest <path>     Folder under which <name>/<name>.sln lives (default: .)");
            Console.WriteLine("                    (alias: --dst)");
            Console.WriteLine("  --name <name>     Project/solution name (default: the --source folder's own name)");
            Console.WriteLine("  --ignore <glob>   Exclude .st files matching this pattern (repeatable);");
            Console.WriteLine("                    merged with a \".stignore\" file in --source, if present");
            Console.WriteLine("  --incremental     Sync only .st files changed/deleted since the last recorded");
            Console.WriteLine("                    sync (see .st-sync-state); requires a prior full sync's baseline.");
            Console.WriteLine("                    Deleted files are only WARNED about unless --confirm-delete is");
            Console.WriteLine("                    also given, which actually removes the matching PLC object(s).");
            Console.WriteLine("  --confirm-delete  Combined with --incremental: actually delete PLC object(s)");
            Console.WriteLine("                    for .st files git reports as deleted (conservative: exact,");
            Console.WriteLine("                    unambiguous name matches only — anything else is skipped/reported)");
            Console.WriteLine("  --confirm-delete-io  Actually delete IO tree items (Device/Box/Terminal) not");
            Console.WriteLine("                    declared in io-devices.xml. Without this flag, undeclared");
            Console.WriteLine("                    items are only WARNED about — never deleted (see IoSyncEngine)");
            Console.WriteLine("  --export <name>   Write the named live PLC object's current text back to its");
            Console.WriteLine("                    mirrored .st file");
            Console.WriteLine("  --export-links    Write ALL currently-linked PLC-variable-to-IO-channel");
            Console.WriteLine("                    mappings out to links.xml (see below) — the way to capture");
            Console.WriteLine("                    links made by hand in the TwinCAT IDE");
            Console.WriteLine("  --config <path>   Look for '.stconfig' in this folder instead of --source");
            Console.WriteLine("  --no-config       Ignore any '.stconfig' defaults file for this run only");
            Console.WriteLine("  --help, -h        Show this message");
            Console.WriteLine();
            Console.WriteLine("Defaults file (\".stconfig\"):");
            Console.WriteLine("  A \"key=value\" per line file (# comments/blank lines OK), discovered at the");
            Console.WriteLine("  top level of --source by default (same place as .stignore) — or --config's");
            Console.WriteLine("  folder if given, or the current directory if neither is given. Supplies");
            Console.WriteLine("  defaults for source/dest/name/export/export-links/incremental/parse-only/format-check/");
            Console.WriteLine("  check-events/check-links and the five stage keys (sync-code, sync-libs,");
            Console.WriteLine("  sync-io, sync-events, build) — an explicit CLI flag always wins.");
            Console.WriteLine("  If the CLI names ANY stage flag, the whole group of stage keys in .stconfig");
            Console.WriteLine("  is ignored for that run (no silent extra stage). --init/--confirm-delete/");
            Console.WriteLine("  --confirm-delete-io are NEVER read from .stconfig, only ever from the real");
            Console.WriteLine("  command line. See .stconfig.example for a fully annotated template.");
            Console.WriteLine();
            Console.WriteLine("Variable links (\"links.xml\"):");
            Console.WriteLine("  TwinCAT's own <VarLinks> export/import format (same as the XAE IDE's \"Export/");
            Console.WriteLine("  Import Variable Mapping\"), applied via ConsumeMappingInfo in one bulk call");
            Console.WriteLine("  alongside io-devices.xml's own <Links> section (both apply, neither replaces");
            Console.WriteLine("  the other). Get real data into it with --export-links after linking by hand");
            Console.WriteLine("  in the IDE. See links.xml.example for a fully annotated template.");
        }
    }
}
