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
        const int VS_LOAD_TIMEOUT_MS = 30000; // 30 seconds max wait for VS to load
        const int VS_LOAD_RETRY_INTERVAL_MS = 1000;
        const int VS_QUIT_TIMEOUT_MS = 30000; // 30 seconds for a graceful dte.Quit() before force-killing

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        static extern int GetWindowThreadProcessId(IntPtr hWnd, out int processId);

        static string Now() => DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");

        /// <summary>
        /// Waits for VS to finish loading by retrying a COM call until it succeeds
        /// or the timeout is reached. Handles RPC_E_SERVERCALL_RETRYLATER (0x8001010A)
        /// which VS raises while its message pump is busy initializing.
        /// </summary>
        static void WaitForVsToLoad(EnvDTE80.DTE2 dte)
        {
            int elapsed = 0;
            while (elapsed < VS_LOAD_TIMEOUT_MS)
            {
                try
                {
                    // Accessing MainWindow forces a COM round-trip; if VS is busy it throws
                    dte.MainWindow.Visible = true;
                    return; // success
                }
                catch (COMException ex) when ((uint)ex.HResult == RPC_E_SERVERCALL_RETRYLATER)
                {
                    Console.WriteLine("{0}: Visual Studio is loading, retrying in 1s... ({1}s elapsed)",
                        Now(), elapsed / 1000);
                    Thread.Sleep(VS_LOAD_RETRY_INTERVAL_MS);
                    elapsed += VS_LOAD_RETRY_INTERVAL_MS;
                }
            }
            throw new TimeoutException("Visual Studio did not finish loading within the timeout period.");
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
                catch (COMException ex) when ((uint)ex.HResult == RPC_E_SERVERCALL_RETRYLATER && elapsed < VS_LOAD_TIMEOUT_MS)
                {
                    Console.WriteLine("{0}: Visual Studio is busy ({1}), retrying in 1s... ({2}s elapsed)",
                        Now(), description, elapsed / 1000);
                    Thread.Sleep(VS_LOAD_RETRY_INTERVAL_MS);
                    elapsed += VS_LOAD_RETRY_INTERVAL_MS;
                }
            }
        }

        static void Main(string[] args)
        {
            // Configuration
            const string standardPlcProjectTemplate = "Standard PLC Template.plcproj";
            const string plcName = "Shark";
            const string solutionName = "Shark";
            string solutionDirectory = @"C:\Users\BhanuKiranChaluvadi\Documents\TwinCAT\Shark";
            string solutionFilePath = Path.Combine(solutionDirectory, solutionName + ".sln");
            string stSourceFolder = @"C:\Users\BhanuKiranChaluvadi\Documents\Tutorials\beckhoffAutomationInterface\ST\Shark";
            string twincatTemplate = @"C:\Program Files (x86)\Beckhoff\TwinCAT\3.1\Components\Base\PrjTemplate\TwinCAT Project.tsproj";
            string pousTreePath = string.Format("TIPC^{0}^{0} Project^POUs", plcName);
            string dutsTreePath = string.Format("TIPC^{0}^{0} Project^DUTs", plcName);
            string gvlsTreePath = string.Format("TIPC^{0}^{0} Project^GVLs", plcName);
            string referencesTreePath = string.Format("TIPC^{0}^{0} Project^References", plcName);
            string libraryManifestPath = Path.Combine(stSourceFolder, "libraries.xml");

            // Pre-flight checks
            if (!File.Exists(twincatTemplate))
            {
                Console.Error.WriteLine("ERROR: TwinCAT project template not found at:");
                Console.Error.WriteLine("  {0}", twincatTemplate);
                Console.Error.WriteLine("Ensure TwinCAT 3.1 XAE is installed.");
                Environment.Exit(1);
            }

            // Create the Visual Studio DTE instance
            Console.WriteLine("{0}: Getting Visual Studio DTE type...", Now());
            Type dteType = Type.GetTypeFromProgID("VisualStudio.DTE.17.0");
            if (dteType == null)
            {
                Console.Error.WriteLine("ERROR: VisualStudio.DTE.17.0 is not registered.");
                Console.Error.WriteLine("Ensure Visual Studio 2022 (17.x) is installed.");
                Environment.Exit(1);
            }
            Console.WriteLine("{0}: Visual Studio DTE type resolved.", Now());

            Console.WriteLine("{0}: Creating the DTE instance...", Now());
            // Snapshot existing devenv PIDs so we can reliably identify the one WE spawn
            // (HWND-based capture is fragile once a modal dialog is up).
            var devenvBefore = new HashSet<int>(
                System.Diagnostics.Process.GetProcessesByName("devenv").Select(p => p.Id));
            EnvDTE80.DTE2 dte = (EnvDTE80.DTE2)Activator.CreateInstance(dteType);
            dte.SuppressUI = false;

            Console.WriteLine("{0}: Waiting for Visual Studio to finish loading...", Now());
            WaitForVsToLoad(dte);
            Console.WriteLine("{0}: Visual Studio is ready.", Now());

            // Identify our devenv process (the one that appeared after CreateInstance), so we
            // can force-kill it later if a graceful dte.Quit() leaves it alive (e.g. behind a
            // modal dialog). Fall back to the HWND method if the diff is inconclusive.
            int devenvPid = System.Diagnostics.Process.GetProcessesByName("devenv")
                .Select(p => p.Id)
                .FirstOrDefault(id => !devenvBefore.Contains(id));
            if (devenvPid == 0)
            {
                try { GetWindowThreadProcessId((IntPtr)dte.MainWindow.HWnd, out devenvPid); }
                catch { /* non-fatal: we just lose the force-kill fallback */ }
            }

            try
            {
                RunSync(dte, standardPlcProjectTemplate, plcName, solutionName, solutionDirectory,
                    solutionFilePath, stSourceFolder, twincatTemplate, pousTreePath, dutsTreePath,
                    gvlsTreePath, referencesTreePath, libraryManifestPath);
            }
            finally
            {
                // Ensure Visual Studio always shuts down, even on failure \u2014 otherwise every
                // run (successful or not) leaks a devenv.exe process, which eventually causes
                // COM calls to fail with RPC_E_SERVERCALL_RETRYLATER as instances pile up.
                // A graceful Quit() can hang (or even "return" while the process lingers) if a
                // modal dialog is up, so we attempt it with a timeout and then ALWAYS verify the
                // process actually exited, force-killing it if not.
                Console.WriteLine("{0}: Closing Visual Studio...", Now());
                TryQuit(dte, VS_QUIT_TIMEOUT_MS);
                EnsureExited(devenvPid);
            }
        }

        /// <summary>Runs dte.Quit() on a background thread and waits up to timeoutMs for it
        /// to finish. Returns false if it didn't complete in time (e.g. a modal dialog is
        /// blocking shutdown).</summary>
        static bool TryQuit(EnvDTE80.DTE2 dte, int timeoutMs)
        {
            var quitThread = new Thread(() => { try { dte.Quit(); } catch { /* ignore */ } })
            {
                IsBackground = true
            };
            quitThread.Start();
            return quitThread.Join(timeoutMs);
        }

        /// <summary>Verifies the devenv process actually exited after a graceful Quit; if it's
        /// still alive after a short grace period (Quit can return while the process lingers
        /// behind a modal dialog), force-kills it so no devenv.exe is leaked.</summary>
        static void EnsureExited(int processId)
        {
            if (processId <= 0) return;
            try
            {
                var p = System.Diagnostics.Process.GetProcessById(processId);
                if (!p.WaitForExit(3000))
                {
                    p.Kill();
                    Console.WriteLine("{0}: Force-killed lingering devenv (pid {1}).", Now(), processId);
                }
            }
            catch { /* already gone \u2014 the happy path */ }
        }

        static void RunSync(EnvDTE80.DTE2 dte, string standardPlcProjectTemplate, string plcName,
            string solutionName, string solutionDirectory, string solutionFilePath, string stSourceFolder,
            string twincatTemplate, string pousTreePath, string dutsTreePath, string gvlsTreePath,
            string referencesTreePath, string libraryManifestPath)
        {
            EnvDTE.Project project;
            ITcSysManager sysManager;

            if (File.Exists(solutionFilePath))
            {
                // Incremental path: reopen the existing project instead of recreating it,
                // so the TwinCAT project persists across repeated sync runs.
                Console.WriteLine("{0}: Opening existing solution at '{1}'...", Now(), solutionFilePath);
                dte.Solution.Open(solutionFilePath);
                project = dte.Solution.Projects.Item(1);
                sysManager = (ITcSysManager)project.Object;

                try
                {
                    sysManager.LookupTreeItem(pousTreePath);
                }
                catch (COMException)
                {
                    Console.WriteLine("{0}: PLC project '{1}' missing from existing solution, creating it...", Now(), plcName);
                    ITcSmTreeItem plcConfig = sysManager.LookupTreeItem("TIPC");
                    plcConfig.CreateChild(plcName, 0, "", standardPlcProjectTemplate);
                }
            }
            else
            {
                // First-run bootstrap: create the solution, TwinCAT project, and PLC project.
                Console.WriteLine("{0}: No existing solution found; bootstrapping a new one at '{1}'...", Now(), solutionDirectory);
                Directory.CreateDirectory(solutionDirectory);

                dte.Solution.Create(solutionDirectory, solutionName);
                dte.Solution.SaveAs(solutionFilePath);

                string twincatProjectPath = Path.Combine(solutionDirectory, plcName);
                Console.WriteLine("{0}: Adding TwinCAT project from template...", Now());
                try
                {
                    project = dte.Solution.AddFromTemplate(twincatTemplate, twincatProjectPath, plcName);
                }
                catch (COMException ex) when (ex.Message.Contains("template") || ex.Message.Contains("cannot be found"))
                {
                    Console.Error.WriteLine("ERROR: TwinCAT XAE extension is not registered in Visual Studio 2022.");
                    Console.Error.WriteLine("Run the following command as Administrator to repair:");
                    Console.Error.WriteLine("  MsiExec.exe /f{{23005E9B-9FED-4C05-B4EB-6AC0ECC0BA7F}}");
                    throw;
                }

                sysManager = (ITcSysManager)project.Object;
                Console.WriteLine("{0}: TwinCAT project created successfully.", Now());

                // Add PLC project — fixes "PLC subsystem initialization failed" caused by the
                // empty <Project/> template which has no PLC configuration node.
                Console.WriteLine("{0}: Adding PLC project '{1}'...", Now(), plcName);
                ITcSmTreeItem plcConfig = sysManager.LookupTreeItem("TIPC");
                if (plcConfig == null)
                    throw new InvalidOperationException("TIPC tree item not found. TwinCAT PLC node is missing from the project.");

                ITcSmTreeItem plcProject = plcConfig.CreateChild(plcName, 0, "", standardPlcProjectTemplate);
                if (plcProject == null)
                    throw new InvalidOperationException("CreateChild returned null. PLC project could not be created from template: " + standardPlcProjectTemplate);

                Console.WriteLine("{0}: PLC project '{1}' added.", Now(), plcName);
            }

            project.Save();
            dte.Solution.SaveAs(solutionFilePath);
            Console.WriteLine("{0}: Solution saved.", Now());

            // Sync .st files -> POUs (create/update/delete)
            Console.WriteLine("{0}: Parsing .st sources from '{1}'...", Now(), stSourceFolder);
            var desiredPous = StFileParser.ParseFolder(stSourceFolder);

            Console.WriteLine("{0}: Syncing {1} PLC object(s)...", Now(), desiredPous.Count);
            var syncEngine = new PouSyncEngine(sysManager, pousTreePath, dutsTreePath, gvlsTreePath);
            SyncReport syncReport = syncEngine.Sync(desiredPous);

            foreach (string name in syncReport.Created) Console.WriteLine("    + created  {0}", name);
            foreach (string name in syncReport.Updated) Console.WriteLine("    ~ updated  {0}", name);
            foreach (string name in syncReport.Deleted) Console.WriteLine("    - deleted  {0}", name);

            project.Save();
            Console.WriteLine("{0}: Sync complete ({1} created, {2} updated, {3} deleted).",
                Now(), syncReport.Created.Count, syncReport.Updated.Count, syncReport.Deleted.Count);

            // Sync library references from libraries.xml (config data, not .st source)
            Console.WriteLine("{0}: Parsing library manifest '{1}'...", Now(), libraryManifestPath);
            var desiredLibraries = LibraryManifestParser.Parse(libraryManifestPath);

            Console.WriteLine("{0}: Syncing {1} library reference(s)...", Now(), desiredLibraries.Count);
            ITcSmTreeItem referencesItem = sysManager.LookupTreeItem(referencesTreePath);
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
            string ioManifestPath = Path.Combine(stSourceFolder, "io-devices.xml");
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
                RetryOnBusy(() => linkReport = VariableLinkEngine.Sync(sysManager, plcName, desiredLinks), "linking variables");

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
