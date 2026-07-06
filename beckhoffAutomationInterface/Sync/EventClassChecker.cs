using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml.Linq;

namespace BeckhoffAutomationInterface.Sync
{
    /// <summary>
    /// Read-only "declared vs actual" check for Event Classes (SYSTEM ▸ Type System ▸ Event
    /// Classes). Automating Event Class CREATION was confirmed a dead end this session (see
    /// docs/ideas/st-plc-bidirectional-sync.md and the old EventClassSync.cs, removed) — Visual
    /// Studio silently drops any hand-authored &lt;DataType&gt; block on its own next save, no
    /// matter the exact schema/placement tried. Event Classes must therefore be created ONCE,
    /// manually, via the real XAE UI. This checker is the "provision for future automation"
    /// the plan calls for: it reports which of the manifest's declared Event Classes are
    /// missing from the live project, WITHOUT attempting to write them — a human (or a future,
    /// better-understood automation) still has to create them.
    ///
    /// "Actual" state is read directly from the .tsproj file's &lt;Project&gt;/&lt;System&gt;
    /// &lt;DataType&gt;&lt;Name&gt; elements — no Visual Studio session needed, so this check can run
    /// standalone (fast) or as a preflight before a full sync.
    /// </summary>
    static class EventClassChecker
    {
        public static EventClassCheckReport Check(string tsprojFilePath, IReadOnlyList<EventClassDefinition> desired)
        {
            var actualNames = new HashSet<string>();

            if (File.Exists(tsprojFilePath))
            {
                XDocument doc = XDocument.Load(tsprojFilePath);
                XElement systemEl = doc.Root.Element("Project")?.Element("System");
                if (systemEl != null)
                {
                    foreach (XElement dataType in systemEl.Elements("DataType"))
                    {
                        string name = (string)dataType.Element("Name");
                        if (name != null)
                            actualNames.Add(name);
                    }
                }
            }

            var report = new EventClassCheckReport();
            foreach (EventClassDefinition eventClass in desired)
            {
                (actualNames.Contains(eventClass.Name) ? report.Present : report.Missing).Add(eventClass.Name);
            }
            return report;
        }
    }

    class EventClassCheckReport
    {
        public List<string> Present { get; } = new List<string>();
        public List<string> Missing { get; } = new List<string>();
    }
}
