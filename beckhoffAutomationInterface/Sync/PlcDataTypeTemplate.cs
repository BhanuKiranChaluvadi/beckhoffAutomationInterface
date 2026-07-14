using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml.Linq;

namespace BeckhoffAutomationInterface.Sync
{
    /// <summary>
    /// The already-computed "Create PLC Data Type" state for one terminal product
    /// (e.g. EL3174), loaded from a hand-authored XML file under
    /// SourceFolder/plc-data-types/&lt;Product&gt;.xml. Setting the
    /// CreateDeviceDataType/DeviceDataTypePerChannel attributes alone does not make
    /// TwinCAT compute the derived types (confirmed against a real project — see
    /// tasks/todo.md Task 3), so this supplies the DataType/PlcDataType XML a
    /// working reference project already generated, instead of asking TwinCAT to
    /// derive it via ConsumeXml. See IoSyncEngine.ApplyPlcDataTypesForDevice.
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

        /// <summary>Returns null if no template file exists for this product (the caller
        /// should treat that as "can't fulfil CreatePlcType for this product yet").</summary>
        public static PlcDataTypeTemplate Load(string sourceFolder, string product)
        {
            string path = Path.Combine(sourceFolder, "plc-data-types", product + ".xml");
            if (!File.Exists(path))
                return null;

            XElement root = XDocument.Load(path).Root;
            var dataTypes = root.Element("DataTypes")?.Elements("DataType").ToList() ?? new List<XElement>();
            var plcDataTypes = root.Element("PlcDataTypes")?.Elements("PlcDataType").ToList() ?? new List<XElement>();
            return new PlcDataTypeTemplate(dataTypes, plcDataTypes);
        }
    }
}
