using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml.Linq;

namespace BeckhoffAutomationInterface.Sync
{
    /// <summary>
    /// Parses a small XML manifest describing the EtherCAT I/O hardware tree a
    /// project needs, e.g.:
    /// <code>
    /// &lt;IoTree&gt;
    ///   &lt;Device Name="Device 1 (EtherCAT)" Disabled="true"&gt;
    ///     &lt;Box Name="Term 1 (EK1100)" Product="EK1100"&gt;
    ///       &lt;Terminal Name="Term 2 (EL1008)" Product="EL1008" /&gt;
    ///       &lt;Terminal Name="Term 3 (EL2008)" Product="EL2008" /&gt;
    ///     &lt;/Box&gt;
    ///   &lt;/Device&gt;
    /// &lt;/IoTree&gt;
    /// </code>
    /// "Box" and "Terminal" elements are interchangeable and nest arbitrarily deep
    /// (both parse to the same IoNodeSpec — see its doc comment) so a real topology
    /// like Device -> Box(CU2508 junction) -> Box(EK1100 coupler) -> Terminal(EL2008)
    /// is expressible; use whichever tag name reads better at each level.
    ///
    /// Hardware topology is not IEC 61131-3 source code, so — like
    /// libraries.xml — it's config data synced directly against the
    /// Automation Interface's I/O tree (TIID) rather than going through the
    /// .st POU/DUT/GVL convention.
    ///
    /// The optional Disabled="true" attribute marks a device as disabled in the
    /// tree (grayed out). This is currently important: an ENABLED EtherCAT master
    /// with no variables linked to a task blocks the build with a modal "needs
    /// sync master" dialog, so until variable-linking is automated, devices are
    /// declared Disabled so the tree is still populated but the build stays green.
    ///
    /// The optional Variants="A,B" attribute (Device/Box/Terminal) puts an item in those
    /// project variants only (see variants.xml and IoVariantRules); no attribute = all
    /// variants. NOT allowed on &lt;Link&gt;: TwinCAT stores links once for all variants
    /// (docs/ideas/variant-io-spike.md), and a link to an item that is disabled in a
    /// variant is harmless.
    ///
    /// Two network layouts (both may be in one file):
    ///   - An independent EtherCAT master (DevType 94) per device, with its own network
    ///     card: &lt;Device Name="BH1" Adapter="02000000B101"&gt; (Adapter = the card's MAC).
    ///   - One real-time Ethernet adapter (DevType 109) with a CU2508 port multiplier, and
    ///     EtherCAT devices (SubType 111) on its ports:
    ///     &lt;Adapter Name="Device 1 (RT-Ethernet Adapter)" Mac="000105992C32" Ports="8" /&gt;
    ///     &lt;Device Name="BH1" Via="Device 1 (RT-Ethernet Adapter)" Port="1"&gt;
    ///   Via and Adapter are exclusive; Via must name an &lt;Adapter&gt;; 1 &lt;= Port &lt;= Ports;
    ///   one device per port.
    ///
    /// A third-party box (not Beckhoff) is made from its ESI identity, not from Product:
    ///   &lt;Box Name="Box 11 (MARPOSS P3XF STANDARD)" Product="MARPOSS P3XF STANDARD"
    ///        VendorId="#x00000916" ProductCode="#x00004000" RevisionNo="#x00010002" /&gt;
    ///   All three or none. Product is then only a check: the new box must have this type name.
    /// </summary>
    static class IoManifestParser
    {
        static readonly string[] NodeElementNames = { "Box", "Terminal" };

        /// <summary>The devices only (see <see cref="ParseManifest"/> for the adapters too).</summary>
        public static List<IoDeviceSpec> Parse(string manifestFilePath) => ParseManifest(manifestFilePath).Devices;

        /// <summary>Adapters and devices, with the Via/Port rules checked (FormatException).</summary>
        public static IoManifest ParseManifest(string manifestFilePath)
        {
            if (!File.Exists(manifestFilePath))
                return new IoManifest(new List<IoAdapterSpec>(), new List<IoDeviceSpec>());

            XDocument doc = XDocument.Load(manifestFilePath);
            List<IoAdapterSpec> adapters = doc.Root.Elements("Adapter").Select(ParseAdapterElement).ToList();
            List<IoDeviceSpec> devices = doc.Root
                .Elements("Device")
                .Select(deviceEl => new IoDeviceSpec(
                    (string)deviceEl.Attribute("Name"),
                    (bool?)deviceEl.Attribute("Disabled") ?? false,
                    ParseChildNodes(deviceEl),
                    ParseVariants(deviceEl),
                    ParseMac(deviceEl, "Adapter", "Device"),
                    (string)deviceEl.Attribute("Via"),
                    ParseInt(deviceEl, "Port", "Device")))
                .ToList();
            CheckLayout(adapters, devices);
            return new IoManifest(adapters, devices);
        }

        /// <summary>&lt;Adapter Name="..." Mac="000105992C32" Ports="8" [DeviceDesc="..."] [Disabled="true"] /&gt;</summary>
        static IoAdapterSpec ParseAdapterElement(XElement el)
        {
            string name = (string)el.Attribute("Name");
            if (string.IsNullOrWhiteSpace(name))
                throw new System.FormatException("io-devices.xml: an <Adapter> has no Name.");
            string mac = ParseMac(el, "Mac", "Adapter");
            if (mac == null)
                throw new System.FormatException(string.Format("io-devices.xml: Adapter '{0}' has no Mac (12 hex digits, for example 000105992C32).", name));
            int? ports = ParseInt(el, "Ports", "Adapter");
            if (ports == null || ports < 1)
                throw new System.FormatException(string.Format("io-devices.xml: Adapter '{0}' needs Ports=\"n\" (n >= 1; 8 for a CU2508).", name));
            return new IoAdapterSpec(name, mac, ports.Value, (string)el.Attribute("DeviceDesc"), (bool?)el.Attribute("Disabled") ?? false);
        }

        /// <summary>The Via/Port rules: Via and Adapter are exclusive, Via names an &lt;Adapter&gt;,
        /// 1 &lt;= Port &lt;= Ports, one device per port, and no name is used twice.</summary>
        static void CheckLayout(List<IoAdapterSpec> adapters, List<IoDeviceSpec> devices)
        {
            var names = new HashSet<string>();
            foreach (string name in adapters.Select(a => a.Name).Concat(devices.Select(d => d.Name)))
                if (!names.Add(name))
                    throw new System.FormatException(string.Format(
                        "io-devices.xml: the name '{0}' is used twice (adapters and devices are all items of the I/O tree).", name));

            var used = new Dictionary<string, string>();
            foreach (IoDeviceSpec device in devices)
            {
                if (device.Via != null && device.Adapter != null)
                    throw new System.FormatException(string.Format(
                        "io-devices.xml: Device '{0}' has Via and Adapter. Use Via + Port for a device on a port of an <Adapter>, " +
                        "or Adapter for an independent EtherCAT master, not both.", device.Name));
                if ((device.Via == null) != (device.Port == null))
                    throw new System.FormatException(string.Format(
                        "io-devices.xml: Device '{0}' needs Via and Port together.", device.Name));
                if (device.Via == null)
                    continue;
                IoAdapterSpec adapter = adapters.FirstOrDefault(a => a.Name == device.Via);
                if (adapter == null)
                    throw new System.FormatException(string.Format(
                        "io-devices.xml: Device '{0}' Via=\"{1}\": there is no <Adapter Name=\"{1}\">.", device.Name, device.Via));
                if (device.Port < 1 || device.Port > adapter.Ports)
                    throw new System.FormatException(string.Format(
                        "io-devices.xml: Device '{0}' Port=\"{1}\": adapter '{2}' has ports 1 to {3}.", device.Name, device.Port, adapter.Name, adapter.Ports));
                string key = adapter.Name + "|" + device.Port;
                if (used.TryGetValue(key, out string other))
                    throw new System.FormatException(string.Format(
                        "io-devices.xml: Devices '{0}' and '{1}' are both on port {2} of '{3}'. One device per port.", other, device.Name, device.Port, adapter.Name));
                used.Add(key, device.Name);
            }
        }

        /// <summary>A MAC address attribute (Adapter="02000000B101" on a Device, Mac="..." on an
        /// Adapter): 12 hex digits, '-' and ':' allowed, upper case; null when absent.</summary>
        static string ParseMac(XElement el, string attribute, string kind)
        {
            string value = ((string)el.Attribute(attribute))?.Replace("-", "").Replace(":", "").Trim().ToUpperInvariant();
            if (value == null)
                return null;
            if (!System.Text.RegularExpressions.Regex.IsMatch(value, "^[0-9A-F]{12}$"))
                throw new System.FormatException(string.Format(
                    "io-devices.xml: {0} '{1}' {2}=\"{3}\" is not a 12-digit hex adapter address (for example 02000000B101).",
                    kind, (string)el.Attribute("Name"), attribute, (string)el.Attribute(attribute)));
            return value;
        }

        /// <summary>An integer attribute; null when absent.</summary>
        static int? ParseInt(XElement el, string attribute, string kind)
        {
            string value = (string)el.Attribute(attribute);
            if (value == null)
                return null;
            if (!int.TryParse(value.Trim(), System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out int result))
                throw new System.FormatException(string.Format(
                    "io-devices.xml: {0} '{1}' {2}=\"{3}\" is not a whole number.", kind, (string)el.Attribute("Name"), attribute, value));
            return result;
        }

        /// <summary>null when the attribute is absent (= all variants).</summary>
        static IReadOnlyList<string> ParseVariants(XElement el)
        {
            string value = (string)el.Attribute("Variants");
            if (value == null)
                return null;
            return value.Split(',').Select(v => v.Trim()).Where(v => v.Length > 0).ToList();
        }

        static List<IoNodeSpec> ParseChildNodes(XElement parentEl) =>
            parentEl.Elements()
                .Where(e => NodeElementNames.Contains(e.Name.LocalName))
                .Select(ParseNode)
                .ToList();

        static IoNodeSpec ParseNode(XElement nodeEl) => new IoNodeSpec(
            (string)nodeEl.Attribute("Name"),
            (string)nodeEl.Attribute("Product"),
            ParseChildNodes(nodeEl),
            (string)nodeEl.Attribute("CreatePlcType"),
            ParseVariants(nodeEl),
            ParseIdentity(nodeEl));

        static readonly string[] IdentityAttributes = { "VendorId", "ProductCode", "RevisionNo" };

        /// <summary>VendorId + ProductCode + RevisionNo (all three or none; "#x00000916", "0x916"
        /// or decimal). Null when none is present.</summary>
        static EtherCatIdentity ParseIdentity(XElement nodeEl)
        {
            string[] values = IdentityAttributes.Select(a => (string)nodeEl.Attribute(a)).ToArray();
            if (values.All(v => v == null))
                return null;
            if (values.Any(v => v == null))
                throw new System.FormatException(string.Format(
                    "io-devices.xml: box '{0}' needs all three of VendorId, ProductCode and RevisionNo, or none of them.",
                    (string)nodeEl.Attribute("Name")));
            var numbers = new uint[3];
            for (int i = 0; i < 3; i++)
            {
                uint? n = ParseEsiNumber(values[i]);
                if (n == null)
                    throw new System.FormatException(string.Format(
                        "io-devices.xml: box '{0}' {1}=\"{2}\" is not a number (use the ESI notation, for example #x00000916).",
                        (string)nodeEl.Attribute("Name"), IdentityAttributes[i], values[i]));
                numbers[i] = n.Value;
            }
            return new EtherCatIdentity(numbers[0], numbers[1], numbers[2]);
        }

        /// <summary>"#x00000916" (ESI/TwinCAT), "0x916" or decimal; null when not a number.</summary>
        internal static uint? ParseEsiNumber(string value)
        {
            string v = (value ?? "").Trim();
            if (v.StartsWith("0x", System.StringComparison.OrdinalIgnoreCase))
                v = "#x" + v.Substring(2);
            return IoDeviceXml.ParseObjectId(v);
        }

        /// <summary>Parses the optional &lt;Links&gt; section: PLC-variable-to-IO-channel links.</summary>
        public static List<LinkSpec> ParseLinks(string manifestFilePath)
        {
            if (!File.Exists(manifestFilePath))
                return new List<LinkSpec>();

            XDocument doc = XDocument.Load(manifestFilePath);
            XElement perVariant = doc.Root.Elements("Links").Elements("Link").FirstOrDefault(e => e.Attribute("Variants") != null);
            if (perVariant != null)
                throw new System.FormatException(string.Format(
                    "io-devices.xml: <Link PlcVar=\"{0}\"> has a Variants attribute. Links are the same in every variant " +
                    "(TwinCAT stores them once); put Variants on the Device/Box/Terminal instead.", (string)perVariant.Attribute("PlcVar")));
            return doc.Root
                .Elements("Links")
                .Elements("Link")
                .Select(e => new LinkSpec(
                    (string)e.Attribute("PlcVar"),
                    (string)e.Attribute("IoChannel")))
                .ToList();
        }
    }
}
