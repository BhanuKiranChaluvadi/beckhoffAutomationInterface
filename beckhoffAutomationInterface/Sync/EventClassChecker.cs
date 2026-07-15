using System.Collections.Generic;

namespace BeckhoffAutomationInterface.Sync
{
    /// <summary>
    /// Read-only "declared vs actual" check for Event Classes (SYSTEM ▸ Type System ▸ Event
    /// Classes) — the read-side counterpart to Sync/TsprojEventClassEditor.cs, which DOES
    /// create missing classes automatically (an earlier session wrongly wrote this off as a
    /// dead end; see tasks/archive/2026-07-14-post-review-hardening/ for the actual fix —
    /// the earlier attempts used the wrong parent element and an invented GUID). This checker
    /// remains useful on its own as a fast, no-Visual-Studio preflight/CI gate
    /// (--check-events): report which of the manifest's declared Event Classes are present
    /// vs still missing, without writing anything.
    ///
    /// "Actual" state is read via Sync/TsprojDataTypePool.cs, from the .tsproj file's
    /// top-level &lt;DataTypes&gt;&lt;DataType&gt;&lt;Name&gt; elements — the same shared pool
    /// PLC Data Types merge into (see Sync/TsprojPlcDataTypeEditor.cs), confirmed by
    /// inspecting a working reference project's .tsproj (tasks/todo.md Task 3): Event
    /// Classes are NOT nested under &lt;Project&gt;/&lt;System&gt; despite the "SYSTEM ▸ Type
    /// System" UI path suggesting otherwise. No Visual Studio session needed, so this check
    /// can run standalone (fast) or as a preflight before a full sync.
    /// </summary>
    static class EventClassChecker
    {
        public static EventClassCheckReport Check(string tsprojFilePath, IReadOnlyList<EventClassDefinition> desired)
        {
            HashSet<string> actualNames = TsprojDataTypePool.ReadNames(tsprojFilePath);

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
