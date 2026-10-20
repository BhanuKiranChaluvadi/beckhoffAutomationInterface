using System.Collections.Generic;

namespace BeckhoffAutomationInterface
{
    /// <summary>
    /// What one SyncPipeline run found, as values: for callers that need more than the exit
    /// code (bai machine), so no caller has to read the console text again.
    /// </summary>
    sealed class PipelineOutcome
    {
        /// <summary>Names synced before and no longer in the source ([drift]): still compiling in the project.</summary>
        public int StaleNames { get; set; }

        public int IoWarnings { get; set; }
        public int IoErrors { get; set; }

        /// <summary>True when the link stage ran (io-devices.xml has links, or links.xml exists).</summary>
        public bool LinkSyncRan { get; set; }
        public int LinksLinked { get; set; }
        public int LinksUnresolved { get; set; }
        /// <summary>Links applied from links.xml (all read back after the apply).</summary>
        public int FileLinks { get; set; }
        /// <summary>EtherCAT masters the link stage disabled because a link did not resolve.</summary>
        public List<string> DisabledMasters { get; } = new List<string>();

        public int Builds { get; set; }
        public int BuildFailures { get; set; }
        public bool BuildTimedOut { get; set; }
        public bool BuildPassed => Builds > 0 && BuildFailures == 0;
    }
}
