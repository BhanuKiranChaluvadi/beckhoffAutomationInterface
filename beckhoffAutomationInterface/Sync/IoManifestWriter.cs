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
    /// the product as CreateChild's vInfo but nothing reads it back). DeriveProduct's
    /// PRIMARY signal, confirmed against the real PLC_NFL_SHARK_V2 project's on-disk
    /// item names (e.g. "Term 13 (EL3174).xti", "Box 40 (EX260-SEC1).xti", "Box 41 (Mass
    /// Flow %_2F Pressure Controller - RJ45).xti"), is TwinCAT's own default naming
    /// convention: dragging a device from the ESI catalog names the tree item
    /// "<InstanceLabel> (<CatalogProductString>)". That parenthetical is used VERBATIM
    /// (not regex-matched down to a short code) because a catalog product string is not
    /// always a tidy "EL1234"-shaped code — third-party/non-Beckhoff modules (the Festo
    /// "EX260-SEC1" above) can carry a hyphenated or multi-word catalog name as their real
    /// CreateChild vInfo. A short-code-only regex would have silently truncated
    /// "EX260-SEC1" to "EX260" — wrong. Only when Name has NO parenthetical (e.g.
    /// TwinCAT's other auto-naming style "EK1100_1.1", or a purely custom label like
    /// "BH1") does this fall back to a regex match embedded in Name itself, then finally
    /// to ItemSubTypeName (the field Beckhoff's own ScanBoxesTC2 sample reads for a device
    /// description) as a last resort. Every value not lifted from a parenthetical is
    /// listed in IoExportReport.ProductsToVerify for human review.
    /// </summary>
    static class IoManifestWriter
    {
        // TwinCAT's own default device-naming convention: "<Label> (<CatalogProduct>)".
        // Captures the LAST parenthetical group so a label that itself contains parens
        // still resolves to the trailing catalog string.
        static readonly Regex TrailingParenthetical = new Regex(@"\(([^()]+)\)\s*$");

        // Beckhoff EtherCAT product codes: a letter prefix (EK/EL/EP/ES/CU/BK...) + digits,
        // optionally with a trailing variant (e.g. "EL3174-0002"), anchored at the START of
        // the string. Used only as a fallback when Name has no parenthetical (e.g.
        // "EK1100_1.1", "EL2008_1.6" — TwinCAT's OTHER auto-naming style, always
        // code-then-underscore). NOTE: deliberately not using \b as a trailing boundary —
        // in .NET regex "_" is a word character, so there is NO boundary between "2008"
        // and the following "_" in "EL2008_1.6", and \b would silently fail to match at all.
        static readonly Regex ProductCode = new Regex(@"^([A-Z]{2,3}\d{3,4}(?:-\d{3,4})?)");

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

                string product = DeriveProduct(child.Name, child.ItemSubTypeName, out bool derivedHeuristically);
                // A node with children reads more naturally as <Box>; a leaf as <Terminal>.
                // IoManifestParser treats both identically, so this is purely cosmetic.
                var el = new XElement(HasGenuineChildren(child) ? "Box" : "Terminal",
                    new XAttribute("Name", child.Name));
                if (!string.IsNullOrEmpty(product))
                    el.Add(new XAttribute("Product", product));

                if (derivedHeuristically)
                    report.ProductsToVerify.Add($"{child.Name} -> Product=\"{product}\" (from Name/ItemSubTypeName=\"{child.ItemSubTypeName}\")");

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

        /// <summary>Recovery of the CreateChild vInfo product string from a live tree item's
        /// Name + ItemSubTypeName (COM-free core below; unit-tested against real device
        /// names). Sets derivedHeuristically=true whenever the value wasn't lifted from
        /// Name's trailing parenthetical (TwinCAT's own default naming convention), so the
        /// caller can flag anything less certain for human verification.</summary>
        public static string DeriveProduct(ITcSmTreeItem item, out bool derivedHeuristically) =>
            DeriveProduct(item.Name, item.ItemSubTypeName, out derivedHeuristically);

        /// <summary>The COM-free core of DeriveProduct (unit-tested). Order of preference:
        /// (1) Name's trailing "(...)" verbatim — TwinCAT's own default device-naming
        /// convention when dragging from the ESI catalog, confirmed reliable even for
        /// non-Beckhoff/hyphenated/multi-word catalog strings; (2) a Beckhoff-shaped
        /// product code embedded directly in Name (e.g. "EK1100_1.1" -> "EK1100"), for
        /// TwinCAT's other auto-naming style with no parenthetical; (3) the same
        /// code-shape match against ItemSubTypeName; (4) ItemSubTypeName verbatim. Only
        /// (1) is treated as confirmed (derivedHeuristically=false) — everything else is
        /// a guess flagged for review.</summary>
        public static string DeriveProduct(string name, string itemSubTypeName, out bool derivedHeuristically)
        {
            Match paren = TrailingParenthetical.Match(name ?? "");
            if (paren.Success)
            {
                derivedHeuristically = false;
                return paren.Groups[1].Value.Trim();
            }

            Match nameCode = ProductCode.Match(name ?? "");
            if (nameCode.Success)
            {
                derivedHeuristically = true;
                return nameCode.Groups[1].Value;
            }

            Match subTypeCode = ProductCode.Match(itemSubTypeName ?? "");
            if (subTypeCode.Success)
            {
                derivedHeuristically = true;
                return subTypeCode.Groups[1].Value;
            }

            derivedHeuristically = true;
            return (itemSubTypeName ?? "").Trim();
        }
    }
}
