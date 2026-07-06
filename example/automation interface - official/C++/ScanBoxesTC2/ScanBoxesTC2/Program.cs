using System;
using System.Collections.Generic;
using System.Text;

using TCatSysManagerLib;
using System.IO;
using System.Reflection;
using System.Xml;

namespace ScanBoxesTC2
{
    class Program
    {
        private static string _tsmPath = Path.GetDirectoryName(Assembly.GetEntryAssembly().Location) + "\\Templates\\Sample.tsm";
        private static TcSysManager _sysManager;

        static void Main(string[] args)
        {
            int i = 1;

            _sysManager = new TcSysManager();
            _sysManager.NewConfiguration();

            /* ==============================================
             * Lookup System Manager node "I/O Configuration^I/O Devices"
             * ============================================== */
            ITcSmTreeItem devicesNode = _sysManager.LookupTreeItem("TIID");


            /* ==============================================
             * Scan Devices by running ProduceXml() on that node. (could take a while)
             * List with newly found devices will be returned as a XML-String
             * ============================================== */
            string xmlDevices = devicesNode.ProduceXml(false);


            /* ==============================================
             * Parse XML String and show found devices in Console window
             * ============================================== */
            XmlDocument xmlDoc = new XmlDocument();
            xmlDoc.LoadXml(xmlDevices);
            XmlNodeList xmlDevNodes = xmlDoc.SelectNodes("TreeItem/DeviceGrpDef/FoundDevices/Device");

            Console.WriteLine("================================================ ");
            Console.WriteLine("The following devices were found on this system: ");
            Console.WriteLine("================================================ ");

            foreach(XmlNode device in xmlDevNodes)
            {
                XmlNode description = device.SelectSingleNode("ItemSubTypeName");
                Console.WriteLine("Device " + i + ": " + description.InnerText);
            }


            /* ==============================================
             * Add all found devices to configuration
             * ============================================== */
            i = 1;
            foreach (XmlNode device in xmlDevNodes)
            {
                Console.Write("Adding device " + i + "...");

                // Create new device
                ITcSmTreeItem newDevice = devicesNode.CreateChild("Device " + i, Convert.ToInt32(device.SelectSingleNode("ItemSubType").InnerText), null, null);
                
                // Prepare xml structure for configuration settings of new device
                XmlNode description = device.SelectSingleNode("AddressInfo");
                XmlDocument doc = new XmlDocument();
                XmlNode treeItem = doc.CreateElement("TreeItem");
                XmlNode devDef = doc.CreateElement("DeviceDef");
                
                // Set together xml structure
                devDef.InnerXml = description.OuterXml;
                treeItem.AppendChild(devDef);
                doc.AppendChild(treeItem);

                // Import configuration settings for new device
                newDevice.ConsumeXml(doc.OuterXml);
                
                i++;
                
                Console.WriteLine("device added!");
            }
            

            ///* ==============================================
            // * Save configuration
            // * ============================================== */
            _sysManager.SaveConfiguration(_tsmPath);
        }
    }
}
