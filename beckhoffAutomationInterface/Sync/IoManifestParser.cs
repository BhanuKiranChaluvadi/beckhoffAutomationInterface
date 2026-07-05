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
    /// Hardware topology is not IEC 61131-3 source code, so \u2014 like
    /// libraries.xml \u2014 it's config data synced directly against the
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
                    deviceEl.Elements("Box").Select(ParseBox).ToList()))
                .ToList();
        }

        static IoBoxSpec ParseBox(XElement boxEl) => new IoBoxSpec(
            (string)boxEl.Attribute("Name"),
            (string)boxEl.Attribute("Product"),
            boxEl.Elements("Terminal")
                .Select(t => new IoTerminalSpec((string)t.Attribute("Name"), (string)t.Attribute("Product")))
                .ToList());
    }
}
