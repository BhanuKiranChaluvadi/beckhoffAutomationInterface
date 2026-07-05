using System.Collections.Generic;
using System.Linq;
using System.Xml.Linq;

namespace BeckhoffAutomationInterface.Sync
{
    /// <summary>
    /// Syncs Event Classes (see EventClassDefinition) directly into a TwinCAT .tsproj file
    /// as &lt;DataType&gt; nodes under &lt;Project&gt;/&lt;System&gt;.
    ///
    /// This is NOT done through the Automation Interface: SYSTEM ▸ Type System has no
    /// documented CreateChild subtype for Event Classes (CreateChild(name, 0, ...) on the
    /// "Type System" tree item expects a .tmc file path, and ConsumeXml() silently no-ops
    /// on a hand-built &lt;TreeItem&gt;&lt;DataType&gt;...&lt;/DataType&gt;&lt;/TreeItem&gt; payload \u2014
    /// both confirmed empirically). Editing the .tsproj XML directly (while devenv is
    /// closed, then reopening) is the same technique the user validated by hand in a prior
    /// project, so that's what this does. MUST be called before Visual Studio opens the
    /// project (devenv holds an in-memory copy and would overwrite this file on save).
    /// </summary>
    static class EventClassSync
    {
        /// <summary>
        /// Reconciles the .tsproj's &lt;System&gt; child &lt;DataType&gt; nodes to match
        /// <paramref name="desired"/> (matched/replaced by the Event Class's Name), leaving
        /// any other System content (Tasks, other DataTypes not in the manifest, etc.)
        /// untouched. Returns the number of Event Classes written.
        /// </summary>
        public static int Sync(string tsprojFilePath, IReadOnlyList<EventClassDefinition> desired)
        {
            XDocument doc = XDocument.Load(tsprojFilePath);
            XElement systemEl = doc.Root.Element("Project").Element("System");

            foreach (EventClassDefinition eventClass in desired)
            {
                systemEl.Elements("DataType")
                    .Where(dt => (string)dt.Element("Name") == eventClass.Name)
                    .ToList()
                    .ForEach(dt => dt.Remove());

                systemEl.Add(BuildDataTypeElement(eventClass));
            }

            doc.Save(tsprojFilePath);
            return desired.Count;
        }

        static XElement BuildDataTypeElement(EventClassDefinition eventClass)
        {
            var dataType = new XElement("DataType",
                new XElement("Name",
                    new XAttribute("GUID", eventClass.Guid),
                    new XAttribute("PersistentType", "true"),
                    eventClass.Name),
                new XElement("DisplayName",
                    new XAttribute("TxtId", ""),
                    new XCData(eventClass.Name)));

            foreach (EventDefinition ev in eventClass.Events)
            {
                dataType.Add(new XElement("EventId",
                    new XElement("Name", new XAttribute("Id", ev.Id), ev.Name),
                    new XElement("DisplayName", new XAttribute("TxtId", ""), new XCData(ev.Message)),
                    new XElement("Severity", ev.Severity)));
            }

            // Boilerplate seen on every user-defined Event Class DataType (verified against a
            // prior working project) — appears to be required for TwinCAT to recognize the
            // DataType as a valid Event Class rather than silently dropping it on save.
            dataType.Add(new XElement("Hides",
                new XElement("Hide", new XAttribute("GUID", "{696CF496-CB58-4488-B235-C6BFECA57842}")),
                new XElement("Hide", new XAttribute("GUID", "{362FAB99-599A-46AB-985E-1DF68F6C61F9}"))));

            return dataType;
        }
    }
}
