// Implementation 1 — Create the TwinCAT Project
//
// Converted from: http://soup01.com/en/2023/03/24/beckhoffusing-automation-interface-2/
//
// Uses the Visual Studio DTE automation object model plus the TwinCAT
// TCatSysManagerLib COM type library to launch Visual Studio and create a
// new TwinCAT project from the standard "TwinCAT Project.tsproj" template.
//
// Requirements:
//   - Build/run as x86 (32-bit) — the Automation Interface requires this.
//   - Reference "EnvDTE" (NuGet) and the "Beckhoff TwinCAT XAE Base 3.x
//     Type Library" (COM reference -> TCatSysManagerLib).
//   - Adjust the VisualStudio.DTE.<version> ProgID and paths below to match
//     your machine (check HKEY_CLASSES_ROOT for the installed DTE ProgID).

using System;
using System.IO;
using TCatSysManagerLib;

namespace ConsoleApp2
{
    class Program
    {
        static string GetCurrentDateTime()
        {
            DateTime dt = DateTime.Now;
            return dt.ToString("yyyy/MM/dd HH:mm:ss");
        }

        static void Main(string[] args)
        {
            // Init
            string myStandardPlcProjectTemplate = "Standard PLC Template.plcproj";
            string plcName = "MyPLC";
            string path = @"C:\Users\chungw\Downloads\Myp";
            int s210Telegram30 = 5;
            int s210Telegram3 = 7;

            // Creating the Visual Studio DTE
            Console.WriteLine("{0}: Getting VisualStudio DTE ID...", GetCurrentDateTime());
            Type t = Type.GetTypeFromProgID("VisualStudio.DTE.16.0");
            Console.WriteLine("{0}: VisualStudio DTE ID is read!", GetCurrentDateTime());

            // Create the Instance
            Console.WriteLine("{0}: Creating the Instance..", GetCurrentDateTime());
            EnvDTE.DTE dte = (EnvDTE.DTE)Activator.CreateInstance(t);
            dte.SuppressUI = false;
            dte.MainWindow.Visible = true;

            Console.WriteLine("{0}: Start to Create Folder..", GetCurrentDateTime());
            DirectoryInfo di = new DirectoryInfo(path);
            di.Create();

            dte.Solution.Create(path, "MySolution1");
            dte.Solution.SaveAs(@"C:\Users\chungw\Downloads\Myp\Solution1.sln");

            // Add Solution — create the TwinCAT project from the standard template
            string template = @"C:\TwinCAT\3.1\Components\Base\PrjTemplate\TwinCAT Project.tsproj";
            ITcSysManager sysManager = (ITcSysManager)dte.Solution
                .AddFromTemplate(template, @"C:\Users\chungw\Downloads\Myp\Solution1", "MyProject")
                .Object;

            Console.WriteLine("{0}: TwinCAT project created.", GetCurrentDateTime());
        }
    }
}
