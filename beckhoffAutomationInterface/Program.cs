using System;
using System.IO;
using Interop.TCatSysManager;

namespace BeckhoffAutomationInterface
{
    class Program
    {
        static string GetCurrentDateTime()
        {
            return DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
        }

        static void Main(string[] args)
        {
            // Configuration
            string standardPlcProjectTemplate = "Standard PLC Template.plcproj";
            string plcName = "MyPLC";
            string projectPath = @"C:\Users\BhanuKiranChaluvadi\Documents\TwinCAT\MyProject";
            int telegram30TypeId = 5;
            int telegram3TypeId = 7;

            // Create the Visual Studio DTE instance
            Console.WriteLine("{0}: Getting Visual Studio DTE type...", GetCurrentDateTime());
            Type dteType = Type.GetTypeFromProgID("VisualStudio.DTE.17.0");
            Console.WriteLine("{0}: Visual Studio DTE type resolved.", GetCurrentDateTime());

            // Create the DTE instance
            Console.WriteLine("{0}: Creating the DTE instance...", GetCurrentDateTime());
            EnvDTE.DTE dte = (EnvDTE.DTE)Activator.CreateInstance(dteType);

            dte.SuppressUI = false;
            dte.MainWindow.Visible = true;

            // Create project directory and solution
            Console.WriteLine("{0}: Creating project folder...", GetCurrentDateTime());
            DirectoryInfo projectDirectory = new DirectoryInfo(projectPath);
            projectDirectory.Create();

            dte.Solution.Create(projectPath, "MySolution1");
            dte.Solution.SaveAs(Path.Combine(projectPath, "MySolution1.sln"));

            // Add TwinCAT project from template
            string twincatTemplate = @"C:\Program Files (x86)\Beckhoff\TwinCAT\3.1\Components\Base\PrjTemplate\TwinCAT Project.tsproj";
            string twincatProjectPath = Path.Combine(projectPath, "MyProject");
            ITcSysManager sysManager = (ITcSysManager)dte.Solution
                .AddFromTemplate(twincatTemplate, twincatProjectPath, "MyProject")
                .Object;
        }
    }
}
