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
    /// </summary>
    static class IoManifestParser
    {
        static readonly string[] NodeElementNames = { "Box", "Terminal" };

        public static List<IoDeviceSpec> Parse(string manifestFilePath)
        {
            if (!File.Exists(manifestFilePath))
                return new List<IoDeviceSpec>();

            XDocument doc = XDocument.Load(manifestFilePath);
            return doc.Root
                .Elements("Device")
                .Select(deviceEl => new IoDeviceSpec(
                    (string)deviceEl.Attribute("Name"),
                    (bool?)deviceEl.Attribute("Disabled") ?? false,
                    ParseChildNodes(deviceEl)))
                .ToList();
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
            (string)nodeEl.Attribute("CreatePlcType"));

        /// <summary>Parses the optional &lt;Links&gt; section: PLC-variable-to-IO-channel links.</summary>
        public static List<LinkSpec> ParseLinks(string manifestFilePath)
        {
            if (!File.Exists(manifestFilePath))
                return new List<LinkSpec>();

            XDocument doc = XDocument.Load(manifestFilePath);
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
