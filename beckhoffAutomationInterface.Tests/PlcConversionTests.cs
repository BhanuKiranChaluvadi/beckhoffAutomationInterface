using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Text;
using System.Xml.Linq;
using BeckhoffAutomationInterface.Sync;
using Xunit;

namespace BeckhoffAutomationInterface.Tests
{
    public class PlcConversionTests
    {
        static StPouSource Block(string body = "", string folder = "Lib/Heating") =>
            new StPouSource("FB_Heat", PouKind.FunctionBlock, null,
                "FUNCTION_BLOCK FB_Heat\r\nVAR\r\nEND_VAR", body) { RelativeFolder = folder };

        static StPouSource Method(string body = "Run := TRUE;") =>
            new StPouSource("Run", PouKind.Method, "FB_Heat", "METHOD PUBLIC Run : BOOL", body);

        static PlcSourceObject Object(params StPouSource[] source) => PlcSourceObject.Group(source).Single();

        [Fact]
        public void SharedLibraryUsesOneConversionAcrossProjectsAndFolders()
        {
            string dir = TestProjects.NewTempDir("bai_conversion");
            var sas = new PlcObjectCache(dir, "converter-one");
            byte[] original = sas.Get(Object(Block(folder: "Lib/Heating"), Method()));
            Assert.Equal(1, sas.Converted);
            var uvm = new PlcObjectCache(dir, "converter-one");
            Assert.Equal(original, uvm.Get(Object(Block(folder: "Bench/Lib"), Method())));
            Assert.Equal(1, uvm.Reused);
            Assert.Equal(0, uvm.Converted);
        }

        [Fact]
        public void SourceChangesAndTestDoublesNeverReuseTheProductionObject()
        {
            var cache = new PlcObjectCache(TestProjects.NewTempDir("bai_conversion"), "converter-one");
            byte[] production = cache.Get(Object(Block(), Method()));
            byte[] changed = cache.Get(Object(Block(), Method("Run := FALSE;")));
            Assert.NotEqual(production, changed);
            Assert.Equal(2, cache.Converted);
        }

        [Fact]
        public void AnotherConverterStartsItsOwnCache()
        {
            string dir = TestProjects.NewTempDir("bai_conversion");
            new PlcObjectCache(dir, "old").Get(Object(Block()));
            var next = new PlcObjectCache(dir, "new");
            next.Get(Object(Block()));
            Assert.Equal(1, next.Converted);
            Assert.Equal(0, next.Reused);
        }

        [Fact]
        public void BrokenCacheEntryIsRebuilt()
        {
            string dir = TestProjects.NewTempDir("bai_conversion");
            var cache = new PlcObjectCache(dir, "converter");
            byte[] expected = cache.Get(Object(Block(), Method()));
            string entry = Directory.GetFiles(dir, "*.json", SearchOption.AllDirectories).Single();
            File.WriteAllText(entry, "interrupted write");
            Assert.Equal(expected, cache.Get(Object(Block(), Method())));
            Assert.Equal(2, cache.Converted);
        }

        [Fact]
        public void WholeObjectContainsMembersAndPreservesConditionalSource()
        {
            var root = Block("{IF defined (TOP_CAMERA_SENSOR)}\r\nx := 1;\r\n{END_IF}");
            var prop = new StPouSource("Value", PouKind.Property, "FB_Heat", "PROPERTY Value : INT", null,
                "INT", "Value := 3;", "x := Value;");
            XDocument xml = PlcObjectXml.Write(Object(root, Method(), prop));
            Assert.Equal(root.ImplementationText, xml.Root.Element("POU").Element("Implementation").Element("ST").Value);
            Assert.Single(xml.Descendants("Method"));
            Assert.Equal("Value := 3;", xml.Descendants("Get").Single().Element("Implementation").Element("ST").Value);
            Assert.Equal("x := Value;", xml.Descendants("Set").Single().Element("Implementation").Element("ST").Value);
        }

        [Theory]
        [InlineData((int)PouKind.Program, "POU")]
        [InlineData((int)PouKind.FunctionBlock, "POU")]
        [InlineData((int)PouKind.Function, "POU")]
        [InlineData((int)PouKind.Interface, "Itf")]
        [InlineData((int)PouKind.StructDut, "DUT")]
        [InlineData((int)PouKind.EnumDut, "DUT")]
        [InlineData((int)PouKind.AliasDut, "DUT")]
        [InlineData((int)PouKind.Gvl, "GVL")]
        public void EverySupportedRootHasItsTwinCatElement(int kind, string element)
        {
            var source = new StPouSource("Example", (PouKind)kind, null, "declaration", "body");
            Assert.Equal(element, PlcObjectXml.Write(Object(source)).Root.Elements().Single().Name.LocalName);
        }

        [Fact]
        public void InterfaceMethodsAndAccessorsHaveNoImplementation()
        {
            var root = new StPouSource("I_Heat", PouKind.Interface, null, "INTERFACE I_Heat", null);
            var method = new StPouSource("Run", PouKind.Method, "I_Heat", "METHOD Run : BOOL", "");
            var prop = new StPouSource("Value", PouKind.Property, "I_Heat", "PROPERTY Value : INT", null, "INT", "", null);
            XDocument xml = PlcObjectXml.Write(Object(root, method, prop));
            Assert.Empty(xml.Descendants("Implementation"));
            Assert.Single(xml.Descendants("Get"));
        }

        [Fact]
        public void InvalidOwnersAndDuplicateMembersAreRefused()
        {
            Assert.Throws<InvalidOperationException>(() => PlcSourceObject.Group(new[] { Method() }));
            Assert.Throws<InvalidOperationException>(() => PlcSourceObject.Group(new[] { Block(), Method(), Method() }));
            var gvl = new StPouSource("FB_Heat", PouKind.Gvl, null, "VAR_GLOBAL\r\nEND_VAR", null);
            Assert.Throws<InvalidOperationException>(() => PlcSourceObject.Group(new[] { gvl, Method() }));
        }

        [Fact]
        public async Task ConcurrentProjectsPublishCompleteCacheEntries()
        {
            string dir = TestProjects.NewTempDir("bai_conversion_concurrent");
            var sources = Object(Block(), Method());
            byte[][] values = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => Task.Run(() =>
                new PlcObjectCache(dir, "converter").Get(sources))));
            foreach (byte[] value in values) Assert.Equal(values[0], value);
            Assert.Single(Directory.GetFiles(dir, "*.json", SearchOption.AllDirectories));
            Assert.Empty(Directory.GetFiles(dir, "*.tmp", SearchOption.AllDirectories));
            var reader = new PlcObjectCache(dir, "converter");
            Assert.Equal(values[0], reader.Get(sources));
            Assert.Equal(1, reader.Reused);
        }

        [Fact]
        public void XmlEscapesSourceWithoutChangingItsMeaning()
        {
            var source = Object(Block("s := ']]> & < >';"));
            string xml = Encoding.UTF8.GetString(PlcObjectXml.Bytes(PlcObjectXml.Write(source)));
            Assert.Equal(source.Root.ImplementationText, XDocument.Parse(xml).Descendants("ST").Single().Value);
        }

        [Fact]
        public void SourceEnumerationOrderDoesNotInvalidateAConversion()
        {
            var cache = new PlcObjectCache(TestProjects.NewTempDir("bai_conversion_order"), "converter");
            byte[] first = cache.Get(Object(Block(), Method()));
            Assert.Equal(first, cache.Get(Object(Method(), Block())));
            Assert.Equal(1, cache.Converted);
            Assert.Equal(1, cache.Reused);
        }

        [Fact]
        public void ProjectReceivesIndependentFilesAndKeepsItsOwnIdentity()
        {
            string dir = TestProjects.NewTempDir("bai_conversion_projects");
            var cache = new PlcObjectCache(Path.Combine(dir, "cache"), "converter");
            string Project(string name)
            {
                string folder = Path.Combine(dir, name);
                Directory.CreateDirectory(folder);
                string path = Path.Combine(folder, name + ".plcproj");
                File.WriteAllText(path, "<Project xmlns='http://schemas.microsoft.com/developer/msbuild/2003'><PropertyGroup><Name>" + name + "</Name><CompilerDefines>" + name + "</CompilerDefines></PropertyGroup></Project>");
                return path;
            }
            string sas = Project("SAS");
            string uvm = Project("UVM");
            var sources = new[] { Block(), Method() };
            PlcProjectCode.Apply(sas, PlcSourceObject.Group(sources), cache, preserveMembers: false);
            PlcProjectCode.Apply(uvm, PlcSourceObject.Group(sources), cache, preserveMembers: false);
            string relative = Path.Combine("Lib", "Heating", "FB_Heat.TcPOU");
            string sasObject = Path.Combine(Path.GetDirectoryName(sas), relative);
            string uvmObject = Path.Combine(Path.GetDirectoryName(uvm), relative);
            Assert.Equal(File.ReadAllBytes(sasObject), File.ReadAllBytes(uvmObject));
            File.WriteAllText(sasObject, "edited in SAS");
            Assert.Contains("FUNCTION_BLOCK FB_Heat", File.ReadAllText(uvmObject));
            Assert.Contains("<CompilerDefines>UVM</CompilerDefines>", File.ReadAllText(uvm));
            Assert.Equal(1, cache.Converted);
            Assert.Equal(1, cache.Reused);
        }

        [Fact]
        public void SyncRetainsExistingIdsAndStandaloneMembersButCanRemoveMembers()
        {
            string dir = TestProjects.NewTempDir("bai_conversion_sync");
            string plc = Path.Combine(dir, "P.plcproj");
            File.WriteAllText(plc, "<Project />");
            var cache = new PlcObjectCache(Path.Combine(dir, "cache"), "converter");
            var initial = PlcSourceObject.Group(new[] { Block(), Method() });
            PlcProjectCode.Apply(plc, initial, cache, preserveMembers: false);
            string path = Path.Combine(dir, "Lib", "Heating", "FB_Heat.TcPOU");
            XDocument before = XDocument.Load(path);
            string existingId = Guid.NewGuid().ToString("B");
            before.Root.Element("POU").SetAttributeValue("Id", existingId);
            before.Save(path);
            var withoutMethod = PlcSourceObject.Group(new[] { Block("x := 1;") });
            PlcProjectCode.Apply(plc, withoutMethod, cache, preserveMembers: true);
            Assert.Equal(existingId, (string)XDocument.Load(path).Root.Element("POU").Attribute("Id"));
            Assert.Single(XDocument.Load(path).Descendants("Method"));
            PlcProjectCode.Apply(plc, withoutMethod, cache, preserveMembers: false);
            Assert.Empty(XDocument.Load(path).Descendants("Method"));
        }

        [Fact]
        public void MainUpdatesTheTemplateProgramInsteadOfDuplicatingIt()
        {
            string dir = TestProjects.NewTempDir("bai_conversion_main");
            Directory.CreateDirectory(Path.Combine(dir, "POUs"));
            string plc = Path.Combine(dir, "P.plcproj");
            File.WriteAllText(plc, "<Project><ItemGroup><Compile Include='POUs\\MAIN.TcPOU' /></ItemGroup></Project>");
            var main = new StPouSource("MAIN", PouKind.Program, null, "PROGRAM MAIN", "x := 1;");
            File.WriteAllText(Path.Combine(dir, "POUs", "MAIN.TcPOU"), PlcObjectXml.Write(Object(main)).ToString());
            PlcProjectCode.Apply(plc, PlcSourceObject.Group(new[] { main }), new PlcObjectCache(Path.Combine(dir, "cache"), "converter"), false);
            Assert.Single(XDocument.Load(plc).Descendants("Compile"));
            Assert.False(File.Exists(Path.Combine(dir, "MAIN.TcPOU")));
        }

        [Fact]
        public void SourceFolderCannotEscapeTheProject()
        {
            string dir = TestProjects.NewTempDir("bai_conversion_path");
            string plc = Path.Combine(dir, "P.plcproj");
            File.WriteAllText(plc, "<Project />");
            var source = Block(folder: "../outside");
            Assert.Throws<InvalidOperationException>(() => PlcProjectCode.Apply(plc,
                PlcSourceObject.Group(new[] { source }), new PlcObjectCache(Path.Combine(dir, "cache"), "converter"), false));
            Assert.Empty(XDocument.Load(plc).Root.Elements());
        }
    }
}
