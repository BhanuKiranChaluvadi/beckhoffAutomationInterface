using System.Collections.Generic;
using System.Xml.Linq;

namespace BeckhoffAutomationInterface.Sync
{
    class EventClassEditResult
    {
        public List<string> Applied { get; } = new List<string>();
        public List<string> Warnings { get; } = new List<string>();
    }

    /// <summary>
    /// Directly edits the .tsproj project file on disk to add missing Event Classes --
    /// same technique and same reason as TsprojPlcDataTypeEditor: not achievable via
    /// ProduceXml/ConsumeXml (a different XML schema than the project file), and Event
    /// Classes are ordinary &lt;DataType&gt; entries in the project's top-level
    /// &lt;DataTypes&gt; pool (see EventClassChecker's doc comment for how this was
    /// confirmed), the SAME pool PLC Data Types merge into -- NOT nested under
    /// &lt;Project&gt;/&lt;System&gt; as previously assumed.
    ///
    /// Content comes from event-classes/&lt;Name&gt;.xml (same file schema as
    /// PlcDataTypeTemplate — only &lt;DataTypes&gt; is populated), copied verbatim from a
    /// working reference project with its REAL GUID, not a freshly generated one --
    /// see tasks/todo.md Task 3 for why a freshly generated GUID was the likely cause
    /// of this being wrongly written off as a dead end.
    ///
    /// MUST be called while the project is NOT open in Visual Studio, same as
    /// TsprojPlcDataTypeEditor. See Sync/TsprojDataTypePool.cs for the shared
    /// load/backup/save envelope (also used by TsprojPlcDataTypeEditor and
    /// EventClassChecker).
    /// </summary>
    static class TsprojEventClassEditor
    {
        public static EventClassEditResult Apply(string tsprojPath, IReadOnlyList<string> missingEventClassNames, string eventClassesFolder)
        {
            var result = new EventClassEditResult();
            if (missingEventClassNames.Count == 0)
                return result;

            XDocument doc = XDocument.Load(tsprojPath, LoadOptions.PreserveWhitespace);
            XElement dataTypesEl = TsprojDataTypePool.LoadOrCreate(doc);
            var existingNames = TsprojDataTypePool.ExistingNames(dataTypesEl);

            bool changed = false;
            foreach (string name in missingEventClassNames)
            {
                if (existingNames.Contains(name))
                {
                    result.Applied.Add($"{name} (already present)");
                    continue;
                }

                PlcDataTypeTemplate template = PlcDataTypeTemplate.Load(eventClassesFolder, name);
                if (template == null || template.DataTypes.Count == 0)
                {
                    result.Warnings.Add($"{name}: no event-classes/{name}.xml template found");
                    continue;
                }

                foreach (XElement dataType in template.DataTypes)
                    dataTypesEl.Add(new XElement(dataType));

                result.Applied.Add(name);
                changed = true;
            }

            TsprojDataTypePool.SaveIfChanged(tsprojPath, doc, changed);
            return result;
        }
    }
}
