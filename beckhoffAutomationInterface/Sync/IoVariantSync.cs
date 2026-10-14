using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml.Linq;
using Interop.TCatSysManager;

namespace BeckhoffAutomationInterface.Sync
{
    /// <summary>A per-variant enable/disable state for one I/O item.</summary>
    sealed class IoVariantState
    {
        public string TreePath { get; }
        public IReadOnlyDictionary<string, bool> DisabledByVariant { get; }

        public IoVariantState(string treePath, IReadOnlyDictionary<string, bool> disabledByVariant)
        {
            TreePath = treePath;
            DisabledByVariant = disabledByVariant;
        }
    }

    sealed class IoVariantPlan
    {
        public List<IoVariantState> Changes { get; } = new List<IoVariantState>();
        public List<string> Warnings { get; } = new List<string>();
    }

    /// <summary>
    /// Applies the io-devices.xml Variants attributes to the project (Io stage). Proven by the
    /// Task 8 spike (docs/ideas/variant-io-spike.md):
    ///   write: ITcSmTreeItem9.PvDisable = true, then per variant (CurrentProjectVariant = v) Disabled;
    ///   read:  ITcSysManager14.ProjectVariantItems — every per-variant item as one XML.
    /// Read before write: a second run changes nothing.
    /// </summary>
    static class IoVariantSync
    {
        /// <summary>Current per-variant disable state of the I/O items (TIID^…) in a
        /// ProjectVariantItems XML string.</summary>
        internal static Dictionary<string, Dictionary<string, bool>> ReadState(string projectVariantItemsXml)
        {
            var state = new Dictionary<string, Dictionary<string, bool>>(StringComparer.Ordinal);
            if (string.IsNullOrWhiteSpace(projectVariantItemsXml))
                return state;
            foreach (XElement item in XDocument.Parse(projectVariantItemsXml).Root.Elements("Item"))
            {
                string path = (string)item.Attribute("Name");
                if (path == null || !path.StartsWith("TIID^", StringComparison.Ordinal) || (string)item.Attribute("PvUseDisable") != "true")
                    continue;
                var byVariant = new Dictionary<string, bool>(StringComparer.Ordinal);
                foreach (XElement v in item.Elements("Variant"))
                    byVariant[((string)v.Element("Name") ?? "").Trim()] = string.Equals(((string)v.Element("Disable") ?? "").Trim(), "true", StringComparison.OrdinalIgnoreCase);
                state[path] = byVariant;
            }
            return state;
        }

        /// <summary>For export: tree path → the variants that HAVE the item, in project order.
        /// Only items that are absent from at least one variant (an item enabled everywhere
        /// needs no attribute).</summary>
        internal static Dictionary<string, List<string>> VariantsByPath(
            Dictionary<string, Dictionary<string, bool>> state, IReadOnlyList<string> projectVariants)
        {
            var result = new Dictionary<string, List<string>>(StringComparer.Ordinal);
            foreach (KeyValuePair<string, Dictionary<string, bool>> item in state)
            {
                List<string> enabled = projectVariants.Where(v => !(item.Value.TryGetValue(v, out bool disabled) && disabled)).ToList();
                if (enabled.Count < projectVariants.Count)
                    result[item.Key] = enabled;
            }
            return result;
        }

        /// <summary>What to change (pure, unit-tested).</summary>
        internal static IoVariantPlan Plan(Dictionary<string, Dictionary<string, bool>> current,
                                           IReadOnlyList<IoVariantItem> desired, IReadOnlyList<string> projectVariants)
        {
            var plan = new IoVariantPlan();
            foreach (IoVariantItem item in desired)
            {
                var target = projectVariants.ToDictionary(v => v, v => !item.EnabledIn(v), StringComparer.Ordinal);
                bool same = current.TryGetValue(item.TreePath, out Dictionary<string, bool> now)
                            && target.All(t => now.TryGetValue(t.Key, out bool d) && d == t.Value);
                if (!same)
                    plan.Changes.Add(new IoVariantState(item.TreePath, target));
            }
            var desiredPaths = new HashSet<string>(desired.Select(d => d.TreePath), StringComparer.Ordinal);
            foreach (string path in current.Keys.Where(p => !desiredPaths.Contains(p)))
                plan.Warnings.Add(string.Format("'{0}' is per-variant in the project but has no Variants attribute in io-devices.xml — left unchanged.", path));
            return plan;
        }

        public static IoVariantPlan Sync(ITcSysManager sysManager, IReadOnlyList<IoVariantItem> desired, IReadOnlyList<string> projectVariants)
        {
            var sys14 = (ITcSysManager14)sysManager;
            IoVariantPlan plan = Plan(ReadState(sys14.ProjectVariantItems), desired, projectVariants);
            if (plan.Changes.Count == 0)
                return plan;

            string original = sys14.CurrentProjectVariant;
            try
            {
                foreach (IoVariantState change in plan.Changes)
                {
                    var item = (ITcSmTreeItem9)sysManager.LookupTreeItem(change.TreePath);
                    item.PvDisable = true;
                    foreach (KeyValuePair<string, bool> v in change.DisabledByVariant)
                    {
                        sys14.CurrentProjectVariant = v.Key;
                        ((ITcSmTreeItem)item).Disabled = v.Value ? DISABLED_STATE.SMDS_DISABLED : DISABLED_STATE.SMDS_NOT_DISABLED;
                    }
                }
            }
            finally
            {
                if (!string.IsNullOrEmpty(original))
                    sys14.CurrentProjectVariant = original;
            }
            return plan;
        }
    }
}
