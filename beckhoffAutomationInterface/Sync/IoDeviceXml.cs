using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Xml.Linq;

namespace BeckhoffAutomationInterface.Sync
{
    /// <summary>What a direct child of TIID is, read from its ProduceXml (see IoDeviceXml.KindOf).</summary>
    enum IoDeviceKind
    {
        /// <summary>Some other device type (not made by this tool).</summary>
        Other,
        /// <summary>A real-time Ethernet adapter (DevType 109, &lt;Adapter&gt;).</summary>
        Adapter,
        /// <summary>An independent EtherCAT master (DevType 94, &lt;Device Adapter="..."&gt;).</summary>
        Master,
        /// <summary>An EtherCAT device on a port of an adapter (SubType 111, &lt;Device Via="..." Port="n"&gt;).</summary>
        OnPort,
    }

    /// <summary>
    /// The COM-free part of the adapter/port layout (unit-tested): reads the ProduceXml of a
    /// TIID device and makes the ConsumeXml that IoSyncEngine sends. Method from the R2.1
    /// spike (BeckhoffLib tasks/2026-10-16-plc-release, 2026-10-17):
    ///   - Adapter: CreateChild(name, 109) + ConsumeXml of Pnp/DeviceData (MAC) and
    ///     EthernetAdapter (PortSelectionType Esl, Ports/Count, TcpIpPort 0). TwinCAT then saves
    ///     Ports="8" VLanMode="2" TcpIpPort="-1", as in the real Shark project.
    ///   - Ports: the adapter's ProduceXml lists EthernetAdapter/Ports/Port[n]/Oid (decimal).
    ///     Never compute an Oid: the adapter takes the next free device id.
    ///   - Device on a port: CreateChild(name, 111) + ConsumeXml of
    ///     DeviceDef/AddressInfo/TcCom/ObjectId = the port's Oid ("#x03010011").
    /// The COM property ItemSubType says 111 for a DevType 94 master too; the XML ItemSubType
    /// and the AddressInfo (TcCom vs Pnp) tell the two apart.
    /// </summary>
    static class IoDeviceXml
    {
        public const int DevTypeEtherCatMaster = 94;
        public const int DevTypeRtEthernetAdapter = 109;
        public const int SubTypeEtherCatOnPort = 111;

        public static IoDeviceKind KindOf(string deviceXml)
        {
            XElement root = Parse(deviceXml);
            if (root == null)
                return IoDeviceKind.Other;
            int subType = (int?)root.Element("ItemSubType") ?? 0;
            if (subType == DevTypeRtEthernetAdapter)
                return IoDeviceKind.Adapter;
            // A new SubType 111 device has a Pnp address until it is put on a port.
            if (TcComObjectIdOf(deviceXml) != null || subType == SubTypeEtherCatOnPort)
                return IoDeviceKind.OnPort;
            if (subType == DevTypeEtherCatMaster)
                return IoDeviceKind.Master;
            return IoDeviceKind.Other;
        }

        /// <summary>The TcCom object ids of the adapter's ports, port 1 first (empty when none).</summary>
        public static List<uint> PortOidsOf(string adapterXml)
        {
            XElement ports = Parse(adapterXml)?.Element("EthernetAdapter")?.Element("Ports");
            if (ports == null)
                return new List<uint>();
            return ports.Elements("Port")
                .Select(p => (string)p.Element("Oid"))
                .Where(v => v != null)
                .Select(v => uint.Parse(v.Trim(), CultureInfo.InvariantCulture))
                .ToList();
        }

        /// <summary>EthernetAdapter/Ports/Count; 0 when absent.</summary>
        public static int PortCountOf(string adapterXml) =>
            (int?)Parse(adapterXml)?.Element("EthernetAdapter")?.Element("Ports")?.Element("Count") ?? 0;

        /// <summary>Pnp/DeviceDesc; null when absent or empty.</summary>
        public static string DeviceDescOf(string deviceXml)
        {
            string value = (string)Parse(deviceXml)?.Element("DeviceDef")?.Element("AddressInfo")?.Element("Pnp")?.Element("DeviceDesc");
            return string.IsNullOrEmpty(value) ? null : value;
        }

        /// <summary>DeviceDef/AddressInfo/TcCom/ObjectId ("#x03010011"); null when the device has no TcCom address.</summary>
        public static uint? TcComObjectIdOf(string deviceXml)
        {
            string value = (string)Parse(deviceXml)?.Element("DeviceDef")?.Element("AddressInfo")?.Element("TcCom")?.Element("ObjectId");
            return ParseObjectId(value);
        }

        /// <summary>"#x03010011" (TwinCAT notation) or a decimal number; null when not a number.</summary>
        public static uint? ParseObjectId(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return null;
            value = value.Trim();
            if (value.StartsWith("#x", System.StringComparison.OrdinalIgnoreCase))
                return uint.TryParse(value.Substring(2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out uint hex) ? hex : (uint?)null;
            return uint.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out uint dec) ? dec : (uint?)null;
        }

        /// <summary>The ESI identity of an EtherCAT box (EtherCAT/Slave/Info VendorId, ProductCode,
        /// RevisionNo, decimal in ProduceXml); null when the XML has none (not an EtherCAT slave).</summary>
        public static EtherCatIdentity EtherCatIdentityOf(string boxXml)
        {
            XElement info = Parse(boxXml)?.Element("EtherCAT")?.Element("Slave")?.Element("Info");
            uint? vendor = ParseObjectId((string)info?.Element("VendorId"));
            uint? product = ParseObjectId((string)info?.Element("ProductCode"));
            uint? revision = ParseObjectId((string)info?.Element("RevisionNo"));
            return vendor == null || product == null || revision == null ? null : new EtherCatIdentity(vendor.Value, product.Value, revision.Value);
        }

        public static string FormatObjectId(uint oid) => "#x" + oid.ToString("x8", CultureInfo.InvariantCulture);

        /// <summary>True when the adapter already has the declared MAC, port count, ESL mode,
        /// TcpIpPort 0 and (when declared) DeviceDesc, so a second sync changes nothing.</summary>
        public static bool AdapterMatches(IoAdapterSpec spec, string adapterXml)
        {
            XElement ethernet = Parse(adapterXml)?.Element("EthernetAdapter");
            return IoSyncEngine.AdapterOf(adapterXml) == spec.Mac
                && PortCountOf(adapterXml) == spec.Ports
                && (string)ethernet?.Element("PortSelectionType") == "Esl"
                && (string)ethernet?.Element("TcpIpPort") == "0"
                && (spec.DeviceDesc == null || DeviceDescOf(adapterXml) == spec.DeviceDesc);
        }

        /// <summary>The ConsumeXml that gives an adapter its MAC, ESL mode and ports.</summary>
        public static string AdapterConsumeXml(IoAdapterSpec spec)
        {
            var pnp = new XElement("Pnp");
            if (spec.DeviceDesc != null)
                pnp.Add(new XElement("DeviceDesc", spec.DeviceDesc));
            pnp.Add(new XElement("DeviceData", spec.Mac.ToLowerInvariant()));
            return new XElement("TreeItem",
                new XElement("DeviceDef", new XElement("AddressInfo", pnp)),
                new XElement("EthernetAdapter",
                    new XElement("PortSelectionType", "Esl"),
                    new XElement("Ports", new XElement("Count", spec.Ports)),
                    new XElement("TcpIpPort", 0))).ToString(SaveOptions.DisableFormatting);
        }

        /// <summary>The ConsumeXml that puts a SubType 111 device on the port with this Oid.</summary>
        public static string PortConsumeXml(uint portOid) =>
            "<TreeItem><DeviceDef><AddressInfo><TcCom><ObjectId>" + FormatObjectId(portOid) +
            "</ObjectId></TcCom></AddressInfo></DeviceDef></TreeItem>";

        static XElement Parse(string xml)
        {
            if (string.IsNullOrWhiteSpace(xml))
                return null;
            try
            {
                return XDocument.Parse(xml).Root;
            }
            catch (System.Xml.XmlException)
            {
                return null;
            }
        }
    }
}
