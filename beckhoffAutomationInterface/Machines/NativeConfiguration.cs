using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Xml;
using System.Xml.Linq;

namespace BeckhoffAutomationInterface.Machines
{
    /// <summary>
    /// A native TwinCAT configuration kept in the PLC repository (Machines/&lt;family&gt;/native/
    /// configuration.xml): the I/O tree, data types, image data, NC/motion, safety references and
    /// mappings of a reviewed project, without its PLC. Port of the PLC repository's
    /// Tools/native_configuration.py, with the same rules:
    ///   Capture - a one-time import from a reviewed project (external .xti files resolved).
    ///   Apply   - puts the sections into a generated, CLOSED .tsproj (one named PLC). Only the PLC
    ///             instance prefix changes; device names and native ids stay.
    ///   Links   - the mappings as a links.xml (native VarLinks), applied after the PLC compile.
    /// DTDs and entities are refused. Comments and processing instructions are ignored, and
    /// output is indented with two spaces, like Python's ElementTree.
    /// </summary>
    static class NativeConfiguration
    {
        public const string Root = "NativeConfiguration";
        public static readonly string[] Sections = { "DataTypes", "ImageDatas", "Io", "Motion", "Safety", "Mappings" };
        static readonly string[] ProjectSections = { "Io", "Motion", "Safety" };
        static readonly string[] CaptureSections = { "DataTypes", "ImageDatas", "Project/Io", "Project/Motion", "Project/Safety", "Mappings" };

        // ------------------------------------------------------------------ apply

        /// <summary>Applies the configuration to a closed .tsproj. Keeps the original as
        /// &lt;tsproj&gt;.before-native (refused when that file exists).</summary>
        public static Dictionary<string, object> Apply(string configuration, string project)
        {
            configuration = Path.GetFullPath(configuration);
            project = Path.GetFullPath(project);
            XDocument target = Load(project);
            string plc = ApplyTo(Load(configuration), target);
            string backup = project + ".before-native";
            if (File.Exists(backup))
                throw new InvalidOperationException("Backup already exists: " + backup);
            File.WriteAllBytes(backup, File.ReadAllBytes(project));
            Save(target, project);
            return new Dictionary<string, object>
            {
                ["status"] = "Applied", ["configurationSha256"] = Sha256(configuration),
                ["projectSha256"] = Sha256(project), ["plc"] = plc,
            };
        }

        /// <summary>Puts each section into the project (pure): Io, Motion and Safety under
        /// Project, the others under the root. A section the project has is replaced (the new one
        /// goes last). Returns the PLC name.</summary>
        internal static string ApplyTo(XDocument native, XDocument target)
        {
            XElement n = native.Root;
            if (n.Name.LocalName != Root || (string)n.Attribute("schemaVersion") != "1")
                throw new InvalidOperationException("Unsupported native configuration.");
            List<string> found = n.Elements().Select(e => e.Name.LocalName).OrderBy(s => s, StringComparer.Ordinal).ToList();
            if (!found.SequenceEqual(Sections.OrderBy(s => s, StringComparer.Ordinal)))
                throw new InvalidOperationException("Native configuration must contain each supported section exactly once.");
            List<XElement> plcs = Path3(target.Root, "Project", "Plc", "Project").ToList();
            if (plcs.Count != 1 || string.IsNullOrEmpty((string)plcs[0].Attribute("Name")))
                throw new InvalidOperationException("Generated project must have exactly one named PLC.");
            string plc = (string)plcs[0].Attribute("Name");
            string oldPrefix = (string)n.Attribute("PlcPrefix");
            if (string.IsNullOrEmpty(oldPrefix) || !oldPrefix.StartsWith("TIPC^", StringComparison.Ordinal))
                throw new InvalidOperationException("Missing native PLC prefix.");
            string newPrefix = Prefix(plc);
            foreach (XElement section in n.Elements())
            {
                var element = new XElement(section);
                Rebase(element, oldPrefix, newPrefix);
                XElement parent = ProjectSections.Contains(section.Name.LocalName) ? target.Root.Element("Project") : target.Root;
                if (parent == null)
                    throw new InvalidOperationException("Generated project has no <Project> element.");
                parent.Element(section.Name)?.Remove();
                parent.Add(element);
            }
            return plc;
        }

        // ------------------------------------------------------------------ simulated NC axes

        /// <summary>The NC axes (Motion/NC/Axis) of a configuration or a .tsproj.</summary>
        public static List<XElement> Axes(XDocument doc) =>
            doc.Descendants("Axis").Where(a => a.Parent?.Name == "NC" && a.Parent.Parent?.Name == "Motion").ToList();

        /// <summary>The axes that have a SimulationMode attribute (any value).</summary>
        public static int SimulatedAxes(XDocument doc) => Axes(doc).Count(a => a.Attribute("SimulationMode") != null);

        /// <summary>For the simulation copy only: SimulationMode="1" on each NC axis (simulated drive
        /// and encoder; PvSimulation alone does not do this). Returns the number of axes.</summary>
        public static int SimulateAxes(XDocument doc)
        {
            List<XElement> axes = Axes(doc);
            foreach (XElement axis in axes)
                axis.SetAttributeValue("SimulationMode", "1");
            return axes.Count;
        }

        // ------------------------------------------------------------------ links

        /// <summary>Writes the mappings as links.xml for PLC 'plc'. Returns the number of links.</summary>
        public static int Links(string configuration, string plc, string output)
        {
            if (string.Equals(Path.GetFullPath(configuration), Path.GetFullPath(output), StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Link output must not replace the native configuration.");
            XDocument links = LinksOf(Load(configuration), plc);
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(output)));
            Save(links, output);
            return CountLinks(links);
        }

        /// <summary>The links.xml text (pure).</summary>
        public static string LinksText(string configuration, string plc) => Text(LinksOf(Load(configuration), plc));

        /// <summary>VarLinks with a copy of each Mappings/OwnerA, the PLC prefix rebased (pure).</summary>
        internal static XDocument LinksOf(XDocument native, string plc)
        {
            string oldPrefix = (string)native.Root.Attribute("PlcPrefix");
            if (string.IsNullOrEmpty(oldPrefix) || string.IsNullOrEmpty(plc) || plc.Contains("^"))
                throw new InvalidOperationException("Invalid PLC prefix/name.");
            var result = new XElement("VarLinks");
            foreach (XElement owner in Path3(native.Root, "Mappings", "OwnerA"))
            {
                var item = new XElement(owner);
                Rebase(item, oldPrefix, Prefix(plc));
                result.Add(item);
            }
            var doc = new XDocument(result);
            if (CountLinks(doc) == 0)
                throw new InvalidOperationException("Native configuration has no links.");
            return doc;
        }

        public static int CountLinks(XDocument doc) => doc.Descendants("Link").Count();

        // ------------------------------------------------------------------ capture

        /// <summary>One-time import from a reviewed project into a NEW folder: configuration.xml
        /// (each external .xti reference resolved) and provenance.json.</summary>
        public static Dictionary<string, object> Capture(string project, string output)
        {
            project = Path.GetFullPath(project);
            output = Path.GetFullPath(output);
            if (Directory.Exists(output) || File.Exists(output))
                throw new InvalidOperationException("Use a new capture directory.");
            var files = new List<KeyValuePair<string, string>> { new KeyValuePair<string, string>(project, Sha256(project)) };
            XDocument configuration = CaptureFrom(Load(project), name => ResolveXti(project, name), files);
            Directory.CreateDirectory(output);
            string file = Path.Combine(output, "configuration.xml");
            Save(configuration, file);
            var record = new Dictionary<string, object>
            {
                ["schemaVersion"] = 1, ["sourceProject"] = project, ["sourceFiles"] = files,
                ["configurationSha256"] = Sha256(file), ["physicalAcceptance"] = false,
            };
            // CRLF, as the Python tool wrote it on Windows (text mode).
            File.WriteAllText(Path.Combine(output, "provenance.json"), (Json(record, "") + "\n").Replace("\n", "\r\n"), new UTF8Encoding(false));
            return record;
        }

        /// <summary>The capture itself (pure except for 'resolve', which returns the .xti file of a
        /// File="..." reference: its path and document). Adds each resolved file to 'files'.</summary>
        internal static XDocument CaptureFrom(XDocument source, Func<string, (string Path, XDocument Doc)> resolve,
            List<KeyValuePair<string, string>> files)
        {
            List<XElement> plc = Path3(source.Root, "Project", "Plc", "Project").ToList();
            if (plc.Count != 1)
                throw new InvalidOperationException("Capture requires exactly one PLC.");
            string name = (string)plc[0].Attribute("Name");
            var root = new XElement(Root, new XAttribute("schemaVersion", "1"),
                new XAttribute("PlcPrefix", string.Format("TIPC^{0}^{0} Instance", name)));
            foreach (string xpath in CaptureSections)
            {
                XElement element = Path3(source.Root, xpath.Split('/')).FirstOrDefault();
                if (element == null)
                    throw new InvalidOperationException("Missing native section: " + xpath);
                root.Add(new XElement(element));
            }
            // Resolve decomposed I/O once. No external path is needed in a clean build.
            foreach (XElement parent in root.DescendantsAndSelf().ToList())
                foreach (XElement child in parent.Elements().ToList())
                {
                    string file = (string)child.Attribute("File");
                    if (string.IsNullOrEmpty(file))
                        continue;
                    (string path, XDocument external) = resolve(file);
                    List<XElement> replacements = external.Root.Elements(child.Name).ToList();
                    if (replacements.Count != 1)
                        throw new InvalidOperationException("Unexpected XTI structure: " + file);
                    var replacement = new XElement(replacements[0]);
                    // TwinCAT resolves this placeholder from the XTI basename. Leaving it literal
                    // creates duplicate device names and discards all their links.
                    XElement label = replacement.Element("Name");
                    if (label != null && LeadingText(label) == "__FILENAME__")
                        SetLeadingText(label, Path.GetFileNameWithoutExtension(path));
                    // PortABoxInfo and other local settings override the external template.
                    MergeReference(replacement, child);
                    child.ReplaceWith(replacement);
                    if (!files.Any(f => f.Key == path))
                        files.Add(new KeyValuePair<string, string>(path, Sha256(path)));
                }
            if (root.DescendantsAndSelf().Any(e => e.Attribute("File") != null))
                throw new InvalidOperationException("Nested external references need explicit resolution before capture.");
            return new XDocument(root);
        }

        static (string Path, XDocument Doc) ResolveXti(string project, string name)
        {
            List<string> matches = Path.GetFileName(name) == name
                ? Directory.EnumerateFiles(Path.GetDirectoryName(project), "*", SearchOption.AllDirectories)
                    .Where(f => string.Equals(Path.GetFileName(f), name, StringComparison.OrdinalIgnoreCase)).ToList()
                : new List<string>();
            if (matches.Count != 1 || Path.GetExtension(matches[0]).ToLowerInvariant() != ".xti")
                throw new InvalidOperationException("Unresolved or ambiguous native reference: " + name);
            return (matches[0], Load(matches[0]));
        }

        /// <summary>The reference owns local ids and topology; the XTI owns the device contents.</summary>
        internal static void MergeReference(XElement target, XElement overlay)
        {
            foreach (XAttribute a in overlay.Attributes().Where(a => !a.IsNamespaceDeclaration && a.Name.LocalName != "File"))
                target.SetAttributeValue(a.Name, a.Value);
            foreach (XElement child in overlay.Elements())
            {
                List<XElement> matches = target.Elements(child.Name).ToList();
                if (matches.Count > 1)
                    throw new InvalidOperationException("Ambiguous XTI override: " + child.Name.LocalName);
                if (matches.Count == 0)
                    target.Add(new XElement(child));
                else
                {
                    MergeReference(matches[0], child);
                    string text = LeadingText(child);
                    if (text != null && text.Trim().Length > 0)
                        SetLeadingText(matches[0], text);
                }
            }
        }

        // ------------------------------------------------------------------ XML helpers

        internal static string Prefix(string plc) => string.Format("TIPC^{0}^{0} Instance", plc);

        /// <summary>Only the PLC instance prefix of attribute values changes.</summary>
        static void Rebase(XElement element, string oldPrefix, string newPrefix)
        {
            foreach (XElement e in element.DescendantsAndSelf())
                foreach (XAttribute a in e.Attributes().Where(a => !a.IsNamespaceDeclaration))
                    if (a.Value == oldPrefix || a.Value.StartsWith(oldPrefix + "^", StringComparison.Ordinal))
                        a.Value = newPrefix + a.Value.Substring(oldPrefix.Length);
        }

        /// <summary>Elements by a path of child names (like ElementTree's find with 'a/b').</summary>
        static IEnumerable<XElement> Path3(XElement start, params string[] names)
        {
            IEnumerable<XElement> current = new[] { start };
            foreach (string name in names)
                current = current.SelectMany(e => e.Elements(name));
            return current;
        }

        /// <summary>ElementTree's 'text': the text before the first child element (null when none).</summary>
        internal static string LeadingText(XElement e)
        {
            var sb = new StringBuilder();
            bool any = false;
            foreach (XNode node in e.Nodes())
            {
                if (node is XElement)
                    break;
                if (node is XText t)
                {
                    sb.Append(t.Value);
                    any = true;
                }
            }
            return any ? sb.ToString() : null;
        }

        static void SetLeadingText(XElement e, string text)
        {
            foreach (XText t in e.Nodes().TakeWhile(n => !(n is XElement)).OfType<XText>().ToList())
                t.Remove();
            e.AddFirst(new XText(text));
        }

        /// <summary>Reads XML with no DTD and no entities (refused), without comments and
        /// processing instructions. Whitespace-only text between elements is dropped (it is
        /// indentation); the text of a leaf element stays as it is.</summary>
        internal static XDocument Load(string path) => Parse(File.ReadAllBytes(path), path);

        internal static XDocument Parse(byte[] data, string name)
        {
            string upper = Encoding.ASCII.GetString(data).ToUpperInvariant();
            if (upper.Contains("<!DOCTYPE") || upper.Contains("<!ENTITY"))
                throw new InvalidOperationException("DTD/entity not permitted: " + name);
            var settings = new XmlReaderSettings
            {
                DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null,
                IgnoreComments = true, IgnoreProcessingInstructions = true,
            };
            XDocument doc;
            using (var stream = new MemoryStream(data))
            using (XmlReader reader = XmlReader.Create(stream, settings))
                doc = XDocument.Load(reader, LoadOptions.PreserveWhitespace);
            foreach (XElement e in doc.Root.DescendantsAndSelf().Where(e => e.HasElements))
                foreach (XText t in e.Nodes().OfType<XText>().Where(t => t.Value.Trim().Length == 0).ToList())
                    t.Remove();
            return doc;
        }

        /// <summary>UTF-8 without BOM (see Text).</summary>
        internal static void Save(XDocument doc, string path) =>
            File.WriteAllText(path, Text(doc), new UTF8Encoding(false));

        /// <summary>The XML exactly as the Python tool wrote it on Windows (ElementTree.indent with
        /// two spaces, then ElementTree.write in text mode): the declaration
        /// &lt;?xml version='1.0' encoding='utf-8'?&gt;, CRLF line ends, no line end after the root,
        /// "&lt;a /&gt;" for an empty element, CDATA written as escaped text, namespace declarations
        /// before the other attributes. So both tools give the same bytes.</summary>
        internal static string Text(XDocument doc)
        {
            var sb = new StringBuilder("<?xml version='1.0' encoding='utf-8'?>\n");
            WriteElement(sb, doc.Root, 0);
            return sb.ToString().Replace("\n", "\r\n");
        }

        static void WriteElement(StringBuilder sb, XElement e, int level)
        {
            sb.Append('<').Append(QName(e, e.Name, element: true));
            foreach (XAttribute a in e.Attributes().Where(a => a.IsNamespaceDeclaration)
                         .OrderBy(a => a.Name.Namespace == XNamespace.None ? "" : a.Name.LocalName, StringComparer.Ordinal))
                sb.Append(a.Name.Namespace == XNamespace.None ? " xmlns" : " xmlns:" + a.Name.LocalName).Append("=\"").Append(EscapeAttribute(a.Value)).Append('"');
            foreach (XAttribute a in e.Attributes().Where(a => !a.IsNamespaceDeclaration))
                sb.Append(' ').Append(QName(e, a.Name, element: false)).Append("=\"").Append(EscapeAttribute(a.Value)).Append('"');
            if (!e.HasElements)
            {
                string text = string.Concat(e.Nodes().OfType<XText>().Select(t => t.Value));
                if (text.Length == 0)
                    sb.Append(" />");
                else
                    sb.Append('>').Append(EscapeText(text)).Append("</").Append(QName(e, e.Name, element: true)).Append('>');
                return;
            }
            sb.Append('>');
            string inner = "\n" + new string(' ', 2 * (level + 1)), outer = "\n" + new string(' ', 2 * level);
            List<XNode> nodes = e.Nodes().Where(n => n is XElement || n is XText).ToList();
            int first = nodes.FindIndex(n => n is XElement);
            string leading = string.Concat(nodes.Take(first).OfType<XText>().Select(t => t.Value));
            sb.Append(leading.Trim().Length == 0 ? inner : EscapeText(leading));
            List<int> elements = Enumerable.Range(0, nodes.Count).Where(i => nodes[i] is XElement).ToList();
            for (int k = 0; k < elements.Count; k++)
            {
                WriteElement(sb, (XElement)nodes[elements[k]], level + 1);
                int end = k + 1 < elements.Count ? elements[k + 1] : nodes.Count;
                string tail = string.Concat(nodes.Skip(elements[k] + 1).Take(end - elements[k] - 1).OfType<XText>().Select(t => t.Value));
                sb.Append(tail.Trim().Length > 0 ? EscapeText(tail) : k + 1 < elements.Count ? inner : outer);
            }
            sb.Append("</").Append(QName(e, e.Name, element: true)).Append('>');
        }

        static string QName(XElement scope, XName name, bool element)
        {
            if (name.Namespace == XNamespace.None)
                return name.LocalName;
            string prefix = scope.GetPrefixOfNamespace(name.Namespace);
            if (prefix == null)
                return element ? name.LocalName : throw new InvalidOperationException("No prefix for namespace " + name.Namespace);
            return prefix + ":" + name.LocalName;
        }

        static string EscapeText(string s) => s.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;");

        static string EscapeAttribute(string s) =>
            EscapeText(s).Replace("\"", "&quot;").Replace("\r", "&#13;").Replace("\n", "&#10;").Replace("\t", "&#09;");

        internal static string Sha256(string file)
        {
            using (var sha = SHA256.Create())
            using (FileStream s = File.OpenRead(file))
                return string.Concat(sha.ComputeHash(s).Select(b => b.ToString("x2")));
        }

        /// <summary>JSON with two-space indent (like Python's json.dumps(indent=2)). Values: string,
        /// int, bool, Dictionary&lt;string, object&gt; and List&lt;KeyValuePair&lt;string, string&gt;&gt; (an ordered object).</summary>
        internal static string Json(object value, string indent)
        {
            string inner = indent + "  ";
            switch (value)
            {
                case string s: return JsonString(s);
                case bool b: return b ? "true" : "false";
                case int i: return i.ToString(System.Globalization.CultureInfo.InvariantCulture);
                case Dictionary<string, object> d:
                    return "{\n" + string.Join(",\n", d.Select(p => inner + Json(p.Key, inner) + ": " + Json(p.Value, inner))) + "\n" + indent + "}";
                case List<KeyValuePair<string, string>> l:
                    return "{\n" + string.Join(",\n", l.Select(p => inner + Json(p.Key, inner) + ": " + Json(p.Value, inner))) + "\n" + indent + "}";
                default: throw new ArgumentException("Unsupported JSON value: " + value?.GetType().Name);
            }
        }

        /// <summary>A JSON string as Python's json.dumps writes it (ensure_ascii).</summary>
        internal static string JsonString(string s)
        {
            var sb = new StringBuilder("\"");
            foreach (char c in s)
            {
                switch (c)
                {
                    case '"': sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    case '\b': sb.Append("\\b"); break;
                    case '\f': sb.Append("\\f"); break;
                    default:
                        if (c < 0x20 || c > 0x7E && c != 0x7F)
                            sb.Append("\\u").Append(((int)c).ToString("x4"));
                        else
                            sb.Append(c);
                        break;
                }
            }
            return sb.Append('"').ToString();
        }
    }

    /// <summary>'native capture|apply|links': the command line of NativeConfiguration (the same
    /// arguments as Tools/native_configuration.py). Prints the result as JSON. Exit 0 = done,
    /// 1 = refused or failed, 2 = wrong command line.</summary>
    static class NativeCommands
    {
        public const string Usage =
            "native capture --project <tsproj> --output <new folder>\n" +
            "  native apply   --configuration <configuration.xml> --project <closed tsproj>\n" +
            "  native links   --configuration <configuration.xml> --plc <PLC name> --output <links.xml>";

        public static int Run(string[] args)
        {
            Runtime.CommandArgs a;
            string mode = args.Length > 1 ? args[1] : null;
            string[] required = mode == "capture" ? new[] { "--project", "--output" }
                              : mode == "apply" ? new[] { "--configuration", "--project" }
                              : mode == "links" ? new[] { "--configuration", "--plc", "--output" } : null;
            try
            {
                if (required == null)
                    throw new ArgumentException("'native' needs a mode: capture, apply or links.");
                a = Runtime.CommandArgs.Parse(args.Skip(1).ToArray(), required, new string[0]);
                if (a.Positional != null)
                    throw new ArgumentException("unrecognized argument: " + a.Positional);
                string missing = required.FirstOrDefault(o => a.Get(o) == null);
                if (missing != null)
                    throw new ArgumentException(missing + " is required.");
            }
            catch (ArgumentException ex)
            {
                Console.Error.WriteLine("ERROR: " + ex.Message);
                Console.Error.WriteLine("Usage: beckhoffAutomationInterface " + Usage);
                return 2;
            }
            try
            {
                object result = mode == "capture" ? NativeConfiguration.Capture(a.Get("--project"), a.Get("--output"))
                    : mode == "apply" ? NativeConfiguration.Apply(a.Get("--configuration"), a.Get("--project"))
                    : (object)new Dictionary<string, object> { ["links"] = NativeConfiguration.Links(a.Get("--configuration"), a.Get("--plc"), a.Get("--output")) };
                Console.WriteLine(NativeConfiguration.Json(result, ""));
                return 0;
            }
            catch (Exception ex) when (ex is InvalidOperationException || ex is IOException || ex is XmlException || ex is UnauthorizedAccessException)
            {
                Console.Error.WriteLine("ERROR: " + ex.Message);
                return 1;
            }
        }
    }
}
