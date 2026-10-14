using System;
using System.Collections.Generic;
using System.Linq;

namespace BeckhoffAutomationInterface.Sync
{
    /// <summary>
    /// Finds PLC objects defined in more than one .st file. TwinCAT object names are global, so
    /// the sync would put both texts into ONE object and the file that comes last would win —
    /// silently, and depending on file order. Example found on ST/Shark: a stale
    /// FB_HeatJacket.st still defined FUNCTION_BLOCK FB_HeatZone, an older version without the
    /// channel safety-trip latch. A METHOD/PROPERTY counts by "Owner.Name".
    /// </summary>
    static class DuplicateObjects
    {
        /// <summary>One line per duplicate: "FB_HeatZone: a/FB_HeatJacket.st, a/FB_HeatZone.st".
        /// Empty when every object comes from one file.</summary>
        public static List<string> Find(IEnumerable<StPouSource> sources) =>
            sources
                .GroupBy(s => s.OwnerName == null ? s.Name : s.OwnerName + "." + s.Name, StringComparer.OrdinalIgnoreCase)
                .Select(g => new { g.Key, Files = g.Select(s => s.SourceRelativePath).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(f => f).ToList() })
                .Where(d => d.Files.Count > 1)
                .OrderBy(d => d.Key, StringComparer.OrdinalIgnoreCase)
                .Select(d => d.Key + ": " + string.Join(", ", d.Files))
                .ToList();
    }
}
