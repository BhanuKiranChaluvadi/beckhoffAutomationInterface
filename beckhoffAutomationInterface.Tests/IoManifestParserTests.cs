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
        public void DirectEthercat111RetainsAdapterAndSubtype()
        {
            string path = WriteManifest("<IoTree><Device Name='NFPro bus' SubType='111' Adapter='000105a1c1ce'/></IoTree>");
            var manifest = IoManifestParser.ParseManifest(path);
            Assert.Equal(111, manifest.Devices[0].SubType);
            Assert.False(manifest.Devices[0].OnPort);
            Assert.Equal("000105A1C1CE", manifest.Devices[0].Adapter);
            IoManifestWriter.ToXml(manifest).Save(path);
            Assert.Equal(111, IoManifestParser.ParseManifest(path).Devices[0].SubType);
            Assert.Throws<FormatException>(() => IoManifestParser.ParseManifest(WriteManifest("<IoTree><Device Name='Bus' SubType='111'/></IoTree>")));
            Assert.Throws<FormatException>(() => IoManifestParser.ParseManifest(WriteManifest("<IoTree><Device Name='Bus' SubType='999'/></IoTree>")));
        }

        [Fact]
        public void MissingManifest_ReturnsEmptyList()
        {
            var devices = IoManifestParser.Parse(Path.Combine(_dir, "does-not-exist.xml"));
            Assert.Empty(devices);
        }

        [Fact]
        public void Adapter_IsTwelveHexDigits_NormalizedAndChecked()   // two masters on one (empty) adapter fail the build (2026-10-15)
        {
            string ok = WriteManifest(@"<IoTree><Device Name=""BH1"" Adapter=""02-00-00-00-b1-01"" /><Device Name=""BH2"" /></IoTree>");
            var devices = IoManifestParser.Parse(ok);
            Assert.Equal("02000000B101", devices[0].Adapter);
            Assert.Null(devices[1].Adapter);

            string bad = WriteManifest(@"<IoTree><Device Name=""BH1"" Adapter=""eth0"" /></IoTree>");
            var ex = Assert.Throws<System.FormatException>(() => IoManifestParser.Parse(bad));
            Assert.Contains("BH1", ex.Message);
            Assert.Equal("02000000B101", IoSyncEngine.AdapterOf("<DeviceDef><AddressInfo><Pnp><DeviceName/><DeviceData>02000000b101</DeviceData></Pnp></AddressInfo></DeviceDef>"));
        }

        // The real Shark layout (PLC_NFL_SHARK_old, R2.1 spike 2026-10-17): one RT-Ethernet adapter
        // with a CU2508 (8 ports) and the EtherCAT devices on its ports.
        const string AdapterLayout = @"
<IoTree>
  <Adapter Name=""Device 1 (RT-Ethernet Adapter)"" Mac=""00:01:05:99:2c:32"" Ports=""8"" />
  <Device Name=""BH1"" Disabled=""false"" Via=""Device 1 (RT-Ethernet Adapter)"" Port=""1"">
    <Box Name=""Box 1 (CU2508)"" Product=""CU2508-0000-0000"" />
  </Device>
  <Device Name=""BH3"" Via=""Device 1 (RT-Ethernet Adapter)"" Port=""3"" />
  <Device Name=""Old"" Adapter=""02000000B101"" />
</IoTree>";

        [Fact]
        public void Adapter_AndDevicesOnItsPorts_AreParsed()
        {
            IoManifest manifest = IoManifestParser.ParseManifest(WriteManifest(AdapterLayout));

            IoAdapterSpec adapter = Assert.Single(manifest.Adapters);
            Assert.Equal("Device 1 (RT-Ethernet Adapter)", adapter.Name);
            Assert.Equal("000105992C32", adapter.Mac);
            Assert.Equal(8, adapter.Ports);
            Assert.Null(adapter.DeviceDesc);
            Assert.False(adapter.Disabled);

            Assert.Equal(3, manifest.Devices.Count);
            Assert.True(manifest.Devices[0].OnPort);
            Assert.Equal("Device 1 (RT-Ethernet Adapter)", manifest.Devices[0].Via);
            Assert.Equal(1, manifest.Devices[0].Port);
            Assert.Null(manifest.Devices[0].Adapter);
            Assert.Equal("CU2508-0000-0000", manifest.Devices[0].Children[0].Product);
            Assert.Equal(3, manifest.Devices[1].Port);
            Assert.False(manifest.Devices[2].OnPort);                 // the old layout still works in the same file
            Assert.Equal("02000000B101", manifest.Devices[2].Adapter);
            Assert.Equal(3, IoManifestParser.Parse(WriteManifest(AdapterLayout)).Count);
        }

        [Fact]
        public void Adapter_DeviceDescAndDisabled_AreOptional()
        {
            string path = WriteManifest(@"<IoTree><Adapter Name=""A"" Mac=""000105992C32"" Ports=""8"" DeviceDesc=""Ethernet 5"" Disabled=""true"" /></IoTree>");
            IoAdapterSpec adapter = Assert.Single(IoManifestParser.ParseManifest(path).Adapters);
            Assert.Equal("Ethernet 5", adapter.DeviceDesc);
            Assert.True(adapter.Disabled);
            Assert.Null(adapter.DeviceName);
        }

        [Fact]
        public void Adapter_DeviceName_IsReadAndChecked()
        {
            string path = WriteManifest(@"<IoTree><Adapter Name=""A"" Mac=""000105B0417E"" Ports=""8"" DeviceName=""\DEVICE\{AB446E9A-DA48-4A47-9290-BFF2E4154EB4}"" /></IoTree>");
            Assert.Equal(@"\DEVICE\{AB446E9A-DA48-4A47-9290-BFF2E4154EB4}", Assert.Single(IoManifestParser.ParseManifest(path).Adapters).DeviceName);

            string bad = WriteManifest(@"<IoTree><Adapter Name=""A"" Mac=""000105B0417E"" Ports=""8"" DeviceName=""{AB446E9A-DA48-4A47-9290-BFF2E4154EB4}"" /></IoTree>");
            var ex = Assert.Throws<System.FormatException>(() => IoManifestParser.ParseManifest(bad));
            Assert.Contains("DeviceName", ex.Message);
        }

        [Theory]
        [InlineData(@"<Adapter Name=""A"" Mac=""0001059"" Ports=""8"" />", "Mac")]                                   // not 12 hex digits
        [InlineData(@"<Adapter Name=""A"" Ports=""8"" />", "no Mac")]
        [InlineData(@"<Adapter Name=""A"" Mac=""000105992C32"" />", "Ports")]
        [InlineData(@"<Adapter Name=""A"" Mac=""000105992C32"" Ports=""0"" />", "Ports")]
        [InlineData(@"<Adapter Name=""A"" Mac=""000105992C32"" Ports=""eight"" />", "whole number")]
        [InlineData(@"<Adapter Mac=""000105992C32"" Ports=""8"" />", "no Name")]
        [InlineData(@"<Adapter Name=""A"" Mac=""000105992C32"" Ports=""8"" /><Device Name=""BH1"" Via=""A"" Port=""1"" Adapter=""02000000B101"" />", "Via and Adapter")]
        [InlineData(@"<Adapter Name=""A"" Mac=""000105992C32"" Ports=""8"" /><Device Name=""BH1"" Via=""B"" Port=""1"" />", "no <Adapter Name=\"B\">")]
        [InlineData(@"<Adapter Name=""A"" Mac=""000105992C32"" Ports=""8"" /><Device Name=""BH1"" Via=""A"" />", "Via and Port together")]
        [InlineData(@"<Adapter Name=""A"" Mac=""000105992C32"" Ports=""8"" /><Device Name=""BH1"" Port=""1"" />", "Via and Port together")]
        [InlineData(@"<Adapter Name=""A"" Mac=""000105992C32"" Ports=""8"" /><Device Name=""BH1"" Via=""A"" Port=""0"" />", "ports 1 to 8")]
        [InlineData(@"<Adapter Name=""A"" Mac=""000105992C32"" Ports=""8"" /><Device Name=""BH1"" Via=""A"" Port=""9"" />", "ports 1 to 8")]
        [InlineData(@"<Adapter Name=""A"" Mac=""000105992C32"" Ports=""8"" /><Device Name=""BH1"" Via=""A"" Port=""2"" /><Device Name=""BH2"" Via=""A"" Port=""2"" />", "both on port 2")]
        [InlineData(@"<Adapter Name=""BH1"" Mac=""000105992C32"" Ports=""8"" /><Device Name=""BH1"" />", "used twice")]
        [InlineData(@"<Adapter Name=""A"" Mac=""000105992C32"" Ports=""8"" /><Adapter Name=""A"" Mac=""000105992C33"" Ports=""8"" />", "used twice")]
        public void AdapterLayout_RuleViolations_AreRefused(string body, string expectedText)
        {
            string path = WriteManifest("<IoTree>" + body + "</IoTree>");
            var ex = Assert.Throws<System.FormatException>(() => IoManifestParser.ParseManifest(path));
            Assert.Contains(expectedText, ex.Message);
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
        public void CreatePlcType_IsParsedWhenPresent_AndNullWhenAbsent()
        {
            string path = WriteManifest(@"
<IoTree>
  <Device Name=""BH2"">
    <Box Name=""EK1100_2.1"" Product=""EK1100"">
      <Terminal Name=""EL3174_2.1"" Product=""EL3174"" CreatePlcType=""Channel"" />
      <Terminal Name=""EL2008_2.1"" Product=""EL2008"" />
    </Box>
  </Device>
</IoTree>");
            IoNodeSpec coupler = IoManifestParser.Parse(path)[0].Children[0];

            Assert.Equal("Channel", coupler.Children[0].CreatePlcType);
            Assert.Null(coupler.Children[1].CreatePlcType);
        }

        [Fact]
        public void EsiIdentity_IsParsed_InEsiHexOrDecimal_AndNullWhenAbsent()   // M5.2: third-party boxes
        {
            string path = WriteManifest(@"
<IoTree>
  <Device Name=""BH5"">
    <Box Name=""Box 12 (CU2508)"" Product=""CU2508-0000-0000"" />
    <Box Name=""Box 11 (MARPOSS P3XF STANDARD)"" Product=""MARPOSS P3XF STANDARD"" VendorId=""#x00000916"" ProductCode=""0x4000"" RevisionNo=""65538"" />
  </Device>
</IoTree>");
            List<IoNodeSpec> boxes = IoManifestParser.Parse(path)[0].Children;

            Assert.Null(boxes[0].Identity);
            Assert.Equal("MARPOSS P3XF STANDARD", boxes[1].Product);
            Assert.Equal(0x916u, boxes[1].Identity.VendorId);
            Assert.Equal(0x4000u, boxes[1].Identity.ProductCode);
            Assert.Equal(0x10002u, boxes[1].Identity.RevisionNo);
        }

        [Theory]
        [InlineData(@"VendorId=""#x00000916"" ProductCode=""#x00004000""", "all three")]
        [InlineData(@"RevisionNo=""#x00010002""", "all three")]
        [InlineData(@"VendorId=""MARPOSS"" ProductCode=""#x00004000"" RevisionNo=""#x00010002""", "VendorId=\"MARPOSS\" is not a number")]
        [InlineData(@"VendorId=""#x916"" ProductCode=""#x"" RevisionNo=""#x00010002""", "ProductCode=\"#x\" is not a number")]
        [InlineData(@"VendorId=""#x916"" ProductCode=""#x4000"" RevisionNo=""-1""", "RevisionNo=\"-1\" is not a number")]
        public void EsiIdentity_PartialOrBad_IsRefused(string attributes, string expectedText)
        {
            string path = WriteManifest(@"<IoTree><Device Name=""BH5""><Box Name=""M"" " + attributes + @" /></Device></IoTree>");
            var ex = Assert.Throws<System.FormatException>(() => IoManifestParser.Parse(path));
            Assert.Contains(expectedText, ex.Message);
            Assert.Contains("box 'M'", ex.Message);
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
