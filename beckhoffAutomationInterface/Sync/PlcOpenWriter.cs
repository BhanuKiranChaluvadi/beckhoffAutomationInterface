using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml.Linq;

namespace BeckhoffAutomationInterface.Sync
{
    /// <summary>
    /// Writes parsed .st objects as ONE PLCopen XML document (TC6 0200 + the 3S extensions that
    /// TwinCAT's PlcOpenImport reads), so a fresh project gets all its objects in one import
    /// instead of one CreateChild per object (bulk import, 2026-10-15).
    ///
    /// What the import keeps, proven by spikes on TwinCAT 4026 (scratch, never saved):
    ///   - the declaration as PLAIN TEXT (3S "interfaceasplaintext") for FUNCTION, FUNCTION_BLOCK,
    ///     PROGRAM, STRUCT, ENUM, ALIAS and GVL: equal to the source (line endings aside);
    ///   - every implementation body, incl. property Get/Set bodies: exact;
    ///   - the folder tree (3S "projectstructure", objects linked by ObjectId).
    /// What it does NOT keep: the plain-text declaration of METHODs, PROPERTYs and INTERFACEs
    /// (TwinCAT builds a stub from the structured part). The normal sync that runs right after
    /// the import writes those (it writes every text that differs), so the end state is the
    /// same as a per-object sync. The structured parts below are therefore only placeholders
    /// that pass TwinCAT's schema check (an enum needs one value, a property a type).
    /// </summary>
    static class PlcOpenWriter
    {
        static readonly XNamespace Tc6 = "http://www.plcopen.org/xml/tc6_0200";
        static readonly XNamespace Xhtml = "http://www.w3.org/1999/xhtml";
        const string Ext = "http://www.3s-software.com/plcopenxml/";

        /// <summary>The document, plus the number of objects in it (top level + members).
        /// newId makes ObjectIds (tests pass a counter for stable output).</summary>
        public static XDocument Write(IReadOnlyList<StPouSource> sources, Func<string> newId = null)
        {
            newId = newId ?? (() => Guid.NewGuid().ToString());
            var topLevel = sources.Where(s => !s.IsMethod && !s.IsProperty).ToList();
            var membersByOwner = sources.Where(s => s.IsMethod || s.IsProperty)
                .GroupBy(s => s.OwnerName, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.OrdinalIgnoreCase);

            var dataTypes = new XElement(Tc6 + "dataTypes");
            var pous = new XElement(Tc6 + "pous");
            var projectData = new List<XElement>();
            var structure = new FolderNode();

            foreach (StPouSource s in topLevel)
            {
                string id = newId();
                var members = membersByOwner.TryGetValue(s.Name, out var m) ? m : new List<StPouSource>();
                var memberIds = members.Select(x => (Member: x, Id: newId())).ToList();
                structure.Add(s.RelativeFolder, new XElement(Tc6 + "Object", new XAttribute("Name", s.Name), new XAttribute("ObjectId", id),
                    memberIds.Select(x => new XElement(Tc6 + "Object", new XAttribute("Name", x.Member.Name), new XAttribute("ObjectId", x.Id)))));

                switch (s.Kind)
                {
                    case PouKind.StructDut:
                        dataTypes.Add(DataType(s, id, new XElement(Tc6 + "struct")));
                        break;
                    case PouKind.EnumDut:
                        dataTypes.Add(DataType(s, id, new XElement(Tc6 + "enum",
                            new XElement(Tc6 + "values", new XElement(Tc6 + "value", new XAttribute("name", "Placeholder"))))));
                        break;
                    case PouKind.AliasDut:
                        dataTypes.Add(DataType(s, id, new XElement(Tc6 + "INT")));
                        break;
                    case PouKind.Gvl:
                        projectData.Add(Data("globalvars", new XElement(Tc6 + "globalVars", new XAttribute("name", s.Name),
                            new XElement(Tc6 + "addData", PlainText(s.DeclarationText), ObjectId(id)))));
                        break;
                    case PouKind.Interface:
                        // <Methods>/<Properties> only when there are members (an empty one is untested).
                        var methods = memberIds.Where(x => x.Member.IsMethod).ToList();
                        var properties = memberIds.Where(x => x.Member.IsProperty).ToList();
                        projectData.Add(Data("interface", new XElement(Tc6 + "Interface", new XAttribute("name", s.Name), new XAttribute("ObjectId", id),
                            methods.Count == 0 ? null : new XElement(Tc6 + "Methods", methods.Select(x =>
                                new XElement(Tc6 + "Method", new XAttribute("name", x.Member.Name), new XAttribute("ObjectId", x.Id),
                                    new XElement(Tc6 + "interface"), new XElement(Tc6 + "addData")))),
                            properties.Count == 0 ? null : new XElement(Tc6 + "Properties", properties.Select(x =>
                                new XElement(Tc6 + "Property", new XAttribute("name", x.Member.Name), new XAttribute("ObjectId", x.Id),
                                    new XElement(Tc6 + "interface", PlaceholderReturnType()), new XElement(Tc6 + "addData")))),
                            new XElement(Tc6 + "addData", PlainText(s.DeclarationText)))));
                        break;
                    case PouKind.Program:
                    case PouKind.FunctionBlock:
                    case PouKind.Function:
                        pous.Add(Pou(s, id, memberIds));
                        break;
                    default:
                        throw new ArgumentOutOfRangeException(nameof(sources), s.Kind, "No PLCopen form for " + s.Name);
                }
            }

            projectData.Add(Data("projectstructure", new XElement(Tc6 + "ProjectStructure", structure.ToXml()), discard: true));
            return new XDocument(new XDeclaration("1.0", "utf-8", null),
                new XElement(Tc6 + "project",
                    new XElement(Tc6 + "fileHeader", new XAttribute("companyName", "Beckhoff Automation GmbH"),
                        new XAttribute("productName", "TwinCAT PLC Control"), new XAttribute("productVersion", "3.5.13.21"),
                        new XAttribute("creationDateTime", "2026-10-15T00:00:00")),
                    new XElement(Tc6 + "contentHeader", new XAttribute("name", "beckhoffAutomationInterface bulk import"),
                        new XAttribute("modificationDateTime", "2026-10-15T00:00:00"),
                        new XElement(Tc6 + "coordinateInfo",
                            new XElement(Tc6 + "fbd", Scaling()), new XElement(Tc6 + "ld", Scaling()), new XElement(Tc6 + "sfc", Scaling()))),
                    new XElement(Tc6 + "types", dataTypes, pous),
                    new XElement(Tc6 + "instances", new XElement(Tc6 + "configurations")),
                    new XElement(Tc6 + "addData", projectData)));
        }

        static XElement Pou(StPouSource s, string id, List<(StPouSource Member, string Id)> members)
        {
            string pouType = s.Kind == PouKind.Program ? "program" : s.Kind == PouKind.FunctionBlock ? "functionBlock" : "function";
            var addData = new XElement(Tc6 + "addData", PlainText(s.DeclarationText));
            foreach (var (member, memberId) in members)
            {
                if (member.IsMethod)
                    addData.Add(Data("method", new XElement(Tc6 + "Method", new XAttribute("name", member.Name), new XAttribute("ObjectId", memberId),
                        new XElement(Tc6 + "interface"), Body(member.ImplementationText), new XElement(Tc6 + "addData"))));
                else
                    addData.Add(Data("property", new XElement(Tc6 + "Property", new XAttribute("name", member.Name), new XAttribute("ObjectId", memberId),
                        new XElement(Tc6 + "interface", PlaceholderReturnType()),
                        member.GetText != null ? Accessor("GetAccessor", member.GetText) : null,
                        member.SetText != null ? Accessor("SetAccessor", member.SetText) : null,
                        new XElement(Tc6 + "addData"))));
            }
            addData.Add(ObjectId(id));
            return new XElement(Tc6 + "pou", new XAttribute("name", s.Name), new XAttribute("pouType", pouType),
                // A function needs a return type to pass the schema check; the plain text sets the real one.
                new XElement(Tc6 + "interface", s.Kind == PouKind.Function ? PlaceholderReturnType() : null),
                Body(s.ImplementationText),
                addData);
        }

        static XElement DataType(StPouSource s, string id, XElement baseType) =>
            new XElement(Tc6 + "dataType", new XAttribute("name", s.Name),
                new XElement(Tc6 + "baseType", baseType),
                new XElement(Tc6 + "addData", PlainText(s.DeclarationText), ObjectId(id)));

        static XElement Accessor(string name, string body) =>
            new XElement(Tc6 + name, new XElement(Tc6 + "interface"), Body(body), new XElement(Tc6 + "addData"));

        static XElement Body(string text) =>
            new XElement(Tc6 + "body", new XElement(Tc6 + "ST", new XElement(Xhtml + "xhtml", text ?? "")));

        static XElement PlainText(string declaration) =>
            Data("interfaceasplaintext", new XElement(Tc6 + "InterfaceAsPlainText", new XElement(Xhtml + "xhtml", declaration ?? "")));

        static XElement ObjectId(string id) => Data("objectid", new XElement(Tc6 + "ObjectId", id), discard: true);

        static XElement PlaceholderReturnType() => new XElement(Tc6 + "returnType", new XElement(Tc6 + "INT"));

        static XElement Data(string extension, XElement content, bool discard = false) =>
            new XElement(Tc6 + "data", new XAttribute("name", Ext + extension),
                new XAttribute("handleUnknown", discard ? "discard" : "implementation"), content);

        static XElement Scaling() => new XElement(Tc6 + "scaling", new XAttribute("x", "1"), new XAttribute("y", "1"));

        /// <summary>The folder tree of the ProjectStructure: folders first, then objects, in source order.</summary>
        sealed class FolderNode
        {
            readonly List<(string Name, FolderNode Node)> _folders = new List<(string, FolderNode)>();
            readonly List<XElement> _objects = new List<XElement>();

            public void Add(string relativeFolder, XElement obj)
            {
                FolderNode node = this;
                if (!string.IsNullOrEmpty(relativeFolder))
                    foreach (string segment in relativeFolder.Split('/'))
                    {
                        var existing = node._folders.FirstOrDefault(f => string.Equals(f.Name, segment, StringComparison.OrdinalIgnoreCase));
                        if (existing.Node == null)
                        {
                            existing = (segment, new FolderNode());
                            node._folders.Add(existing);
                        }
                        node = existing.Node;
                    }
                node._objects.Add(obj);
            }

            public IEnumerable<XElement> ToXml() =>
                _folders.Select(f => new XElement(Tc6 + "Folder", new XAttribute("Name", f.Name), f.Node.ToXml()))
                    .Concat(_objects);
        }
    }
}
