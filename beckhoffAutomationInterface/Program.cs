using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using Interop.TCatSysManager;

namespace BeckhoffAutomationInterface
{
    class Program
    {
        const uint RPC_E_SERVERCALL_RETRYLATER = 0x8001010A;
        const int VS_LOAD_TIMEOUT_MS = 30000; // 30 seconds max wait for VS to load
        const int VS_LOAD_RETRY_INTERVAL_MS = 1000;

        static string GetCurrentDateTime()
        {
            return DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
        }

        /// <summary>
        /// Waits for VS to finish loading by retrying a COM call until it succeeds
        /// or the timeout is reached. Handles RPC_E_SERVERCALL_RETRYLATER (0x8001010A)
        /// which VS raises while its message pump is busy initializing.
        /// </summary>
        static void WaitForVsToLoad(EnvDTE.DTE dte)
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
                        GetCurrentDateTime(), elapsed / 1000);
                    Thread.Sleep(VS_LOAD_RETRY_INTERVAL_MS);
                    elapsed += VS_LOAD_RETRY_INTERVAL_MS;
                }
            }
            throw new TimeoutException("Visual Studio did not finish loading within the timeout period.");
        }

        static void Main(string[] args)
        {
            // Configuration
            string standardPlcProjectTemplate = "Standard PLC Template.plcproj";
            string plcName = "MyPLC";
            string projectPath = @"C:\Users\BhanuKiranChaluvadi\Documents\TwinCAT\MyProject";
            int telegram30TypeId = 5;
            int telegram3TypeId = 7;

            string twincatTemplate = @"C:\Program Files (x86)\Beckhoff\TwinCAT\3.1\Components\Base\PrjTemplate\TwinCAT Project.tsproj";

            // Pre-flight checks
            if (!File.Exists(twincatTemplate))
            {
                Console.Error.WriteLine("ERROR: TwinCAT project template not found at:");
                Console.Error.WriteLine("  {0}", twincatTemplate);
                Console.Error.WriteLine("Ensure TwinCAT 3.1 XAE is installed.");
                Environment.Exit(1);
            }

            // Create the Visual Studio DTE instance
            Console.WriteLine("{0}: Getting Visual Studio DTE type...", GetCurrentDateTime());
            Type dteType = Type.GetTypeFromProgID("VisualStudio.DTE.17.0");
            if (dteType == null)
            {
                Console.Error.WriteLine("ERROR: VisualStudio.DTE.17.0 is not registered.");
                Console.Error.WriteLine("Ensure Visual Studio 2022 (17.x) is installed.");
                Environment.Exit(1);
            }
            Console.WriteLine("{0}: Visual Studio DTE type resolved.", GetCurrentDateTime());

            // Create the DTE instance
            Console.WriteLine("{0}: Creating the DTE instance...", GetCurrentDateTime());
            EnvDTE.DTE dte = (EnvDTE.DTE)Activator.CreateInstance(dteType);
            dte.SuppressUI = false;

            // Wait for VS to finish initializing before making further COM calls
            Console.WriteLine("{0}: Waiting for Visual Studio to finish loading...", GetCurrentDateTime());
            WaitForVsToLoad(dte);
            Console.WriteLine("{0}: Visual Studio is ready.", GetCurrentDateTime());

            // Create project directory and solution (clean up any previous run first)
            Console.WriteLine("{0}: Creating project folder...", GetCurrentDateTime());
            DirectoryInfo projectDirectory = new DirectoryInfo(projectPath);
            if (projectDirectory.Exists)
            {
                Console.WriteLine("{0}: Removing existing project folder for clean run...", GetCurrentDateTime());
                projectDirectory.Delete(recursive: true);
            }
            projectDirectory.Create();

            dte.Solution.Create(projectPath, "MySolution1");
            dte.Solution.SaveAs(Path.Combine(projectPath, "MySolution1.sln"));

            // Add TwinCAT project from template
            Console.WriteLine("{0}: Adding TwinCAT project from template...", GetCurrentDateTime());
            string twincatProjectPath = Path.Combine(projectPath, "MyProject");

            EnvDTE.Project project;
            try
            {
                project = dte.Solution.AddFromTemplate(twincatTemplate, twincatProjectPath, "MyProject");
            }
            catch (COMException ex) when (ex.Message.Contains("template") || ex.Message.Contains("cannot be found"))
            {
                Console.Error.WriteLine("ERROR: TwinCAT XAE extension is not registered in Visual Studio 2022.");
                Console.Error.WriteLine("Run the following command as Administrator to repair:");
                Console.Error.WriteLine("  MsiExec.exe /f{{23005E9B-9FED-4C05-B4EB-6AC0ECC0BA7F}}");
                throw;
            }

            ITcSysManager sysManager = (ITcSysManager)project.Object;
            Console.WriteLine("{0}: TwinCAT project created successfully.", GetCurrentDateTime());

            // Add PLC project — fixes "PLC subsystem initialization failed" caused by the
            // empty <Project/> template which has no PLC configuration node.
            Console.WriteLine("{0}: Adding PLC project '{1}'...", GetCurrentDateTime(), plcName);
            ITcSmTreeItem plcConfig = sysManager.LookupTreeItem("TIPC");
            if (plcConfig == null)
                throw new InvalidOperationException("TIPC tree item not found. TwinCAT PLC node is missing from the project.");

            ITcSmTreeItem plcProject = plcConfig.CreateChild(plcName, 0, "", standardPlcProjectTemplate);
            if (plcProject == null)
                throw new InvalidOperationException("CreateChild returned null. PLC project could not be created from template: " + standardPlcProjectTemplate);

            Console.WriteLine("{0}: PLC project '{1}' added.", GetCurrentDateTime(), plcName);

            // Save TwinCAT project and solution via DTE (SaveConfiguration not supported outside Legacy Mode)
            project.Save();
            dte.Solution.SaveAs(Path.Combine(projectPath, "MySolution1.sln"));
            Console.WriteLine("{0}: Solution saved successfully.", GetCurrentDateTime());
        }
    }
}
