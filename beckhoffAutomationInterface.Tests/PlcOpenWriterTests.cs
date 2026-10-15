using System.Collections.Generic;
using System.Linq;
using System.Xml.Linq;
using BeckhoffAutomationInterface.Sync;
using Xunit;

namespace BeckhoffAutomationInterface.Tests
{
    /// <summary>The PLCopen document of the bulk import (Sync/PlcOpenWriter). The shapes are the
    /// ones TwinCAT 4026 accepted in the 2026-10-15 spikes; the e2e round trip proves the rest.</summary>
    public class PlcOpenWriterTests
    {
        static readonly XNamespace Tc6 = "http://www.plcopen.org/xml/tc6_0200";
        static readonly XNamespace Xhtml = "http://www.w3.org/1999/xhtml";
        const string Ext = "http://www.3s-software.com/plcopenxml/";

        static StPouSource Src(string name, PouKind kind, string decl, string impl = null, string folder = "", string owner = null,
            string getText = null, string setText = null)
        {
            var s = new StPouSource(name, kind, owner, decl, impl, null, getText, setText);
            s.RelativeFolder = folder;
            return s;
        }

        static XDocument Write(params StPouSource[] sources)
        {
            int n = 0;
            // Save and parse again: what TwinCAT reads is the text, not the object tree.
            return XDocument.Parse(PlcOpenWriter.Write(sources, () => "id" + (++n)).ToString(SaveOptions.DisableFormatting));
        }

        static string PlainText(XElement owner) =>
            owner.Descendants(Tc6 + "data").First(d => (string)d.Attribute("name") == Ext + "interfaceasplaintext")
                .Element(Tc6 + "InterfaceAsPlainText").Element(Xhtml + "xhtml").Value;

        static string Body(XElement owner) => owner.Element(Tc6 + "body").Element(Tc6 + "ST").Element(Xhtml + "xhtml").Value;

        [Fact]
        public void Function_HasPlainTextDeclaration_Body_AndPlaceholderReturnType()
        {
            var doc = Write(Src("F_X", PouKind.Function, "FUNCTION F_X : LREAL\r\nVAR_INPUT\r\n    a : INT;   // c\r\nEND_VAR", "F_X := a;"));

            XElement pou = doc.Descendants(Tc6 + "pou").Single();
            Assert.Equal("function", (string)pou.Attribute("pouType"));
            Assert.NotNull(pou.Element(Tc6 + "interface").Element(Tc6 + "returnType"));
            Assert.Equal("FUNCTION F_X : LREAL\nVAR_INPUT\n    a : INT;   // c\nEND_VAR", PlainText(pou).Replace("\r\n", "\n"));
            Assert.Equal("F_X := a;", Body(pou));
        }

        [Fact]
        public void SpecialCharacters_SurviveTheXml()
        {
            string body = "IF a < b AND_THEN c > 0 THEN s := 'x & y'; END_IF";
            var doc = Write(Src("F_Y", PouKind.Function, "FUNCTION F_Y : BOOL", body));

            Assert.Equal(body, Body(doc.Descendants(Tc6 + "pou").Single()));
        }

        [Fact]
        public void FunctionBlock_MethodsAndProperties_AreInsideTheFb()
        {
            var doc = Write(
                Src("FB_A", PouKind.FunctionBlock, "FUNCTION_BLOCK FB_A", ";", "Lib"),
                Src("Run", PouKind.Method, "METHOD PUBLIC Run : BOOL", "Run := TRUE;", "Lib", "FB_A"),
                Src("Value", PouKind.Property, "PROPERTY Value : INT", null, "Lib", "FB_A", getText: "Value := 1;", setText: ";"));

            XElement pou = doc.Descendants(Tc6 + "pou").Single();
            Assert.Equal("functionBlock", (string)pou.Attribute("pouType"));
            XElement method = pou.Descendants(Tc6 + "Method").Single();
            Assert.Equal("Run", (string)method.Attribute("name"));
            Assert.Equal("Run := TRUE;", Body(method));
            XElement property = pou.Descendants(Tc6 + "Property").Single();
            Assert.Equal("Value := 1;", Body(property.Element(Tc6 + "GetAccessor")));
            Assert.Equal(";", Body(property.Element(Tc6 + "SetAccessor")));
        }

        [Fact]
        public void Property_WithoutSetter_HasNoSetAccessor()
        {
            var doc = Write(
                Src("FB_A", PouKind.FunctionBlock, "FUNCTION_BLOCK FB_A", ";"),
                Src("Value", PouKind.Property, "PROPERTY Value : INT", null, "", "FB_A", getText: "Value := 1;"));

            XElement property = doc.Descendants(Tc6 + "Property").Single();
            Assert.NotNull(property.Element(Tc6 + "GetAccessor"));
            Assert.Null(property.Element(Tc6 + "SetAccessor"));
        }

        [Fact]
        public void Interface_IsProjectData_WithItsMembers_AndNoEmptyLists()
        {
            var doc = Write(
                Src("I_A", PouKind.Interface, "INTERFACE I_A EXTENDS I_B"),
                Src("Open", PouKind.Method, "METHOD Open : BOOL", null, "", "I_A"));

            XElement itf = doc.Descendants(Tc6 + "Interface").Single();
            Assert.Equal("I_A", (string)itf.Attribute("name"));
            Assert.Equal("Open", (string)itf.Element(Tc6 + "Methods").Element(Tc6 + "Method").Attribute("name"));
            Assert.Null(itf.Element(Tc6 + "Properties"));   // an empty list was never tested on TwinCAT
            Assert.Equal("INTERFACE I_A EXTENDS I_B", PlainText(itf));
            Assert.Empty(doc.Descendants(Tc6 + "pou"));
        }

        [Fact]
        public void Duts_HaveSchemaValidPlaceholders_AndPlainText()
        {
            var doc = Write(
                Src("ST_A", PouKind.StructDut, "TYPE ST_A :\r\nSTRUCT\r\n    a : INT;\r\nEND_STRUCT\r\nEND_TYPE"),
                Src("E_A", PouKind.EnumDut, "TYPE E_A :\r\n(\r\n    X := 1\r\n);\r\nEND_TYPE"),
                Src("T_A", PouKind.AliasDut, "TYPE T_A : ARRAY[1..3] OF REAL; END_TYPE"));

            var types = doc.Descendants(Tc6 + "dataType").ToDictionary(d => (string)d.Attribute("name"));
            Assert.NotNull(types["ST_A"].Element(Tc6 + "baseType").Element(Tc6 + "struct"));
            // TwinCAT rejects the WHOLE import when an enum has no value.
            Assert.Single(types["E_A"].Descendants(Tc6 + "value"));
            Assert.NotNull(types["T_A"].Element(Tc6 + "baseType").Element(Tc6 + "INT"));
            Assert.StartsWith("TYPE T_A : ARRAY", PlainText(types["T_A"]));
        }

        [Fact]
        public void Gvl_IsGlobalVarsProjectData_WithPlainText()
        {
            var doc = Write(Src("GVL_A", PouKind.Gvl, "{attribute 'qualified_only'}\r\nVAR_GLOBAL\r\n    n : INT;\r\nEND_VAR"));

            XElement gvl = doc.Descendants(Tc6 + "globalVars").Single();
            Assert.Equal("GVL_A", (string)gvl.Attribute("name"));
            Assert.StartsWith("{attribute 'qualified_only'}", PlainText(gvl));
        }

        [Fact]
        public void ProjectStructure_NestsFolders_AndLinksEveryObjectById()
        {
            var doc = Write(
                Src("F_1", PouKind.Function, "FUNCTION F_1 : INT", "F_1 := 1;", "Lib/Math"),
                Src("F_2", PouKind.Function, "FUNCTION F_2 : INT", "F_2 := 2;", "Lib"),
                Src("GVL_A", PouKind.Gvl, "VAR_GLOBAL\r\nEND_VAR", null, "App"),
                Src("FB_A", PouKind.FunctionBlock, "FUNCTION_BLOCK FB_A", ";", "Lib/Math"),
                Src("Run", PouKind.Method, "METHOD Run : BOOL", ";", "Lib/Math", "FB_A"));

            XElement structure = doc.Descendants(Tc6 + "ProjectStructure").Single();
            XElement lib = structure.Elements(Tc6 + "Folder").Single(f => (string)f.Attribute("Name") == "Lib");
            XElement math = lib.Elements(Tc6 + "Folder").Single(f => (string)f.Attribute("Name") == "Math");
            Assert.Equal(new[] { "F_1", "FB_A" }, math.Elements(Tc6 + "Object").Select(o => (string)o.Attribute("Name")));
            Assert.Equal("Run", (string)math.Elements(Tc6 + "Object").Last().Element(Tc6 + "Object").Attribute("Name"));
            Assert.Equal("F_2", (string)lib.Element(Tc6 + "Object").Attribute("Name"));

            // Every ObjectId in the structure belongs to exactly one object in the document.
            var structureIds = structure.Descendants(Tc6 + "Object").Select(o => (string)o.Attribute("ObjectId")).ToList();
            var objectIds = doc.Descendants(Tc6 + "ObjectId").Select(e => e.Value)
                .Concat(doc.Descendants().Where(e => e.Name != Tc6 + "Object").Select(e => (string)e.Attribute("ObjectId")).Where(v => v != null))
                .ToList();
            Assert.Equal(structureIds.OrderBy(x => x), objectIds.OrderBy(x => x));
            Assert.Equal(structureIds.Count, structureIds.Distinct().Count());
        }

        [Fact]
        public void ToImport_SkipsExistingNames_AndTheirMembers()
        {
            var desired = new List<StPouSource>
            {
                Src("MAIN", PouKind.Program, "PROGRAM MAIN", ";"),
                Src("_Init", PouKind.Method, "METHOD _Init", ";", "", "MAIN"),
                Src("FB_A", PouKind.FunctionBlock, "FUNCTION_BLOCK FB_A", ";"),
                Src("Run", PouKind.Method, "METHOD Run : BOOL", ";", "", "FB_A"),
            };

            var names = PlcOpenBulkImport.ToImport(desired, new[] { "main" }).Select(s => s.Name);

            Assert.Equal(new[] { "FB_A", "Run" }, names);
        }
    }
}
