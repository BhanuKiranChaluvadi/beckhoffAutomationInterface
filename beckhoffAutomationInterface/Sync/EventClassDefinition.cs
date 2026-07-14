using System.Collections.Generic;

namespace BeckhoffAutomationInterface.Sync
{
    /// <summary>One severity-level entry within an Event Class (e.g. "Error").</summary>
    class EventDefinition
    {
        public string Name { get; }
        public int Id { get; }
        public string Severity { get; }
        public string Message { get; }

        public EventDefinition(string name, int id, string severity, string message)
        {
            Name = name;
            Id = id;
            Severity = severity;
            Message = message;
        }
    }

    /// <summary>
    /// A TwinCAT "Event Class" (SYSTEM ▸ Type System ▸ Event Classes), as needed by
    /// Tc3_EventLogger's FB_TcEventLoggerSink. Not IEC 61131-3 source — it's a project
    /// config item (an ordinary &lt;DataType&gt; node in the .tsproj's top-level
    /// &lt;DataTypes&gt; pool, despite the "Type System" UI path — see
    /// EventClassChecker's doc comment), so it's synced the same way
    /// libraries.xml/io-devices.xml are: from a small XML manifest, not from .st source.
    /// </summary>
    class EventClassDefinition
    {
        public string Name { get; }
        public string Guid { get; }
        public List<EventDefinition> Events { get; }

        public EventClassDefinition(string name, string guid, List<EventDefinition> events)
        {
            Name = name;
            Guid = guid;
            Events = events;
        }
    }
}
