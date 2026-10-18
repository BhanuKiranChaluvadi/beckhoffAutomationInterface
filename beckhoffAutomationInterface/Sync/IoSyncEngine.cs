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

        /// <summary>Orphans found but NOT deleted because confirmDelete was false — see
        /// IoSyncEngine.Sync's confirmDelete parameter.</summary>
        public List<string> Warnings { get; } = new List<string>();

        /// <summary>Items that exist with the wrong kind (for example a DevType 94 master where
        /// io-devices.xml declares a device on an adapter port) and were not changed because
        /// confirmDelete was false. The sync fails then.</summary>
        public List<string> Errors { get; } = new List<string>();
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
    /// - A third-party box (MARPOSS, SMC, Bronkhorst ...): a product string fails ("Invalid item
    ///   sub type"); CreateChild(name, 9099, "", int[4] { vendor, product, revision, 0 }) works
    ///   when the ESI file is installed (M5.2 spike, 2026-10-18; see EtherCatIdentity).
    ///
    /// IMPORTANT CAVEAT (see docs/ideas/st-source-twincat-sync.md): TwinCAT pops a
    /// BLOCKING native "needs sync master (at least one variable linked to a tasked
    /// variable)" dialog on Build whenever an EtherCAT master has no linked
    /// variables — confirmed true even with terminals attached, not just an empty
    /// master. This is resolved by VariableLinkEngine (which links the master's
    /// channels to the PLC %I*/%Q* variables so the validation is satisfied); if
    /// links can't be resolved, IoSyncEngine.DisableAllMasters disables the master
    /// as a fallback so unattended builds still pass.
    ///
    /// ORPHAN DELETION IS OPT-IN (confirmDelete) — confirmed dangerous by default
    /// (2026-07-14). A manifest/reality mismatch, OR a TreeItemFactory.GetOrCreate
    /// lookup miss for a legitimately-existing item (confirmed against a scratch
    /// project: chaining terminals as children of another terminal, e.g. 19
    /// terminals nested under "EL3174_2.1" rather than under a proper coupler,
    /// makes LookupTreeItem throw for that item even though it already exists —
    /// TreeItemFactory then treats it as missing, recreating it, which makes the
    /// OLD one look like a fresh orphan on the very next run), can make this
    /// delete real, hand-configured EtherCAT hardware with no warning. Without
    /// --confirm-delete-io, orphans are reported via IoSyncReport.Warnings only —
    /// never deleted — matching the existing --incremental/--confirm-delete
    /// safer-default pattern for .st sync.
    ///
    /// ADAPTER/PORT LAYOUT (2026-10-17, R2.1 spike; see IoDeviceXml): &lt;Adapter&gt; items
    /// (DevType 109) are made first, then a &lt;Device Via Port&gt; is made as SubType 111 with
    /// the TcCom object id of that adapter port as its address. TwinCAT cannot change the
    /// kind of an existing device (94 master vs 111 on a port vs 109 adapter): a wrong kind
    /// is an IoSyncReport.Errors entry, or, with --confirm-delete-io, the item is deleted
    /// and made again (the link stage then makes its links again).
    /// </summary>
    static class IoSyncEngine
    {
        const int TSM_DEV_TYPE_ETHERCAT = 94;
        const int TREEITEMTYPE_TERM = 6;

        public static IoSyncReport Sync(ITcSysManager sysManager, IReadOnlyList<IoDeviceSpec> desiredDevices, bool confirmDelete = false) =>
            Sync(sysManager, new List<IoAdapterSpec>(), desiredDevices, confirmDelete);

        public static IoSyncReport Sync(ITcSysManager sysManager, IReadOnlyList<IoAdapterSpec> desiredAdapters,
                                        IReadOnlyList<IoDeviceSpec> desiredDevices, bool confirmDelete = false)
        {
            var report = new IoSyncReport();
            ITcSmTreeItem ioRoot = sysManager.LookupTreeItem("TIID");

            // Adapters are TIID children too: never orphans while declared.
            var desiredDeviceNames = new HashSet<string>(desiredAdapters.Select(a => a.Name).Concat(desiredDevices.Select(d => d.Name)));
            DeleteOrphans(ioRoot, desiredDeviceNames, report, confirmDelete);

            // Adapters first: a device on a port needs the port's object id.
            var portOids = new Dictionary<string, List<uint>>();
            foreach (IoAdapterSpec adapterSpec in desiredAdapters)
            {
                ITcSmTreeItem adapter = GetOrCreateOfKind(sysManager, ioRoot, adapterSpec.Name, IoDeviceKind.Adapter, report, confirmDelete);
                if (adapter == null)
                    continue;
                SetDisabled(adapter, adapterSpec.Name, adapterSpec.Disabled, report);
                if (!IoDeviceXml.AdapterMatches(adapterSpec, adapter.ProduceXml()))
                {
                    adapter.ConsumeXml(IoDeviceXml.AdapterConsumeXml(adapterSpec));
                    report.StateChanged.Add($"{adapterSpec.Name} -> MAC {adapterSpec.Mac}, {adapterSpec.Ports} port(s)");
                }
                List<uint> oids = IoDeviceXml.PortOidsOf(adapter.ProduceXml());
                if (oids.Count < adapterSpec.Ports)
                    throw new InvalidOperationException(string.Format(
                        "io-devices.xml: adapter '{0}' has {1} port(s) in TwinCAT, not {2}.", adapterSpec.Name, oids.Count, adapterSpec.Ports));
                portOids[adapterSpec.Name] = oids;
            }

            foreach (IoDeviceSpec deviceSpec in desiredDevices)
            {
                ITcSmTreeItem device = GetOrCreateOfKind(sysManager, ioRoot, deviceSpec.Name,
                    deviceSpec.OnPort ? IoDeviceKind.OnPort : IoDeviceKind.Master, report, confirmDelete);
                if (device == null)
                    continue;

                // Enable/disable the master as declared. An ENABLED EtherCAT master with
                // no variable linked to a task blocks the build with a modal "needs sync
                // master" dialog, so declaring Disabled="true" keeps the tree populated
                // (device shown, grayed out) while letting the build stay green until
                // variable-linking is automated.
                SetDisabled(device, deviceSpec.Name, deviceSpec.Disabled, report);

                if (deviceSpec.OnPort && portOids.TryGetValue(deviceSpec.Via, out List<uint> oids))
                {
                    uint portOid = oids[deviceSpec.Port.Value - 1];
                    if (IoDeviceXml.TcComObjectIdOf(device.ProduceXml()) != portOid)
                    {
                        device.ConsumeXml(IoDeviceXml.PortConsumeXml(portOid));
                        report.StateChanged.Add($"{deviceSpec.Name} -> {deviceSpec.Via} port {deviceSpec.Port} ({IoDeviceXml.FormatObjectId(portOid)})");
                    }
                }

                if (deviceSpec.Adapter != null && AdapterOf(device.ProduceXml()) != deviceSpec.Adapter)
                {
                    device.ConsumeXml("<TreeItem><DeviceDef><AddressInfo><Pnp><DeviceData>" + deviceSpec.Adapter +
                                      "</DeviceData></Pnp></AddressInfo></DeviceDef></TreeItem>");
                    report.StateChanged.Add($"{deviceSpec.Name} -> adapter {deviceSpec.Adapter}");
                }

                SyncChildren(sysManager, device, deviceSpec.Children, report, confirmDelete);
            }

            return report;
        }

        static void SetDisabled(ITcSmTreeItem item, string name, bool disabled, IoSyncReport report)
        {
            DISABLED_STATE desiredState = disabled ? DISABLED_STATE.SMDS_DISABLED : DISABLED_STATE.SMDS_NOT_DISABLED;
            if (item.Disabled != desiredState)
            {
                item.Disabled = desiredState;
                report.StateChanged.Add($"{name} -> {(disabled ? "disabled" : "enabled")}");
            }
        }

        /// <summary>The TIID child of that name and kind, made when missing. An existing item of
        /// another kind cannot be converted: with confirmDelete it is deleted and made again;
        /// without, it is reported in IoSyncReport.Errors and null is returned (skip it).</summary>
        static ITcSmTreeItem GetOrCreateOfKind(ITcSysManager sysManager, ITcSmTreeItem ioRoot, string name, IoDeviceKind kind,
                                               IoSyncReport report, bool confirmDelete)
        {
            int type = kind == IoDeviceKind.Adapter ? IoDeviceXml.DevTypeRtEthernetAdapter
                     : kind == IoDeviceKind.OnPort ? IoDeviceXml.SubTypeEtherCatOnPort
                     : TSM_DEV_TYPE_ETHERCAT;
            ITcSmTreeItem existing = TryLookup(sysManager, ioRoot.PathName + "^" + name);
            if (existing != null)
            {
                IoDeviceKind actual = IoDeviceXml.KindOf(existing.ProduceXml());
                if (actual == kind)
                    return existing;
                if (!confirmDelete)
                {
                    report.Errors.Add(string.Format(
                        "{0} is {1} in the project, but io-devices.xml declares {2}. TwinCAT cannot change the kind of a device; " +
                        "re-run with --confirm-delete-io to delete it and make it again (its links are made again too).",
                        name, Describe(actual), Describe(kind)));
                    return null;
                }
                ioRoot.DeleteChild(name);
                report.Deleted.Add($"{name} ({Describe(actual)}; made again as {Describe(kind)})");
            }
            return GetOrCreate(sysManager, ioRoot, name, type, null, report);
        }

        static ITcSmTreeItem TryLookup(ITcSysManager sysManager, string path)
        {
            try
            {
                return sysManager.LookupTreeItem(path);
            }
            catch (COMException)
            {
                return null;
            }
        }

        static string Describe(IoDeviceKind kind)
        {
            switch (kind)
            {
                case IoDeviceKind.Adapter: return "an RT-Ethernet adapter (DevType 109, <Adapter>)";
                case IoDeviceKind.Master: return "an EtherCAT master with its own adapter (DevType 94, Adapter=)";
                case IoDeviceKind.OnPort: return "an EtherCAT device on an adapter port (SubType 111, Via= Port=)";
                default: return "another device type";
            }
        }

        /// <summary>Recursively reconciles a Box/Terminal node's children against parent,
        /// to match arbitrarily deep real topologies (e.g. Device -> CU2508 -> EK1100 ->
        /// EL2008). Box and Terminal are the same underlying node kind — see IoNodeSpec.</summary>
        static void SyncChildren(ITcSysManager sysManager, ITcSmTreeItem parent, IReadOnlyList<IoNodeSpec> desiredChildren, IoSyncReport report, bool confirmDelete)
        {
            var desiredNames = new HashSet<string>(desiredChildren.Select(c => c.Name));
            DeleteOrphans(parent, desiredNames, report, confirmDelete);

            foreach (IoNodeSpec nodeSpec in desiredChildren)
            {
                ITcSmTreeItem node = GetOrCreateNode(sysManager, parent, nodeSpec, report);
                SyncChildren(sysManager, node, nodeSpec.Children, report, confirmDelete);
            }
        }

        /// <summary>A Beckhoff box from its Product string (TREEITEMTYPE_TERM); a box with an ESI
        /// identity (a third-party box) with CreateChild(name, 9099, "", int[4]). A new box with an
        /// identity must have the Product as its type name (ItemSubTypeName), if Product is given.</summary>
        static ITcSmTreeItem GetOrCreateNode(ITcSysManager sysManager, ITcSmTreeItem parent, IoNodeSpec nodeSpec, IoSyncReport report)
        {
            int createdBefore = report.Created.Count;
            ITcSmTreeItem node;
            try
            {
                node = nodeSpec.Identity == null
                    ? GetOrCreate(sysManager, parent, nodeSpec.Name, TREEITEMTYPE_TERM, nodeSpec.Product, report)
                    : GetOrCreate(sysManager, parent, nodeSpec.Name, EtherCatIdentity.BoxSubType, nodeSpec.Identity.ToVInfo(), report);
            }
            catch (COMException ex)
            {
                throw new InvalidOperationException(string.Format(
                    "io-devices.xml: TwinCAT cannot make box '{0}' ({1}): {2}. Is the ESI file of this box installed " +
                    @"(TwinCAT\3.1\Config\Io\EtherCAT)? A box that is not a Beckhoff box needs VendorId, ProductCode and RevisionNo.",
                    nodeSpec.Name, nodeSpec.Identity?.ToString() ?? "Product \"" + nodeSpec.Product + "\"", ex.Message), ex);
            }

            bool isNew = report.Created.Count > createdBefore;
            if (isNew && nodeSpec.Identity != null && !string.IsNullOrEmpty(nodeSpec.Product) && node.ItemSubTypeName != nodeSpec.Product)
                throw new InvalidOperationException(string.Format(
                    "io-devices.xml: box '{0}': {1} made a \"{2}\", but Product says \"{3}\". Check the three numbers against the ESI file.",
                    nodeSpec.Name, nodeSpec.Identity, node.ItemSubTypeName, nodeSpec.Product));
            return node;
        }

        /// <summary>The adapter address (Pnp/DeviceData) in a device's XML; null when absent.</summary>
        internal static string AdapterOf(string deviceXml)
        {
            var m = System.Text.RegularExpressions.Regex.Match(deviceXml ?? "", "<Pnp>.*?<DeviceData>([0-9A-Fa-f]*)</DeviceData>", System.Text.RegularExpressions.RegexOptions.Singleline);
            return m.Success ? m.Groups[1].Value.ToUpperInvariant() : null;
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
        static ITcSmTreeItem GetOrCreate(ITcSysManager sysManager, ITcSmTreeItem parent, string name, int type, object vInfo, IoSyncReport report)
        {
            ITcSmTreeItem item = TreeItemFactory.GetOrCreate(sysManager, parent, name, type, vInfo, out bool isNew);
            if (isNew)
            {
                // TwinCAT may put a new item somewhere else than asked: a terminal cannot hold
                // E-bus terminals, so "children" of a terminal land beside it. The next run would
                // not find them and create them again (found on ST/Shark BH2, 2026-10-14).
                string expected = parent.PathName + "^" + name;
                if (item.PathName != expected)
                    throw new InvalidOperationException(string.Format(
                        "io-devices.xml: TwinCAT placed '{0}' at '{1}', not at '{2}'. Check the nesting: " +
                        "E-bus terminals belong under their coupler (for example EK1100), not under another terminal.",
                        name, item.PathName, expected));
                report.Created.Add(name);
            }
            return item;
        }

        static void DeleteOrphans(ITcSmTreeItem parent, HashSet<string> desiredNames, IoSyncReport report, bool confirmDelete)
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
                if (confirmDelete)
                {
                    parent.DeleteChild(name);
                    report.Deleted.Add(name);
                }
                else
                {
                    report.Warnings.Add($"{name} (not in manifest — re-run with --confirm-delete-io to remove)");
                }
            }
        }
    }
}
