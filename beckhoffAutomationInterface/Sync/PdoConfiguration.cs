using System;
using System.Linq;
using System.Xml.Linq;
using Interop.TCatSysManager;

namespace BeckhoffAutomationInterface.Sync
{
    // Native ProcessData XML is kept intact. Entries and assignments are ordered.
    // Supplying a complete section avoids silently disabling an omitted PDO.
    static class PdoConfiguration
    {
        public static XElement Parse(XElement node)
        {
            var sections = node.Elements("ProcessData").ToArray();
            if (sections.Length == 0) return null;
            if (sections.Length != 1) throw new FormatException("Duplicate ProcessData section.");
            var section = sections[0];
            var pdos = section.Elements().Where(e => e.Name == "RxPdo" || e.Name == "TxPdo").ToArray();
            if (pdos.Length == 0) throw new FormatException("ProcessData needs the complete PDO configuration.");
            var keys = pdos.Select(p => p.Name + ":" + Number(p.Element("Index"), 65535)).ToArray();
            if (keys.Distinct().Count() != keys.Length) throw new FormatException("Duplicate PDO index.");
            foreach (var pdo in pdos)
            {
                foreach (var entry in pdo.Elements("Entry"))
                {
                    uint index = Number(entry.Element("Index"), 65535);
                    // ESI padding has index zero and may omit SubIndex entirely.
                    if (index != 0 || entry.Element("SubIndex") != null) Number(entry.Element("SubIndex"), 255);
                    if (Number(entry.Element("BitLen"), 65535) == 0) throw new FormatException("PDO entry BitLen must be positive.");
                }
            }
            return new XElement(section);
        }

        static uint Number(XElement field, uint maximum)
        {
            var number = IoManifestParser.ParseEsiNumber(field?.Value);
            if (number == null || number > maximum) throw new FormatException("Invalid PDO field: " + field?.Name);
            return number.Value;
        }

        public static XElement Read(string xml)
        {
            var section = XElement.Parse(xml).Element("EtherCAT")?.Element("Slave")?.Element("ProcessData");
            if (section?.Elements().Any(e => e.Name == "RxPdo" || e.Name == "TxPdo") != true) return null;
            var result = new XElement(section);
            // TwinCAT allocates these addresses when the complete tree is built.
            // They are not PDO configuration and are invalid in a different project.
            result.Elements().Where(e => e.Name == "Send" || e.Name == "Recv").Remove();
            result.Descendants("AdsInfo").Remove();
            return result;
        }

        // TwinCAT may add derived fields, but every requested field must round-trip.
        // PDO/Entry/assignment counts and order must also match; extra mappings are errors.
        internal static bool Matches(XElement expected, XElement actual)
        {
            if (expected == null || actual == null || expected.Name != actual.Name) return false;
            foreach (var attribute in expected.Attributes())
                if ((string)actual.Attribute(attribute.Name) != attribute.Value) return false;
            if (!expected.HasElements)
            {
                var a = IoManifestParser.ParseEsiNumber(expected.Value);
                var b = IoManifestParser.ParseEsiNumber(actual.Value);
                return a != null && b != null ? a == b : expected.Value == actual.Value;
            }
            foreach (var group in expected.Elements().GroupBy(e => e.Name))
            {
                var existing = actual.Elements(group.Key).ToArray();
                var wanted = group.ToArray();
                if (wanted.Length != existing.Length || !wanted.Zip(existing, Matches).All(v => v)) return false;
            }
            foreach (string ordered in new[] { "RxPdo", "TxPdo", "Entry", "Pdo" })
                if (expected.Elements(ordered).Count() != actual.Elements(ordered).Count()) return false;
            return true;
        }

        public static bool Apply(ITcSmTreeItem node, XElement desired)
        {
            if (desired == null || Matches(desired, Read(node.ProduceXml(false)))) return false;
            var xml = new XElement("TreeItem", new XElement("EtherCAT", new XElement("Slave", new XElement(desired))));
            node.ConsumeXml(xml.ToString(SaveOptions.DisableFormatting));
            if (!Matches(desired, Read(node.ProduceXml(false))))
                throw new InvalidOperationException(node.PathName + ": PDO read-back differs from the manifest.");
            return true;
        }
    }
}
