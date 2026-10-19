using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Interop.TCatSysManager;

namespace BeckhoffAutomationInterface.Sync
{
    // Ordered user startup commands; TwinCAT owns ESI/PDO-generated Fixed commands.
    // Absent CoE leaves user commands unchanged; empty CoE explicitly clears them.
    // This edits the project, never the online mailbox.
    static class CoeStartup
    {
        public static IReadOnlyList<XElement> Parse(XElement node)
        {
            var sections = node.Elements("CoE").ToArray();
            if (sections.Length == 0) return null;
            if (sections.Length != 1 || sections[0].HasAttributes || sections[0].Elements().Any(e => e.Name != "InitCmd"))
                throw new FormatException("CoE must contain only an ordered list of InitCmd elements.");
            return sections[0].Elements().Select(Normalize).ToArray();
        }

        static XElement FindCoe(XElement root) => root.Element("EtherCAT")?.Element("Slave")?.Element("Mailbox")?.Element("CoE");
        static bool IsFixed(XElement command) => (string)command.Attribute("Fixed") == "true" || (string)command.Attribute("Fixed") == "1";

        public static IReadOnlyList<XElement> Read(string nativeXml)
        {
            var coe = FindCoe(XElement.Parse(nativeXml));
            if (coe == null) return null;
            return (coe.Element("InitCmds")?.Elements("InitCmd") ?? Enumerable.Empty<XElement>())
                .Where(e => !IsFixed(e)).Select(Normalize).ToArray();
        }

        internal static XElement Normalize(XElement command)
        {
            if (command.Name != "InitCmd" || IsFixed(command))
                throw new FormatException("Only user CoE InitCmd entries are configurable; TwinCAT owns Fixed commands.");
            string fixedFlag = (string)command.Attribute("Fixed");
            if (fixedFlag != null && fixedFlag != "false" && fixedFlag != "0")
                throw new FormatException("Invalid CoE Fixed flag.");
            var result = new XElement(command);
            result.Attribute("Fixed")?.Remove();
            foreach (string field in new[] { "Index", "SubIndex", "Ccs", "Timeout", "Data", "Comment" })
                if (result.Elements(field).Count() > 1) throw new FormatException("Duplicate CoE field: " + field);
            if (!result.Elements("Transition").Any() || result.Elements("Transition").Any(e =>
                !Regex.IsMatch(e.Value, "^(IP|PI|PS|SP|SO|OS|SI|OI|IB|BI)$")))
                throw new FormatException("CoE InitCmd needs a supported EtherCAT Transition.");
            Number(result, "Index", 65535, null);
            Number(result, "SubIndex", 255, null);
            Number(result, "Ccs", 2, "1");
            if (result.Element("Ccs").Value == "0") throw new FormatException("CoE Ccs must be 1 or 2.");
            Number(result, "Timeout", uint.MaxValue, "0");
            var data = result.Element("Data");
            if (data == null || !Regex.IsMatch(data.Value, "^(?:[0-9a-fA-F]{2})+$"))
                throw new FormatException("CoE Data must contain complete hexadecimal bytes.");
            data.Value = data.Value.ToLowerInvariant();
            if (result.Element("Comment") == null) result.Add(new XElement("Comment", ""));
            return Canonical(result);
        }

        static void Number(XElement command, string name, uint maximum, string fallback)
        {
            var field = command.Element(name);
            if (field == null && fallback != null) { field = new XElement(name, fallback); command.Add(field); }
            uint? value = IoManifestParser.ParseEsiNumber(field?.Value);
            if (value == null || value > maximum) throw new FormatException("Invalid CoE " + name + ".");
            field.Value = value.Value.ToString(CultureInfo.InvariantCulture);
        }

        // Normalize CDATA and field/attribute order; the outer command list stays ordered.
        static XElement Canonical(XElement element) => new XElement(element.Name,
            element.Attributes().OrderBy(a => a.Name.ToString()).Select(a => new XAttribute(a)),
            element.HasElements ? (object)element.Elements().OrderBy(e => e.Name.ToString()).Select(Canonical) : element.Value);

        internal static bool Equal(IReadOnlyList<XElement> left, IReadOnlyList<XElement> right) =>
            left != null && right != null && left.Count == right.Count &&
            left.Zip(right, (a, b) => XNode.DeepEquals(a, b)).All(equal => equal);

        public static bool Apply(ITcSmTreeItem terminal, IReadOnlyList<XElement> desired)
        {
            if (desired == null) return false;
            string before = terminal.ProduceXml(false);
            var existing = Read(before);
            if (existing == null) throw new InvalidOperationException(terminal.PathName + ": terminal has no CoE mailbox.");
            if (Equal(existing, desired)) return false;
            XElement wrapped = new XElement("InitCmds", desired.Select(e => new XElement(e)));
            foreach (string name in new[] { "CoE", "Mailbox", "Slave", "EtherCAT", "TreeItem" })
                wrapped = new XElement(name, wrapped);
            terminal.ConsumeXml(wrapped.ToString(SaveOptions.DisableFormatting));
            string after = terminal.ProduceXml(false);
            if (!Equal(Read(after), desired))
                throw new InvalidOperationException(terminal.PathName + ": CoE startup read-back differs from the manifest.");
            if (!Equal(FixedCommands(before), FixedCommands(after)))
                throw new InvalidOperationException(terminal.PathName + ": fixed CoE startup commands changed during import.");
            return true;
        }

        static IReadOnlyList<XElement> FixedCommands(string xml) =>
            (FindCoe(XElement.Parse(xml))?.Element("InitCmds")?.Elements("InitCmd") ?? Enumerable.Empty<XElement>())
                .Where(IsFixed).Select(Canonical).ToArray();
    }
}
