using System.Collections.Generic;
using System.Linq;
using BeckhoffAutomationInterface.Sync;
using Xunit;

namespace BeckhoffAutomationInterface.Tests
{
    /// <summary>Variants attribute in io-devices.xml.</summary>
    public class IoVariantRulesTests : System.IDisposable
    {
        readonly string _dir;

        public IoVariantRulesTests()
        {
            _dir = Path.Combine(Path.GetTempPath(), "IoVariantRulesTests_" + Guid.NewGuid().ToString("N").Substring(0, 8));
            Directory.CreateDirectory(_dir);
        }

        public void Dispose() => Directory.Delete(_dir, recursive: true);

        static readonly VariantManifest TwoVariants = new VariantManifest(
            new[] { "MachineA", "MachineB" }, new Dictionary<string, IReadOnlyList<string>>());

        string Write(string xml)
        {
            string path = Path.Combine(_dir, "io-devices.xml");
            File.WriteAllText(path, xml);
            return path;
        }

        const string Manifest =
            "<IoTree>" +
            "<Device Name=\"Device 1 (EtherCAT)\">" +
            "  <Box Name=\"Term 1 (EK1100)\" Product=\"EK1100\">" +
            "    <Terminal Name=\"Term 2 (EL1008)\" Product=\"EL1008\" />" +
            "    <Terminal Name=\"Term 3 (EL2008)\" Product=\"EL2008\" Variants=\"MachineA\" />" +
            "  </Box>" +
            "</Device>" +
            "<Device Name=\"Device 2 (EtherCAT)\" Variants=\" MachineB , MachineA \" />" +
            "</IoTree>";

        [Fact]
        public void Parser_ReadsTheAttribute_AbsentMeansAllVariants()
        {
            List<IoDeviceSpec> devices = IoManifestParser.Parse(Write(Manifest));

            Assert.Null(devices[0].Variants);
            Assert.Null(devices[0].Children[0].Children[0].Variants);                 // EL1008: all
            Assert.Equal(new[] { "MachineA" }, devices[0].Children[0].Children[1].Variants);
            Assert.Equal(new[] { "MachineB", "MachineA" }, devices[1].Variants);      // trimmed
        }

        [Fact]
        public void ValidManifest_HasNoProblems_AndListsTheVariantItems()
        {
            List<IoDeviceSpec> devices = IoManifestParser.Parse(Write(Manifest));

            Assert.Empty(IoVariantRules.Validate(devices, TwoVariants));
            List<IoVariantItem> items = IoVariantRules.ItemsWithVariants(devices);
            Assert.Equal(2, items.Count);
            Assert.Equal("TIID^Device 1 (EtherCAT)^Term 1 (EK1100)^Term 3 (EL2008)", items[0].TreePath);
            Assert.True(items[0].EnabledIn("MachineA"));
            Assert.False(items[0].EnabledIn("MachineB"));
        }

        [Fact]
        public void UnknownVariant_IsReported()
        {
            var devices = new List<IoDeviceSpec> { new IoDeviceSpec("D", false, new List<IoNodeSpec>(), new[] { "Google" }) };

            Assert.Contains(IoVariantRules.Validate(devices, TwoVariants), p => p.Contains("'Google' is not declared"));
        }

        [Fact]
        public void ChildWiderThanParent_IsReported()
        {
            var terminal = new IoNodeSpec("T", "EL2008", new List<IoNodeSpec>(), variants: new[] { "MachineA", "MachineB" });
            var box = new IoNodeSpec("B", "EK1100", new List<IoNodeSpec> { terminal }, variants: new[] { "MachineA" });
            var devices = new List<IoDeviceSpec> { new IoDeviceSpec("D", false, new List<IoNodeSpec> { box }) };

            Assert.Contains(IoVariantRules.Validate(devices, TwoVariants), p => p.Contains("'TIID^D^B^T' is in variant 'MachineB', but its parent is not"));
        }

        [Fact]
        public void EmptyAttribute_IsReported()
        {
            var devices = new List<IoDeviceSpec> { new IoDeviceSpec("D", false, new List<IoNodeSpec>(), new string[0]) };

            Assert.Contains(IoVariantRules.Validate(devices, TwoVariants), p => p.Contains("empty Variants"));
        }

        [Fact]
        public void AttributeWithoutVariantsXml_IsReported()
        {
            var devices = new List<IoDeviceSpec> { new IoDeviceSpec("D", false, new List<IoNodeSpec>(), new[] { "MachineA" }) };

            Assert.Contains(IoVariantRules.Validate(devices, null), p => p.Contains("no variants.xml"));
        }

        [Fact]
        public void ManifestWithoutAttributes_IsUnaffected()
        {
            List<IoDeviceSpec> devices = IoManifestParser.Parse(Write(
                "<IoTree><Device Name=\"D\"><Terminal Name=\"T\" Product=\"EL1008\" /></Device></IoTree>"));

            Assert.Empty(IoVariantRules.Validate(devices, null));
            Assert.Empty(IoVariantRules.ItemsWithVariants(devices));
        }

        [Fact]
        public void LinkWithVariants_IsRejected_BecauseLinksAreGlobal()
        {
            string path = Write(
                "<IoTree><Links><Link PlcVar=\"P^PlcTask Outputs^GVL_IO.bOut\" IoChannel=\"D^T^Channel 1^Output\" Variants=\"MachineA\" /></Links></IoTree>");

            var ex = Assert.Throws<System.FormatException>(() => IoManifestParser.ParseLinks(path));
            Assert.Contains("Links are the same in every variant", ex.Message);
        }
    }
}
