using System;
using System.IO;
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
            EnvDTE80.DTE2 dte = (EnvDTE80.DTE2)Activator.CreateInstance(dteType);
            dte.SuppressUI = false;

            Console.WriteLine("{0}: Waiting for Visual Studio to finish loading...", Now());
            WaitForVsToLoad(dte);
            Console.WriteLine("{0}: Visual Studio is ready.", Now());

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
            var syncEngine = new PouSyncEngine(sysManager, pousTreePath, dutsTreePath);
            SyncReport syncReport = syncEngine.Sync(desiredPous);

            foreach (string name in syncReport.Created) Console.WriteLine("    + created  {0}", name);
            foreach (string name in syncReport.Updated) Console.WriteLine("    ~ updated  {0}", name);
            foreach (string name in syncReport.Deleted) Console.WriteLine("    - deleted  {0}", name);

            project.Save();
            Console.WriteLine("{0}: Sync complete ({1} created, {2} updated, {3} deleted).",
                Now(), syncReport.Created.Count, syncReport.Updated.Count, syncReport.Deleted.Count);

            // Build and report
            Console.WriteLine("{0}: Building solution...", Now());
            BuildReport buildReport = BuildRunner.Build(dte);

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
