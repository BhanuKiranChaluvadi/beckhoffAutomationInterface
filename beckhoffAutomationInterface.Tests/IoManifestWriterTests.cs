using BeckhoffAutomationInterface.Sync;
using Xunit;

namespace BeckhoffAutomationInterface.Tests
{
    /// <summary>
    /// The COM-free core of IoManifestWriter — DeriveProduct(name, itemSubTypeName, ...),
    /// the one piece with no exact read-back API. Test data is real device Names taken
    /// directly from the live PLC_NFL_SHARK_V2 production project's on-disk .xti item
    /// names (tasks/2026-07-15-reverse-export-scaffold/, live validation 2026-07-16) —
    /// not synthetic examples — including the case that disproved a pure regex-code
    /// approach: "Box 40 (EX260-SEC1)" would truncate to "EX260" under a bare
    /// "prefix+digits" regex, which is wrong; the whole hyphenated string is the real
    /// Festo catalog product.
    /// </summary>
    public class IoManifestWriterTests
    {
        [Theory]
        [InlineData("Term 13 (EL3174)", "EL3174")]
        [InlineData("Box 1 (CU2508)", "CU2508")]
        [InlineData("Term 12 (EK1100)", "EK1100")]
        [InlineData("Box 40 (EX260-SEC1)", "EX260-SEC1")] // hyphenated non-Beckhoff catalog string, NOT truncated to "EX260"
        [InlineData("Device 1 (RT-Ethernet Adapter)", "RT-Ethernet Adapter")] // multi-word catalog string, used verbatim
        public void DeriveProduct_PrefersTrailingParenthetical_UsedVerbatim_NotHeuristic(string name, string expectedProduct)
        {
            string product = IoManifestWriter.DeriveProduct(name, itemSubTypeName: "irrelevant", out bool heuristic);

            Assert.Equal(expectedProduct, product);
            Assert.False(heuristic); // Name's parenthetical is TwinCAT's own confirmed convention
        }

        [Theory]
        [InlineData("EK1100_1.1", "EK1100")]
        [InlineData("EL2008_1.6", "EL2008")]
        [InlineData("EL9011_1.1", "EL9011")]
        [InlineData("EL9505_1.1", "EL9505")]
        public void DeriveProduct_FallsBackToCodeEmbeddedInName_WhenNoParenthetical(string name, string expectedProduct)
        {
            string product = IoManifestWriter.DeriveProduct(name, itemSubTypeName: "irrelevant", out bool heuristic);

            Assert.Equal(expectedProduct, product);
            Assert.True(heuristic); // no parenthetical — this is a fallback guess, flagged for review
        }

        [Theory]
        [InlineData("Box 1 (CU2508)", "CU2508 Ethernet Port", "CU2508-0000-0000")]            // the real box on an adapter port
        [InlineData("Box 48 (CU2508-0022)", "CU2508-0022 Ethernet Port", "CU2508-0022-0000")]
        [InlineData("Box 44 (CU2508)", "CU2508 Configuration Port", "CU2508")]               // the other box keeps its short name
        public void DeriveProduct_Cu2508EthernetPort_UsesTheFullProductString(string name, string itemSubTypeName, string expectedProduct)
        {
            // R2.1 spike, 2026-10-17: Product="CU2508" makes "CU2508 Configuration Port" (an EtherCAT
            // slave); the real projects have "CU2508 Ethernet Port" = CU2508-0000-0000.
            string product = IoManifestWriter.DeriveProduct(name, itemSubTypeName, out bool heuristic);

            Assert.Equal(expectedProduct, product);
            Assert.False(heuristic);
        }

        [Fact]
        public void ToXml_RoundTripsThroughTheParser_BothLayouts()
        {
            string dir = Path.Combine(Path.GetTempPath(), "IoManifestWriterTests_" + Guid.NewGuid());
            Directory.CreateDirectory(dir);
            try
            {
                string source = Path.Combine(dir, "source.xml");
                File.WriteAllText(source, @"
<IoTree>
  <Adapter Name=""Device 1 (RT-Ethernet Adapter)"" Mac=""000105992C32"" Ports=""8"" DeviceDesc=""Ethernet 5"" />
  <Adapter Name=""Spare"" Mac=""000105992C33"" Ports=""2"" Disabled=""true"" />
  <Device Name=""BH1"" Via=""Device 1 (RT-Ethernet Adapter)"" Port=""1"">
    <Box Name=""Box 1 (CU2508)"" Product=""CU2508-0000-0000"" />
    <Box Name=""EK1100_1.1"" Product=""EK1100-0000-0018"">
      <Terminal Name=""EL3174_1.1"" Product=""EL3174-0000-0017"" CreatePlcType=""Channel"" />
      <Terminal Name=""EL2008_1.1"" Product=""EL2008"" Variants=""A"" />
    </Box>
  </Device>
  <Device Name=""BH2"" Disabled=""true"" Via=""Device 1 (RT-Ethernet Adapter)"" Port=""8"">
    <Box Name=""Box 12 (CU2508)"" Product=""CU2508-0000-0000"" />
    <Box Name=""Box 11 (MARPOSS P3XF STANDARD)"" Product=""MARPOSS P3XF STANDARD"" VendorId=""0x916"" ProductCode=""16384"" RevisionNo=""#x00010002"" />
  </Device>
  <Device Name=""BH 3 (Safety)"" Adapter=""02000000B301"" Variants=""A,B"" />
</IoTree>");
                IoManifest first = IoManifestParser.ParseManifest(source);

                string written = Path.Combine(dir, "written.xml");
                IoManifestWriter.ToXml(first).Save(written);
                IoManifest second = IoManifestParser.ParseManifest(written);

                Assert.Equal(Describe(first), Describe(second));
                Assert.Contains("Adapter Name=\"Device 1 (RT-Ethernet Adapter)\" Mac=\"000105992C32\" Ports=\"8\"", File.ReadAllText(written));
                Assert.Contains("Via=\"Device 1 (RT-Ethernet Adapter)\" Port=\"1\"", File.ReadAllText(written));
                // The ESI identity is written back in ESI notation (M5.2: third-party boxes).
                Assert.Contains("Product=\"MARPOSS P3XF STANDARD\" VendorId=\"#x00000916\" ProductCode=\"#x00004000\" RevisionNo=\"#x00010002\"", File.ReadAllText(written));
                Assert.DoesNotContain("CU2508-0000-0000\" VendorId", File.ReadAllText(written));
            }
            finally
            {
                Directory.Delete(dir, recursive: true);
            }
        }

        /// <summary>Every parsed field, one line per item, for a deep compare.</summary>
        static string Describe(IoManifest manifest)
        {
            var lines = new List<string>();
            foreach (IoAdapterSpec a in manifest.Adapters)
                lines.Add($"A|{a.Name}|{a.Mac}|{a.Ports}|{a.DeviceDesc}|{a.Disabled}");
            foreach (IoDeviceSpec d in manifest.Devices)
            {
                lines.Add($"D|{d.Name}|{d.Disabled}|{Join(d.Variants)}|{d.Adapter}|{d.Via}|{d.Port}");
                DescribeNodes(d.Children, "  ", lines);
            }
            return string.Join("|", lines);
        }

        static void DescribeNodes(IReadOnlyList<IoNodeSpec> nodes, string indent, List<string> lines)
        {
            foreach (IoNodeSpec n in nodes)
            {
                lines.Add($"{indent}N|{n.Name}|{n.Product}|{n.CreatePlcType}|{Join(n.Variants)}|{n.Identity}");
                DescribeNodes(n.Children, indent + "  ", lines);
            }
        }

        static string Join(IReadOnlyList<string> values) => values == null ? "<all>" : string.Join(",", values);

        [Fact]
        public void DeriveProduct_FlagsPureCustomLabelWithNoProductInfo_ForReview()
        {
            // "BH1" — a real purely-organizational label from the production project,
            // carrying no product information at all.
            string product = IoManifestWriter.DeriveProduct("BH1", itemSubTypeName: "", out bool heuristic);

            Assert.True(heuristic);
        }

        [Fact]
        public void DeriveProduct_FallsBackToItemSubTypeName_WhenNameHasNoUsableInfo()
        {
            string product = IoManifestWriter.DeriveProduct("BH2", "EL1008 8Ch. Dig. Input 24V, 3ms", out bool heuristic);

            Assert.Equal("EL1008", product); // code-shape match against ItemSubTypeName
            Assert.True(heuristic);
        }
    }
}
