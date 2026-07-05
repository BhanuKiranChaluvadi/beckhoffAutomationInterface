using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml.Linq;

namespace BeckhoffAutomationInterface.Sync
{
    /// <summary>
    /// Parses a small XML manifest listing the Event Classes a project needs, e.g.:
    /// <code>
    /// &lt;EventClasses&gt;
    ///   &lt;EventClass Name="BeckhoffLibEvents" Guid="{...}"&gt;
    ///     &lt;Event Name="Verbose" Id="1" Severity="Verbose" Message="{0} - {1}" /&gt;
    ///   &lt;/EventClass&gt;
    /// &lt;/EventClasses&gt;
    /// </code>
    /// </summary>
    static class EventManifestParser
    {
        public static List<EventClassDefinition> Parse(string manifestFilePath)
        {
            if (!File.Exists(manifestFilePath))
                return new List<EventClassDefinition>();

            XDocument doc = XDocument.Load(manifestFilePath);
            return doc.Root
                .Elements("EventClass")
                .Select(ec => new EventClassDefinition(
                    (string)ec.Attribute("Name"),
                    (string)ec.Attribute("Guid"),
                    ec.Elements("Event")
                        .Select(ev => new EventDefinition(
                            (string)ev.Attribute("Name"),
                            (int)ev.Attribute("Id"),
                            (string)ev.Attribute("Severity"),
                            (string)ev.Attribute("Message")))
                        .ToList()))
                .ToList();
        }
    }
}
