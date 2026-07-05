using System.Collections.Generic;
using System.Linq;
using System.Xml.Linq;

namespace BeckhoffAutomationInterface.Sync
{
    /// <summary>
    /// Syncs Event Classes (see EventClassDefinition) directly into a TwinCAT .tsproj file
    /// as &lt;DataType&gt; nodes under &lt;Project&gt;/&lt;System&gt;.
    ///
    /// STATUS: NOT YET WORKING. This is NOT done through the Automation Interface: SYSTEM ▸
    /// Type System has no discovered CreateChild subtype for Event Classes (CreateChild(name,
    /// 0, ...) on the "Type System" tree item expects a .tmc file path, and ConsumeXml()
    /// silently no-ops on a hand-built &lt;TreeItem&gt;&lt;DataType&gt;...&lt;/DataType&gt;&lt;/TreeItem&gt;
    /// payload — both confirmed empirically, no exception in either case). Editing the
    /// .tsproj XML directly (while devenv is closed, then reopening) was tried as the
    /// fallback instead — the file write itself is confirmed correct (verified with Visual
    /// Studio never having opened the file), and Visual Studio opens the edited project
    /// without error, but its own next save SILENTLY DROPS the &lt;DataType&gt; block entirely.
    /// Tried and all failed the same way: as the last child of &lt;System&gt;, as the first child
    /// (in case of a schema ordering requirement), with an added &lt;Hides&gt; block (copied from
    /// a real, confirmed-working example the user found in another project), and wrapped in a
    /// synthesized &lt;TypeSystem&gt; element (in case that wrapper is required) — every variant
    /// is silently stripped by Visual Studio's own save. This means Visual Studio's project
    /// loader is validating &lt;System&gt; content against something (an internal schema/type
    /// registry) that our hand-built XML never satisfies, for reasons not yet identified.
    /// NEXT STEP: get a &lt;DataType&gt; sample created via the real XAE UI (SYSTEM ▸ Type System ▸
    /// Event Classes ▸ New) *in this exact TwinCAT project* (3.1.4026.24) to diff against —
    /// the placement/wrapper requirement may differ by TwinCAT version, or Visual Studio may
    /// only ever persist objects it created itself via its own object model (in which case
    /// this whole file-edit technique is a dead end for this object type, regardless of
    /// schema, and the Event Class must be created once by hand through the UI).
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
