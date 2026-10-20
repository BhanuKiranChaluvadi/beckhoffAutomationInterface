using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml.Linq;

namespace BeckhoffAutomationInterface.Sync
{
    /// <summary>
    /// Already-computed &lt;DataType&gt;/&lt;PlcDataType&gt; XML for one named template
    /// (e.g. a terminal product like "EL3174", or an Event Class like
    /// "BeckhoffLibEvents"), loaded from &lt;templatesFolder&gt;/&lt;name&gt;.xml. Two real
    /// project settings turned out not to be settable via ProduceXml/ConsumeXml at all
    /// (confirmed against a real project): a terminal's
    /// "Create PLC Data Type" and an Event Class's existence. Both instead need the
    /// exact already-computed XML a working reference project produced, written
    /// directly into the .tsproj file (see TsprojPlcDataTypeEditor,
    /// TsprojEventClassEditor) rather than asking TwinCAT to derive/create it.
    /// PlcDataTypes is simply empty for an Event Class template (no such element).
    /// </summary>
    class PlcDataTypeTemplate
    {
        // Catalog descriptions can contain '/', ':' and other filename characters.
        // Keep simple existing template names compatible, and encode other names without
        // truncation or collisions. Never interpret a catalog name as a filesystem path.
        internal static string FileName(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) throw new System.ArgumentException("A template name is required.", nameof(name));
            if (name.Length <= 120 && System.Text.RegularExpressions.Regex.IsMatch(name, @"\A[A-Za-z0-9_][A-Za-z0-9_.-]*\z")
                && !System.Text.RegularExpressions.Regex.IsMatch(name, @"\A(CON|PRN|AUX|NUL|COM[1-9]|LPT[1-9])(\.|$)", System.Text.RegularExpressions.RegexOptions.IgnoreCase))
                return name + ".xml";
            using (var hash = System.Security.Cryptography.SHA256.Create())
                return "product-" + System.BitConverter.ToString(hash.ComputeHash(System.Text.Encoding.UTF8.GetBytes(name))).Replace("-", "").ToLowerInvariant() + ".xml";
        }

        public IReadOnlyList<XElement> DataTypes { get; }
        public IReadOnlyList<XElement> PlcDataTypes { get; }

        /// <summary>"EL3214-0000-0017" -> "EL3214"; a name without a revision is returned as is.</summary>
        internal static string WithoutRevision(string product) =>
            System.Text.RegularExpressions.Regex.Replace(product ?? "", @"-\d{4}-\d{4}$", "");

        PlcDataTypeTemplate(List<XElement> dataTypes, List<XElement> plcDataTypes)
        {
            DataTypes = dataTypes;
            PlcDataTypes = plcDataTypes;
        }

        /// <summary>Returns null if no template file exists for this name (the caller
        /// should treat that as "can't fulfil this request yet"). A product with a revision
        /// ("EL3214-0000-0017") uses its own file when there is one, else the product's
        /// ("EL3214.xml").</summary>
        public static PlcDataTypeTemplate Load(string templatesFolder, string name)
        {
            string path = Path.Combine(templatesFolder, FileName(name));
            if (!File.Exists(path))
                path = Path.Combine(templatesFolder, FileName(WithoutRevision(name)));
            if (!File.Exists(path))
                return null;

            XElement root = XDocument.Load(path).Root;
            var dataTypes = root.Element("DataTypes")?.Elements("DataType").ToList() ?? new List<XElement>();
            var plcDataTypes = root.Element("PlcDataTypes")?.Elements("PlcDataType").ToList() ?? new List<XElement>();
            return new PlcDataTypeTemplate(dataTypes, plcDataTypes);
        }
    }
}
