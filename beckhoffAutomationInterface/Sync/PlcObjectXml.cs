using System;
using System.Linq;
using System.Text;
using System.Xml;
using System.Xml.Linq;

namespace BeckhoffAutomationInterface.Sync
{
    /// <summary>Pure conversion to the TwinCAT 3 PLC source-file format. Pragmas stay as source text.</summary>
    static class PlcObjectXml
    {
        public const string Format = "tc-plc-object-1";

        public static XDocument Write(PlcSourceObject source)
        {
            StPouSource root = source.Root;
            string tag = root.IsDut ? "DUT" : root.IsGvl ? "GVL" : root.Kind == PouKind.Interface ? "Itf" : "POU";
            XElement element = Named(tag, root.Name, root.Kind + "/" + root.Name);
            if (tag == "POU") element.SetAttributeValue("SpecialFunc", "None");
            element.Add(Text("Declaration", root.DeclarationText));
            bool isInterface = root.Kind == PouKind.Interface;
            if (tag == "POU") element.Add(Implementation(root.ImplementationText));
            foreach (StPouSource member in source.Sources.Skip(1))
                element.Add(Member(member, root.Name, isInterface));
            return new XDocument(new XDeclaration("1.0", "utf-8", null),
                new XElement("TcPlcObject", new XAttribute("Version", "1.1.0.1"), element));
        }

        static XElement Member(StPouSource source, string owner, bool isInterface)
        {
            string identity = owner + "/" + source.Kind + "/" + source.Name;
            var member = Named(source.IsMethod ? "Method" : "Property", source.Name, identity);
            member.Add(Text("Declaration", source.DeclarationText));
            if (source.IsMethod)
            {
                if (!isInterface) member.Add(Implementation(source.ImplementationText));
            }
            else
            {
                if (source.GetText != null) member.Add(Accessor("Get", source.GetText, identity, isInterface));
                if (source.SetText != null) member.Add(Accessor("Set", source.SetText, identity, isInterface));
            }
            return member;
        }

        static XElement Accessor(string name, string body, string owner, bool isInterface)
        {
            var accessor = Named(name, name, owner + "/" + name);
            accessor.Add(Text("Declaration", "PUBLIC\r\nVAR\r\nEND_VAR"));
            if (!isInterface) accessor.Add(Implementation(body));
            return accessor;
        }

        static XElement Named(string tag, string name, string identity)
        {
            byte[] hash = HexBytes(PlcSourceObject.Sha256(Encoding.UTF8.GetBytes(Format + "/" + identity)));
            return new XElement(tag, new XAttribute("Name", name), new XAttribute("Id", new Guid(hash).ToString("B")));
        }

        static byte[] HexBytes(string hash) => Enumerable.Range(0, 16).Select(i => Convert.ToByte(hash.Substring(i * 2, 2), 16)).ToArray();
        static XElement Implementation(string text) => new XElement("Implementation", Text("ST", text));

        // A legal ST string or comment can contain the CDATA terminator; split it without changing the text.
        static XElement Text(string name, string text)
        {
            var element = new XElement(name);
            string[] parts = (text ?? "").Split(new[] { "]]>" }, StringSplitOptions.None);
            for (int i = 0; i < parts.Length; i++)
            {
                if (i > 0) element.Add(new XText("]]>"));
                element.Add(new XCData(parts[i]));
            }
            return element;
        }

        public static byte[] Bytes(XDocument document)
        {
            using (var stream = new System.IO.MemoryStream())
            {
                using (XmlWriter writer = XmlWriter.Create(stream, new XmlWriterSettings
                {
                    Encoding = new UTF8Encoding(false), Indent = true, NewLineHandling = NewLineHandling.None
                })) document.Save(writer);
                return stream.ToArray();
            }
        }

    }
}
