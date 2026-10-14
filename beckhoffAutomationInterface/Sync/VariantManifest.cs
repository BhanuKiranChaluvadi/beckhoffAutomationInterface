using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;

namespace BeckhoffAutomationInterface.Sync
{
    /// <summary>
    /// TwinCAT project variants (TwinCAT 3.1.4024+ Variant Management), e.g. one variant per
    /// machine of a family. Source of truth: a "variants.xml" manifest next to the other
    /// manifests:
    /// <code>
    /// &lt;Variants DefineMode="Implicit"&gt;
    ///   &lt;Variant Name="Shark" /&gt;  &lt;Variant Name="SAS" /&gt;
    ///   &lt;Group Name="PCV_MODBUS"&gt;   &lt;Member&gt;Shark&lt;/Member&gt; &lt;/Group&gt;
    ///   &lt;Group Name="PCV_ETHERCAT"&gt; &lt;Member&gt;SAS&lt;/Member&gt;   &lt;/Group&gt;
    /// &lt;/Variants&gt;
    /// </code>
    /// DefineMode="Implicit": the PLC gets the active variant's name and the names of its
    /// groups as compiler defines, so a group works as a feature switch
    /// ({IF defined (PCV_MODBUS)}). Variant and group names therefore must be valid PLC
    /// identifiers.
    /// </summary>
    sealed class VariantManifest
    {
        public IReadOnlyList<string> Variants { get; }
        public IReadOnlyDictionary<string, IReadOnlyList<string>> Groups { get; } // group -> members, in file order

        public VariantManifest(IReadOnlyList<string> variants, IReadOnlyDictionary<string, IReadOnlyList<string>> groups)
        {
            Variants = variants;
            Groups = groups;
        }

        /// <summary>The XML that ITcSysManager14.ProjectVariantConfig takes (groups, then variants;
        /// the shape Beckhoff's Automation Interface sample uses).</summary>
        public string ToProjectVariantConfigXml()
        {
            var sb = new StringBuilder("<?xml version=\"1.0\"?><ProjectVariants>");
            foreach (KeyValuePair<string, IReadOnlyList<string>> group in Groups)
            {
                sb.Append("<Group><Name>").Append(group.Key).Append("</Name>");
                foreach (string member in group.Value)
                    sb.Append("<Member>").Append(member).Append("</Member>");
                sb.Append("</Group>");
            }
            foreach (string variant in Variants)
                sb.Append("<Variant><Name>").Append(variant).Append("</Name></Variant>");
            return sb.Append("</ProjectVariants>").ToString();
        }

        /// <summary>Reads a ProjectVariantConfig XML string (as returned by the project) back into
        /// a manifest-shaped model. Empty/absent config gives no variants.</summary>
        public static VariantManifest FromProjectVariantConfigXml(string xml)
        {
            if (string.IsNullOrWhiteSpace(xml))
                return new VariantManifest(new List<string>(), new Dictionary<string, IReadOnlyList<string>>());

            XElement root = XDocument.Parse(xml).Root;
            List<string> variants = root.Elements("Variant").Select(v => ((string)v.Element("Name") ?? "").Trim()).ToList();
            var groups = new Dictionary<string, IReadOnlyList<string>>();
            foreach (XElement g in root.Elements("Group"))
                groups[((string)g.Element("Name") ?? "").Trim()] = g.Elements("Member").Select(m => m.Value.Trim()).ToList();
            return new VariantManifest(variants, groups);
        }

        /// <summary>Same variants and same group memberships (order-insensitive, case-sensitive
        /// because the names become PLC defines).</summary>
        public bool SameAs(VariantManifest other) =>
            new HashSet<string>(Variants).SetEquals(other.Variants)
            && Groups.Count == other.Groups.Count
            && Groups.All(g => other.Groups.TryGetValue(g.Key, out IReadOnlyList<string> m) && new HashSet<string>(g.Value).SetEquals(m));
    }

    sealed class VariantManifestException : Exception
    {
        public IReadOnlyList<string> Problems { get; }

        public VariantManifestException(string path, IReadOnlyList<string> problems)
            : base(string.Format("{0} is invalid:{1}  {2}", path, Environment.NewLine, string.Join(Environment.NewLine + "  ", problems)))
        {
            Problems = problems;
        }
    }

    static class VariantManifestParser
    {
        static readonly Regex PlcIdentifier = new Regex(@"^[A-Za-z_][A-Za-z0-9_]*$");

        /// <summary>Null when the file does not exist (the project then has no variant
        /// behaviour at all). Throws VariantManifestException listing every problem.</summary>
        public static VariantManifest Parse(string manifestFilePath)
        {
            if (!File.Exists(manifestFilePath))
                return null;
            return Parse(XDocument.Load(manifestFilePath, LoadOptions.SetLineInfo), manifestFilePath);
        }

        internal static VariantManifest Parse(XDocument doc, string pathForMessages)
        {
            var problems = new List<string>();
            XElement root = doc.Root;
            if (root == null || root.Name.LocalName != "Variants")
                throw new VariantManifestException(pathForMessages, new[] { "root element must be <Variants>" });

            string mode = (string)root.Attribute("DefineMode") ?? "Implicit";
            if (mode != "Implicit")
                problems.Add(string.Format("line {0}: DefineMode '{1}' is not supported (use \"Implicit\")", Line(root), mode));

            var variants = new List<string>();
            var allNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (XElement v in root.Elements("Variant"))
            {
                string name = ((string)v.Attribute("Name") ?? "").Trim();
                if (CheckName(name, "variant", v, allNames, problems))
                    variants.Add(name);
            }
            if (variants.Count == 0 && problems.Count == 0)
                problems.Add("declares no <Variant>");

            var groups = new Dictionary<string, IReadOnlyList<string>>();
            foreach (XElement g in root.Elements("Group"))
            {
                string name = ((string)g.Attribute("Name") ?? "").Trim();
                if (!CheckName(name, "group", g, allNames, problems))
                    continue;
                var members = new List<string>();
                foreach (XElement m in g.Elements("Member"))
                {
                    string member = m.Value.Trim();
                    if (!variants.Contains(member))
                        problems.Add(string.Format("line {0}: group '{1}' member '{2}' is not a declared <Variant>", Line(m), name, member));
                    else if (members.Contains(member))
                        problems.Add(string.Format("line {0}: group '{1}' lists '{2}' twice", Line(m), name, member));
                    else
                        members.Add(member);
                }
                groups[name] = members;
            }

            if (problems.Count > 0)
                throw new VariantManifestException(pathForMessages, problems);
            return new VariantManifest(variants, groups);
        }

        static bool CheckName(string name, string kind, XElement element, HashSet<string> allNames, List<string> problems)
        {
            if (!PlcIdentifier.IsMatch(name))
            {
                problems.Add(string.Format("line {0}: {1} name '{2}' is not a valid PLC identifier (it becomes a compiler define)", Line(element), kind, name));
                return false;
            }
            if (!allNames.Add(name))
            {
                problems.Add(string.Format("line {0}: name '{1}' is used twice (variant and group names must be unique)", Line(element), name));
                return false;
            }
            return true;
        }

        static int Line(XObject node) => ((IXmlLineInfo)node).HasLineInfo() ? ((IXmlLineInfo)node).LineNumber : 0;
    }
}
