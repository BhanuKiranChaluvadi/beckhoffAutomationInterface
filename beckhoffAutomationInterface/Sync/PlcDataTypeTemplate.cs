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
    /// (confirmed against a real project — see tasks/todo.md Task 3): a terminal's
    /// "Create PLC Data Type" and an Event Class's existence. Both instead need the
    /// exact already-computed XML a working reference project produced, written
    /// directly into the .tsproj file (see TsprojPlcDataTypeEditor,
    /// TsprojEventClassEditor) rather than asking TwinCAT to derive/create it.
    /// PlcDataTypes is simply empty for an Event Class template (no such element).
    /// </summary>
    class PlcDataTypeTemplate
    {
        public IReadOnlyList<XElement> DataTypes { get; }
        public IReadOnlyList<XElement> PlcDataTypes { get; }

        PlcDataTypeTemplate(List<XElement> dataTypes, List<XElement> plcDataTypes)
        {
            DataTypes = dataTypes;
            PlcDataTypes = plcDataTypes;
        }

        /// <summary>Returns null if no template file exists for this name (the caller
        /// should treat that as "can't fulfil this request yet").</summary>
        public static PlcDataTypeTemplate Load(string templatesFolder, string name)
        {
            string path = Path.Combine(templatesFolder, name + ".xml");
            if (!File.Exists(path))
                return null;

            XElement root = XDocument.Load(path).Root;
            var dataTypes = root.Element("DataTypes")?.Elements("DataType").ToList() ?? new List<XElement>();
            var plcDataTypes = root.Element("PlcDataTypes")?.Elements("PlcDataType").ToList() ?? new List<XElement>();
            return new PlcDataTypeTemplate(dataTypes, plcDataTypes);
        }
    }
}
