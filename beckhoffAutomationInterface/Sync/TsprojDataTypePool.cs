using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml.Linq;

namespace BeckhoffAutomationInterface.Sync
{
    /// <summary>
    /// Shared load/read/write envelope for the .tsproj project file's top-level
    /// &lt;DataTypes&gt; pool — the single shared XML pool that both "Create PLC Data
    /// Type" terminal types (TsprojPlcDataTypeEditor) and Event Classes
    /// (TsprojEventClassEditor, EventClassChecker) live in, confirmed by inspecting a
    /// working reference project (see tasks/todo.md Task 3): despite the "SYSTEM ▸
    /// Type System" UI path suggesting otherwise, Event Classes are NOT nested under
    /// &lt;Project&gt;/&lt;System&gt; — they're ordinary &lt;DataType&gt; entries in this
    /// same top-level pool.
    ///
    /// Neither setting has an Automation Interface equivalent (ProduceXml/ConsumeXml use
    /// a completely different XML schema that can't express either — see
    /// TsprojPlcDataTypeEditor's doc comment), so both editors write directly to the
    /// project file Visual Studio itself saves. That MUST happen while the project is
    /// NOT open in Visual Studio (no DTE session holding the file) — see
    /// Program.RunSync/SyncPipeline, which closes/reopens the VS session around it.
    /// </summary>
    static class TsprojDataTypePool
    {
        /// <summary>Finds (or creates, as the document's first child) the top-level
        /// &lt;DataTypes&gt; element on an already-loaded .tsproj document.</summary>
        public static XElement LoadOrCreate(XDocument doc)
        {
            XElement dataTypesEl = doc.Root.Element("DataTypes");
            if (dataTypesEl == null)
            {
                dataTypesEl = new XElement("DataTypes");
                doc.Root.AddFirst(dataTypesEl);
            }
            return dataTypesEl;
        }

        /// <summary>Every &lt;DataType&gt;&lt;Name&gt; text value directly in the pool.</summary>
        public static HashSet<string> ExistingNames(XElement dataTypesEl) =>
            new HashSet<string>(dataTypesEl.Elements("DataType").Select(dt => (string)dt.Element("Name")));

        /// <summary>Every &lt;DataType&gt;&lt;Name GUID="..."&gt; attribute value directly in
        /// the pool (entries with no GUID excluded).</summary>
        public static HashSet<string> ExistingGuids(XElement dataTypesEl) =>
            new HashSet<string>(
                dataTypesEl.Elements("DataType")
                    .Select(dt => (string)dt.Element("Name")?.Attribute("GUID"))
                    .Where(guid => guid != null));

        /// <summary>Read-only convenience for callers (EventClassChecker) that only need
        /// the pool's declared names, without loading a full editable XDocument. Returns
        /// an empty set if the file doesn't exist yet or has no &lt;DataTypes&gt; pool.</summary>
        public static HashSet<string> ReadNames(string tsprojPath)
        {
            if (!File.Exists(tsprojPath))
                return new HashSet<string>();

            XDocument doc = XDocument.Load(tsprojPath);
            XElement dataTypesEl = doc.Root.Element("DataTypes");
            return dataTypesEl == null ? new HashSet<string>() : ExistingNames(dataTypesEl);
        }

        /// <summary>Writes a ".bak" copy of the original file (matching TwinCAT's own
        /// convention seen in decomposed-project .xti.bak files) before saving, so a
        /// failed edit is trivially recoverable. No-op when nothing changed.</summary>
        public static void SaveIfChanged(string tsprojPath, XDocument doc, bool changed)
        {
            if (!changed)
                return;
            File.Copy(tsprojPath, tsprojPath + ".bak", overwrite: true);
            doc.Save(tsprojPath, SaveOptions.DisableFormatting);
        }
    }
}
