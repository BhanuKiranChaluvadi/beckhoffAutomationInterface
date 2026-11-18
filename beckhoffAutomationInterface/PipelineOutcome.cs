using System.Collections.Generic;
using System.Linq;

namespace BeckhoffAutomationInterface
{
    enum PipelinePhase { Sync, Build, Validation }

    /// <summary>
    /// What one SyncPipeline run found, as values: for callers that need more than the exit
    /// code (bai machine), so no caller has to read the console text again.
    /// </summary>
    sealed class PipelineOutcome
    {
        readonly List<KeyValuePair<PipelinePhase, string>> _failures = new List<KeyValuePair<PipelinePhase, string>>();
        public void Fail(PipelinePhase phase, string reason) => _failures.Add(new KeyValuePair<PipelinePhase, string>(phase, reason));
        public IEnumerable<string> Failures => _failures.Select(f => f.Value);
        bool _syncCompleted;
        public bool SyncCompleted => _syncCompleted && !SyncProblems;
        public bool ValidationCompleted { get; internal set; }
        public bool ValidationFailed => _failures.Any(f => f.Key == PipelinePhase.Validation);
        public int? ExpectedLinks { get; set; }
        public bool LinksComplete(int declared) => DisabledMasters.Count == 0 && LinksUnresolved == 0 &&
            (declared == 0 || (LinkSyncRan && LinksLinked + FileLinks == declared));
        public bool HasErrors => _failures.Count > 0 || IoErrors > 0 || LinksUnresolved > 0 ||
            DisabledMasters.Count > 0 || (ExpectedLinks.HasValue && !LinksComplete(ExpectedLinks.Value)) ||
            BuildFailures > 0 || BuildTimedOut;
        public bool Succeeded => SyncCompleted && ValidationCompleted && BuildPassed && !HasErrors;

        bool SyncProblems => _failures.Any(f => f.Key == PipelinePhase.Sync) || IoErrors > 0 ||
            LinksUnresolved > 0 || DisabledMasters.Count > 0 || StaleNames > 0 ||
            (ExpectedLinks.HasValue && !LinksComplete(ExpectedLinks.Value));
        internal void CompleteSync() => _syncCompleted = true;

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
