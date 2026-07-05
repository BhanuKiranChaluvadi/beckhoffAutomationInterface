using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using Interop.TCatSysManager;

namespace BeckhoffAutomationInterface.Sync
{
    class IoSyncReport
    {
        public List<string> Created { get; } = new List<string>();
        public List<string> Deleted { get; } = new List<string>();
        public List<string> StateChanged { get; } = new List<string>();
    }

    /// <summary>
    /// Reconciles the I/O Devices tree (TIID) against a desired Device/Box/Terminal
    /// hierarchy parsed from an io-devices.xml manifest.
    ///
    /// Validated 2026-07-05 against a live TwinCAT/Shark project:
    /// - Device (EtherCAT master): CreateChild(name, TSM_DEV_TYPE_ETHERCAT=94, "", null).
    /// - Box/Terminal (any Beckhoff product, e.g. EK1100/EL1008/EL2008):
    ///   CreateChild(name, TREEITEMTYPE_TERM=6, "", "&lt;ProductName&gt;") \u2014 the key
    ///   fix is passing the product name as vInfo (not null); the specific
    ///   TREEITEMTYPE/legacy TCSYSMANAGERBOXTYPES constant doesn't matter.
    ///
    /// IMPORTANT CAVEAT (see docs/ideas/st-source-twincat-sync.md): TwinCAT pops a
    /// BLOCKING native "needs sync master (at least one variable linked to a tasked
    /// variable)" dialog on Build whenever an EtherCAT master has no linked
    /// variables \u2014 confirmed true even with terminals attached, not just an empty
    /// master. Until LinkVariables' correct PLC-side path is found, persisting any
    /// device via this engine WILL block unattended/automated builds until a human
    /// manually dismisses that dialog.
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

                var desiredBoxNames = new HashSet<string>(deviceSpec.Boxes.Select(b => b.Name));
                DeleteOrphans(device, desiredBoxNames, report);

                foreach (IoBoxSpec boxSpec in deviceSpec.Boxes)
                {
                    ITcSmTreeItem box = GetOrCreate(sysManager, device, boxSpec.Name, TREEITEMTYPE_TERM, boxSpec.Product, report);

                    var desiredTerminalNames = new HashSet<string>(boxSpec.Terminals.Select(t => t.Name));
                    DeleteOrphans(box, desiredTerminalNames, report);

                    foreach (IoTerminalSpec terminalSpec in boxSpec.Terminals)
                        GetOrCreate(sysManager, box, terminalSpec.Name, TREEITEMTYPE_TERM, terminalSpec.Product, report);
                }
            }

            return report;
        }

        /// <summary>
        /// Looks up an existing child by its full tree path (parent.PathName + "^" +
        /// name) \u2014 the same LookupTreeItem convention already validated elsewhere
        /// in this engine \u2014 and only calls CreateChild if it doesn't exist yet.
        /// This makes the sync idempotent: re-running against a project that already
        /// has some (or all) of the desired hardware only creates what's missing.
        /// </summary>
        static ITcSmTreeItem GetOrCreate(ITcSysManager sysManager, ITcSmTreeItem parent, string name, int type, string vInfo, IoSyncReport report)
        {
            string fullPath = parent.PathName + "^" + name;
            try
            {
                return sysManager.LookupTreeItem(fullPath);
            }
            catch (COMException)
            {
                ITcSmTreeItem created = parent.CreateChild(name, type, "", vInfo);
                report.Created.Add(name);
                return created;
            }
        }

        static void DeleteOrphans(ITcSmTreeItem parent, HashSet<string> desiredNames, IoSyncReport report)
        {
            // NOTE: TwinCAT enumerates EtherCAT terminals BOTH under their coupler AND
            // flat under the device (with the same coupler-nested PathName). So when
            // pruning a device's children we must only consider GENUINE direct children
            // (PathName == parent^name); otherwise the terminals \u2014 which really live
            // under the coupler \u2014 look like device-level orphans and get deleted, then
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
