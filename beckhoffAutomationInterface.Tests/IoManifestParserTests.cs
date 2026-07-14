using BeckhoffAutomationInterface.Sync;
using Xunit;

namespace BeckhoffAutomationInterface.Tests
{
    public class IoManifestParserTests : System.IDisposable
    {
        readonly string _dir;

        public IoManifestParserTests()
        {
            _dir = Path.Combine(Path.GetTempPath(), "IoManifestParserTests_" + Guid.NewGuid());
            Directory.CreateDirectory(_dir);
        }

        public void Dispose() => Directory.Delete(_dir, recursive: true);

        string WriteManifest(string xml)
        {
            string path = Path.Combine(_dir, "io-devices.xml");
            File.WriteAllText(path, xml);
            return path;
        }

        [Fact]
        public void MissingManifest_ReturnsEmptyList()
        {
            var devices = IoManifestParser.Parse(Path.Combine(_dir, "does-not-exist.xml"));
            Assert.Empty(devices);
        }

        [Fact]
        public void DeviceWithDirectTerminals_ParsesFlatBoxAndTerminals()
        {
            string path = WriteManifest(@"
<IoTree>
  <Device Name=""Device 1 (EtherCAT)"" Disabled=""true"">
    <Box Name=""Term 1 (EK1100)"" Product=""EK1100"">
      <Terminal Name=""Term 2 (EL1008)"" Product=""EL1008"" />
      <Terminal Name=""Term 3 (EL2008)"" Product=""EL2008"" />
    </Box>
  </Device>
</IoTree>");
            var devices = IoManifestParser.Parse(path);

            IoDeviceSpec device = Assert.Single(devices);
            Assert.Equal("Device 1 (EtherCAT)", device.Name);
            Assert.True(device.Disabled);

            IoNodeSpec coupler = Assert.Single(device.Children);
            Assert.Equal("Term 1 (EK1100)", coupler.Name);
            Assert.Equal("EK1100", coupler.Product);
            Assert.Equal(2, coupler.Children.Count);
            Assert.Equal("Term 2 (EL1008)", coupler.Children[0].Name);
            Assert.Equal("EL1008", coupler.Children[0].Product);
            Assert.Empty(coupler.Children[0].Children);
        }

        [Fact]
        public void NestedBoxes_ParseToArbitraryDepth()
        {
            // Mirrors the real BH1 topology: Device -> Box(CU2508 junction) ->
            // Box(EK1100 coupler) -> Terminal(EL2008), i.e. one level deeper than the
            // old fixed Device->Box->Terminal schema supported.
            string path = WriteManifest(@"
<IoTree>
  <Device Name=""BH1"">
    <Box Name=""Box 1"" Product=""CU2508"">
      <Box Name=""EK1100_1.1"" Product=""EK1100"">
        <Terminal Name=""EL2008_1.1"" Product=""EL2008"" />
      </Box>
    </Box>
  </Device>
</IoTree>");
            var devices = IoManifestParser.Parse(path);

            IoNodeSpec junction = devices[0].Children[0];
            Assert.Equal("Box 1", junction.Name);
            Assert.Equal("CU2508", junction.Product);

            IoNodeSpec coupler = Assert.Single(junction.Children);
            Assert.Equal("EK1100_1.1", coupler.Name);

            IoNodeSpec terminal = Assert.Single(coupler.Children);
            Assert.Equal("EL2008_1.1", terminal.Name);
            Assert.Equal("EL2008", terminal.Product);
        }

        [Fact]
        public void DisabledAttribute_DefaultsToFalseWhenAbsent()
        {
            string path = WriteManifest(@"<IoTree><Device Name=""D1"" /></IoTree>");
            IoDeviceSpec device = Assert.Single(IoManifestParser.Parse(path));
            Assert.False(device.Disabled);
            Assert.Empty(device.Children);
        }

        [Fact]
        public void Links_AreParsedFromLinksSection()
        {
            string path = WriteManifest(@"
<IoTree>
  <Links>
    <Link PlcVar=""GVL.gInput1"" IoChannel=""BH1.Term2.Channel1.Input"" />
  </Links>
</IoTree>");
            List<LinkSpec> links = IoManifestParser.ParseLinks(path);

            LinkSpec link = Assert.Single(links);
            Assert.Equal("GVL.gInput1", link.PlcVar);
            Assert.Equal("BH1.Term2.Channel1.Input", link.IoChannel);
        }
    }
}
