using System;
using System.Collections.Generic;
using System.Linq;

namespace BeckhoffAutomationInterface.Sync
{
    /// <summary>One I/O tree item that exists in some project variants only.</summary>
    sealed class IoVariantItem
    {
        public string TreePath { get; }                 // e.g. "TIID^Device 1 (EtherCAT)^Term 1 (EK1100)^Term 3 (EL2008)"
        public IReadOnlyList<string> Variants { get; }  // the variants that HAVE the item

        public IoVariantItem(string treePath, IReadOnlyList<string> variants)
        {
            TreePath = treePath;
            Variants = variants;
        }

        public bool EnabledIn(string variant) => Variants.Contains(variant, StringComparer.Ordinal);
    }

    /// <summary>
    /// Rules for the Variants="A,B" attribute in io-devices.xml (pure, unit-tested):
    /// names must be declared in variants.xml, the list must not be empty, and a box/terminal
    /// cannot exist in a variant where its parent does not.
    /// </summary>
    static class IoVariantRules
    {
        public static List<string> Validate(IReadOnlyList<IoDeviceSpec> devices, VariantManifest variants)
        {
            var problems = new List<string>();
            foreach (IoDeviceSpec device in devices)
            {
                string path = "TIID^" + device.Name;
                CheckOne(path, device.Variants, null, variants, problems);
                IReadOnlyList<string> effective = device.Variants;
                foreach (IoNodeSpec child in device.Children)
                    CheckNode(child, path, effective, variants, problems);
            }
            return problems;
        }

        static void CheckNode(IoNodeSpec node, string parentPath, IReadOnlyList<string> parentEffective,
                              VariantManifest variants, List<string> problems)
        {
            string path = parentPath + "^" + node.Name;
            CheckOne(path, node.Variants, parentEffective, variants, problems);
            IReadOnlyList<string> effective = node.Variants ?? parentEffective;
            foreach (IoNodeSpec child in node.Children)
                CheckNode(child, path, effective, variants, problems);
        }

        static void CheckOne(string path, IReadOnlyList<string> own, IReadOnlyList<string> parentEffective,
                             VariantManifest variants, List<string> problems)
        {
            if (own == null)
                return;
            if (variants == null)
            {
                problems.Add(string.Format("'{0}' has a Variants attribute, but there is no variants.xml.", path));
                return;
            }
            if (own.Count == 0)
            {
                problems.Add(string.Format("'{0}' has an empty Variants attribute (it would exist in no variant).", path));
                return;
            }
            foreach (string v in own.Where(v => !variants.Variants.Contains(v, StringComparer.Ordinal)))
                problems.Add(string.Format("'{0}': variant '{1}' is not declared in variants.xml ({2}).",
                    path, v, string.Join(", ", variants.Variants)));
            if (parentEffective != null)
                foreach (string v in own.Where(v => !parentEffective.Contains(v, StringComparer.Ordinal)))
                    problems.Add(string.Format("'{0}' is in variant '{1}', but its parent is not.", path, v));
        }

        /// <summary>Every item with its own Variants attribute, as an I/O tree path. Items without
        /// the attribute follow their parent in TwinCAT automatically and need no per-variant setting.</summary>
        public static List<IoVariantItem> ItemsWithVariants(IReadOnlyList<IoDeviceSpec> devices)
        {
            var items = new List<IoVariantItem>();
            foreach (IoDeviceSpec device in devices)
            {
                string path = "TIID^" + device.Name;
                if (device.Variants != null)
                    items.Add(new IoVariantItem(path, device.Variants));
                foreach (IoNodeSpec child in device.Children)
                    Collect(child, path, items);
            }
            return items;
        }

        static void Collect(IoNodeSpec node, string parentPath, List<IoVariantItem> items)
        {
            string path = parentPath + "^" + node.Name;
            if (node.Variants != null)
                items.Add(new IoVariantItem(path, node.Variants));
            foreach (IoNodeSpec child in node.Children)
                Collect(child, path, items);
        }
    }
}
