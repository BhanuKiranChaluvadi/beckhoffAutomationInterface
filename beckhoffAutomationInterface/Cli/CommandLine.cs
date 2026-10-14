using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BeckhoffAutomationInterface.Sync;

namespace BeckhoffAutomationInterface.Cli
{
    /// <summary>A command-line problem: printed with the command's usage, exit code 1.</summary>
    sealed class UsageException : Exception
    {
        public string Usage { get; }
        public UsageException(string message, string usage) : base(message) { Usage = usage; }
    }

    /// <summary>
    /// Parses the subcommand syntax straight into RunOptions — no translation to the old flat flags
    /// (docs/ideas/cli-subcommand-redesign.md, "Final CLI spec"). One name per concept:
    /// &lt;source&gt; (positional .st folder), --project (TwinCAT project), --plc, --variant, --shell.
    /// activate / verify / doctor have their own parsers in Runtime/.
    /// </summary>
    static class CommandLine
    {
        public const string BuildUsage =
            "build <project> [--plc <name>] [--clean] [--variant <name> | --all-variants]";
        public const string CheckUsage =
            "check parse|format|links <source> [--ignore <glob>]...\n  check events <source> --project <project>";
        public const string SyncUsage =
            "sync code|libs|io|events|variants|all <source> --project <project> [--incremental] [--clean]\n" +
            "       [--confirm-delete] [--confirm-delete-io] [--ignore <glob>]... [--variant <name> | --all-variants]";
        public const string ExportUsage =
            "export code|libs|io|events|links|all <source> --project <project> [--plc <name>] [--overwrite]\n" +
            "  export object <ObjectName> <source> --project <project> [--plc <name>]";
        public const string InitUsage =
            "init <source> --into <folder> [--name <name>] [--ignore <glob>]...";

        // Every command: where .stconfig is read from, and whether at all.
        static readonly string[] ConfigOptions = { "--config" };
        static readonly string[] ConfigSwitches = { "--no-config" };

        /// <summary>Supported .stconfig keys. Everything else is an error (old keys get a hint).</summary>
        internal static readonly string[] ConfigKeys = { "source", "project", "shell", "clean", "incremental" };

        static readonly Dictionary<string, string> RemovedConfigKeys = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["dest"] = "use project=<path to the .sln, its folder or the .tsproj>",
            ["name"] = "use project=<path to the .sln, its folder or the .tsproj>",
            ["tsproj"] = "use project=",
            ["plc-name"] = "pass --plc on the command line (a wrong PLC name must never come from a defaults file)",
            ["export"] = "use the command: export object <Name> …",
            ["export-links"] = "use the command: export links …",
            ["parse-only"] = "use the command: check parse …",
            ["format-check"] = "use the command: check format …",
            ["check-links"] = "use the command: check links …",
            ["check-events"] = "use the command: check events …",
            ["sync-code"] = "the sync command chooses the stage: sync code …",
            ["sync-libs"] = "the sync command chooses the stage: sync libs …",
            ["sync-io"] = "the sync command chooses the stage: sync io …",
            ["sync-events"] = "the sync command chooses the stage: sync events …",
            ["sync-variants"] = "the sync command chooses the stage: sync variants …",
            ["build"] = "use the command: build <project>",
        };

        /// <summary>Flags of the old flat CLI, rejected with the replacement (README "Other flags").</summary>
        static readonly Dictionary<string, string> RemovedOptions = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["--dest"] = "sync: --project <the .sln, its folder or the .tsproj>; init: --into <folder>",
            ["--source"] = "pass the source folder as the path after the command",
            ["--tsproj"] = "use --project (export, check events) or the path after 'build'",
            ["--plc-name"] = "use --plc",
            ["--init"] = "use the command: init <source> --into <folder>",
            ["--parse-only"] = "use the command: check parse <source>",
            ["--format-check"] = "use the command: check format <source>",
            ["--check-links"] = "use the command: check links <source>",
            ["--check-events"] = "use the command: check events <source> --project <project>",
            ["--export"] = "use the command: export object <Name> <source> --project <project>",
            ["--export-links"] = "use the command: export links <source> --project <project>",
            ["--sync-code"] = "use the command: sync code …",
            ["--sync-libs"] = "use the command: sync libs …",
            ["--sync-io"] = "use the command: sync io …",
            ["--sync-events"] = "use the command: sync events …",
            ["--build"] = "use the command: build <project>",
        };

        static readonly Dictionary<string, SyncStages> SyncModes = new Dictionary<string, SyncStages>
        {
            ["code"] = SyncStages.Code,
            ["libs"] = SyncStages.Libraries,
            ["io"] = SyncStages.Io,
            ["events"] = SyncStages.Events,
            ["variants"] = SyncStages.Variants,
            ["all"] = SyncStages.All,
        };

        static readonly Dictionary<string, ReverseExports> ExportModes = new Dictionary<string, ReverseExports>
        {
            ["code"] = ReverseExports.Code,
            ["libs"] = ReverseExports.Libraries,
            ["io"] = ReverseExports.Io,
            ["events"] = ReverseExports.Events,
            ["all"] = ReverseExports.All,
            ["links"] = ReverseExports.None, // links.xml only (ExportLinks)
        };

        /// <summary>args[0] is the command. shell: the global --shell value (already taken out).
        /// cwd: stands in for the process directory in tests.</summary>
        public static RunOptions Parse(string[] args, string shell = null, string cwd = null)
        {
            switch (args[0])
            {
                case "build": return ParseBuild(args, shell, cwd);
                case "check": return ParseCheck(args, shell, cwd);
                case "sync": return ParseSync(args, shell, cwd);
                case "export": return ParseExport(args, shell, cwd);
                case "init": return ParseInit(args, shell, cwd);
                default: throw new UsageException("unknown command '" + args[0] + "'.", null);
            }
        }

        public static string UsageOf(string command)
        {
            switch (command)
            {
                case "build": return BuildUsage;
                case "check": return CheckUsage;
                case "sync": return SyncUsage;
                case "export": return ExportUsage;
                case "init": return InitUsage;
                default: return null;
            }
        }

        // ------------------------------------------------------------------ commands

        static RunOptions ParseBuild(string[] args, string shell, string cwd)
        {
            Args a = Args.Read(args, 1, BuildUsage, new[] { "--plc", "--variant" }, new[] { "--clean", "--all-variants" });
            string project = a.Positionals.Count == 1 ? a.Positionals[0] : throw new UsageException("'build' needs exactly one <project>.", BuildUsage);
            Config config = Config.Load(a, null, cwd);
            string tsproj = ResolveTsproj(project, BuildUsage);
            return Make(config, shell, a,
                stages: SyncStages.Build,
                sourceFolder: config.Source,
                projectName: Path.GetFileNameWithoutExtension(tsproj),
                existingTsprojPath: tsproj,
                plcProjectName: ProjectLocator.ResolvePlcName(tsproj, a.Get("--plc")));
        }

        static RunOptions ParseCheck(string[] args, string shell, string cwd)
        {
            if (args.Length < 2)
                throw new UsageException("'check' needs a mode: parse|format|links|events.", CheckUsage);
            string mode = args[1];
            if (!new[] { "parse", "format", "links", "events" }.Contains(mode))
                throw new UsageException("unknown 'check' mode '" + mode + "'.", CheckUsage);

            Args a = Args.Read(args, 2, CheckUsage, new[] { "--project", "--ignore" }, new string[0]);
            string source = SingleSource(a, "check " + mode, CheckUsage);
            Config config = Config.Load(a, source, cwd);
            string tsproj = null;
            if (mode == "events")
                tsproj = ResolveTsproj(a.Get("--project") ?? config.Project
                    ?? throw new UsageException("'check events' needs --project <project>.", CheckUsage), CheckUsage);
            return Make(config, shell, a,
                stages: SyncStages.All, // not used: a check run never syncs
                sourceFolder: source,
                parseOnly: mode == "parse",
                formatCheck: mode == "format",
                checkLinks: mode == "links",
                checkEvents: mode == "events",
                existingTsprojPath: tsproj);
        }

        static RunOptions ParseExport(string[] args, string shell, string cwd)
        {
            if (args.Length < 2)
                throw new UsageException("'export' needs a mode: object|code|libs|io|events|links|all.", ExportUsage);
            string mode = args[1];
            string objectName = null;
            int first = 2;
            if (mode == "object")
            {
                if (args.Length < 3 || args[2].StartsWith("--"))
                    throw new UsageException("'export object' needs <ObjectName> <source> --project <project>.", ExportUsage);
                objectName = args[2];
                first = 3;
            }
            else if (!ExportModes.ContainsKey(mode))
                throw new UsageException("unknown 'export' mode '" + mode + "'.", ExportUsage);

            Args a = Args.Read(args, first, ExportUsage, new[] { "--project", "--plc" }, new[] { "--overwrite" });
            string source = SingleSource(a, "export " + mode, ExportUsage);
            Config config = Config.Load(a, source, cwd);
            string tsproj = ResolveTsproj(a.Get("--project") ?? config.Project
                ?? throw new UsageException("'export " + mode + "' needs --project <project>.", ExportUsage), ExportUsage);
            return Make(config, shell, a,
                stages: SyncStages.All, // not used: an export run never syncs
                sourceFolder: source,
                existingTsprojPath: tsproj,
                plcProjectName: ProjectLocator.ResolvePlcName(tsproj, a.Get("--plc")),
                reverseExports: objectName == null ? ExportModes[mode] : ReverseExports.None,
                exportLinks: mode == "links" || mode == "all",
                exportObjectName: objectName,
                overwrite: a.Has("--overwrite"));
        }

        static RunOptions ParseSync(string[] args, string shell, string cwd)
        {
            if (args.Length < 2)
                throw new UsageException("'sync' needs a mode: code|libs|io|events|variants|all.", SyncUsage);
            string mode = args[1];
            if (!SyncModes.ContainsKey(mode))
                throw new UsageException("unknown 'sync' mode '" + mode + "'.", SyncUsage);

            Args a = Args.Read(args, 2, SyncUsage, new[] { "--project", "--ignore", "--variant" },
                new[] { "--incremental", "--clean", "--confirm-delete", "--confirm-delete-io", "--all-variants" });
            string source = SingleSource(a, "sync " + mode, SyncUsage);
            Config config = Config.Load(a, source, cwd);
            string project = a.Get("--project") ?? config.Project
                ?? throw new UsageException("'sync " + mode + "' needs --project <project> (or project= in .stconfig).", SyncUsage);
            (string dest, string name) = OwnedProject.Resolve(project);
            return Make(config, shell, a,
                stages: SyncModes[mode],
                sourceFolder: source,
                destinationFolder: dest,
                projectName: name,
                incremental: a.Has("--incremental") || config.Incremental,
                confirmDelete: a.Has("--confirm-delete"),
                confirmDeleteIo: a.Has("--confirm-delete-io"));
        }

        static RunOptions ParseInit(string[] args, string shell, string cwd)
        {
            Args a = Args.Read(args, 1, InitUsage, new[] { "--into", "--name", "--ignore" }, new string[0]);
            string source = SingleSource(a, "init", InitUsage);
            Config config = Config.Load(a, source, cwd);
            string into = a.Get("--into") ?? throw new UsageException("'init' needs --into <folder> (where the new solution is created).", InitUsage);
            return Make(config, shell, a,
                stages: SyncStages.All,
                sourceFolder: source,
                destinationFolder: Path.GetFullPath(into),
                projectName: a.Get("--name") ?? new DirectoryInfo(source).Name,
                init: true);
        }

        // ------------------------------------------------------------------ helpers

        static RunOptions Make(Config config, string shell, Args a, SyncStages stages, string sourceFolder,
            string destinationFolder = null, string projectName = null, bool init = false,
            bool parseOnly = false, bool formatCheck = false, bool checkLinks = false, bool checkEvents = false,
            bool incremental = false, bool confirmDelete = false, bool confirmDeleteIo = false,
            ReverseExports reverseExports = ReverseExports.None, bool exportLinks = false, string exportObjectName = null,
            bool overwrite = false, string plcProjectName = null, string existingTsprojPath = null)
        {
            string source = Path.GetFullPath(sourceFolder ?? config.Source ?? ".");
            return new RunOptions(
                sourceFolder: source,
                destinationFolder: destinationFolder ?? Path.GetFullPath("."),
                projectName: projectName ?? new DirectoryInfo(source).Name,
                stages: stages,
                init: init,
                checkEvents: checkEvents,
                checkLinks: checkLinks,
                parseOnly: parseOnly,
                ignorePatterns: a.GetAll("--ignore"),
                incremental: incremental,
                exportObjectName: exportObjectName,
                exportLinks: exportLinks,
                confirmDelete: confirmDelete,
                formatCheck: formatCheck,
                confirmDeleteIo: confirmDeleteIo,
                reverseExports: reverseExports,
                overwrite: overwrite,
                plcProjectName: plcProjectName,
                existingTsprojPath: existingTsprojPath,
                configFileLoaded: config.Loaded,
                shell: shell ?? config.Shell,
                clean: a.Has("--clean") || config.Clean,
                sourceFolderExplicit: sourceFolder != null || config.Source != null,
                variant: a.Get("--variant"),
                allVariants: a.Has("--all-variants"));
        }

        static string SingleSource(Args a, string command, string usage)
        {
            if (a.Positionals.Count != 1)
                throw new UsageException(string.Format("'{0}' needs exactly one <source> folder.", command), usage);
            return Path.GetFullPath(a.Positionals[0]);
        }

        static string ResolveTsproj(string path, string usage)
        {
            try { return ProjectLocator.ResolveTsprojPath(path); }
            catch (Exception ex) when (ex is ArgumentException || ex is IOException || ex is InvalidOperationException)
            {
                throw new UsageException(ex.Message, usage);
            }
        }

        /// <summary>Argument reader for one command: positionals, "--name value" options (an option
        /// may repeat, e.g. --ignore), "--flag" switches, plus --config/--no-config.</summary>
        sealed class Args
        {
            public List<string> Positionals { get; } = new List<string>();
            readonly Dictionary<string, List<string>> _options = new Dictionary<string, List<string>>();
            readonly HashSet<string> _switches = new HashSet<string>();

            public static Args Read(string[] args, int first, string usage, IEnumerable<string> options, IEnumerable<string> switches)
            {
                var optionSet = new HashSet<string>(options.Concat(ConfigOptions));
                var switchSet = new HashSet<string>(switches.Concat(ConfigSwitches));
                var a = new Args();
                for (int i = first; i < args.Length; i++)
                {
                    string arg = args[i];
                    if (optionSet.Contains(arg))
                    {
                        if (i + 1 >= args.Length)
                            throw new UsageException(arg + " needs a value.", usage);
                        if (!a._options.TryGetValue(arg, out List<string> values))
                            a._options[arg] = values = new List<string>();
                        values.Add(args[++i]);
                    }
                    else if (switchSet.Contains(arg))
                        a._switches.Add(arg);
                    else if (arg.StartsWith("--"))
                        throw new UsageException("unknown option " + arg + "."
                            + (RemovedOptions.TryGetValue(arg, out string hint) ? " Removed - " + hint : ""), usage);
                    else
                        a.Positionals.Add(arg);
                }
                if (a.Has("--variant") && a.Has("--all-variants"))
                    throw new UsageException("use either --variant or --all-variants, not both.", usage);
                return a;
            }

            public string Get(string option) => _options.TryGetValue(option, out List<string> v) ? v[v.Count - 1] : null;
            public List<string> GetAll(string option) => _options.TryGetValue(option, out List<string> v) ? v : new List<string>();
            public bool Has(string flag) => _switches.Contains(flag) || _options.ContainsKey(flag);
        }

        /// <summary>.stconfig defaults. Discovery: --config's folder, else the source folder, else the
        /// current directory. Command-line values always win. Safety-relevant choices (creating a
        /// project, deleting, overwriting, the PLC name) are never read from it.</summary>
        sealed class Config
        {
            public bool Loaded;
            public string Source, Project, Shell;
            public bool Clean, Incremental;

            public static Config Load(Args a, string source, string cwd)
            {
                var c = new Config();
                if (a.Has("--no-config"))
                    return c;
                string dir = Path.GetFullPath(a.Get("--config") ?? source ?? cwd ?? Directory.GetCurrentDirectory());
                string path = Path.Combine(dir, ".stconfig");
                if (!File.Exists(path))
                    return c;

                IReadOnlyDictionary<string, string> values = StConfigFile.Load(dir);
                var problems = new List<string>();
                foreach (string key in values.Keys)
                {
                    if (RemovedConfigKeys.TryGetValue(key, out string hint))
                        problems.Add(string.Format("'{0}' is no longer supported — {1}", key, hint));
                    else if (!ConfigKeys.Contains(key, StringComparer.OrdinalIgnoreCase))
                        problems.Add(string.Format("unknown key '{0}'", key));
                }
                if (problems.Count > 0)
                    throw new UsageException(string.Format("{0}:\n    {1}\n  Supported keys: {2}.", path,
                        string.Join("\n    ", problems), string.Join(", ", ConfigKeys)), null);

                c.Loaded = true;
                c.Source = values.GetString("source");
                c.Project = values.GetString("project");
                c.Shell = values.GetString("shell");
                c.Clean = values.GetBool("clean");
                c.Incremental = values.GetBool("incremental");
                if (c.Source != null)
                    c.Source = Path.GetFullPath(Path.Combine(dir, c.Source));
                if (c.Project != null)
                    c.Project = Path.GetFullPath(Path.Combine(dir, c.Project));
                return c;
            }
        }
    }

    /// <summary>
    /// A project created by this tool (init) lives at &lt;dest&gt;\&lt;name&gt;\&lt;name&gt;.sln with the
    /// TwinCAT project in &lt;dest&gt;\&lt;name&gt;\&lt;name&gt;\&lt;name&gt;.tsproj. --project may name the .sln,
    /// its folder, or the .tsproj; this recovers (dest, name) and refuses anything else, so a
    /// wrong path can never quietly point a sync somewhere new.
    /// </summary>
    static class OwnedProject
    {
        public static (string Dest, string Name) Resolve(string path)
        {
            string full = Path.GetFullPath(path);
            string sln = null;
            if (File.Exists(full) && full.EndsWith(".sln", StringComparison.OrdinalIgnoreCase))
                sln = full;
            else if (File.Exists(full) && full.EndsWith(".tsproj", StringComparison.OrdinalIgnoreCase))
            {
                string name = Path.GetFileNameWithoutExtension(full);
                string solutionDir = Path.GetDirectoryName(Path.GetDirectoryName(full));
                sln = Path.Combine(solutionDir, name + ".sln");
            }
            else if (Directory.Exists(full))
            {
                string[] slns = Directory.GetFiles(full, "*.sln");
                if (slns.Length != 1)
                    throw new UsageException(string.Format("'{0}' must contain exactly one .sln (found {1}).", full, slns.Length), CommandLine.SyncUsage);
                sln = slns[0];
            }
            else
                throw new UsageException(string.Format("project not found: '{0}' (expected a .sln, its folder or a .tsproj; create a new one with 'init').", full), CommandLine.SyncUsage);

            string projectName = Path.GetFileNameWithoutExtension(sln);
            string slnDir = Path.GetDirectoryName(sln);
            if (!File.Exists(sln) || !string.Equals(Path.GetFileName(slnDir), projectName, StringComparison.OrdinalIgnoreCase))
                throw new UsageException(string.Format(
                    "'{0}' is not a project created by this tool (expected <folder>\\{1}\\{1}.sln).", path, projectName), CommandLine.SyncUsage);
            return (Path.GetDirectoryName(slnDir), projectName);
        }
    }
}
