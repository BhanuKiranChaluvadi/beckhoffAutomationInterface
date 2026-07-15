using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml.Linq;

namespace BeckhoffAutomationInterface.Sync
{
    /// <summary>One event class recovered from the .tsproj pool (see EventManifestWriter).</summary>
    class ExportedEventClass
    {
        public string Name { get; }
        public string Guid { get; }

        /// <summary>The verbatim &lt;DataType&gt; element — written into the
        /// event-classes/&lt;Name&gt;.xml template exactly as TwinCAT stored it (real GUID,
        /// Hides block, everything), so the forward editor round-trips it unchanged.</summary>
        public XElement RawDataType { get; }

        /// <summary>Best-effort per-event rows for events.xml (Name/Id/Severity/Message).
        /// The template file above is the authoritative round-trip artifact; these are the
        /// human-readable manifest view.</summary>
        public IReadOnlyList<EventDefinition> Events { get; }

        public ExportedEventClass(string name, string guid, XElement rawDataType, IReadOnlyList<EventDefinition> events)
        {
            Name = name;
            Guid = guid;
            RawDataType = rawDataType;
            Events = events;
        }
    }

    class EventExportReport
    {
        public List<string> WrittenTemplates { get; } = new List<string>();
        public bool EventsXmlWritten { get; set; }
    }

    /// <summary>
    /// Reverse of EventManifestParser + TsprojEventClassEditor: reads the Event Classes out
    /// of the .tsproj's top-level &lt;DataTypes&gt; pool and regenerates both the
    /// event-classes/&lt;Name&gt;.xml templates (the exact input the forward editor consumes)
    /// and the human-readable events.xml manifest. Pure file read — no Visual Studio.
    ///
    /// An Event Class is distinguished from an ordinary PLC data type / MDP5001_* type in
    /// the shared pool by having at least one &lt;EventId&gt; child (regular data types have
    /// &lt;SubItem&gt;s, never &lt;EventId&gt;s) — see the shape in
    /// ST/Shark/event-classes/BeckhoffLibEvents.xml.
    /// </summary>
    static class EventManifestWriter
    {
        /// <summary>Reads every Event Class from the .tsproj pool. Empty if the file
        /// doesn't exist, has no pool, or the pool has no event-class entries.</summary>
        public static List<ExportedEventClass> ReadFromTsproj(string tsprojPath)
        {
            var result = new List<ExportedEventClass>();
            if (!File.Exists(tsprojPath))
                return result;

            XElement dataTypesEl = XDocument.Load(tsprojPath).Root.Element("DataTypes");
            if (dataTypesEl == null)
                return result;

            foreach (XElement dataType in dataTypesEl.Elements("DataType"))
            {
                List<XElement> eventIds = dataType.Elements("EventId").ToList();
                if (eventIds.Count == 0)
                    continue; // not an event class — an ordinary PLC data type

                XElement nameEl = dataType.Element("Name");
                string name = nameEl?.Value;
                string guid = (string)nameEl?.Attribute("GUID");

                var events = eventIds.Select(ParseEvent).Where(e => e != null).ToList();
                result.Add(new ExportedEventClass(name, guid, new XElement(dataType), events));
            }
            return result;
        }

        static EventDefinition ParseEvent(XElement eventIdEl)
        {
            XElement nameEl = eventIdEl.Element("Name");
            if (nameEl == null)
                return null;
            int id = (int?)nameEl.Attribute("Id") ?? 0;
            string severity = eventIdEl.Element("Severity")?.Value ?? "";
            string message = eventIdEl.Element("DisplayName")?.Value ?? "";
            return new EventDefinition(nameEl.Value, id, severity, message);
        }

        /// <summary>Builds the events.xml manifest document (the shape EventManifestParser reads).</summary>
        public static XDocument BuildEventsXml(IReadOnlyList<ExportedEventClass> classes)
        {
            var root = new XElement("EventClasses");
            foreach (ExportedEventClass ec in classes)
            {
                var classEl = new XElement("EventClass",
                    new XAttribute("Name", ec.Name ?? ""),
                    new XAttribute("Guid", ec.Guid ?? ""));
                foreach (EventDefinition ev in ec.Events)
                {
                    classEl.Add(new XElement("Event",
                        new XAttribute("Name", ev.Name ?? ""),
                        new XAttribute("Id", ev.Id),
                        new XAttribute("Severity", ev.Severity ?? ""),
                        new XAttribute("Message", ev.Message ?? "")));
                }
                root.Add(classEl);
            }
            return new XDocument(new XDeclaration("1.0", "utf-8", null), root);
        }

        /// <summary>Wraps one class's verbatim &lt;DataType&gt; in the PlcDataTypeTemplate
        /// envelope the forward editor (PlcDataTypeTemplate.Load) expects.</summary>
        public static XDocument BuildTemplate(ExportedEventClass ec)
        {
            return new XDocument(
                new XDeclaration("1.0", "utf-8", null),
                new XElement("PlcDataTypeTemplate",
                    new XElement("DataTypes", new XElement(ec.RawDataType))));
        }

        /// <summary>Reads the .tsproj and writes events.xml + one event-classes/&lt;Name&gt;.xml
        /// per class. Writes nothing (and reports EventsXmlWritten=false) if there are none.</summary>
        public static EventExportReport Export(string tsprojPath, string eventManifestPath, string eventClassesFolder)
        {
            var report = new EventExportReport();
            List<ExportedEventClass> classes = ReadFromTsproj(tsprojPath);
            if (classes.Count == 0)
                return report;

            Directory.CreateDirectory(eventClassesFolder);
            foreach (ExportedEventClass ec in classes)
            {
                string templatePath = Path.Combine(eventClassesFolder, ec.Name + ".xml");
                BuildTemplate(ec).Save(templatePath);
                report.WrittenTemplates.Add(ec.Name + ".xml");
            }

            BuildEventsXml(classes).Save(eventManifestPath);
            report.EventsXmlWritten = true;
            return report;
        }
    }
}
