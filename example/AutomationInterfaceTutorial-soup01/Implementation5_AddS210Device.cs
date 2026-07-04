// Implementation 5 — Add an S210 Profinet Device (with PROFIsafe/Standard Telegrams)
//
// Converted from: http://soup01.com/en/2023/03/24/beckhoffusing-automation-interface-2/
//
// Builds on Implementation 4: after creating the Profinet IO Controller,
// this adds a Siemens Sinamics S210 drive as a Profinet device beneath it
// (from its GSDML file), renames the box, sets its IP/gateway, and inserts
// a PROFIsafe telegram (Telegram 30) and a standard telegram (Telegram 3)
// on the drive's terminal.
//
// Requirements: same as Implementation1_CreateTwinCATProject.cs, plus the
// Siemens Sinamics S210 GSDML file installed under
// C:\TwinCAT\3.1\Config\Io\Profinet\ (adjust the file name/version to match
// what is actually installed on your system).

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

            profinetController.ConsumeXml(
                "<TreeItem><DevicePnControllerDef><IpSettings><IP>#x0103a8c0</IP></IpSettings></DevicePnControllerDef></TreeItem>");

            profinetController.ConsumeXml(
                "<TreeItem><DevicePnControllerDef><IpSettings><Subnet>#x00ffffff</Subnet></IpSettings></DevicePnControllerDef></TreeItem>");

            // Create S210 — add the Siemens Sinamics S210 as a Profinet device from its GSDML.
            // 97 = device type id for a generic Profinet box/device.
            // The path after "#" selects the specific device revision (V5.1) from the GSDML.
            Console.WriteLine("{0}: Creating the Profinet IO Device (S210)..", GetCurrentDateTime());
            ITcSmTreeItem s210 = profinetController.CreateChild(
                "PNDevices_1_S210", 97, null,
                @"C:\TwinCAT\3.1\Config\Io\Profinet\GSDML-V2.25-Siemens-Sinamics_S210-20220506.xml#0x0002020C");

            // Change the Box Name
            Console.WriteLine("{0}: Changing the Box Name..", GetCurrentDateTime());
            s210.ConsumeXml("<TreeItem><ItemName>box1234</ItemName></TreeItem>");

            // Change the IP / Gateway
            // #x0a03a8c0 decodes (byte-reversed) to 192.168.3.10
            // #x0103a8c0 decodes (byte-reversed) to 192.168.3.1
            Console.WriteLine("{0}: Changing the IP..", GetCurrentDateTime());
            s210.ConsumeXml(
                "<TreeItem><PnIoBoxDef><IpSettings><IP>#x0a03a8c0</IP></IpSettings></PnIoBoxDef></TreeItem>");
            s210.ConsumeXml(
                "<TreeItem><PnIoBoxDef><IpSettings><Gateway>#x0103a8c0</Gateway></IpSettings></PnIoBoxDef></TreeItem>");

            // Navigate down to the drive terminal:
            //   Child(1) -> the device's API object
            //   Child(2) -> Term 2 (Drive)
            ITcSmTreeItem s210Api = s210.get_Child(1);
            ITcSmTreeItem drive = s210Api.get_Child(2);

            Console.WriteLine("{0}: Inserting the Telegrams..", GetCurrentDateTime());
            ITcSmTreeItem profiSafeTelegram = drive.CreateChild("ProfiSAFE_Telegram30", s210Telegram30, null, null);
            ITcSmTreeItem standardTelegram = drive.CreateChild("StandardTelegram3", s210Telegram3, null, null);

            Console.WriteLine("{0}: Done!", GetCurrentDateTime());
        }
    }
}
