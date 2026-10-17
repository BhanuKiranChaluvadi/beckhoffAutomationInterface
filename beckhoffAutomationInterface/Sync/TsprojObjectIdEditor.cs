using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Xml.Linq;

namespace BeckhoffAutomationInterface.Sync
{
    enum SafetyObjectKind { Box, Module }

    /// <summary>One EtherCAT object id that a TwinSAFE project refers to: a box (the safety
    /// logic terminal, from TargetSystemConfig.xml) or a module of a box (an FSoE connection,
    /// from an alias device file "&lt;box name&gt; - &lt;module name&gt;.sds").</summary>
    sealed class SafetyObjectId
    {
        public SafetyObjectKind Kind { get; }
        public string BoxName { get; }
        public string ModuleName { get; } // null for a box
        public int Id { get; }             // box: the Box Id; module: the full object id (0x0309xxxx)
        public string Source { get; }      // the safety file that asks for it

        public SafetyObjectId(SafetyObjectKind kind, string boxName, string moduleName, int id, string source)
        {
            Kind = kind;
            BoxName = boxName;
            ModuleName = moduleName;
            Id = id;
            Source = source;
        }

        public override string ToString() => Kind == SafetyObjectKind.Box
            ? string.Format("Box '{0}' Id {1} ({2})", BoxName, Id, Source)
            : string.Format("Module '{0}' of '{1}' Id #x{2:x8} ({3})", ModuleName, BoxName, Id, Source);
    }

    /// <summary>
    /// Reads the EtherCAT object ids from the files of a TwinSAFE project. TwinCAT keeps them as
    /// decimal numbers: 0x0302nnnn = box nnnn (TargetSystemObjectId in TargetSystemConfig.xml),
    /// 0x0309nnnn = a module (ObjectId of a safety alias device). Spike S3.1 (BeckhoffLib
    /// tasks/2026-10-16-plc-release): when the ids in the .tsproj differ, the safety project is
    /// added with no error, but the logic terminal gets no Standard In/Out channels.
    /// </summary>
    static class SafetyObjectIds
    {
        public const int BoxObjectType = 0x0302;
        public const int ModuleObjectType = 0x0309;

        /// <summary>The ids the safety files ask for. Throws InvalidOperationException listing
        /// every file that cannot be read this way.</summary>
        public static List<SafetyObjectId> Read(string targetSystemConfigPath, IEnumerable<string> aliasDeviceFiles)
        {
            var ids = new List<SafetyObjectId>();
            var problems = new List<string>();

            XElement tsc = XDocument.Load(targetSystemConfigPath).Root;
            string boxName = ((string)tsc?.Element("TargetSystemObjectName"))?.Trim();
            string objectIdText = ((string)tsc?.Element("TargetSystemObjectId"))?.Trim();
            if (string.IsNullOrEmpty(boxName) || !TryParseId(objectIdText, out int objectId))
                problems.Add(string.Format("{0}: no TargetSystemObjectName/TargetSystemObjectId", targetSystemConfigPath));
            else if (objectId >> 16 != BoxObjectType)
                problems.Add(string.Format("{0}: TargetSystemObjectId #x{1:x8} is not an EtherCAT box (#x0302nnnn)", targetSystemConfigPath, objectId));
            else
                ids.Add(new SafetyObjectId(SafetyObjectKind.Box, boxName, null, objectId & 0xFFFF, Path.GetFileName(targetSystemConfigPath)));

            foreach (string file in aliasDeviceFiles)
            {
                string text = (string)XDocument.Load(file).Root?.Element("SafetyAliasDevice")?.Element("ObjectId");
                if (text == null || !TryParseId(text.Trim(), out int id) || id == 0)
                    continue; // a standard alias device, or the logic terminal itself (ObjectId 0)
                string name = Path.GetFileNameWithoutExtension(file);
                int type = id >> 16;
                if (type == BoxObjectType)
                    ids.Add(new SafetyObjectId(SafetyObjectKind.Box, name, null, id & 0xFFFF, Path.GetFileName(file)));
                else if (type == ModuleObjectType)
                {
                    int split = name.LastIndexOf(" - ", StringComparison.Ordinal);
                    if (split <= 0)
                        problems.Add(string.Format("{0}: module id #x{1:x8}, but the file name is not '<box> - <module>.sds'", file, id));
                    else
                        ids.Add(new SafetyObjectId(SafetyObjectKind.Module, name.Substring(0, split), name.Substring(split + 3), id, Path.GetFileName(file)));
                }
                else
                    problems.Add(string.Format("{0}: ObjectId #x{1:x8} is not a box (#x0302nnnn) or a module (#x0309nnnn)", file, id));
            }

            if (problems.Count > 0)
                throw new InvalidOperationException("the safety project's object ids cannot be read:\n    " + string.Join("\n    ", problems));
            return ids;
        }

        static bool TryParseId(string text, out int id)
        {
            bool ok = long.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out long value) && value >= 0 && value <= uint.MaxValue;
            id = ok ? unchecked((int)(uint)value) : 0;
            return ok;
        }
    }

    sealed class ObjectIdEditResult
    {
        public List<string> Applied { get; } = new List<string>();
        public List<string> Problems { get; } = new List<string>();
        public bool Changed => Applied.Count > 0;
    }

    /// <summary>
    /// Gives the boxes and modules of the .tsproj the object ids that a TwinSAFE project refers
    /// to (see SafetyObjectIds). Box and module ids have no Automation Interface path
    /// (ConsumeXml with an ObjectId does not change it, spike S3.1), so this edits the project
    /// file. The boxes and modules are found by name. When another box (module) already has
    /// the id, the two swap ids. Box ids are also in the PortABoxInfo-style attributes
    /// ("#xPP00nnnn": port PP of box nnnn); these follow the change. Idempotent: a second run
    /// changes nothing. MUST be called while the project is NOT open in Visual Studio.
    /// </summary>
    static class TsprojObjectIdEditor
    {
        /// <summary>dryRun: report what would change, write nothing. The file is not written
        /// when there is a problem.</summary>
        public static ObjectIdEditResult Apply(string tsprojPath, IReadOnlyList<SafetyObjectId> required, bool dryRun = false)
        {
            XDocument doc = XDocument.Load(tsprojPath, LoadOptions.PreserveWhitespace);
            ObjectIdEditResult result = Edit(doc, required);
            if (!dryRun && result.Problems.Count == 0)
                TsprojDataTypePool.SaveIfChanged(tsprojPath, doc, result.Changed);
            return result;
        }

        /// <summary>The XML transform itself (changes doc in place).</summary>
        internal static ObjectIdEditResult Edit(XDocument doc, IReadOnlyList<SafetyObjectId> required)
        {
            var result = new ObjectIdEditResult();
            List<XElement> boxes = doc.Descendants("Box").Where(b => b.Attribute("Id") != null).ToList();
            Dictionary<XElement, int> originalBoxIds = boxes.ToDictionary(b => b, BoxId);

            var assignedBoxes = new Dictionary<XElement, SafetyObjectId>();
            foreach (SafetyObjectId want in required.Where(r => r.Kind == SafetyObjectKind.Box))
            {
                XElement box = FindBox(boxes, want, result);
                if (box == null)
                    continue;
                XElement holder = boxes.FirstOrDefault(b => b != box && BoxId(b) == want.Id);
                if (!CanAssign(box, holder, want, assignedBoxes, result))
                    continue;
                int current = BoxId(box);
                assignedBoxes[box] = want;
                if (current == want.Id)
                    continue;
                if (holder != null)
                    holder.SetAttributeValue("Id", current.ToString(CultureInfo.InvariantCulture));
                box.SetAttributeValue("Id", want.Id.ToString(CultureInfo.InvariantCulture));
                result.Applied.Add(string.Format("Box '{0}': Id {1} -> {2}{3}", want.BoxName, current, want.Id,
                    holder == null ? "" : string.Format(" ('{0}': {1} -> {2})", (string)holder.Element("Name"), want.Id, current)));
            }
            RenumberBoxReferences(doc, boxes.Where(b => BoxId(b) != originalBoxIds[b]).ToDictionary(b => originalBoxIds[b], BoxId));

            List<XElement> modules = doc.Descendants("Module").Where(m => m.Attribute("Id") != null).ToList();
            var assignedModules = new Dictionary<XElement, SafetyObjectId>();
            foreach (SafetyObjectId want in required.Where(r => r.Kind == SafetyObjectKind.Module))
            {
                XElement box = FindBox(boxes, want, result);
                if (box == null)
                    continue;
                List<XElement> named = modules.Where(m => m.Ancestors("Box").FirstOrDefault() == box && (string)m.Element("Name") == want.ModuleName).ToList();
                if (named.Count != 1)
                {
                    result.Problems.Add(string.Format("{0}: {1} module(s) named '{2}' in box '{3}' (expected 1)", want.Source, named.Count, want.ModuleName, want.BoxName));
                    continue;
                }
                XElement module = named[0];
                XElement holder = modules.FirstOrDefault(m => m != module && ModuleId(m) == want.Id);
                if (!CanAssign(module, holder, want, assignedModules, result))
                    continue;
                int current = ModuleId(module);
                assignedModules[module] = want;
                if (current == want.Id)
                    continue;
                if (holder != null)
                    holder.SetAttributeValue("Id", HexId(current));
                module.SetAttributeValue("Id", HexId(want.Id));
                result.Applied.Add(string.Format("Module '{0}' of '{1}': Id {2} -> {3}{4}", want.ModuleName, want.BoxName, HexId(current), HexId(want.Id),
                    holder == null ? "" : string.Format(" ('{0}' of '{1}': {2} -> {3})", (string)holder.Element("Name"),
                        (string)holder.Ancestors("Box").FirstOrDefault()?.Element("Name"), HexId(want.Id), HexId(current))));
            }
            return result;
        }

        static XElement FindBox(List<XElement> boxes, SafetyObjectId want, ObjectIdEditResult result)
        {
            List<XElement> named = boxes.Where(b => (string)b.Element("Name") == want.BoxName).ToList();
            if (named.Count == 1)
                return named[0];
            result.Problems.Add(string.Format("{0}: {1} box(es) named '{2}' in the project (expected 1){3}", want.Source, named.Count, want.BoxName,
                named.Count == 0 ? " — is it in io-devices.xml with this name?" : ""));
            return null;
        }

        /// <summary>False (with a problem) when the item, or the item that holds the id, already
        /// got a different id from another safety file.</summary>
        static bool CanAssign(XElement item, XElement holder, SafetyObjectId want, Dictionary<XElement, SafetyObjectId> assigned, ObjectIdEditResult result)
        {
            if (assigned.TryGetValue(item, out SafetyObjectId earlier) && earlier.Id != want.Id)
            {
                result.Problems.Add(string.Format("{0} and {1} ask for different ids for the same object", earlier, want));
                return false;
            }
            if (holder != null && assigned.TryGetValue(holder, out SafetyObjectId other))
            {
                result.Problems.Add(string.Format("{0} and {1} ask for the same id", other, want));
                return false;
            }
            return true;
        }

        /// <summary>"#xPP00nnnn" box references (PortABoxInfo, PortBBoxInfo, ...): nnnn is the box id.</summary>
        static void RenumberBoxReferences(XDocument doc, Dictionary<int, int> newIdByOldId)
        {
            if (newIdByOldId.Count == 0)
                return;
            foreach (XAttribute reference in doc.Descendants().Attributes().Where(a => a.Name.LocalName.EndsWith("BoxInfo", StringComparison.Ordinal)).ToList())
            {
                if (!TryParseHex(reference.Value, out int value) || value == 0x00FFFFFF) // #x00ffffff = no box
                    continue;
                if (newIdByOldId.TryGetValue(value & 0xFFFF, out int newId))
                    reference.Value = HexId((value & ~0xFFFF) | newId);
            }
        }

        static int BoxId(XElement box) => int.Parse((string)box.Attribute("Id"), CultureInfo.InvariantCulture);

        static int ModuleId(XElement module) => TryParseHex((string)module.Attribute("Id"), out int id) ? id : -1;

        static string HexId(int id) => "#x" + id.ToString("x8", CultureInfo.InvariantCulture);

        static bool TryParseHex(string text, out int value)
        {
            value = 0;
            if (text == null || !text.StartsWith("#x", StringComparison.OrdinalIgnoreCase))
                return false;
            bool ok = uint.TryParse(text.Substring(2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out uint v);
            value = unchecked((int)v);
            return ok;
        }
    }
}
