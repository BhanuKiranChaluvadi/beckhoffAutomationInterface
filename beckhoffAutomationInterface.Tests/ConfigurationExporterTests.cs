using System.Xml.Linq;
using BeckhoffAutomationInterface.Sync;
using Xunit;

namespace BeckhoffAutomationInterface.Tests
{
    public class ConfigurationExporterTests : IDisposable
    {
        readonly string root = Path.Combine(Path.GetTempPath(), "bai-export-test-" + Guid.NewGuid().ToString("N"));
        public ConfigurationExporterTests() => Directory.CreateDirectory(root);
        public void Dispose() => Directory.Delete(root, true);
        string Write(string relative, string value)
        {
            string path = Path.Combine(root, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(path)); File.WriteAllText(path, value); return path;
        }

        [Fact]
        public void NativeSnapshotResolvesDecomposedIoReferences()
        {
            string project = Write("decomposed/M.tsproj", "<TcSmProject><Project><Io><Device><Name>Bus</Name><Box File='MFC.xti'/></Device></Io></Project></TcSmProject>");
            Write("decomposed/_Config/IO/Bus/MFC.xti", "<TcSmItem><Box><Name>MFC</Name></Box></TcSmItem>");
            string output = Path.Combine(root, "decomposed-export"); Directory.CreateDirectory(output);
            NativeProjectExport.Write(project, output);
            Assert.Equal(2, XDocument.Load(Path.Combine(output, "native-inventory.xml")).Root.Elements("File").Count());
        }

        [Theory]
        [InlineData("EL3174")]
        [InlineData("EL3174 4Ch. Ana. Input +/-10V, 0/4-20mA")]
        [InlineData("../outside")]
        [InlineData("CON")]
        public void TerminalTypesRoundTripWithOriginalGuids(string product)
        {
            string project = Write("types/M.tsproj", "<TcSmProject><DataTypes><DataType><Name GUID='{123}'>ST_Input</Name></DataType></DataTypes><Project><Io><Device><Box><Name>AI</Name><EtherCAT CreateDeviceDataType='true' DeviceDataTypePerChannel='true'><PlcDataTypes><PlcDataType GUID='{123}'/></PlcDataTypes></EtherCAT></Box></Device></Io></Project></TcSmProject>");
            var manifest = XDocument.Parse("<IoTree><Device Name='Bus'><Terminal Name='AI' Product='EL3174'/></Device></IoTree>");
            manifest.Descendants("Terminal").Single().SetAttributeValue("Product", product);
            string output = Path.Combine(root, "type-export");
            ConfigurationExporter.PlcDataTypes(project, manifest, output);
            Assert.Equal("Channel", (string)manifest.Descendants("Terminal").Single().Attribute("CreatePlcType"));
            var template = PlcDataTypeTemplate.Load(Path.Combine(output, "plc-data-types"), product);
            Assert.Single(Directory.GetFiles(Path.Combine(output, "plc-data-types")));
            Assert.NotEqual(PlcDataTypeTemplate.FileName(product), PlcDataTypeTemplate.FileName(product + " different"));
            Assert.Equal("{123}", (string)template.DataTypes[0].Element("Name").Attribute("GUID"));
            Assert.Equal("{123}", (string)template.PlcDataTypes[0].Attribute("GUID"));
            manifest.Descendants("Device").Single().Add(new XElement(manifest.Descendants("Terminal").Single()));
            Assert.Throws<InvalidOperationException>(() => ConfigurationExporter.PlcDataTypes(project, manifest, output));
        }

        [Fact]
        public void TaskExportPreservesCallOrderAndTiming()
        {
            var doc = ConfigurationExporter.Tasks(new[] { new RtTaskState("Fast", 10000, 2) },
                new[] { new PlcTaskRefState("Fast", "TIRT^Fast", new[] { "Device", "Logger" }) });
            var parsed = TaskManifestParser.Parse(doc, "test");
            Assert.Equal(1000, parsed.Tasks[0].CycleTimeUs);
            Assert.Equal(2, parsed.Tasks[0].Priority);
            Assert.Equal(new[] { "Device", "Logger" }, parsed.Tasks[0].Calls);
            Assert.Throws<InvalidOperationException>(() => ConfigurationExporter.Tasks(new[] { new RtTaskState("Fast", 10000, 2) },
                new[] { new PlcTaskRefState("Fast", "TIRT^Missing", new[] { "Device" }) }));
        }

        [Fact]
        public void VariantGroupsSurviveExport()
        {
            var original = new VariantManifest(new[] { "A", "B" }, new Dictionary<string, IReadOnlyList<string>> { ["Feature"] = new[] { "B" } });
            Assert.True(original.SameAs(VariantManifestParser.Parse(ConfigurationExporter.Variants(original), "test")));
        }

        [Fact]
        public void SafetyFilesStayByteExactAndCannotBeOverwritten()
        {
            string tsproj = Write("project/Machine.tsproj", "<TcSmProject><Project><Safety><Project Name='Safe' PrjFilePath='Safe/Safe.splcproj'/></Safety></Project></TcSmProject>");
            string project = Write("project/Safe/Safe.splcproj", "<Safety CRC='123'/>\r\n");
            string target = Write("project/Safe/TargetSystemConfig.xml", "<Target CRC='456'>  unchanged  </Target>\r\n");
            string output = Path.Combine(root, "export"); Directory.CreateDirectory(output);
            ConfigurationExporter.Safety(tsproj, output);
            Assert.Equal(File.ReadAllBytes(project), File.ReadAllBytes(Path.Combine(output, "safety/Safe/Safe.splcproj")));
            Assert.Equal(File.ReadAllBytes(target), File.ReadAllBytes(Path.Combine(output, "safety/Safe/TargetSystemConfig.xml")));
            Assert.Throws<IOException>(() => ConfigurationExporter.Safety(tsproj, output));
            Assert.Throws<IOException>(() => ConfigurationExporter.Safety(tsproj, Path.GetDirectoryName(project)));
        }

        [Fact]
        public void NativeSnapshotRetainsEveryFileAndRejectsUnsafeLocations()
        {
            string project = Write("native/Machine.tsproj", "<TcSmProject><Project><Plc><Project PrjFilePath='PLC/PLC.plcproj'/></Plc></Project></TcSmProject>");
            Write("native/PLC/PLC.plcproj", "<Project/>"); Write("native/motion.xml", "<NC>raw configuration</NC>");
            string output = Path.Combine(root, "snapshot"); Directory.CreateDirectory(output);
            NativeProjectExport.Write(project, output);
            var inventory = XDocument.Load(Path.Combine(output, "native-inventory.xml"));
            Assert.Equal(3, inventory.Root.Elements("File").Count());
            Assert.All(inventory.Root.Elements("File"), f => Assert.Equal(64, ((string)f.Attribute("Sha256")).Length));
            Assert.True(new FileInfo(Path.Combine(output, "native-project.zip")).Length > 0);
            Assert.Throws<IOException>(() => NativeProjectExport.Write(project, output));
            Assert.Throws<IOException>(() => NativeProjectExport.Write(project, Path.Combine(root, "native/nested")));
            File.WriteAllText(project, "<TcSmProject><Project><Plc><Project PrjFilePath='../outside.plcproj'/></Plc></Project></TcSmProject>");
            string second = Path.Combine(root, "second"); Directory.CreateDirectory(second);
            Assert.Throws<IOException>(() => NativeProjectExport.Write(project, second));
        }

        [Theory]
        [InlineData("../escape")]
        [InlineData("C:/absolute")]
        [InlineData("")]
        public void ExternalProjectPathRejected(string path) => Assert.Throws<IOException>(() => ConfigurationExporter.Within(root, path));
    }
}
