using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Xml.Linq;
using Interop.TCatSysManager;

namespace BeckhoffAutomationInterface.Sync
{
    class IoSyncReport
    {
        public List<string> Created { get; } = new List<string>();
        public List<string> Deleted { get; } = new List<string>();
        public List<string> StateChanged { get; } = new List<string>();

        /// <summary>Attempted changes that were confirmed NOT to have taken effect
        /// (e.g. a ConsumeXml call that didn't throw but also didn't persist) --
        /// surfaced explicitly instead of being reported as a silent success.</summary>
        public List<string> Warnings { get; } = new List<string>();
    }

    /// <summary>
    /// Reconciles the I/O Devices tree (TIID) against a desired Device/Box/Terminal
    /// hierarchy parsed from an io-devices.xml manifest.
    ///
    /// Validated 2026-07-05 against a live TwinCAT/Shark project:
    /// - Device (EtherCAT master): CreateChild(name, TSM_DEV_TYPE_ETHERCAT=94, "", null).
    /// - Box/Terminal (any Beckhoff product, e.g. EK1100/EL1008/EL2008):
    ///   CreateChild(name, TREEITEMTYPE_TERM=6, "", "&lt;ProductName&gt;") — the key
    ///   fix is passing the product name as vInfo (not null); the specific
    ///   TREEITEMTYPE/legacy TCSYSMANAGERBOXTYPES constant doesn't matter.
    ///
    /// IMPORTANT CAVEAT (see docs/ideas/st-source-twincat-sync.md): TwinCAT pops a
    /// BLOCKING native "needs sync master (at least one variable linked to a tasked
    /// variable)" dialog on Build whenever an EtherCAT master has no linked
    /// variables — confirmed true even with terminals attached, not just an empty
    /// master. This is resolved by VariableLinkEngine (which links the master's
    /// channels to the PLC %I*/%Q* variables so the validation is satisfied); if
    /// links can't be resolved, IoSyncEngine.DisableAllMasters disables the master
    /// as a fallback so unattended builds still pass.
    /// </summary>
    static class IoSyncEngine
    {
        const int TSM_DEV_TYPE_ETHERCAT = 94;
        const int TREEITEMTYPE_TERM = 6;

        public static IoSyncReport Sync(ITcSysManager sysManager, IReadOnlyList<IoDeviceSpec> desiredDevices)
        {
            var report = new IoSyncReport();
            ITcSmTreeItem ioRoot = sysManager.LookupTreeItem("TIID");

            var desiredDeviceNames = new HashSet<string>(desiredDevices.Select(d => d.Name));
            DeleteOrphans(ioRoot, desiredDeviceNames, report);

            foreach (IoDeviceSpec deviceSpec in desiredDevices)
            {
                ITcSmTreeItem device = GetOrCreate(sysManager, ioRoot, deviceSpec.Name, TSM_DEV_TYPE_ETHERCAT, null, report);

                // Enable/disable the master as declared. An ENABLED EtherCAT master with
                // no variable linked to a task blocks the build with a modal "needs sync
                // master" dialog, so declaring Disabled="true" keeps the tree populated
                // (device shown, grayed out) while letting the build stay green until
                // variable-linking is automated.
                DISABLED_STATE desiredState = deviceSpec.Disabled ? DISABLED_STATE.SMDS_DISABLED : DISABLED_STATE.SMDS_NOT_DISABLED;
                if (device.Disabled != desiredState)
                {
                    device.Disabled = desiredState;
                    report.StateChanged.Add($"{deviceSpec.Name} -> {(deviceSpec.Disabled ? "disabled" : "enabled")}");
                }

                SyncChildren(sysManager, device, deviceSpec.Children, report);
            }

            return report;
        }

        /// <summary>Recursively reconciles a Box/Terminal node's children against parent,
        /// to match arbitrarily deep real topologies (e.g. Device -> CU2508 -> EK1100 ->
        /// EL2008). Box and Terminal are the same underlying node kind — see IoNodeSpec.</summary>
        static void SyncChildren(ITcSysManager sysManager, ITcSmTreeItem parent, IReadOnlyList<IoNodeSpec> desiredChildren, IoSyncReport report)
        {
            var desiredNames = new HashSet<string>(desiredChildren.Select(c => c.Name));
            DeleteOrphans(parent, desiredNames, report);

            foreach (IoNodeSpec nodeSpec in desiredChildren)
            {
                ITcSmTreeItem node = GetOrCreate(sysManager, parent, nodeSpec.Name, TREEITEMTYPE_TERM, nodeSpec.Product, report);
                if (nodeSpec.CreatePlcType != null)
                    ApplyPlcDataTypeSetting(node, nodeSpec.CreatePlcType, report);
                SyncChildren(sysManager, node, nodeSpec.Children, report);
            }
        }

        /// <summary>
        /// Turns on a terminal's "Plc" tab "Create PLC Data Type" setting (needed for
        /// e.g. EL3174/EL3214 analog channels to resolve as a named PLC type such as
        /// MDP5001_300_7E2119CA — see tasks/todo.md Task 3). Confirmed by reading a
        /// working reference project's saved .xti: the setting is the EtherCAT
        /// element's CreateDeviceDataType/DeviceDataTypePerChannel attributes. Modifies
        /// only those two attributes via ProduceXml/ConsumeXml (rather than
        /// hand-authoring XML) so everything else about the node's current
        /// configuration is preserved untouched.
        /// </summary>
        static void ApplyPlcDataTypeSetting(ITcSmTreeItem node, string createPlcType, IoSyncReport report)
        {
            bool perChannel = string.Equals(createPlcType, "Channel", StringComparison.OrdinalIgnoreCase);
            if (!perChannel && !string.Equals(createPlcType, "Device", StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException($"CreatePlcType must be \"Device\" or \"Channel\" (got \"{createPlcType}\") for '{node.Name}'.");

            XDocument doc = XDocument.Parse(node.ProduceXml(false));
            XElement etherCat = doc.Descendants("EtherCAT").FirstOrDefault();
            if (etherCat == null)
                return; // not an EtherCAT slave item (e.g. a non-terminal box) -- nothing to set

            bool alreadySet = (bool?)etherCat.Attribute("CreateDeviceDataType") == true
                && ((bool?)etherCat.Attribute("DeviceDataTypePerChannel") ?? false) == perChannel;
            if (alreadySet)
                return;

            etherCat.SetAttributeValue("CreateDeviceDataType", "true");
            etherCat.SetAttributeValue("DeviceDataTypePerChannel", perChannel ? "true" : "false");
            node.ConsumeXml(doc.ToString());

            // ConsumeXml not throwing does NOT mean the attribute actually stuck --
            // confirmed 2026-07-14 against a real project that it can silently no-op.
            // Re-read the node's own XML and verify before claiming success.
            XElement verifyEtherCat = XDocument.Parse(node.ProduceXml(false)).Descendants("EtherCAT").FirstOrDefault();
            bool applied = verifyEtherCat != null && (bool?)verifyEtherCat.Attribute("CreateDeviceDataType") == true;
            if (applied)
                report.StateChanged.Add($"{node.Name} -> Create PLC Data Type ({createPlcType})");
            else
                report.Warnings.Add($"{node.Name}: Create PLC Data Type ({createPlcType}) did not take effect (ConsumeXml silently no-op'd)");
        }

        /// <summary>
        /// Safety fallback: disables every EtherCAT master (direct child of TIID) so an
        /// unlinked master can't block the build with the "needs sync master" dialog.
        /// Used when declared variable links couldn't be resolved (e.g. no runtime
        /// target to activate against). Returns the names of devices it disabled.
        /// </summary>
        public static List<string> DisableAllMasters(ITcSysManager sysManager)
        {
            var disabled = new List<string>();
            ITcSmTreeItem ioRoot = sysManager.LookupTreeItem("TIID");
            string directPrefix = ioRoot.PathName + "^";
            for (int i = 1; i <= ioRoot.ChildCount; i++)
            {
                ITcSmTreeItem device = ioRoot.get_Child(i);
                if (device.PathName != directPrefix + device.Name) continue; // genuine direct child only
                if (device.Disabled != DISABLED_STATE.SMDS_DISABLED)
                {
                    device.Disabled = DISABLED_STATE.SMDS_DISABLED;
                    disabled.Add(device.Name);
                }
            }
            return disabled;
        }

        /// <summary>
        /// Delegates to the shared TreeItemFactory (idempotent check-then-create), reporting
        /// anything actually created. This makes the sync idempotent: re-running against a
        /// project that already has some (or all) of the desired hardware only creates
        /// what's missing.
        /// </summary>
        static ITcSmTreeItem GetOrCreate(ITcSysManager sysManager, ITcSmTreeItem parent, string name, int type, string vInfo, IoSyncReport report)
        {
            ITcSmTreeItem item = TreeItemFactory.GetOrCreate(sysManager, parent, name, type, vInfo, out bool isNew);
            if (isNew)
                report.Created.Add(name);
            return item;
        }

        static void DeleteOrphans(ITcSmTreeItem parent, HashSet<string> desiredNames, IoSyncReport report)
        {
            // NOTE: TwinCAT enumerates EtherCAT terminals BOTH under their coupler AND
            // flat under the device (with the same coupler-nested PathName). So when
            // pruning a device's children we must only consider GENUINE direct children
            // (PathName == parent^name); otherwise the terminals — which really live
            // under the coupler — look like device-level orphans and get deleted, then
            // recreated by the box loop (an idempotency-breaking create/delete churn).
            string directChildPrefix = parent.PathName + "^";
            int childCount = parent.ChildCount;
            var directOrphans = new List<string>();
            for (int i = 1; i <= childCount; i++)
            {
                ITcSmTreeItem child = parent.get_Child(i);
                bool isDirectChild = child.PathName == directChildPrefix + child.Name;
                if (isDirectChild && !desiredNames.Contains(child.Name))
                    directOrphans.Add(child.Name);
            }

            foreach (string name in directOrphans)
            {
                parent.DeleteChild(name);
                report.Deleted.Add(name);
            }
        }
    }
}
