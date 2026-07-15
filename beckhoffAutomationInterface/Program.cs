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
                PrintLines("! ", lintIssues);
            }

            return failures.Count == 0 ? 0 : 1;
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
                new SyncPipeline(session, options, ignore).Run();
            }
        }
    }
}
