using System.Collections.Generic;

namespace BeckhoffAutomationInterface.Sync
{
    /// <summary>
    /// Desired-state model for one EtherCAT device (an independent master, or a device
    /// on a port of an &lt;Adapter&gt;) and its bus topology,
    /// parsed from an io-devices.xml manifest. Mirrors the real hardware tree:
    /// Device (master) -> Box (e.g. a CU2508 junction or EK1100 coupler) -> Box/
    /// Terminal, nested arbitrarily deep (e.g. Device -> CU2508 -> EK1100 -> EL2008).
    /// </summary>
    class IoDeviceSpec
    {
        public string Name { get; }
        public bool Disabled { get; }
        public List<IoNodeSpec> Children { get; }

        /// <summary>Project variants that have this device (Variants="A,B"); null = all variants.</summary>
        public IReadOnlyList<string> Variants { get; }

        /// <summary>Network adapter address of the EtherCAT master (Adapter="02000000B101", 12 hex
        /// digits = DeviceDef/AddressInfo/Pnp/DeviceData); null = leave it as it is. Two enabled
        /// masters with the same (empty) adapter fail the system build with no error text.</summary>
        public string Adapter { get; }

        /// <summary>Name of the &lt;Adapter&gt; (RT-Ethernet adapter, CU2508) that this device is on
        /// (Via="Device 1 (RT-Ethernet Adapter)"); null = an independent master (DevType 94).
        /// With Via the device is an EtherCAT device of SubType 111 whose address is the TcCom
        /// object id of port <see cref="Port"/> of that adapter. Via and Adapter are exclusive.</summary>
        public string Via { get; }

        /// <summary>Port of the Via adapter, 1-based (Port="1" = the adapter's first port); null without Via.</summary>
        public int? Port { get; }

        /// <summary>True for a SubType 111 device on a port of an &lt;Adapter&gt; (Via + Port).</summary>
        public bool OnPort => Via != null;

        public IoDeviceSpec(string name, bool disabled, List<IoNodeSpec> children, IReadOnlyList<string> variants = null, string adapter = null,
                            string via = null, int? port = null)
        {
            Name = name;
            Disabled = disabled;
            Children = children;
            Variants = variants;
            Adapter = adapter;
            Via = via;
            Port = port;
        }
    }

    /// <summary>
    /// One real-time Ethernet adapter (DevType 109, "RT-Ethernet Adapter"), declared as
    /// &lt;Adapter Name="..." Mac="000105992C32" Ports="8" /&gt; in io-devices.xml. With a
    /// CU2508 port multiplier it has 8 ports (ESL mode); each EtherCAT device on a port
    /// (&lt;Device Via="..." Port="n"&gt;) has the port's TcCom object id as its address.
    /// Validated in the R2.1 spike (BeckhoffLib tasks/2026-10-16-plc-release, 2026-10-17)
    /// against the real Shark project (PLC_NFL_SHARK_old).
    /// </summary>
    class IoAdapterSpec
    {
        public string Name { get; }

        /// <summary>MAC address of the network card, 12 hex digits, upper case
        /// (DeviceDef/AddressInfo/Pnp/DeviceData).</summary>
        public string Mac { get; }

        /// <summary>Number of ports (8 for a CU2508). The ports exist in ESL mode only.</summary>
        public int Ports { get; }

        /// <summary>Pnp/DeviceDesc, the adapter's description in XAE; null = leave it as it is.
        /// Optional: TwinCAT finds the adapter by its MAC.</summary>
        public string DeviceDesc { get; }

        public bool Disabled { get; }

        public IoAdapterSpec(string name, string mac, int ports, string deviceDesc = null, bool disabled = false)
        {
            Name = name;
            Mac = mac;
            Ports = ports;
            DeviceDesc = deviceDesc;
            Disabled = disabled;
        }
    }

    /// <summary>The whole io-devices.xml tree part: adapters (first) and devices.</summary>
    class IoManifest
    {
        public List<IoAdapterSpec> Adapters { get; }
        public List<IoDeviceSpec> Devices { get; }

        public IoManifest(List<IoAdapterSpec> adapters, List<IoDeviceSpec> devices)
        {
            Adapters = adapters;
            Devices = devices;
        }
    }

    /// <summary>
    /// One Box or Terminal in the bus topology. TwinCAT creates both identically
    /// (CreateChild(name, TREEITEMTYPE_TERM=6, "", product) — see IoSyncEngine), so a
    /// "Box" (e.g. a coupler with terminals plugged into it) and a "Terminal" (a leaf
    /// module) are the same underlying concept here: a named node with a product code
    /// and, possibly, its own children. Nesting is unbounded to match real topologies
    /// like Device -> Box(CU2508) -> Box(EK1100) -> Terminal(EL2008).
    /// </summary>
    class IoNodeSpec
    {
        public string Name { get; }
        public string Product { get; }
        public List<IoNodeSpec> Children { get; }

        /// <summary>Mirrors the terminal's "Plc" tab "Create PLC Data Type" setting
        /// (null = leave unchanged/off; "Device" or "Channel" = the matching
        /// granularity radio button — see IoSyncEngine.ApplyPlcDataTypeSetting).
        /// Needed for terminals like EL3174/EL3214 whose analog channels are only
        /// resolvable as a named PLC type (e.g. MDP5001_300_7E2119CA) once this is
        /// turned on; plain digital terminals (EL2008 etc.) typically leave it null.</summary>
        public string CreatePlcType { get; }

        /// <summary>Project variants that have this box/terminal (Variants="A,B"); null = all variants.</summary>
        public IReadOnlyList<string> Variants { get; }

        /// <summary>The ESI identity (VendorId/ProductCode/RevisionNo attributes) of a box that
        /// the Product string cannot make: a third-party box such as the MARPOSS spectrometer.
        /// Null = make the box from Product (all Beckhoff boxes).</summary>
        public EtherCatIdentity Identity { get; }

        public IoNodeSpec(string name, string product, List<IoNodeSpec> children, string createPlcType = null, IReadOnlyList<string> variants = null,
                          EtherCatIdentity identity = null)
        {
            Name = name;
            Product = product;
            Children = children;
            CreatePlcType = createPlcType;
            Variants = variants;
            Identity = identity;
        }
    }

    /// <summary>
    /// The identity of an EtherCAT slave in its ESI file: vendor id, product code and revision.
    /// TwinCAT makes a box of any vendor from it with CreateChild(name, 9099, "", int[4]
    /// { vendor, product, revision, 0 }) (Beckhoff SysManSamples, CreateEtherCATBox). A product
    /// string works only for Beckhoff boxes: "MARPOSS P3XF STANDARD" gives "Invalid item sub
    /// type" (M5.2 spike, TwinCAT 3.1.4026, 2026-10-18). The ESI file must be installed.
    /// </summary>
    sealed class EtherCatIdentity
    {
        /// <summary>The Beckhoff vendor id. Boxes of this vendor are made from Product.</summary>
        public const uint BeckhoffVendorId = 2;

        /// <summary>The box type id of CreateChild for an EtherCAT box made from its identity.</summary>
        public const int BoxSubType = 9099;

        public uint VendorId { get; }
        public uint ProductCode { get; }
        public uint RevisionNo { get; }

        public EtherCatIdentity(uint vendorId, uint productCode, uint revisionNo)
        {
            VendorId = vendorId;
            ProductCode = productCode;
            RevisionNo = revisionNo;
        }

        /// <summary>The vInfo of CreateChild(name, 9099, "", vInfo).</summary>
        public int[] ToVInfo() => new[] { unchecked((int)VendorId), unchecked((int)ProductCode), unchecked((int)RevisionNo), 0 };

        /// <summary>TwinCAT notation, as in the ESI file and the .xti: "#x00000916".</summary>
        public static string Format(uint value) => IoDeviceXml.FormatObjectId(value);

        public override string ToString() =>
            string.Format("VendorId {0}, ProductCode {1}, RevisionNo {2}", Format(VendorId), Format(ProductCode), Format(RevisionNo));
    }
}
