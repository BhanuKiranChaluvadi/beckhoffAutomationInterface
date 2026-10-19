using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml.Linq;

namespace BeckhoffAutomationInterface.Sync
{
    // TwinCAT caches the linked system task's timing in the PLC .TcTTO file.
    // ConsumeXml(TaskDef) can leave that cache stale, even after LinkedTask is set.
    // Edit only a closed tool-owned project and retain all call/GUID metadata.
    static class PlcTaskTimingEditor
    {
        internal static Dictionary<string, XDocument> Changes(string tsproj, string plcName, TaskManifest desired)
        {
            var project = XDocument.Load(tsproj).Descendants("Plc").Elements("Project")
                .Single(p => string.Equals((string)p.Attribute("Name"), plcName, StringComparison.OrdinalIgnoreCase));
            string plcFile = ConfigurationExporter.Within(Path.GetDirectoryName(tsproj), (string)project.Attribute("PrjFilePath"));
            string root = Path.GetDirectoryName(plcFile);
            var files = XDocument.Load(plcFile).Descendants().Where(e => e.Name.LocalName == "Compile")
                .Select(e => (string)e.Attribute("Include")).Where(p => p != null && p.EndsWith(".TcTTO", StringComparison.OrdinalIgnoreCase))
                .Select(p => ConfigurationExporter.Within(root, p)).ToDictionary(p => p, XDocument.Load);
            var changes = new Dictionary<string, XDocument>();
            foreach (TaskSpec task in desired.Tasks)
            {
                var matches = files.Where(p => string.Equals((string)p.Value.Root?.Element("Task")?.Attribute("Name"), task.Name, StringComparison.OrdinalIgnoreCase)).ToArray();
                if (matches.Length != 1) throw new InvalidOperationException("Cannot uniquely locate PLC task file: " + task.Name);
                var pair = matches[0];
                var element = pair.Value.Root.Element("Task");
                if ((int?)element.Element("CycleTime") == task.CycleTimeUs && (int?)element.Element("Priority") == task.Priority) continue;
                element.SetElementValue("CycleTime", task.CycleTimeUs);
                element.SetElementValue("Priority", task.Priority);
                changes.Add(pair.Key, pair.Value);
            }
            return changes;
        }
    }
}
