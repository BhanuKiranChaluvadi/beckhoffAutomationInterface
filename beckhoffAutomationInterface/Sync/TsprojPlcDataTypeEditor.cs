using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml.Linq;

namespace BeckhoffAutomationInterface.Sync
{
    /// <summary>One EtherCAT terminal that needs "Create PLC Data Type" turned on,
    /// identified by its live Box name (matches IoNodeSpec.Name/Product/CreatePlcType).</summary>
    class PlcDataTypeTarget
    {
        public string BoxName { get; }
        public string Product { get; }
        public string CreatePlcType { get; }

        public PlcDataTypeTarget(string boxName, string product, string createPlcType)
        {
            BoxName = boxName;
            Product = product;
            CreatePlcType = createPlcType;
        }

        /// <summary>Flattens every IoNodeSpec (recursively) with CreatePlcType set, across
        /// all devices in a parsed io-devices.xml, into edit targets for TsprojPlcDataTypeEditor.</summary>
        public static List<PlcDataTypeTarget> CollectFrom(IReadOnlyList<IoDeviceSpec> devices)
        {
            var targets = new List<PlcDataTypeTarget>();
            foreach (IoDeviceSpec device in devices)
                Walk(device.Children, targets);
            return targets;
        }

        static void Walk(IReadOnlyList<IoNodeSpec> nodes, List<PlcDataTypeTarget> targets)
        {
            foreach (IoNodeSpec node in nodes)
            {
                if (node.CreatePlcType != null)
                    targets.Add(new PlcDataTypeTarget(node.Name, node.Product, node.CreatePlcType));
                Walk(node.Children, targets);
            }
        }
    }

    class PlcDataTypeEditResult
    {
        public List<string> Applied { get; } = new List<string>();
        public List<string> Warnings { get; } = new List<string>();
    }

    /// <summary>
    /// Directly edits the .tsproj project file on disk to turn on "Create PLC Data
    /// Type" for specific EtherCAT terminals.
    ///
    /// Confirmed (tasks/todo.md Task 3) that ITcSmTreeItem.ProduceXml/ConsumeXml use a
    /// completely different XML schema (a flat "TreeItem" summary — ItemName/PathName/
    /// DeviceDef/master-level EtherCAT settings, no Box, no CreateDeviceDataType/
    /// PlcDataTypes at all, even recursively) than the actual project FILE format
    /// (TcSmProject/TcSmItem, with Box/EtherCAT carrying CreateDeviceDataType). "Create
    /// PLC Data Type" is simply not expressible through the documented Automation
    /// Interface — so this edits the same file Visual Studio itself saves, instead.
    ///
    /// MUST be called while the project is NOT open in Visual Studio (no DTE session
    /// holding the file) — see Program.RunSync, which closes/reopens the VS session
    /// around this call. See Sync/TsprojDataTypePool.cs for the shared load/backup/save
    /// envelope (also used by TsprojEventClassEditor and EventClassChecker).
    /// </summary>
    static class TsprojPlcDataTypeEditor
    {
        public static PlcDataTypeEditResult Apply(string tsprojPath, IReadOnlyList<PlcDataTypeTarget> targets, string plcDataTypesFolder)
        {
            var result = new PlcDataTypeEditResult();
            if (targets.Count == 0)
                return result;

            XDocument doc = XDocument.Load(tsprojPath, LoadOptions.PreserveWhitespace);
            XElement dataTypesEl = TsprojDataTypePool.LoadOrCreate(doc);
            var existingGuids = TsprojDataTypePool.ExistingGuids(dataTypesEl);

            bool changed = false;
            foreach (PlcDataTypeTarget target in targets)
            {
                bool perChannel = string.Equals(target.CreatePlcType, "Channel", StringComparison.OrdinalIgnoreCase);
                if (!perChannel && !string.Equals(target.CreatePlcType, "Device", StringComparison.OrdinalIgnoreCase))
                    throw new ArgumentException($"CreatePlcType must be \"Device\" or \"Channel\" (got \"{target.CreatePlcType}\") for '{target.BoxName}'.");

                XElement box = doc.Descendants("Box").FirstOrDefault(b => (string)b.Element("Name") == target.BoxName);
                XElement etherCat = box?.Element("EtherCAT");
                if (etherCat == null)
                {
                    result.Warnings.Add($"{target.BoxName}: not found as a Box/EtherCAT element in '{tsprojPath}'");
                    continue;
                }

                bool alreadySet = (bool?)etherCat.Attribute("CreateDeviceDataType") == true
                    && ((bool?)etherCat.Attribute("DeviceDataTypePerChannel") ?? false) == perChannel
                    && etherCat.Element("PlcDataTypes") != null;
                if (alreadySet)
                {
                    result.Applied.Add($"{target.BoxName} (already set)");
                    continue;
                }

                PlcDataTypeTemplate template = PlcDataTypeTemplate.Load(plcDataTypesFolder, target.Product);
                if (template == null)
                {
                    result.Warnings.Add($"{target.BoxName}: no plc-data-types/{target.Product}.xml template found");
                    continue;
                }

                foreach (XElement dataType in template.DataTypes)
                {
                    string guid = (string)dataType.Element("Name")?.Attribute("GUID");
                    if (guid != null && existingGuids.Contains(guid))
                        continue;
                    dataTypesEl.Add(new XElement(dataType));
                    if (guid != null) existingGuids.Add(guid);
                }

                etherCat.SetAttributeValue("CreateDeviceDataType", "true");
                etherCat.SetAttributeValue("DeviceDataTypePerChannel", perChannel ? "true" : "false");
                etherCat.Element("PlcDataTypes")?.Remove();
                etherCat.Add(new XElement("PlcDataTypes", template.PlcDataTypes.Select(e => new XElement(e))));

                result.Applied.Add(target.BoxName);
                changed = true;
            }

            TsprojDataTypePool.SaveIfChanged(tsprojPath, doc, changed);
            return result;
        }
    }
}
