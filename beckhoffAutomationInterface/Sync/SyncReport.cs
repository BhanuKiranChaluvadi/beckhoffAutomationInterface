using System.Collections.Generic;

namespace BeckhoffAutomationInterface.Sync
{
    /// <summary>Summarizes which POUs were created, updated, or deleted during a sync pass.</summary>
    class SyncReport
    {
        public List<string> Created { get; } = new List<string>();
        public List<string> Updated { get; } = new List<string>();
        public List<string> Deleted { get; } = new List<string>();
    }
}
