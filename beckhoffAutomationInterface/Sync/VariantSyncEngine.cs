using System.Collections.Generic;
using System.Linq;
using Interop.TCatSysManager;

namespace BeckhoffAutomationInterface.Sync
{
    class VariantSyncReport
    {
        public List<string> Added { get; } = new List<string>();
        public List<string> Removed { get; } = new List<string>();
        public bool ConfigChanged { get; set; }
        public bool DefinesSwitchedOn { get; set; }   // PvDefine and/or EnableImplicitDefines were off
        public bool NoChange => !ConfigChanged && !DefinesSwitchedOn;
    }

    /// <summary>
    /// Applies variants.xml to the project (Variants stage). Read before write, so a second
    /// run changes nothing. Proven on TwinCAT 3.1.4026 by the BeckhoffLib VariantDemo
    /// prototype (2026-10-14):
    ///   1. ITcSysManager14.ProjectVariantConfig = &lt;ProjectVariants&gt; XML (variants + groups);
    ///   2. on the PLC item (TIPC^&lt;plc&gt;): ITcSmTreeItem9.PvDefine = true — without it
    ///      the PLC gets NO variant defines (the first prototype build failed this way);
    ///   3. ConsumeXml &lt;EnableImplicitDefines&gt;true — the variant name and its group
    ///      names become the PLC's compiler defines.
    /// </summary>
    static class VariantSyncEngine
    {
        const string ImplicitDefinesOn = "<EnableImplicitDefines>true</EnableImplicitDefines>";

        public static VariantSyncReport Sync(ITcSysManager sysManager, string plcItemPath, VariantManifest desired)
        {
            var sys14 = (ITcSysManager14)sysManager;
            VariantManifest current = VariantManifest.FromProjectVariantConfigXml(sys14.ProjectVariantConfig);
            VariantSyncReport report = Plan(current, desired);
            if (report.ConfigChanged)
                sys14.ProjectVariantConfig = desired.ToProjectVariantConfigXml();

            var plcItem = (ITcSmTreeItem9)sysManager.LookupTreeItem(plcItemPath);
            if (!plcItem.PvDefine)
            {
                plcItem.PvDefine = true;
                report.DefinesSwitchedOn = true;
            }
            if (!ImplicitDefinesEnabled(((ITcSmTreeItem)plcItem).ProduceXml(false)))
            {
                ((ITcSmTreeItem)plcItem).ConsumeXml(
                    "<TreeItem><PlcProjectDef>" + ImplicitDefinesOn + "</PlcProjectDef></TreeItem>");
                report.DefinesSwitchedOn = true;
            }
            return report;
        }

        /// <summary>What the config part of a sync would change (pure, unit-tested).</summary>
        internal static VariantSyncReport Plan(VariantManifest current, VariantManifest desired)
        {
            var report = new VariantSyncReport { ConfigChanged = !current.SameAs(desired) };
            report.Added.AddRange(desired.Variants.Where(v => !current.Variants.Contains(v)));
            report.Removed.AddRange(current.Variants.Where(v => !desired.Variants.Contains(v)));
            return report;
        }

        internal static bool ImplicitDefinesEnabled(string plcItemXml) =>
            plcItemXml != null && plcItemXml.Contains(ImplicitDefinesOn);

        /// <summary>The project's variant names (empty when it has none).</summary>
        public static IReadOnlyList<string> ProjectVariants(ITcSysManager sysManager) =>
            VariantManifest.FromProjectVariantConfigXml(((ITcSysManager14)sysManager).ProjectVariantConfig).Variants;

        /// <summary>True when the PLC item passes variant defines to the PLC (both switches on).</summary>
        public static bool DefinesEnabled(ITcSysManager sysManager, string plcItemPath)
        {
            var plcItem = (ITcSmTreeItem9)sysManager.LookupTreeItem(plcItemPath);
            return plcItem.PvDefine && ImplicitDefinesEnabled(((ITcSmTreeItem)plcItem).ProduceXml(false));
        }
    }
}
