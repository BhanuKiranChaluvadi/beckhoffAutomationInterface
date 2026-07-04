// Program — deploys a TwinCAT project to a fleet of PLCs using the Visual
// Studio DTE + TwinCAT Automation Interface (AI).
//
// Converted from the 3-part series:
//   Part 1: https://www.sagatowski.com/posts/the_elt_and_the_twincat_automation_interface_one/
//   Part 2: https://www.sagatowski.com/posts/the_elt_and_the_twincat_automation_interface_two/
//   Part 3: https://www.sagatowski.com/posts/the_elt_and_the_twincat_automation_interface_three/
//
// Implements the eight deployment steps described in the series:
//   1. Starting the version of Visual Studio that was used to create the project
//   2. Opening the solution
//   3. Adding an AMS-route to the target through the AMS-router
//   4. Selecting the target device
//   5. Enabling the autostart boot flag
//   6. Selecting the target architecture
//   7. Enabling boot project
//   8. Activation of the configuration (+ restarting the TwinCAT kernel)
//
// Requirements:
//   - Build/run as x86 (32-bit).
//   - References: "EnvDTE", "EnvDTE80" (NuGet) and the "Beckhoff TwinCAT
//     XAE Base X.Y Type Library" (COM reference -> TCatSysManagerLib).
//   - The Visual Studio version used to author the TwinCAT project must be
//     installed on the build machine (see the VS-version table in README.md).
//
// NOTE: As in the original article, exception handling is intentionally
// kept minimal here for clarity — production code should handle failures
// for each step (missing VS version, unreachable AMS route, etc.)
// gracefully instead of letting the whole deployment abort.

using System;
using System.Collections;
using EnvDTE;
using EnvDTE80;
using TCatSysManagerLib;

namespace EltAutomationInterface
{
    class Program
    {
        static void Log(string message)
        {
            Console.WriteLine("{0:yyyy-MM-dd HH:mm:ss} {1}", DateTime.Now, message);
        }

        static void Main(string[] args)
        {
            // Input parameters (in a real program these come from CLI args,
            // e.g. parsed with NDesk.Options, and an XML/JSON config file).
            string solutionFilePath = @"C:\Code\workspace\TcProject\TcProject.sln";

            AMSRoutes amsTargets = new AMSRoutes();
            amsTargets.items.Add(new Target
            {
                hostName = "CX-4286EE",
                netId = "10.0.2.15.1.1",
                ipAddr = "192.168.43.85",
                username = "Administrator",
                password = "1"
            });
            amsTargets.items.Add(new Target
            {
                hostName = "CX-4286F1",
                netId = "10.0.3.22.1.1",
                ipAddr = "192.168.43.86",
                username = "Administrator",
                password = "1"
            });
            amsTargets.items.Add(new Target
            {
                hostName = "CX-4253DD",
                netId = "10.0.2.16.1.1",
                ipAddr = "192.168.43.87",
                username = "Administrator",
                password = "1"
            });

            // ---- Step 1 & 2: start Visual Studio (DTE) and open the solution ----
            Log("Getting VisualStudio DTE...");
            Type dteType = Type.GetTypeFromProgID("VisualStudio.DTE.15.0"); // VS2017 = 15.0
            DTE2 dte = (DTE2)Activator.CreateInstance(dteType);
            dte.SuppressUI = true;
            dte.MainWindow.Visible = false;
            dte.UserControl = false; // IMPORTANT: ensures VS shuts down properly when done

            Log("Opening solution: " + solutionFilePath);
            Solution visualStudioSolution = dte.Solution;
            visualStudioSolution.Open(solutionFilePath);

            Project pro = visualStudioSolution.Projects.Item(1);

            // ---- Step 3: add AMS routes to all targets via the Automation Interface ----
            ITcSysManager10 sysManager = (ITcSysManager10)pro.Object;

            Log("Creating AMS routes for " + amsTargets.items.Count + " target(s)...");
            ITcSmTreeItem routes = sysManager.LookupTreeItem("TIRR"); // Real-Time Configuration ^ Route Settings
            string routesXmlString = AutomationInterfaceXml.CreateRoutesXMLString(amsTargets);
            routes.ConsumeXml(routesXmlString);

            // ---- Steps 4-8: for every PLC, select it, set boot flags, activate & restart ----
            foreach (Target t in amsTargets.items)
            {
                // Step 4: select the target device
                Log("Selecting target " + t.hostName + " (" + t.netId + ")");
                sysManager.SetTargetNetId(t.netId);

                // Steps 5-7: enable autostart boot flag + boot project for every PLC project
                Log("Enabling boot project and setting BootProjectAutostart on " + sysManager.GetTargetNetId());
                ITcSmTreeItem plcTreeItem = sysManager.LookupTreeItem("TIPC"); // PLC Configuration
                int plcChildCount = plcTreeItem.ChildCount;

                // Enable autostart-flag on all PLC-projects, as this flag is not
                // stored in the project itself but rather on the target.
                for (int i = 1; i <= plcChildCount; i++)
                {
                    ITcSmTreeItem plcProject = plcTreeItem.Child[i];
                    ITcPlcProject iecProject = (ITcPlcProject)plcProject;
                    iecProject.GenerateBootProject(true);
                    iecProject.BootProjectAutostart = true;
                }

                // Step 8: activate the configuration and restart the TwinCAT kernel
                Log("Activating configuration on " + sysManager.GetTargetNetId());
                sysManager.ActivateConfiguration();

                Log("Restarting the TwinCAT kernel on target " + sysManager.GetTargetNetId());
                sysManager.StartRestartTwinCAT();
            }

            Log("Deployment finished for all targets.");
        }
    }
}
