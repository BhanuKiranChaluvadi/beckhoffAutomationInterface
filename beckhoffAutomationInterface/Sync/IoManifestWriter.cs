using System.Collections.Generic;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Interop.TCatSysManager;

namespace BeckhoffAutomationInterface.Sync
{
    class IoExportReport
    {
        public int DeviceCount { get; set; }
        public int NodeCount { get; set; }

        /// <summary>Nodes whose Product could only be derived heuristically from
        /// ItemSubTypeName (see IoManifestWriter.DeriveProduct) — surfaced so the user
        /// verifies the generated Product strings against the real hardware before relying
        /// on the manifest for a forward sync.</summary>
        public List<string> ProductsToVerify { get; } = new List<string>();
    }

    /// <summary>
    /// Reverse of IoManifestParser + IoSyncEngine: walks the live I/O Devices tree (TIID)
    /// and writes an io-devices.xml manifest describing the Device -> Box -> Terminal
    /// hierarchy. The output round-trips through IoManifestParser.Parse.
    ///
    /// Two things carry over directly from the forward engine:
    ///   - The GENUINE-DIRECT-CHILD rule (child.PathName == parent^name): TwinCAT
    ///     enumerates EtherCAT terminals BOTH under their coupler AND flat under the
    ///     device, so without this filter every terminal would be emitted twice (see
    ///     IoSyncEngine.DeleteOrphans for the same guard on the write side).
    ///   - Disabled state, read straight from ITcSmTreeItem.Disabled.
    ///
    /// PRODUCT read-back is the one piece with no proven, exact API (IoSyncEngine WRITES
    /// the product as CreateChild's vInfo but nothing reads it back). DeriveProduct makes
    /// a best-effort extraction from ItemSubTypeName — the same field Beckhoff's own
    /// ScanBoxesTC2 sample reads for a device description — and every node it can't derive
    /// cleanly is listed in IoExportReport.ProductsToVerify. Confirming/replacing this
    /// against real hardware is the open spike (tasks/2026-07-15-reverse-export-scaffold/,
    /// Task 2); until then the emitted io-devices.xml should be reviewed, not trusted blind.
    /// </summary>
    static class IoManifestWriter
    {
        // Beckhoff EtherCAT product codes: a letter prefix (EK/EL/EP/ES/CU/EK/BK...) + digits,
        // optionally with a trailing variant (e.g. "EL3174-0002"). Used to pull the bare
        // product token out of a human-readable ItemSubTypeName description.
        static readonly Regex ProductCode = new Regex(@"\b([A-Z]{2,3}\d{3,4}(?:-\d{3,4})?)\b");

        public static XDocument Build(ITcSmTreeItem ioRoot, IoExportReport report)
        {
            var root = new XElement("IoTree");
            string directPrefix = ioRoot.PathName + "^";

            for (int i = 1; i <= ioRoot.ChildCount; i++)
            {
                ITcSmTreeItem device = ioRoot.get_Child(i);
                if (device.PathName != directPrefix + device.Name)
                    continue; // not a genuine direct child (flat-enumerated terminal) — skip

                var deviceEl = new XElement("Device", new XAttribute("Name", device.Name));
                if (device.Disabled == DISABLED_STATE.SMDS_DISABLED)
                    deviceEl.Add(new XAttribute("Disabled", "true"));

                AppendChildren(device, deviceEl, report);
                root.Add(deviceEl);
                report.DeviceCount++;
            }

            return new XDocument(new XDeclaration("1.0", "utf-8", null), root);
        }

        static void AppendChildren(ITcSmTreeItem parent, XElement parentEl, IoExportReport report)
        {
            string directPrefix = parent.PathName + "^";
            for (int i = 1; i <= parent.ChildCount; i++)
            {
                ITcSmTreeItem child = parent.get_Child(i);
                if (child.PathName != directPrefix + child.Name)
                    continue; // genuine direct children only (flat-nested guard)

                string product = DeriveProduct(child, out bool derivedHeuristically);
                // A node with children reads more naturally as <Box>; a leaf as <Terminal>.
                // IoManifestParser treats both identically, so this is purely cosmetic.
                var el = new XElement(HasGenuineChildren(child) ? "Box" : "Terminal",
                    new XAttribute("Name", child.Name));
                if (!string.IsNullOrEmpty(product))
                    el.Add(new XAttribute("Product", product));

                if (derivedHeuristically)
                    report.ProductsToVerify.Add($"{child.Name} -> Product=\"{product}\" (from \"{child.ItemSubTypeName}\")");

                AppendChildren(child, el, report);
                parentEl.Add(el);
                report.NodeCount++;
            }
        }

        static bool HasGenuineChildren(ITcSmTreeItem item)
        {
            string directPrefix = item.PathName + "^";
            for (int i = 1; i <= item.ChildCount; i++)
                if (item.get_Child(i).PathName == directPrefix + item.get_Child(i).Name)
                    return true;
            return false;
        }

        /// <summary>Best-effort recovery of the CreateChild vInfo product string (e.g.
        /// "EL1008") from a live tree item. Prefers a recognizable Beckhoff product code
        /// embedded in ItemSubTypeName; falls back to the whole ItemSubTypeName. Sets
        /// derivedHeuristically=true whenever the value wasn't a clean product-code match,
        /// so the caller can flag it for human verification.</summary>
        public static string DeriveProduct(ITcSmTreeItem item, out bool derivedHeuristically) =>
            ExtractProductCode(item.ItemSubTypeName ?? "", out derivedHeuristically);

        /// <summary>The COM-free core of DeriveProduct (unit-tested): pull a Beckhoff
        /// product code out of a description string, else return the trimmed string and
        /// flag it as a heuristic guess needing review.</summary>
        public static string ExtractProductCode(string subTypeName, out bool derivedHeuristically)
        {
            Match m = ProductCode.Match(subTypeName ?? "");
            if (m.Success)
            {
                derivedHeuristically = false;
                return m.Groups[1].Value;
            }
            derivedHeuristically = true;
            return (subTypeName ?? "").Trim();
        }
    }
}
