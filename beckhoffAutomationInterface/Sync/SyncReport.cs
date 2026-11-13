using System.Collections.Generic;

namespace BeckhoffAutomationInterface.Sync
{
    /// <summary>Summarizes which POUs were created, updated, or deleted during a sync pass.
    /// Unchanged = the object exists and its text already matches the source (nothing written).</summary>
    class SyncReport
    {
        public List<string> Created { get; } = new List<string>();
        public List<string> Updated { get; } = new List<string>();
        public List<string> Unchanged { get; } = new List<string>();
        public List<string> Deleted { get; } = new List<string>();
        /// <summary>Of Unchanged: objects not read over COM because the sync manifest showed them unchanged.</summary>
        public int Skipped { get; set; }
    }
}
