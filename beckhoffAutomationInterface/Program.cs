using System;
using System.Collections.Generic;
using System.IO;
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

        [STAThread]
        static void Main(string[] args)
        {
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
                new SyncPipeline(session, options, ignore).Run();
            }
        }
    }
}
