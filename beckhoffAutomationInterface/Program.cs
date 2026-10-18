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

            List<string> duplicates = Sync.DuplicateObjects.Find(parsed);
            if (duplicates.Count > 0)
            {
                Console.WriteLine("{0}: [parse-only] {1} object(s) defined in more than one file (the last file would silently win):", Now(), duplicates.Count);
                PrintLines("! DUPLICATE ", duplicates);
            }

            List<string> lintIssues = Sync.StLinter.Lint(parsed);
            if (lintIssues.Count > 0)
            {
                Console.WriteLine("{0}: [lint] {1} naming convention warning(s):", Now(), lintIssues.Count);
                PrintLines("! ", lintIssues);
            }

            PrintLinkCheck(options, parsed);

            return failures.Count == 0 && duplicates.Count == 0 ? 0 : 1;
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

        /// <summary>Prints the top-level usage (docs/ideas/cli-subcommand-redesign.md, "Final CLI
        /// spec"). '<command> --help' prints that command's own usage line.</summary>
        static void PrintTopLevelUsage()
        {
            Console.WriteLine("Usage: beckhoffAutomationInterface <command> [args]");
            Console.WriteLine();
            Console.WriteLine("Commands:");
            Console.WriteLine("  build  <path> [--plc X] [--clean] [--variant V | --all-variants]");
            Console.WriteLine("      Compile an existing PLC project and report pass/fail.");
            Console.WriteLine("      <path> may be a .tsproj file, or a folder containing exactly one.");
            Console.WriteLine();
            Console.WriteLine("  check  parse|format|links <source-path>");
            Console.WriteLine("  check  events <source-path> --project <path>");
            Console.WriteLine("      Read-only preflight checks against .st source (and, for 'events', the project).");
            Console.WriteLine();
            Console.WriteLine("  sync   code|libs|io|events|variants|safety|tasks|all <source> --project <project> [--incremental] [--clean] [...]");
            Console.WriteLine("      Push .st source (and manifests) forward into the TwinCAT project.");
            Console.WriteLine();
            Console.WriteLine("  export code|libs|io|events|all|links <source-path> --project <path> [--plc X] [--overwrite]");
            Console.WriteLine("  export object <ObjectName> <source-path> --project <path> [--plc X]");
            Console.WriteLine("      Regenerate .st source (or manifests) FROM an existing project (reverse export);");
            Console.WriteLine("      'object' writes just the one named live PLC object back to .st.");
            Console.WriteLine();
            Console.WriteLine("  activate <path> --target umrt|local [--variant V] [--plc X] [--yes]");
            Console.WriteLine("      Build one variant and activate it on UmRT_Default or the local runtime, then wait for");
            Console.WriteLine("      the PLC to run. Without --yes: only shows what is on the target now (changes nothing).");
            Console.WriteLine();
            Console.WriteLine("  verify --target umrt|local --expect <expect.json> [--variant V] [--timeout S]");
            Console.WriteLine("      Read live PLC values over ADS and compare them with expect.json (exit 0 = all as expected).");
            Console.WriteLine();
            Console.WriteLine("  test   --target umrt|local --scenarios <file-or-folder> [--variant V] [--variants <variants.xml> | --groups G1,G2]");
            Console.WriteLine("         [--tags a,b] [--junit <file>] [--timeout S]");
            Console.WriteLine("      Run scenario files (send commands, wait for and check PLC values) over ADS. Exit 0 = all");
            Console.WriteLine("      passed, 1 = a scenario failed, 2 = wrong command line or invalid scenario file.");
            Console.WriteLine();
            Console.WriteLine("  doctor");
            Console.WriteLine("      Read-only report: DTE shells, TwinCAT version, local runtime and UmRT state,");
            Console.WriteLine("      boot projects and license expiry.");
            Console.WriteLine();
            Console.WriteLine("  init   <source> --into <folder> [--name X]");
            Console.WriteLine("      Bootstrap a brand-new TwinCAT solution/PLC project.");
            Console.WriteLine();
            Console.WriteLine("Global option (any command that opens TwinCAT):");
            Console.WriteLine("  --shell xae|vs2022   DTE shell to automate. Default: TwinCAT XAE shell if installed,");
            Console.WriteLine("                       else Visual Studio 2022. Also settable as 'shell=' in .stconfig.");
            Console.WriteLine();
            Console.WriteLine("Run '<command> --help' for that command's usage; README.md has the full flag reference.");
        }

        /// <summary>Removes "flag value" from args and returns the value (null when absent).
        /// For options every subcommand accepts.</summary>
        internal static string TakeGlobalOption(ref string[] args, string flag)
        {
            int i = Array.IndexOf(args, flag);
            if (i < 0 || i + 1 >= args.Length)
                return null;
            string value = args[i + 1];
            args = args.Take(i).Concat(args.Skip(i + 2)).ToArray();
            return value;
        }

        /// <summary>Parses a subcommand with Cli.CommandLine; prints the error and the command's
        /// usage and exits 1 on a command-line problem; prints usage and exits 0 on --help.</summary>
        static RunOptions ParseOrExit(string[] args, string shell)
        {
            if (args.Skip(1).Any(a => a == "--help" || a == "-h"))
            {
                Console.WriteLine("Usage: beckhoffAutomationInterface " + Cli.CommandLine.UsageOf(args[0]));
                Environment.Exit(0);
            }
            try
            {
                return Cli.CommandLine.Parse(args, shell);
            }
            catch (Cli.UsageException ex)
            {
                Console.Error.WriteLine("ERROR: " + ex.Message);
                if (ex.Usage != null)
                    Console.Error.WriteLine("Usage: beckhoffAutomationInterface " + ex.Usage);
                Environment.Exit(1);
                return null; // unreachable
            }
        }

        [STAThread]
        static void Main(string[] args)
        {
            if (args.Length == 0 || args[0] == "--help" || args[0] == "-h")
            {
                PrintTopLevelUsage();
                Environment.Exit(args.Length == 0 ? 1 : 0);
            }

            string shell = TakeGlobalOption(ref args, "--shell");
            if (shell != null && !DteShellResolver.IsKnownName(shell))
            {
                Console.Error.WriteLine("ERROR: unknown --shell '{0}'. Use one of: {1}.", shell,
                    string.Join(", ", DteShell.All.Select(s => s.Name)));
                Environment.Exit(1);
            }

            if (args.Skip(1).Any(a => a == "--help" || a == "-h"))
            {
                string usage = args[0] == "verify" ? Runtime.RuntimeCommands.VerifyUsage
                             : args[0] == "activate" ? Runtime.RuntimeCommands.ActivateUsage
                             : args[0] == "test" ? Runtime.RuntimeCommands.TestUsage
                             : args[0] == "doctor" ? "Usage: beckhoffAutomationInterface doctor"
                             : null;
                if (usage != null)
                {
                    Console.WriteLine(usage);
                    Environment.Exit(0);
                }
            }

            RunOptions options = null;
            switch (args[0])
            {
                // Runtime commands: new CLI style, no legacy-flag translation (see Runtime/).
                case "verify":
                    Environment.Exit(Runtime.RuntimeCommands.Verify(args));
                    break;
                case "activate":
                    Environment.Exit(Runtime.RuntimeCommands.Activate(args, shell));
                    break;
                case "test":
                    Environment.Exit(Runtime.RuntimeCommands.Test(args));
                    break;
                case "doctor":
                    Environment.Exit(Runtime.Doctor.Run());
                    break;
                case "build":
                case "check":
                case "export":
                case "sync":
                case "init":
                    options = ParseOrExit(args, shell);
                    break;
                default:
                    Console.Error.WriteLine("ERROR: unknown command '{0}'.", args[0]);
                    Console.Error.WriteLine("Expected one of: build, check, sync, export, init, activate, verify, test, doctor.");
                    Console.Error.WriteLine("Run with --help for usage.");
                    Environment.Exit(1);
                    break;
            }

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
                        Console.WriteLine("    ! MISSING '{0}' — will be created by the events stage ('sync events' or 'sync all').", name);
                }
            }
            if (options.CheckEvents)
            {
                Console.WriteLine("{0}: check events: event class check complete, skipping Visual Studio.", Now());
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
                    Console.Error.WriteLine("ERROR: the source folder already contains {0} file(s) that reverse export would overwrite:", existing.Count);
                    foreach (string f in existing.Take(10))
                        Console.Error.WriteLine("  {0}", f);
                    if (existing.Count > 10)
                        Console.Error.WriteLine("  ... and {0} more", existing.Count - 10);
                    Console.Error.WriteLine("Pass --overwrite to proceed, or export into an empty folder.");
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
                    Console.Error.WriteLine("Check that --project (or the <project> argument) points at the intended project.");
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
                Console.Error.WriteLine("Check that --project points at the intended project, or create a new one with");
                Console.Error.WriteLine("'init <source> --into <folder>'.");
                Environment.Exit(1);
            }

            // A project created or reopened BY NAME needs a name TwinCAT accepts. Check it
            // here, before a ~25 s shell start ends in "Cannot create new PLC project".
            if (!options.IsReverseExport && options.ExistingTsprojPath == null)
            {
                string nameProblem = RunOptions.PlcProjectNameProblem(options.PlcProjectName);
                if (nameProblem != null)
                {
                    Console.Error.WriteLine("ERROR: " + nameProblem);
                    Environment.Exit(1);
                }
            }

            // The Visual Studio session is owned by TwinCatSession: stages open it
            // lazily and the pipeline legitimately closes/reopens it mid-run for the
            // direct .tsproj edits — `using` here just guarantees final disposal.
            // A failed stage ends the run with one ERROR line and exit 1, not an unhandled-exception
            // crash; the session is still closed (the catch runs after the using's Dispose).
            try
            {
                using (var session = new TwinCatSession(options))
                {
                    new SyncPipeline(session, options, ignore).Run();
                }
            }
            catch (Exception ex) when (ex is InvalidOperationException || ex is IOException || ex is UnauthorizedAccessException
                                       || ex is FormatException || ex is ArgumentException || ex is System.Xml.XmlException)
            {
                Console.Error.WriteLine("ERROR: " + ex.Message);
                Environment.Exit(1);
            }
        }
    }
}
