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
    /// needs no Visual Studio (check parse, check format, check events), or hands off
    /// to SyncPipeline for everything else.
    ///
    /// Kept deliberately thin — pipeline orchestration and per-stage business logic live
    /// in SyncPipeline.cs (see its own doc comment for why the split): this class owns
    /// only "parse args, do the cheap up-front checks, decide whether to proceed."
    /// </summary>
    class Program
    {
        /// <summary>Parses every .st file under the source folder, collecting each parsed
        /// StPouSource plus a human-readable failure message (file-relative path + parser
        /// exception) per file that didn't parse.</summary>
        internal static List<Sync.StPouSource> ParseAllStFiles(string stSourceFolder, IgnoreRules ignore, out List<string> failures, out int filesOk)
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
        /// more failed). Used by 'check parse'.</summary>
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
        /// affecting the caller's exit code — a non-blocking part of 'check parse'.</summary>
        static void PrintLinkCheck(RunOptions options, IReadOnlyList<Sync.StPouSource> parsed)
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

            foreach (string manifest in new[] { options.LibraryManifestPath, options.IoManifestPath, options.EventManifestPath, options.VarLinksManifestPath, options.TaskManifestPath, options.SafetyManifestPath, options.VariantManifestPath, Path.Combine(options.SourceFolder, "native-project.zip") })
                if (File.Exists(manifest))
                    found.Add(Path.GetFileName(manifest));

            return found;
        }

        /// <summary>Removes "flag value" from args and returns the value (null when absent).
        /// For options every subcommand accepts.</summary>
        internal static string TakeGlobalOption(ref string[] args, string flag)
        {
            int i = Array.IndexOf(args, flag);
            if (i < 0) return null;
            if (i + 1 >= args.Length || args[i + 1].StartsWith("-", StringComparison.Ordinal) || string.IsNullOrWhiteSpace(args[i + 1]))
                throw new Cli.UsageException(flag + " needs a value.", null);
            if (args.Skip(i + 1).Contains(flag))
                throw new Cli.UsageException(flag + " may only be specified once.", null);
            string value = args[i + 1];
            args = args.Take(i).Concat(args.Skip(i + 2)).ToArray();
            return value;
        }

        /// <summary>Parses a subcommand with Cli.CommandLine; prints the error and the command's
        /// usage and exits 1 on a command-line problem; prints usage and exits 0 on --help.</summary>
        static RunOptions ParseOrExit(string[] args, string shell)
        {
            try
            {
                return Cli.CommandLine.Parse(args, shell);
            }
            catch (Cli.UsageException ex)
            {
                Cli.CliHelp.WriteError(Console.Error, args, ex.Message, Cli.ConsoleLayout.TerminalWidth);
                Environment.Exit(1);
                return null; // unreachable
            }
        }

        [STAThread]
        static void Main(string[] args)
        {
            Cli.ColorConsole.Install();
            string shell;
            try { shell = TakeGlobalOption(ref args, "--shell"); }
            catch (Cli.UsageException ex)
            {
                Cli.CliHelp.WriteError(Console.Error, args, ex.Message);
                Environment.Exit(1);
                return;
            }

            if (args.Length == 0)
            {
                Cli.CliHelp.Write(Console.Out, Console.Error, new string[0], Cli.ConsoleLayout.TerminalWidth);
                Environment.Exit(1);
                return;
            }
            if (args[0] == "help" || args.Contains("--help") || args.Contains("-h"))
            {
                string[] path = args[0] == "help"
                    ? args.Skip(1).Where(a => a != "--help" && a != "-h").ToArray()
                    : Cli.CliHelp.HelpPath(args);
                Environment.Exit(Cli.CliHelp.Write(Console.Out, Console.Error, path, Cli.ConsoleLayout.TerminalWidth));
                return;
            }
            if (args.Length == 1 && args[0] == "--version")
            {
                Console.WriteLine("bai " + Cli.CliHelp.Version);
                return;
            }
            if (shell != null && !DteShellResolver.IsKnownName(shell))
            {
                Cli.CliHelp.WriteError(Console.Error, args, "unknown --shell '" + shell + "'. Use one of: " +
                    string.Join(", ", DteShell.All.Select(s => s.Name)) + ".");
                Environment.Exit(1);
                return;
            }

            RunOptions options = null;
            switch (args[0])
            {
                case "package":
                case "libraries":
                case "toolchain":
                case "esi":
                    Environment.Exit(Release.ArtifactCommands.Run(args));
                    break;
                case "release":
                    Environment.Exit(Release.ReleaseCommands.Run(args, shell));
                    break;
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
                case "license":
                    Environment.Exit(Runtime.LicenseCommands.Run(args, shell));
                    break;
                case "doctor":
                    if (args.Length != 1)
                    {
                        Cli.CliHelp.WriteError(Console.Error, args, "doctor does not accept arguments: " + string.Join(" ", args.Skip(1)));
                        Environment.Exit(1);
                    }
                    Environment.Exit(Runtime.Doctor.Run());
                    break;
                case "runtime-state":
                    Environment.Exit(Runtime.RuntimeStateCommand.Run(args));
                    break;
                case "machine":
                    Environment.Exit(Machines.MachineCommands.Run(args, shell));
                    break;
                case "native":
                    Environment.Exit(Machines.NativeCommands.Run(args));
                    break;
                case "build":
                case "check":
                case "export":
                case "sync":
                case "init":
                    options = ParseOrExit(args, shell);
                    break;
                default:
                    Cli.CliHelp.WriteError(Console.Error, args, "unknown command '" + args[0] + "'.");
                    Environment.Exit(1);
                    break;
            }

            if (options.ConfigFileLoaded)
                Console.WriteLine("{0}: Loaded defaults from '.stconfig' (pass --no-config to ignore it).", Now());
            ConsoleReport.Heading("bai " + args[0] + (args.Length > 1 && new[] { "check", "sync", "export" }.Contains(args[0]) ? " " + args[1] : ""));
            var context = new Dictionary<string, object> { ["Source"] = options.SourceFolder };
            if (options.ExistingTsprojPath != null) context["Project"] = options.ExistingTsprojPath;
            else if (options.Init || args[0] == "sync") context["Project"] = options.SolutionFilePath;
            ConsoleReport.Fields(context);
            Console.WriteLine();
            IgnoreRules ignore = IgnoreRules.Load(options.SourceFolder, options.IgnorePatterns);

            // Fast preflight: parse all .st files WITHOUT opening Visual Studio, so parser
            // issues surface in seconds (not after a ~40s VS round-trip). 'check parse'.
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

            // Read-only "declared vs actual" Event Class preflight (reads the .tsproj
            // directly, no VS session needed). Informational on a normal run — the Events
            // stage creates anything missing via TsprojEventClassEditor later. Under
            // 'check events' it's the whole run: report and exit, code 1 if anything is
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

            // Reverse-export overwrite guard: regenerating the source tree is destructive
            // if the source folder already holds hand-edited files. Refuse unless --overwrite
            // (CLI-only, like --confirm-delete-io) — safe-by-default, so a mistyped
            // source path can't silently clobber real work. The forward direction is
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

            // Reverse export, and a .tsproj given to 'build' (CI use), both target
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
            // Creating a NEW project is explicit ('init'), never a silent fallback: a
            // mistyped path must fail loudly, not green-build a fresh empty project.
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
                                       || ex is FormatException || ex is ArgumentException || ex is System.Xml.XmlException
                                       || ex is System.Runtime.InteropServices.COMException)
            {
                Console.Error.WriteLine("ERROR: " + (ex.Data[TreeItemFactory.CreationContextKey] ?? ex.Message));
                Environment.Exit(1);
            }
        }
    }
}
