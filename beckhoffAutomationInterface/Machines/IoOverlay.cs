using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Xml;

namespace BeckhoffAutomationInterface.Machines
{
    /// <summary>
    /// Resolves a machine's io-devices.xml into one plain I/O tree (pure, unit-tested). A machine
    /// file can name a base file and change it:
    /// <code>
    /// &lt;IoTree Base="../Platform/io-devices.xml" PlcInstance="NFLite Instance"&gt;
    ///   &lt;Set     Path="/IoTree/Device[@Name='BH 1']/Box[@Name='Box 1']" Attribute="RevisionNo" Value="#x16" /&gt;
    ///   &lt;Replace Path="/IoTree/Adapter[@Name='A']"&gt; &lt;Adapter ... /&gt; &lt;/Replace&gt;
    ///   &lt;Add     Path="/IoTree"&gt; &lt;Device ... /&gt; &lt;/Add&gt;
    /// &lt;/IoTree&gt;
    /// </code>
    /// A base can have a base. Each Path must find exactly one node. PlcInstance replaces
    /// ${PLC_INSTANCE} in every attribute (only in the file of the machine). The result is the
    /// same text as Read-IoManifest in the PLC repository's Tools/build_checks.ps1 (OuterXml of
    /// an XmlDocument that does not keep whitespace), so a build stamp does not change when
    /// the tool that resolves the tree changes.
    /// </summary>
    static class IoOverlay
    {
        public const string InstancePlaceholder = "${PLC_INSTANCE}";

        /// <summary>The resolved tree. root: the folder a base must stay in (default: the
        /// parent of the machine folder, i.e. the machine family).</summary>
        public static XmlDocument Resolve(string file, string root = null) => Resolve(file, root, new List<string>());

        /// <summary>The resolved tree as text (what goes into the build source folder).</summary>
        public static string ResolveText(string file, string root = null) => Resolve(file, root).OuterXml;

        static XmlDocument Resolve(string file, string root, List<string> visited)
        {
            file = Path.GetFullPath(file);
            if (string.IsNullOrEmpty(root))
                root = Path.GetDirectoryName(Path.GetDirectoryName(file));
            root = Path.GetFullPath(root).TrimEnd('\\', '/') + Path.DirectorySeparatorChar;
            if (!file.StartsWith(root, StringComparison.OrdinalIgnoreCase) || visited.Contains(file, StringComparer.OrdinalIgnoreCase))
                throw new InvalidOperationException("I/O base must stay inside its machine family and must not form a cycle: " + file);

            XmlDocument input = Load(file);
            string baseFile = input.DocumentElement.GetAttribute("Base");
            if (string.IsNullOrEmpty(baseFile))
                return input;
            if (input.DocumentElement.Name != "IoTree")
                throw new InvalidOperationException("Expected IoTree in " + file);
            if (Path.IsPathRooted(baseFile))
                throw new InvalidOperationException("I/O Base must be a relative path: " + file);

            XmlDocument result = Resolve(Path.Combine(Path.GetDirectoryName(file), baseFile), root, visited.Concat(new[] { file }).ToList());
            foreach (XmlNode operation in input.DocumentElement.ChildNodes)
            {
                if (operation.NodeType != XmlNodeType.Element)
                    continue;
                var op = (XmlElement)operation;
                XmlNodeList matches = result.SelectNodes(op.GetAttribute("Path"));
                if (matches.Count != 1)
                    throw new InvalidOperationException("I/O override must match exactly one node: " + op.OuterXml);
                XmlNode node = matches[0];
                switch (op.Name)
                {
                    case "Set":
                        string attribute = op.GetAttribute("Attribute");
                        if (!(node is XmlElement target) || !target.HasAttribute(attribute))
                            throw new InvalidOperationException("I/O override attribute does not exist: " + attribute);
                        target.SetAttribute(attribute, op.GetAttribute("Value"));
                        break;
                    case "Replace":
                        List<XmlNode> elements = op.ChildNodes.Cast<XmlNode>().Where(n => n.NodeType == XmlNodeType.Element).ToList();
                        if (elements.Count != 1)
                            throw new InvalidOperationException("I/O Replace needs exactly one child element.");
                        node.ParentNode.ReplaceChild(result.ImportNode(elements[0], true), node);
                        break;
                    case "Add":
                        foreach (XmlNode child in op.ChildNodes)
                            if (child.NodeType == XmlNodeType.Element)
                                node.AppendChild(result.ImportNode(child, true));
                        break;
                    default:
                        throw new InvalidOperationException("Unknown I/O override operation: " + op.Name);
                }
            }

            if (visited.Count == 0)
            {
                string instance = input.DocumentElement.GetAttribute("PlcInstance");
                if (!string.IsNullOrEmpty(instance))
                    foreach (XmlAttribute a in result.SelectNodes("//@*"))
                        a.Value = a.Value.Replace(InstancePlaceholder, instance);
                if (result.OuterXml.Contains(InstancePlaceholder))
                    throw new InvalidOperationException("I/O overlay needs PlcInstance: " + file);
            }
            return result;
        }

        /// <summary>As PowerShell's [xml] cast of Get-Content -Raw -Encoding UTF8: no BOM, no
        /// insignificant whitespace.</summary>
        static XmlDocument Load(string file)
        {
            var doc = new XmlDocument { PreserveWhitespace = false, XmlResolver = null };
            doc.LoadXml(File.ReadAllText(file, Encoding.UTF8));
            return doc;
        }
    }
}
