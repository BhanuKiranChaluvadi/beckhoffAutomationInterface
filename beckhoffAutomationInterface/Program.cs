using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BeckhoffAutomationInterface.Sync;
using static BeckhoffAutomationInterface.Clock;
using static BeckhoffAutomationInterface.ConsoleReport;

namespace BeckhoffAutomationInterface
{
    /// <summary>
    /// CLI entry point: parses arguments, then either runs a read-only preflight that
    /// needs no Visual Studio (--parse-only, --format-check, --check-events, the
    /// --incremental baseline check), or hands off to SyncPipeline for everything else.
    ///
    /// Kept deliberately thin — pipeline orchestration and per-stage business logic live
    /// in SyncPipeline.cs (see its own doc comment for why the split): this class owns
    /// only "parse args, do the cheap up-front checks, decide whether to proceed."
    /// </summary>
    class Program
    {
        /// <summary>Parses every .st file under the source folder, collecting each parsed
        /// StPouSource plus a human-readable failure message (file-relative path + parser
        /// exception) per file that didn't parse. Shared by --parse-only and --check-links
        /// so both preflights pay for exactly one parse pass, not one each.</summary>
        static List<Sync.StPouSource> ParseAllStFiles(string stSourceFolder, IgnoreRules ignore, out List<string> failures, out int filesOk)
        {
            failures = new List<string>();
            filesOk = 0;
            var parsed = new List<Sync.StPouSource>();
            foreach (string file in Sync.StFileParser.GetStFiles(stSourceFolder, ignore))
            {
                try
                {
                    parsed.AddRange(Sync.StFileParser.ParseFile(file));
                    filesOk++;
                }
                catch (Exception ex)
                {
                    string rel = file.Substring(stSourceFolder.Length).TrimStart('\\', '/');
                    failures.Add($"  {rel}\n      {ex.Message}");
                }
            }
            return parsed;
        }

        /// <summary>Parses every .st file under the source folder without opening Visual
        /// Studio, aggregating and printing all parser failures, naming-convention lint
        /// warnings, and unlinked %I/%Q variables (all non-blocking — only parse failures
        /// affect the exit code). Returns a process exit code (0 = all parsed, 1 = one or
        /// more failed). Used by the --parse-only preflight.</summary>
        static int ParseOnly(RunOptions options, IgnoreRules ignore)
        {
            List<Sync.StPouSource> parsed = ParseAllStFiles(options.SourceFolder, ignore, out List<string> failures, out int filesOk);

            Console.WriteLine("{0}: [parse-only] {1} file(s) parsed OK ({2} PLC objects), {3} failed.",
                Now(), filesOk, parsed.Count, failures.Count);
            foreach (string f in failures)
                Console.WriteLine(f);

            List<string> lintIssues = Sync.StLinter.Lint(parsed);
            if (lintIssues.Count > 0)
            {
                Console.WriteLine("{0}: [lint] {1} naming convention warning(s):", Now(), lintIssues.Count);
                PrintLines("! ", lintIssues);
            }

            PrintLinkCheck(options, parsed);

            return failures.Count == 0 ? 0 : 1;
        }

        /// <summary>Prints the %I/%Q &lt;-&gt; &lt;Links&gt; report (see Sync/LinkChecker.cs), never
        /// affecting the caller's exit code — used both as a non-blocking note inside
        /// --parse-only and as the whole point of the dedicated --check-links preflight.</summary>
        static LinkCheckReport PrintLinkCheck(RunOptions options, IReadOnlyList<Sync.StPouSource> parsed)
        {
            var links = IoManifestParser.ParseLinks(options.IoManifestPath);
            var varLinks = VarLinksFile.Parse(options.VarLinksManifestPath);
            LinkCheckReport report = LinkChecker.Check(parsed, links, varLinks);

            Console.WriteLine("{0}: [check-links] {1} declared, {2} linked, {3} unlinked, {4} stale link(s).",
                Now(), report.Linked.Count + report.Unlinked.Count, report.Linked.Count, report.Unlinked.Count,
                report.OrphanedLinks.Count + report.OrphanedVarLinks.Count);
            foreach (DeclaredIoVariable variable in report.Unlinked)
                Console.WriteLine("    ! UNLINKED {0} ({1}) — {2}", variable.Key, variable.Direction, variable.SourceRelativePath);
            foreach (LinkSpec link in report.OrphanedLinks)
                Console.WriteLine("    ~ STALE link entry (io-devices.xml), no matching declaration: PlcVar=\"{0}\"", link.PlcVar);
            foreach (VarLinkEntry link in report.OrphanedVarLinks)
                Console.WriteLine("    ~ STALE link entry (links.xml), no matching declaration: VarA=\"{0}\"", link.VarA);
            if (report.Unresolvable.Count > 0)
            {
                Console.WriteLine("    ({0} links.xml entry(s) reference nested/FB-instance variables and can't be statically verified)",
                    report.Unresolvable.Count);
            }

            return report;
        }

        /// <summary>Lists the source-relative paths of files a reverse export would
        /// overwrite: every existing .st file under --source, plus any of the manifests
        /// (libraries/io-devices/events/links) already present. Empty when --source
        /// doesn't exist yet or is empty — the normal bootstrap case.</summary>
        static List<string> FindExistingSourceArtifacts(RunOptions options)
        {
            var found = new List<string>();
            if (!Directory.Exists(options.SourceFolder))
                return found;

            foreach (string st in Directory.EnumerateFiles(options.SourceFolder, "*.st", SearchOption.AllDirectories))
                found.Add(st.Substring(options.SourceFolder.Length).TrimStart('\\', '/'));

            foreach (string manifest in new[] { options.LibraryManifestPath, options.IoManifestPath, options.EventManifestPath, options.VarLinksManifestPath })
                if (File.Exists(manifest))
                    found.Add(Path.GetFileName(manifest));

            return found;
        }

        /// <summary>Translates the new `build &lt;path&gt; [--plc-name X]` subcommand syntax
        /// into the equivalent legacy flag array (--tsproj/--plc-name/--build), so it reuses
        /// the exact same, already-proven RunOptions/SyncPipeline path rather than duplicating
        /// it. First slice of docs/ideas/cli-subcommand-redesign.md — purely additive, the
        /// legacy flags below are untouched and still work exactly as before.</summary>
        static string[] TranslateBuildSubcommand(string[] args)
        {
            if (args.Skip(1).Any(a => a == "--help" || a == "-h"))
            {
                Console.WriteLine("Usage: beckhoffAutomationInterface build <path-to-.tsproj-or-folder> [--plc-name <name>]");
                Console.WriteLine();
                Console.WriteLine("Compiles an existing TwinCAT PLC project and reports pass/fail.");
                Console.WriteLine("  <path>              A .tsproj file, or a folder containing exactly one.");
                Console.WriteLine("  --plc-name <name>   The real PLC project name inside TIPC, if it differs");
                Console.WriteLine("                      from the .tsproj file's own name.");
                Console.WriteLine();
                Console.WriteLine("Exit code: 0 = build passed, 1 = build failed or timed out.");
                Environment.Exit(0);
            }

            string path = null;
            string plcName = null;
            for (int i = 1; i < args.Length; i++)
            {
                if (args[i] == "--plc-name" && i + 1 < args.Length)
                {
                    plcName = args[++i];
                }
                else if (path == null && !args[i].StartsWith("--"))
                {
                    path = args[i];
                }
                else
                {
                    Console.Error.WriteLine("ERROR: unrecognized argument to 'build': {0}", args[i]);
                    Environment.Exit(1);
                }
            }

            if (path == null)
            {
                Console.Error.WriteLine("ERROR: 'build' requires a path to the PLC project.");
                Console.Error.WriteLine("Usage: beckhoffAutomationInterface build <path-to-.tsproj-or-folder> [--plc-name <name>]");
                Environment.Exit(1);
            }

            string tsprojPath;
            try
            {
                tsprojPath = ProjectLocator.ResolveTsprojPath(path);
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine("ERROR: {0}", ex.Message);
                Environment.Exit(1);
                return null; // unreachable, satisfies the compiler
            }

            string resolvedPlcName = ProjectLocator.ResolvePlcName(tsprojPath, plcName);
            return new[] { "--tsproj", tsprojPath, "--plc-name", resolvedPlcName, "--build" };
        }

        /// <summary>Translates the new `check &lt;mode&gt; &lt;source-path&gt; [--project X]`
        /// subcommand syntax into the equivalent legacy flags (--source/--parse-only,
        /// --format-check, --check-links, --check-events/--tsproj) — same additive,
        /// zero-duplication approach as TranslateBuildSubcommand. Second slice of
        /// docs/ideas/cli-subcommand-redesign.md.</summary>
        static string[] TranslateCheckSubcommand(string[] args)
        {
            if (args.Length < 2)
            {
                Console.Error.WriteLine("ERROR: 'check' requires a mode: parse|format|links|events.");
                Environment.Exit(1);
            }
            string mode = args[1];

            string sourcePath = null;
            string projectPath = null;
            for (int i = 2; i < args.Length; i++)
            {
                if (args[i] == "--project" && i + 1 < args.Length)
                {
                    projectPath = args[++i];
                }
                else if (sourcePath == null && !args[i].StartsWith("--"))
                {
                    sourcePath = args[i];
                }
                else
                {
                    Console.Error.WriteLine("ERROR: unrecognized argument to 'check {0}': {1}", mode, args[i]);
                    Environment.Exit(1);
                }
            }

            if (sourcePath == null)
            {
                Console.Error.WriteLine("ERROR: 'check {0}' requires a path to the .st source folder.", mode);
                Console.Error.WriteLine("Usage: beckhoffAutomationInterface check {0} <source-path>{1}", mode,
                    mode == "events" ? " --project <path-to-tsproj-or-folder>" : "");
                Environment.Exit(1);
            }

            switch (mode)
            {
                case "parse":
                    return new[] { "--source", sourcePath, "--parse-only" };
                case "format":
                    return new[] { "--source", sourcePath, "--format-check" };
                case "links":
                    return new[] { "--source", sourcePath, "--check-links" };
                case "events":
                    if (projectPath == null)
                    {
                        Console.Error.WriteLine("ERROR: 'check events' requires --project <path-to-tsproj-or-folder>.");
                        Environment.Exit(1);
                    }
                    string tsprojPath;
                    try
                    {
                        tsprojPath = ProjectLocator.ResolveTsprojPath(projectPath);
                    }
                    catch (Exception ex)
                    {
                        Console.Error.WriteLine("ERROR: {0}", ex.Message);
                        Environment.Exit(1);
                        return null; // unreachable, satisfies the compiler
                    }
                    return new[] { "--source", sourcePath, "--tsproj", tsprojPath, "--check-events" };
                default:
                    Console.Error.WriteLine("ERROR: unknown 'check' mode '{0}' (expected parse|format|links|events).", mode);
                    Environment.Exit(1);
                    return null; // unreachable, satisfies the compiler
            }
        }

        /// <summary>Maps a `sync` subcommand mode to its legacy stage flag; null for "all"
        /// since no stage flag means SyncStages.All already (RunOptions' existing default).</summary>
        static readonly Dictionary<string, string> SyncModeToStageFlag = new Dictionary<string, string>
        {
            ["code"] = "--sync-code",
            ["libs"] = "--sync-libs",
            ["io"] = "--sync-io",
            ["events"] = "--sync-events",
            ["all"] = null,
        };

        /// <summary>Translates the new `sync &lt;mode&gt; &lt;source-path&gt; [legacy flags...]`
        /// subcommand syntax into the equivalent legacy --source/--sync-*/--build flags. Unlike
        /// `build`/`check`, sync's remaining flags (--dest/--name/--init/--incremental/
        /// --confirm-delete/--confirm-delete-io/--ignore) are forwarded verbatim rather than
        /// individually re-parsed here — RunOptions.Parse already validates all of them, so
        /// re-validating would just duplicate that logic. Third slice of
        /// docs/ideas/cli-subcommand-redesign.md.</summary>
        static string[] TranslateSyncSubcommand(string[] args)
        {
            if (args.Length < 2)
            {
                Console.Error.WriteLine("ERROR: 'sync' requires a mode: code|libs|io|events|all.");
                Environment.Exit(1);
            }
            string mode = args[1];
            if (!SyncModeToStageFlag.ContainsKey(mode))
            {
                Console.Error.WriteLine("ERROR: unknown 'sync' mode '{0}' (expected code|libs|io|events|all).", mode);
                Environment.Exit(1);
            }

            string sourcePath = null;
            var passthrough = new List<string>();
            for (int i = 2; i < args.Length; i++)
            {
                if (sourcePath == null && !args[i].StartsWith("--"))
                    sourcePath = args[i];
                else
                    passthrough.Add(args[i]);
            }

            if (sourcePath == null)
            {
                Console.Error.WriteLine("ERROR: 'sync {0}' requires a path to the .st source folder.", mode);
                Environment.Exit(1);
            }

            var result = new List<string> { "--source", sourcePath };
            result.AddRange(passthrough);
            string stageFlag = SyncModeToStageFlag[mode];
            if (stageFlag != null)
                result.Add(stageFlag);
            return result.ToArray();
        }

        /// <summary>Maps an `export` subcommand mode to its legacy reverse-export flag.</summary>
        static readonly Dictionary<string, string> ExportModeToFlag = new Dictionary<string, string>
        {
            ["code"] = "--export-code",
            ["libs"] = "--export-libs",
            ["io"] = "--export-io",
            ["events"] = "--export-events",
            ["all"] = "--export-all",
            ["links"] = "--export-links",
        };

        /// <summary>Translates the new `export &lt;mode&gt; &lt;source-path&gt; --project X
        /// [--plc-name Y] [--overwrite]` subcommand syntax into the equivalent legacy
        /// --source/--tsproj/--plc-name/--export-*/--overwrite flags, PLUS the single-object
        /// `export object &lt;ObjectName&gt; &lt;source-path&gt; --project X` shape (a different
        /// positional layout — object name AND source path are both positional — since it
        /// maps to legacy `--export &lt;name&gt;`, not one of the whole-project --export-* flags).
        /// --plc-name is resolved the same way `build` does (ProjectLocator.ResolvePlcName)
        /// rather than left to RunOptions' own default, which — with --tsproj but no --name —
        /// falls back to the SOURCE folder's directory name, not the .tsproj's; leaving it
        /// unresolved here would silently target the wrong PLC project. Fourth slice of
        /// docs/ideas/cli-subcommand-redesign.md.</summary>
        static string[] TranslateExportSubcommand(string[] args)
        {
            if (args.Length < 2)
            {
                Console.Error.WriteLine("ERROR: 'export' requires a mode: object|code|libs|io|events|all|links.");
                Environment.Exit(1);
            }
            string mode = args[1];

            if (mode == "object")
            {
                if (args.Length < 3)
                {
                    Console.Error.WriteLine("ERROR: 'export object' requires <ObjectName> <source-path> --project <path-to-tsproj-or-folder>.");
                    Environment.Exit(1);
                }
                string objectName = args[2];
                string objSourcePath = null;
                string objProjectPath = null;
                string objPlcName = null;
                var objPassthrough = new List<string>();
                for (int i = 3; i < args.Length; i++)
                {
                    if (args[i] == "--project" && i + 1 < args.Length)
                        objProjectPath = args[++i];
                    else if (args[i] == "--plc-name" && i + 1 < args.Length)
                        objPlcName = args[++i];
                    else if (objSourcePath == null && !args[i].StartsWith("--"))
                        objSourcePath = args[i];
                    else
                        objPassthrough.Add(args[i]);
                }

                if (objSourcePath == null)
                {
                    Console.Error.WriteLine("ERROR: 'export object' requires a path to the .st source destination folder.");
                    Environment.Exit(1);
                }
                if (objProjectPath == null)
                {
                    Console.Error.WriteLine("ERROR: 'export object' requires --project <path-to-tsproj-or-folder>.");
                    Environment.Exit(1);
                }

                string objTsprojPath;
                try
                {
                    objTsprojPath = ProjectLocator.ResolveTsprojPath(objProjectPath);
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine("ERROR: {0}", ex.Message);
                    Environment.Exit(1);
                    return null; // unreachable, satisfies the compiler
                }
                string objResolvedPlcName = ProjectLocator.ResolvePlcName(objTsprojPath, objPlcName);

                var objResult = new List<string> { "--source", objSourcePath, "--tsproj", objTsprojPath, "--plc-name", objResolvedPlcName, "--export", objectName };
                objResult.AddRange(objPassthrough);
                return objResult.ToArray();
            }

            if (!ExportModeToFlag.ContainsKey(mode))
            {
                Console.Error.WriteLine("ERROR: unknown 'export' mode '{0}' (expected object|code|libs|io|events|all|links).", mode);
                Environment.Exit(1);
            }

            string sourcePath = null;
            string projectPath = null;
            string plcName = null;
            var passthrough = new List<string>();
            for (int i = 2; i < args.Length; i++)
            {
                if (args[i] == "--project" && i + 1 < args.Length)
                    projectPath = args[++i];
                else if (args[i] == "--plc-name" && i + 1 < args.Length)
                    plcName = args[++i];
                else if (sourcePath == null && !args[i].StartsWith("--"))
                    sourcePath = args[i];
                else
                    passthrough.Add(args[i]);
            }

            if (sourcePath == null)
            {
                Console.Error.WriteLine("ERROR: 'export {0}' requires a path to the .st source destination folder.", mode);
                Environment.Exit(1);
            }
            if (projectPath == null)
            {
                Console.Error.WriteLine("ERROR: 'export {0}' requires --project <path-to-tsproj-or-folder>.", mode);
                Environment.Exit(1);
            }

            string tsprojPath;
            try
            {
                tsprojPath = ProjectLocator.ResolveTsprojPath(projectPath);
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine("ERROR: {0}", ex.Message);
                Environment.Exit(1);
                return null; // unreachable, satisfies the compiler
            }
            string resolvedPlcName = ProjectLocator.ResolvePlcName(tsprojPath, plcName);

            var result = new List<string> { "--source", sourcePath, "--tsproj", tsprojPath, "--plc-name", resolvedPlcName };
            result.AddRange(passthrough);
            result.Add(ExportModeToFlag[mode]);
            return result.ToArray();
        }

        /// <summary>Translates the new `init &lt;source-path&gt; --dest X [--name Y]`
        /// subcommand syntax into the equivalent legacy --source/--dest/--name/--init flags.
        /// Unlike build/check/export, init has nothing pre-existing to locate — it's the
        /// bootstrap case ProjectLocator doesn't apply to — so it keeps the conventional
        /// --dest/--name targeting verbatim rather than resolving a .tsproj. Fourth slice of
        /// docs/ideas/cli-subcommand-redesign.md.</summary>
        static string[] TranslateInitSubcommand(string[] args)
        {
            string sourcePath = null;
            var passthrough = new List<string>();
            for (int i = 1; i < args.Length; i++)
            {
                if (sourcePath == null && !args[i].StartsWith("--"))
                    sourcePath = args[i];
                else
                    passthrough.Add(args[i]);
            }

            if (sourcePath == null)
            {
                Console.Error.WriteLine("ERROR: 'init' requires a path to the .st source folder.");
                Environment.Exit(1);
            }
            if (!passthrough.Contains("--dest"))
            {
                Console.Error.WriteLine("ERROR: 'init' requires --dest <path> (where the new TwinCAT solution/project is created).");
                Environment.Exit(1);
            }

            var result = new List<string> { "--source", sourcePath };
            result.AddRange(passthrough);
            result.Add("--init");
            return result.ToArray();
        }

        /// <summary>Prints the new top-level subcommand usage. Replaces the old flat-flag
        /// entry point as of the Slice 5 cutover (docs/ideas/cli-subcommand-redesign.md) —
        /// RunOptions.PrintUsage()'s detailed flag reference still exists and still backs
        /// every subcommand internally (see the Translate* functions), but is no longer
        /// reachable by typing those flags directly at the top level.</summary>
        static void PrintTopLevelUsage()
        {
            Console.WriteLine("Usage: beckhoffAutomationInterface <command> [args]");
            Console.WriteLine();
            Console.WriteLine("Commands:");
            Console.WriteLine("  build  <path> [--plc-name X]");
            Console.WriteLine("      Compile an existing PLC project and report pass/fail.");
            Console.WriteLine("      <path> may be a .tsproj file, or a folder containing exactly one.");
            Console.WriteLine();
            Console.WriteLine("  check  parse|format|links <source-path>");
            Console.WriteLine("  check  events <source-path> --project <path>");
            Console.WriteLine("      Read-only preflight checks against .st source (and, for 'events', the project).");
            Console.WriteLine();
            Console.WriteLine("  sync   code|libs|io|events|all <source-path> [--dest X] [--init] [--incremental] [...]");
            Console.WriteLine("      Push .st source (and manifests) forward into the TwinCAT project.");
            Console.WriteLine();
            Console.WriteLine("  export code|libs|io|events|all|links <source-path> --project <path> [--plc-name X] [--overwrite]");
            Console.WriteLine("  export object <ObjectName> <source-path> --project <path> [--plc-name X]");
            Console.WriteLine("      Regenerate .st source (or manifests) FROM an existing project (reverse export);");
            Console.WriteLine("      'object' writes just the one named live PLC object back to .st.");
            Console.WriteLine();
            Console.WriteLine("  init   <source-path> --dest <path> [--name X]");
            Console.WriteLine("      Bootstrap a brand-new TwinCAT solution/PLC project.");
            Console.WriteLine();
            Console.WriteLine("Run '<command> --help' is not yet supported per-command; see README.md for full flag reference.");
        }

        [STAThread]
        static void Main(string[] args)
        {
            if (args.Length == 0 || args[0] == "--help" || args[0] == "-h")
            {
                PrintTopLevelUsage();
                Environment.Exit(args.Length == 0 ? 1 : 0);
            }

            switch (args[0])
            {
                case "build":
                    args = TranslateBuildSubcommand(args);
                    break;
                case "check":
                    args = TranslateCheckSubcommand(args);
                    break;
                case "sync":
                    args = TranslateSyncSubcommand(args);
                    break;
                case "export":
                    args = TranslateExportSubcommand(args);
                    break;
                case "init":
                    args = TranslateInitSubcommand(args);
                    break;
                default:
                    Console.Error.WriteLine("ERROR: unknown command '{0}'.", args[0]);
                    Console.Error.WriteLine("Expected one of: build, check, sync, export, init.");
                    Console.Error.WriteLine("Run with --help for usage.");
                    Environment.Exit(1);
                    break;
            }

            RunOptions options = RunOptions.Parse(args);
            if (options.ConfigFileLoaded)
                Console.WriteLine("{0}: Loaded defaults from '.stconfig' (pass --no-config to ignore it).", Now());
            Console.WriteLine("{0}: Source='{1}'  Dest='{2}'  Project='{3}'", Now(), options.SourceFolder, options.DestinationFolder, options.ProjectName);
            IgnoreRules ignore = IgnoreRules.Load(options.SourceFolder, options.IgnorePatterns);

            // Fast preflight: parse all .st files WITHOUT opening Visual Studio, so parser
            // issues surface in seconds (not after a ~40s VS round-trip). Run with --parse-only.
            if (options.ParseOnly)
            {
                Environment.Exit(ParseOnly(options, ignore));
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

            // Read-only "declared vs linked" %I/%Q preflight (see Sync/LinkChecker.cs) — no
            // Visual Studio, no existing project needed at all (unlike --check-events below,
            // which reads the live .tsproj). A full .st parse isn't free the way an XML file
            // read is, so — unlike the events check — this is NOT run informationally on
            // every normal invocation, only when explicitly requested here or via
            // --parse-only (see PrintLinkCheck, folded into ParseOnly's own output above).
            if (options.CheckLinks)
            {
                List<Sync.StPouSource> parsed = ParseAllStFiles(options.SourceFolder, ignore, out List<string> parseFailures, out int _);
                foreach (string f in parseFailures)
                    Console.WriteLine(f);
                LinkCheckReport linkReport = PrintLinkCheck(options, parsed);
                Environment.Exit(parseFailures.Count > 0 || linkReport.Unlinked.Count > 0 ? 1 : 0);
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

            // Reverse-export overwrite guard: regenerating the source tree is destructive
            // if --source already holds hand-edited files. Refuse unless --overwrite
            // (CLI-only, like --init/--confirm-delete*) — safe-by-default, so a mistyped
            // --source can't silently clobber real work. The forward direction is
            // unaffected. Runs before Visual Studio ever opens.
            if (options.IsReverseExport && !options.Overwrite)
            {
                List<string> existing = FindExistingSourceArtifacts(options);
                if (existing.Count > 0)
                {
                    Console.Error.WriteLine("ERROR: --source already contains {0} file(s) that reverse export would overwrite:", existing.Count);
                    foreach (string f in existing.Take(10))
                        Console.Error.WriteLine("  {0}", f);
                    if (existing.Count > 10)
                        Console.Error.WriteLine("  ... and {0} more", existing.Count - 10);
                    Console.Error.WriteLine("Pass --overwrite to proceed, or point --source at an empty folder.");
                    Environment.Exit(1);
                }
            }

            // Pre-flight checks
            if (!File.Exists(options.TwinCatTemplate))
            {
                Console.Error.WriteLine("ERROR: TwinCAT project template not found at:");
                Console.Error.WriteLine("  {0}", options.TwinCatTemplate);
                Console.Error.WriteLine("Ensure TwinCAT 3.1 XAE is installed.");
                Environment.Exit(1);
            }

            // Reverse export, and a plain --tsproj given for --build/CI use, both target
            // an ALREADY-EXISTING project, read-only — neither bootstraps (there'd be
            // nothing to export from / compile) and neither needs a .sln (see
            // TwinCatSession.EnsureOpen). Only the resolved .tsproj file's existence is
            // checked here; TsprojFilePath already resolves to --tsproj when given, else
            // the conventional dest/name-derived path.
            if (options.IsReverseExport || options.ExistingTsprojPath != null)
            {
                if (!File.Exists(options.TsprojFilePath))
                {
                    Console.Error.WriteLine("ERROR: project file not found:");
                    Console.Error.WriteLine("  {0}", options.TsprojFilePath);
                    Console.Error.WriteLine("Check --source/--dest/--name/--tsproj point at the intended project.");
                    Console.Error.WriteLine("This mode never bootstraps a new project — there must be a real one to read from.");
                    Environment.Exit(1);
                }
            }
            // Creating a NEW project is explicit (--init), never a silent fallback: a
            // mistyped --dest/--name used to quietly bootstrap a fresh empty project
            // (and once planted one inside the real project's own folder tree — see
            // tasks/archive/2026-07-14-post-review-hardening/). In CI, a wrong path now
            // fails loudly instead of green-building an empty project.
            else if (!File.Exists(options.SolutionFilePath) && !options.Init)
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
                new SyncPipeline(session, options, ignore).Run();
            }
        }
    }
}
