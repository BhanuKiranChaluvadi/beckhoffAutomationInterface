using System;
using System.IO;
using System.Runtime.InteropServices;
using Interop.TCatSysManager;

namespace BeckhoffAutomationInterface
{
    /// <summary>
    /// Gets an open (EnvDTE.Project, ITcSysManager) handle for the project described by a
    /// RunOptions: reopens the solution if it already exists (so the TwinCAT project persists
    /// across repeated sync runs — the whole point of this idempotent-by-default tool), or
    /// bootstraps a brand new solution/TwinCAT project/PLC project from scratch on first run.
    /// Extracted from Program.RunSync so "how do we get a project handle" is separate from
    /// "what do we sync once we have one".
    /// </summary>
    static class TwinCatProjectOpener
    {
        public static (EnvDTE.Project Project, ITcSysManager SysManager) Open(EnvDTE80.DTE2 dte, RunOptions options)
        {
            if (File.Exists(options.SolutionFilePath))
                return ReopenExisting(dte, options);
            return BootstrapNew(dte, options);
        }

        static (EnvDTE.Project, ITcSysManager) ReopenExisting(EnvDTE80.DTE2 dte, RunOptions options)
        {
            Console.WriteLine("{0}: Opening existing solution at '{1}'...", Now(), options.SolutionFilePath);
            dte.Solution.Open(options.SolutionFilePath);
            EnvDTE.Project project = dte.Solution.Projects.Item(1);
            var sysManager = (ITcSysManager)project.Object;

            try
            {
                sysManager.LookupTreeItem(options.PousTreePath);
            }
            catch (COMException)
            {
                Console.WriteLine("{0}: PLC project '{1}' missing from existing solution, creating it...", Now(), options.ProjectName);
                ITcSmTreeItem plcConfig = sysManager.LookupTreeItem("TIPC");
                plcConfig.CreateChild(options.ProjectName, 0, "", options.StandardPlcProjectTemplate);
            }

            return (project, sysManager);
        }

        static (EnvDTE.Project, ITcSysManager) BootstrapNew(EnvDTE80.DTE2 dte, RunOptions options)
        {
            Console.WriteLine("{0}: No existing solution found; bootstrapping a new one at '{1}'...", Now(), options.SolutionDirectory);
            Directory.CreateDirectory(options.SolutionDirectory);

            dte.Solution.Create(options.SolutionDirectory, options.ProjectName);
            dte.Solution.SaveAs(options.SolutionFilePath);

            string twincatProjectPath = Path.Combine(options.SolutionDirectory, options.ProjectName);
            Console.WriteLine("{0}: Adding TwinCAT project from template...", Now());
            EnvDTE.Project project;
            try
            {
                project = dte.Solution.AddFromTemplate(options.TwinCatTemplate, twincatProjectPath, options.ProjectName);
            }
            catch (COMException ex) when (ex.Message.Contains("template") || ex.Message.Contains("cannot be found"))
            {
                Console.Error.WriteLine("ERROR: TwinCAT XAE extension is not registered in Visual Studio 2022.");
                Console.Error.WriteLine("Run the following command as Administrator to repair:");
                Console.Error.WriteLine("  MsiExec.exe /f{{23005E9B-9FED-4C05-B4EB-6AC0ECC0BA7F}}");
                throw;
            }

            var sysManager = (ITcSysManager)project.Object;
            Console.WriteLine("{0}: TwinCAT project created successfully.", Now());

            // Add PLC project — fixes "PLC subsystem initialization failed" caused by the
            // empty <Project/> template which has no PLC configuration node.
            Console.WriteLine("{0}: Adding PLC project '{1}'...", Now(), options.ProjectName);
            ITcSmTreeItem plcConfig = sysManager.LookupTreeItem("TIPC");
            if (plcConfig == null)
                throw new InvalidOperationException("TIPC tree item not found. TwinCAT PLC node is missing from the project.");

            ITcSmTreeItem plcProject = plcConfig.CreateChild(options.ProjectName, 0, "", options.StandardPlcProjectTemplate);
            if (plcProject == null)
                throw new InvalidOperationException("CreateChild returned null. PLC project could not be created from template: " + options.StandardPlcProjectTemplate);

            Console.WriteLine("{0}: PLC project '{1}' added.", Now(), options.ProjectName);
            return (project, sysManager);
        }

        static string Now() => DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
    }
}
