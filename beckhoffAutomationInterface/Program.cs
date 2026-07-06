using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using BeckhoffAutomationInterface.Sync;
using Interop.TCatSysManager;

namespace BeckhoffAutomationInterface
{
    /// <summary>
    /// Syncs ST/Shark/*.st files into a persistent "Shark" TwinCAT PLC project via
    /// the Automation Interface: opens the project if it already exists (instead of
    /// recreating it every run), reconciles POUs (create/update/delete) using
    /// Sync.PouSyncEngine, then builds and reports pass/fail via Sync.BuildRunner.
    ///
    /// This is the MVP engine described in docs/ideas/st-source-twincat-sync.md,
    /// assembled from spikes validated end-to-end on 2026-07-04.
    /// </summary>
    class Program
    {
        const uint RPC_E_SERVERCALL_RETRYLATER = 0x8001010A;
        const int RETRY_TIMEOUT_MS = 30000;
        const int RETRY_INTERVAL_MS = 1000;

        static string Now() => DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");

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
                foreach (string issue in lintIssues)
                    Console.WriteLine("    ! {0}", issue);
            }

            return failures.Count == 0 ? 0 : 1;
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
                catch (COMException ex) when ((uint)ex.HResult == RPC_E_SERVERCALL_RETRYLATER && elapsed < RETRY_TIMEOUT_MS)
                {
                    Console.WriteLine("{0}: Visual Studio is busy ({1}), retrying in 1s... ({2}s elapsed)",
                        Now(), description, elapsed / 1000);
                    Thread.Sleep(RETRY_INTERVAL_MS);
                    elapsed += RETRY_INTERVAL_MS;
                }
            }
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

            // Event Classes (events.xml) are a .tsproj-level config item with no known
            // Automation Interface creation path — automating creation is a confirmed dead
            // end (see docs/ideas/st-plc-bidirectional-sync.md), so Event Classes must be
            // created ONCE, manually, via the real XAE UI. This is a read-only "declared vs
            // actual" check (reads the .tsproj directly, no VS session needed) that reports
            // which declared classes are still missing, rather than attempting to write them.
            if (File.Exists(options.TsprojFilePath))
            {
                var desiredEventClasses = EventManifestParser.Parse(options.EventManifestPath);
                if (desiredEventClasses.Count > 0)
                {
                    EventClassCheckReport eventReport = EventClassChecker.Check(options.TsprojFilePath, desiredEventClasses);
                    Console.WriteLine("{0}: Event class check: {1} declared, {2} present, {3} missing.",
                        Now(), desiredEventClasses.Count, eventReport.Present.Count, eventReport.Missing.Count);
                    foreach (string name in eventReport.Missing)
                        Console.WriteLine("    ! MISSING '{0}' — create manually via SYSTEM \u25b8 Type System \u25b8 Event Classes \u25b8 New (see events.xml), then re-run.", name);
                }
            }
            if (options.EventsOnly)
            {
                Console.WriteLine("{0}: --events-only: event class check complete, skipping Visual Studio.", Now());
                Environment.Exit(0);
            }
            // Pre-flight checks
            if (!File.Exists(options.TwinCatTemplate))
            {
                Console.Error.WriteLine("ERROR: TwinCAT project template not found at:");
                Console.Error.WriteLine("  {0}", options.TwinCatTemplate);
                Console.Error.WriteLine("Ensure TwinCAT 3.1 XAE is installed.");
                Environment.Exit(1);
            }

            // Create the Visual Studio DTE instance
            using (VisualStudioSession vs = VisualStudioSession.Start())
            {
                RunSync(vs.Dte, options, ignore);
            }
        }

        static void RunSync(EnvDTE80.DTE2 dte, RunOptions options, IgnoreRules ignore)
        {
            (EnvDTE.Project project, ITcSysManager sysManager) = TwinCatProjectOpener.Open(dte, options);

            project.Save();
            dte.Solution.SaveAs(options.SolutionFilePath);
            Console.WriteLine("{0}: Solution saved.", Now());

            if (!options.BuildOnly)
            {
            // Sync .st files -> POUs (create/update/delete). --incremental narrows this to
            // only the files git says changed/were deleted since the last recorded sync,
            // instead of re-parsing/re-syncing the whole source folder every time.
            List<StPouSource> desiredPous;
            if (options.Incremental)
            {
                string lastSha = SyncState.Read(options.SyncStatePath);
                if (lastSha == null)
                {
                    Console.Error.WriteLine("ERROR: --incremental requested but no baseline found at '{0}'.", options.SyncStatePath);
                    Console.Error.WriteLine("Run a full sync (without --incremental) first to establish one.");
                    Environment.Exit(1);
                }

                Console.WriteLine("{0}: Computing .st changes since {1}...", Now(), lastSha);
                GitDiffResult diff = GitDiffHelper.GetChangedStFiles(options.SourceFolder, lastSha);

                desiredPous = new List<StPouSource>();
                foreach (string relativePath in diff.Changed)
                {
                    if (ignore.IsIgnored(relativePath))
                        continue;
                    string fullPath = Path.Combine(options.SourceFolder, relativePath.Replace('/', Path.DirectorySeparatorChar));
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
                    Console.WriteLine("{0}: NOTE: deleting the corresponding PLC object(s) is not yet automated — remove manually if desired:", Now());
                    foreach (string f in diff.Deleted) Console.WriteLine("    ! {0}", f);
                }
            }
            else
            {
                Console.WriteLine("{0}: Parsing .st sources from '{1}'...", Now(), options.SourceFolder);
                desiredPous = StFileParser.ParseFolder(options.SourceFolder, ignore);
            }

            List<string> lintIssues = StLinter.Lint(desiredPous);
            if (lintIssues.Count > 0)
            {
                Console.WriteLine("{0}: [lint] {1} naming convention warning(s):", Now(), lintIssues.Count);
                foreach (string issue in lintIssues)
                    Console.WriteLine("    ! {0}", issue);
            }

            Console.WriteLine("{0}: Syncing {1} PLC object(s)...", Now(), desiredPous.Count);
            var syncEngine = new PouSyncEngine(sysManager, options.ProjectRootPath);
            SyncReport syncReport = syncEngine.Sync(desiredPous);

            foreach (string name in syncReport.Created) Console.WriteLine("    + created  {0}", name);
            foreach (string name in syncReport.Updated) Console.WriteLine("    ~ updated  {0}", name);
            foreach (string name in syncReport.Deleted) Console.WriteLine("    - deleted  {0}", name);

            project.Save();
            Console.WriteLine("{0}: Sync complete ({1} created, {2} updated, {3} deleted).",
                Now(), syncReport.Created.Count, syncReport.Updated.Count, syncReport.Deleted.Count);

            // Record the new sync baseline (best-effort; silently skipped if SourceFolder
            // isn't inside a git repo, since --incremental is opt-in and needs one anyway).
            string headSha = GitDiffHelper.TryGetHeadSha(options.SourceFolder);
            if (headSha != null)
            {
                SyncState.Write(options.SyncStatePath, headSha);
                Console.WriteLine("{0}: Recorded sync baseline {1} in '{2}'.", Now(), headSha, options.SyncStatePath);
            }

            // Sync library references from libraries.xml (config data, not .st source)
            Console.WriteLine("{0}: Parsing library manifest '{1}'...", Now(), options.LibraryManifestPath);
            var desiredLibraries = LibraryManifestParser.Parse(options.LibraryManifestPath);

            Console.WriteLine("{0}: Syncing {1} library reference(s)...", Now(), desiredLibraries.Count);
            ITcSmTreeItem referencesItem = sysManager.LookupTreeItem(options.ReferencesTreePath);
            ITcPlcLibraryManager libManager = (ITcPlcLibraryManager)referencesItem;
            LibrarySyncReport libraryReport = null;
            RetryOnBusy(() => libraryReport = LibrarySyncEngine.Sync(libManager, desiredLibraries), "syncing library references");

            foreach (string name in libraryReport.Added) Console.WriteLine("    + added    {0}", name);
            foreach (string name in libraryReport.Removed) Console.WriteLine("    - removed  {0}", name);

            project.Save();
            Console.WriteLine("{0}: Library sync complete ({1} added, {2} removed).",
                Now(), libraryReport.Added.Count, libraryReport.Removed.Count);

            // ---------------------------------------------------------------
            // Sync the I/O hardware tree (Device -> Box -> Terminal) from
            // io-devices.xml (config data, not .st source \u2014 same rationale as
            // libraries.xml). Idempotent: existing items are detected via
            // LookupTreeItem and left untouched; only missing ones are created,
            // and only orphaned ones (removed from the manifest) are deleted.
            // The master is then linked to the PLC %I*/%Q* variables (see the
            // <Links> section handling below) so the "needs sync master"
            // validation is satisfied and the build passes with the master
            // enabled \u2014 no popup, fully unattended.
            // ---------------------------------------------------------------
            string ioManifestPath = options.IoManifestPath;
            Console.WriteLine("{0}: Parsing IO manifest '{1}'...", Now(), ioManifestPath);
            var desiredIoDevices = IoManifestParser.Parse(ioManifestPath);

            Console.WriteLine("{0}: Syncing {1} IO device(s)...", Now(), desiredIoDevices.Count);
            IoSyncReport ioReport = null;
            RetryOnBusy(() => ioReport = IoSyncEngine.Sync(sysManager, desiredIoDevices), "syncing IO tree");

            foreach (string name in ioReport.Created) Console.WriteLine("    + created  {0}", name);
            foreach (string name in ioReport.Deleted) Console.WriteLine("    - deleted  {0}", name);
            foreach (string change in ioReport.StateChanged) Console.WriteLine("    ~ state    {0}", change);

            project.Save();
            Console.WriteLine("{0}: IO sync complete ({1} created, {2} deleted, {3} state change(s)).",
                Now(), ioReport.Created.Count, ioReport.Deleted.Count, ioReport.StateChanged.Count);

            // ---------------------------------------------------------------
            // Sync PLC-variable <-> IO-channel links declared in <Links> of
            // io-devices.xml. Path format confirmed from Beckhoff's official
            // EtherCATLinking.cs sample. If any declared link can't be resolved
            // (the PLC instance image and EtherCAT channels only materialize as
            // tree items after Activate Configuration on a real/simulated target
            // \u2014 unavailable in a plain dev environment), we fall back to
            // disabling the master(s) so the build stays green and unattended.
            // ---------------------------------------------------------------
            var desiredLinks = IoManifestParser.ParseLinks(ioManifestPath);
            if (desiredLinks.Count > 0)
            {
                Console.WriteLine("{0}: Syncing {1} variable link(s)...", Now(), desiredLinks.Count);
                VariableLinkReport linkReport = null;
                RetryOnBusy(() => linkReport = VariableLinkEngine.Sync(sysManager, options.ProjectName, desiredLinks), "linking variables");

                foreach (string s in linkReport.Linked) Console.WriteLine("    + linked   {0}", s);
                foreach (string s in linkReport.Failed) Console.WriteLine("    x unlinked {0}", s);

                if (!linkReport.AllLinked)
                {
                    Console.WriteLine("{0}: Some links unresolved (the PLC instance image / EtherCAT channels", Now());
                    Console.WriteLine("        require Activate Configuration against a real or simulated target).");
                    List<string> disabled = IoSyncEngine.DisableAllMasters(sysManager);
                    foreach (string name in disabled)
                        Console.WriteLine("        ~ disabled master '{0}' to keep the build green.", name);
                }
                project.Save();
                Console.WriteLine("{0}: Variable link sync complete ({1} linked, {2} unresolved).",
                    Now(), linkReport.Linked.Count, linkReport.Failed.Count);
            }
            } // end if (!options.BuildOnly)

            // Build and report
            Console.WriteLine("{0}: Building solution...", Now());
            BuildReport buildReport = null;
            try
            {
                RetryOnBusy(() => buildReport = BuildRunner.Build(dte), "building solution");
            }
            catch (BuildTimeoutException ex)
            {
                Console.WriteLine("{0}: BUILD TIMED OUT \u2014 {1}", Now(), ex.Message);
                return; // finally in Main will force-close VS (dialog is still up)
            }

            if (buildReport.Success)
            {
                Console.WriteLine("{0}: BUILD PASSED \u2014 project compiled cleanly with no errors.", Now());
            }
            else
            {
                Console.WriteLine("{0}: BUILD FAILED \u2014 {1} error(s):", Now(), buildReport.Errors.Count);
                foreach (BuildError error in buildReport.Errors)
                    Console.WriteLine("    [ERROR] {0} ({1}:{2})", error.Description, error.FileName, error.Line);
            }

            if (buildReport.Warnings.Count > 0)
            {
                Console.WriteLine("{0}: {1} warning(s):", Now(), buildReport.Warnings.Count);
                foreach (BuildError warning in buildReport.Warnings)
                    Console.WriteLine("    [WARN] {0} ({1}:{2})", warning.Description, warning.FileName, warning.Line);
            }
        }
    }
}
