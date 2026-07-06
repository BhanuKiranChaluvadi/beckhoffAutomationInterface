using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

using TCatSysManagerLib;
using System.IO;
using System.Reflection;
using System.Xml;

namespace EtherCATLinkingTC2
{
    class Program
    {
        private static string _tsmPath = Path.GetDirectoryName(Assembly.GetEntryAssembly().Location) + "\\Templates\\Sample.tsm";
        private static string _tpyPath = Path.GetDirectoryName(Assembly.GetEntryAssembly().Location) + "\\Templates\\Sample.tpy";
        private static TcSysManager _sysManager;

        static void Main(string[] args)
        {
            _sysManager = new TcSysManager();
            _sysManager.NewConfiguration();

            /* =============================================================
             * Proceed with getting needed node infos for later reference
             * ============================================================= */
            ITcSmTreeItem ncConfig = _sysManager.LookupTreeItem("TINC");                    // Getting NC Configuration
            ITcSmTreeItem ncTask1 = ncConfig.CreateChild("NC-Task 1", 1);                   // Creating NC TAsk
            ITcSmTreeItem ncAxes = _sysManager.LookupTreeItem("TINC^NC-Task 1 SAF^Axes");   // Getting Axes Folder
            ITcSmTreeItem ncAxis1 = ncAxes.CreateChild("Axis 1", 1);                        // Create Axis
            ITcSmTreeItem plcConfig = _sysManager.LookupTreeItem("TIPC");                   // Getting PLC-Configuration
            ITcSmTreeItem devices = _sysManager.LookupTreeItem("TIID");                     // Getting IO-Configuration


            /* =============================================================
             * Scan the Fieldbus interfaces and add an EtherCAT Device
             * ============================================================= */
            ITcSmTreeItem device = CreateEthernetDevice(_sysManager, DeviceType.EtherCAT_DirectMode, "EtherCAT Master");

            
            /* =============================================================
             * Create EK1100 A2P
             * ============================================================= */
            ITcSmTreeItem a2p = device.CreateChild("A2P (EK1100)", (int)TCSYSMANAGERBOXTYPES.TSM_BOX_TYPE_EXXXXX, "", "EK1100-0000-0001");


            /* =============================================================
             * Create Terminals for EK1100 A2P
             * ============================================================= */
            device.CreateChild("100 (EL1014)", (int)BoxType.EtherCAT_EXXXXX, "", "EL1014-0000-0000");
            device.CreateChild("101 (EL9400)", (int)BoxType.EtherCAT_EXXXXX, "", "EL9400");
            device.CreateChild("102 (EL2004)", (int)BoxType.EtherCAT_EXXXXX, "", "EL2004-0000-0000");
            device.CreateChild("103 (EL9100)", (int)BoxType.EtherCAT_EXXXXX, "", "EL9100");
            device.CreateChild("104 (EL2004)", (int)BoxType.EtherCAT_EXXXXX, "", "EL2004-0000-0000");
            device.CreateChild("105 (EL2004)", (int)BoxType.EtherCAT_EXXXXX, "", "EL2004-0000-0000");
            device.CreateChild("106 (EL2004)", (int)BoxType.EtherCAT_EXXXXX, "", "EL2004-0000-0000");
            device.CreateChild("107 (EL2004)", (int)BoxType.EtherCAT_EXXXXX, "", "EL2004-0000-0000");
            device.CreateChild("108 (EL2004)", (int)BoxType.EtherCAT_EXXXXX, "", "EL2004-0000-0000");
            device.CreateChild("109 (EL1004)", (int)BoxType.EtherCAT_EXXXXX, "", "EL1004-0000-0000");
            device.CreateChild("110 (EL1014)", (int)BoxType.EtherCAT_EXXXXX, "", "EL1014-0000-0000");
            device.CreateChild("111 (EK1110)", (int)BoxType.EtherCAT_EXXXXX, "", "EK1110-0000-0000");


            /* =============================================================
             * Create EK1100 A3P
             * ============================================================= */
            ITcSmTreeItem a3p = device.CreateChild("A3P (EK1100)", (int)BoxType.EtherCAT_EXXXXX, "", "EK1100-0000-0000");


            /* =============================================================
             * Create Terminals for EK1100 A3P
             * ============================================================= */
            a3p.CreateChild("204 (EL6751)", (int)BoxType.EtherCAT_EXXXXX, "", "EL6751-0000-0000");
            a3p.CreateChild("205 (EL6731)", (int)BoxType.EtherCAT_EXXXXX, "", "EL6731-0000-0000");
            a3p.CreateChild("206 (EL9010)", (int)BoxType.EtherCAT_EXXXXX, "", "EL9011");


            /* =============================================================
             * Create BK1120 A4P
             * ============================================================= */
            ITcSmTreeItem a4p = device.CreateChild("A4P (BK1120)", (int)BoxType.EtherCAT_EXXXXX, "", "BK1120-0000-9995");

            
            /* =============================================================
             * Create Terminals for BK1120 A4P
             * ============================================================= */
            a4p.CreateChild("Term3 (KL2114)", 2114, "End Term (KL9010)");
            a4p.CreateChild("Term4 (KL1104)", 1104, "End Term (KL9010)");
            a4p.CreateChild("Term5 (KL1104)", 1104, "End Term (KL9010)");
            a4p.CreateChild("Term6 (KL1408)", 1408, "End Term (KL9010)");
            a4p.CreateChild("Term7 (KL1408)", 1408, "End Term (KL9010)");


            /* =============================================================
             * Create CANopen Master Device
             * ============================================================= */
            ITcSmTreeItem canOpenMaster = devices.CreateChild("CANopen Master (EL6751)", 87);


            /* =============================================================
             * Search button functionality of Profibus Master device
             * Set the EtherCATDeviceName to the path of the EL6751-Terminal (known from above, separated by //^//)
             * I/O Configuration      = "TIID"
             *  + EtherCAT-Master name = "EtherCAT Master"
             *  + EK1100 coupler name  = "A2P (EK1100)"
             *  + EL6751 name          = "204 (EL6751)"
             *  EL6751 path = "TIID^EtherCAT Master^A3P (EK1100)^204 (EL6751)"
             * ============================================================= */
            canOpenMaster.ConsumeXml("<TreeItem><DeviceDef><AddressInfo><Ecat><EtherCATDeviceName>TIID^EtherCAT Master^A3P (EK1100)^204 (EL6751)</EtherCATDeviceName></Ecat></AddressInfo></DeviceDef></TreeItem>");


            /* =============================================================
             * Create CANopen Slave Devices
             * Create BK5150
             * ============================================================= */
            ITcSmTreeItem box11 = canOpenMaster.CreateChild("Box11 (BK5150)", 5008);
            box11.ConsumeXml("<TreeItem><BoxDef><FieldbusAddress>11</FieldbusAddress></BoxDef></TreeItem>"); // Set node address


            /* =============================================================
             * Create Terminals for BK5150
             * ============================================================= */
            box11.CreateChild("Term2 (KL1002)", 1002, "End Term (KL9010)");
            box11.CreateChild("Term3 (KL2114)", 2114, "End Term (KL9010)");


            /* =============================================================
             * Create Profibus Master Device
             * ============================================================= */
            ITcSmTreeItem profibusMaster = devices.CreateChild("Profibus Master (EL6731)", 86);


            /* =============================================================
             * Assign Profibus Master device to EL6731 terminal
             * Set the EtherCATDeviceName to the path of the EL6731-Terminal (known from above, separated by //^//)
             * I/O Configuration      = "TIID"
             *  + EtherCAT-Master name = "EtherCAT Master"
             *  + EK1100 coupler name  = "A2P (EK1100)"
             *  + EL6731 name          = "205 (EL6731)"
             *  EL6731 path = "TIID^EtherCAT Master^A3P (EK1100)^205 (EL6731)"
             * ============================================================= */
            profibusMaster.ConsumeXml("<TreeItem><DeviceDef><AddressInfo><Ecat><EtherCATDeviceName>TIID^EtherCAT Master^A3P (EK1100)^205 (EL6731)</EtherCATDeviceName></Ecat></AddressInfo></DeviceDef></TreeItem>");


            /* =============================================================
             * Create Profibus Slave Devices
             * Create Drive
             * ============================================================= */
            profibusMaster.CreateChild("Screw A (AXIS_Screw_A_FC310x)", 1008);


            /* =============================================================
             * Create BK3120
             * ============================================================= */
            ITcSmTreeItem box22 = profibusMaster.CreateChild("Box22 (BK3120)", 1010);
            box22.ConsumeXml("<TreeItem><BoxDef><FieldbusAddress>22</FieldbusAddress></BoxDef></TreeItem>"); // Set DP-Address


            /* =============================================================
             * Create Terminals for BK3120
             * ============================================================= */
            box22.CreateChild("Term5 (KL2114)", 2114, "End Term (KL9010)");


            /* =============================================================
             * Set DP-Address and Status 'Disabled'
             * ============================================================= */
            ITcSmTreeItem screwA = _sysManager.LookupTreeItem("TIID^Profibus Master (EL6731)^Screw A (AXIS_Screw_A_FC310x)");
            screwA.ConsumeXml("<TreeItem><BoxDef><FieldbusAddress>2</FieldbusAddress></BoxDef></TreeItem>");
            screwA.ConsumeXml("<TreeItem><Disabled>1</Disabled></TreeItem>");


            /* =============================================================
             * Create Virtual USB Master Device
             * Create Profibus Master
             * ============================================================= */
            ITcSmTreeItem usbDevice = devices.CreateChild("Virtual USB Interface (USB)", 57);
            usbDevice.ConsumeXml("<TreeItem><USB><VirtualDeviceNames>1</VirtualDeviceNames></USB></TreeItem>"); // Set virtual device name


            /* =============================================================
             * Create USB-Box
             * ============================================================= */
            usbDevice.CreateChild("Box 0 (CPX8XX)", 9591);


            /* =============================================================
             * PLC Configuration
             * Search for PLC project
             * ============================================================= */
            ITcSmTreeItem plc = plcConfig.CreateChild(_tpyPath, 0, "", null);
            ITcSmTreeItem plcProject = _sysManager.LookupTreeItem("TIPC^Sample");


            /* =============================================================
             * Create links
             * Look for NC Axis, then link NC Axis to Drive
             * ============================================================= */
            ITcSmTreeItem axis1 = _sysManager.LookupTreeItem("TINC^NC-Task 1 SAF^Axes^Axis 1");
            axis1.ConsumeXml("<TreeItem><NcAxisDef><AxisType>1</AxisType><AxisIoType>ProfibusMC</AxisIoType><IoItem><PathName>TIID^Profibus Master (EL6731)^Screw A (AXIS_Screw_A_FC310x)</PathName></IoItem></NcAxisDef></TreeItem>");


            /* =============================================================
             * Link Terminal channel to PLC variable
             * ============================================================= */
            _sysManager.LinkVariables("TIPC^Sample^Standard^Inputs^MAIN.bIn", "TIID^EtherCAT Master^A2P (EK1100)^100 (EL1014)^Channel 1^Input");
            _sysManager.LinkVariables("TIPC^Sample^Standard^Outputs^MAIN.bOut", "TIID^EtherCAT Master^A2P (EK1100)^102 (EL2004)^Channel 1^Output");
            _sysManager.LinkVariables("TIPC^Sample^Standard^Outputs^MAIN.bOut", "TIID^EtherCAT Master^A4P (BK1120)^Term3 (KL2114)^Channel 1^Output");
            _sysManager.LinkVariables("TIPC^Sample^Standard^Outputs^MAIN.bOut", "TIID^CANopen Master (EL6751)^Box11 (BK5150)^Term3 (KL2114)^Channel 1^Output");
            _sysManager.LinkVariables("TIPC^Sample^Standard^Outputs^MAIN.bOut", "TIID^Profibus Master (EL6731)^Box22 (BK3120)^Term5 (KL2114)^Channel 1^Output");


            /* =============================================================
             * Link Control Panel LED to PLC Variable
             * ============================================================= */
            _sysManager.LinkVariables("TIPC^Sample^Standard^Outputs^MAIN.bOut", "TIID^Virtual USB Interface (USB)^Box 0 (CPX8XX)^Outputs^LED 1");

            _sysManager.SaveConfiguration(_tsmPath);
        }


        /* =============================================================
         * The following two methods are helper methods used by this class
         * ============================================================= */

        private static XmlNodeList ScanDevices(ITcSysManager systemManager)
        {
            ITcSmTreeItem3 devices = (ITcSmTreeItem3)systemManager.LookupTreeItem("TIID");
            XmlDocument doc = new XmlDocument();
            string xml = devices.ProduceXml(false);

            doc.LoadXml(xml);
            return doc.SelectNodes("TreeItem/DeviceGrpDef/FoundDevices/Device");
        }


        private static ITcSmTreeItem3 CreateEthernetDevice(ITcSysManager systemManager, DeviceType type, string deviceName)
        {
            // Scans and Creates the appropriate device
            ITcSmTreeItem3 device = null;
            XmlNodeList nodes = ScanDevices(systemManager);
            List<XmlNode> ethernetNodes = new List<XmlNode>();

            foreach (XmlNode node in nodes)
            {
                XmlNode typeNode = node.SelectSingleNode("ItemSubType");

                int subType = int.Parse(typeNode.InnerText);

                if (subType == (int)DeviceType.EtherCAT_AutomationProtocol || subType == (int)DeviceType.Ethernet_RTEthernet || subType == (int)DeviceType.EtherCAT_DirectMode || subType == (int)DeviceType.EtherCAT_DirectModeV210)
                {
                    ethernetNodes.Add(node);
                }
            }

            ITcSmTreeItem3 devices = (ITcSmTreeItem3)systemManager.LookupTreeItem("TIID");
            device = (ITcSmTreeItem3)devices.CreateChild(deviceName, (int)type, null, null);

            if (ethernetNodes.Count > 0)
            {
                // Limitation: Taking only the first found Ethernet Adapter here!!!
                XmlNode ethernetNode = ethernetNodes[0];
                XmlNode addressInfoNode = ethernetNode.SelectSingleNode("AddressInfo");

                // Set the Address Info
                string xml = string.Format("<TreeItem><DeviceDef>{0}</DeviceDef></TreeItem>", addressInfoNode.OuterXml);
                device.ConsumeXml(xml);
            }

            return device;
        }
    }
}
