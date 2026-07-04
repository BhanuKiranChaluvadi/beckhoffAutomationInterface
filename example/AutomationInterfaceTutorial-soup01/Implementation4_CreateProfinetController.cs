// Implementation 4 — Create a Profinet IO Controller
//
// Converted from: http://soup01.com/en/2023/03/24/beckhoffusing-automation-interface-2/
//
// Builds on Implementation 3: after setting up the PLC project and its
// library references, this adds a Profinet IO Controller (RT) under the
// I/O ("TIID") tree node and configures its IP address / subnet mask via
// ConsumeXml.
//
// Requirements: same as Implementation1_CreateTwinCATProject.cs.

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

            // PLC — create a Standard PLC Project under the PLC ("TIPC") tree node
            Console.WriteLine("{0}: TwinCAT is started.", GetCurrentDateTime());
            ITcSmTreeItem plc = sysManager.LookupTreeItem("TIPC");

            Console.WriteLine("{0}: Creating PLC...", GetCurrentDateTime());
            ITcSmTreeItem newPlc = plc.CreateChild(plcName, 0, "", myStandardPlcProjectTemplate);

            // Get the PLC Project object via its tree path ("^" separates path segments)
            ITcSmTreeItem plcProject = sysManager.LookupTreeItem("TIPC^MyPLC^MyPLC Project");
            ITcPlcIECProject importExport = (ITcPlcIECProject)plcProject;

            // Import the GVL from a PLCopenXML file
            importExport.PlcOpenImport(
                @"C:\Users\chungw\Downloads\Myp\GVL_1.xml",
                (int)PLCIMPORTOPTIONS.PLCIMPORTOPTIONS_NONE);

            // Reference Library — add Tc2_MC2 and Tc2_MC2_Drive to the PLC project's References
            ITcSmTreeItem references = sysManager.LookupTreeItem("TIPC^MyPLC^MyPLC Project^References");
            ITcPlcLibraryManager libManager = (ITcPlcLibraryManager)references;

            libManager.AddLibrary("Tc2_MC2", "*", "Beckhoff Automation GmbH");
            libManager.AddLibrary("Tc2_MC2_Drive", "*", "Beckhoff Automation GmbH");

            // Create the Profinet IO Controller under the I/O ("TIID") tree node.
            // 113 = device type id for "Profinet Controller (RT)".
            Console.WriteLine("{0}: Creating the Profinet IO Controller..", GetCurrentDateTime());
            ITcSmTreeItem io = sysManager.LookupTreeItem("TIID");
            ITcSmTreeItem profinetController = io.CreateChild("PNIO Controller", 113, null, null);

            // Configure IP / Subnet via ConsumeXml.
            // #x0103a8c0 decodes (byte-reversed) to 192.168.3.1
            // #x00ffffff decodes (byte-reversed) to 255.255.255.0
            profinetController.ConsumeXml(
                "<TreeItem><DevicePnControllerDef><IpSettings><IP>#x0103a8c0</IP></IpSettings></DevicePnControllerDef></TreeItem>");

            profinetController.ConsumeXml(
                "<TreeItem><DevicePnControllerDef><IpSettings><Subnet>#x00ffffff</Subnet></IpSettings></DevicePnControllerDef></TreeItem>");

            Console.WriteLine("{0}: PNIO Controller created and configured.", GetCurrentDateTime());
        }
    }
}
