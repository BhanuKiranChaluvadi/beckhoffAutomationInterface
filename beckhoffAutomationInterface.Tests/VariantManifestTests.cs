using System.Xml.Linq;
using BeckhoffAutomationInterface.Sync;
using Xunit;

namespace BeckhoffAutomationInterface.Tests
{
    /// <summary>variants.xml (tasks/2026-10-14-variants-closed-loop, Task 5).</summary>
    public class VariantManifestTests
    {
        const string Valid =
            "<Variants DefineMode=\"Implicit\">\n" +
            "  <Variant Name=\"MachineA\" />\n" +
            "  <Variant Name=\"MachineB\" />\n" +
            "  <Group Name=\"PCV_MODBUS\"><Member>MachineA</Member></Group>\n" +
            "  <Group Name=\"PCV_ETHERCAT\"><Member>MachineB</Member></Group>\n" +
            "</Variants>";

        static VariantManifest Parse(string xml) =>
            VariantManifestParser.Parse(XDocument.Parse(xml, LoadOptions.SetLineInfo), "variants.xml");

        [Fact]
        public void Valid_ParsesVariantsAndGroups()
        {
            VariantManifest m = Parse(Valid);

            Assert.Equal(new[] { "MachineA", "MachineB" }, m.Variants);
            Assert.Equal(new[] { "MachineA" }, m.Groups["PCV_MODBUS"]);
            Assert.Equal(new[] { "MachineB" }, m.Groups["PCV_ETHERCAT"]);
        }

        [Fact]
        public void Valid_ProducesTheProjectVariantConfigXmlTheProtoypeProved()
        {
            // Same string the VariantDemo prototype passed to ProjectVariantConfig (2026-10-14).
            Assert.Equal(
                "<?xml version=\"1.0\"?><ProjectVariants>" +
                "<Group><Name>PCV_MODBUS</Name><Member>MachineA</Member></Group>" +
                "<Group><Name>PCV_ETHERCAT</Name><Member>MachineB</Member></Group>" +
                "<Variant><Name>MachineA</Name></Variant>" +
                "<Variant><Name>MachineB</Name></Variant>" +
                "</ProjectVariants>",
                Parse(Valid).ToProjectVariantConfigXml());
        }

        [Fact]
        public void ProjectVariantConfig_RoundTrips()
        {
            VariantManifest m = Parse(Valid);

            VariantManifest back = VariantManifest.FromProjectVariantConfigXml(m.ToProjectVariantConfigXml());

            Assert.True(m.SameAs(back));
        }

        [Fact]
        public void SameAs_IgnoresOrder_ButSeesAMembershipChange()
        {
            VariantManifest a = Parse(Valid);
            VariantManifest reordered = Parse(
                "<Variants><Variant Name=\"MachineB\"/><Variant Name=\"MachineA\"/>" +
                "<Group Name=\"PCV_ETHERCAT\"><Member>MachineB</Member></Group>" +
                "<Group Name=\"PCV_MODBUS\"><Member>MachineA</Member></Group></Variants>");
            VariantManifest moved = Parse(
                "<Variants><Variant Name=\"MachineA\"/><Variant Name=\"MachineB\"/>" +
                "<Group Name=\"PCV_MODBUS\"><Member>MachineB</Member></Group>" +
                "<Group Name=\"PCV_ETHERCAT\"><Member>MachineA</Member></Group></Variants>");

            Assert.True(a.SameAs(reordered));
            Assert.False(a.SameAs(moved));
        }

        [Fact]
        public void EmptyProjectConfig_HasNoVariants()
        {
            Assert.Empty(VariantManifest.FromProjectVariantConfigXml(null).Variants);
            Assert.Empty(VariantManifest.FromProjectVariantConfigXml("").Variants);
        }

        [Fact]
        public void MissingFile_ReturnsNull()
        {
            Assert.Null(VariantManifestParser.Parse(System.IO.Path.Combine(System.IO.Path.GetTempPath(), "no-such-dir-" + System.Guid.NewGuid(), "variants.xml")));
        }

        [Fact]
        public void DuplicateVariant_IsReportedWithLine()
        {
            var ex = Assert.Throws<VariantManifestException>(() => Parse(
                "<Variants>\n<Variant Name=\"SAS\"/>\n<Variant Name=\"SAS\"/>\n</Variants>"));

            Assert.Contains(ex.Problems, p => p.Contains("line 3") && p.Contains("'SAS'"));
        }

        [Fact]
        public void GroupNamedLikeAVariant_IsRejected()
        {
            var ex = Assert.Throws<VariantManifestException>(() => Parse(
                "<Variants><Variant Name=\"SAS\"/><Group Name=\"sas\"><Member>SAS</Member></Group></Variants>"));

            Assert.Contains(ex.Problems, p => p.Contains("used twice"));
        }

        [Fact]
        public void UnknownMember_IsRejected()
        {
            var ex = Assert.Throws<VariantManifestException>(() => Parse(
                "<Variants><Variant Name=\"SAS\"/><Group Name=\"OZONE\"><Member>Google</Member></Group></Variants>"));

            Assert.Contains(ex.Problems, p => p.Contains("'Google'") && p.Contains("not a declared"));
        }

        [Theory]
        [InlineData("2Fast")]
        [InlineData("SAS-2")]
        [InlineData("Machine A")]
        [InlineData("")]
        public void NameThatIsNotAPlcIdentifier_IsRejected(string name)
        {
            var ex = Assert.Throws<VariantManifestException>(() => Parse(
                "<Variants><Variant Name=\"" + name + "\"/></Variants>"));

            Assert.Contains(ex.Problems, p => p.Contains("not a valid PLC identifier"));
        }

        [Fact]
        public void UnsupportedDefineMode_IsRejected()
        {
            var ex = Assert.Throws<VariantManifestException>(() => Parse(
                "<Variants DefineMode=\"Manual\"><Variant Name=\"SAS\"/></Variants>"));

            Assert.Contains(ex.Problems, p => p.Contains("DefineMode"));
        }

        [Fact]
        public void AllProblemsAreReportedAtOnce()
        {
            var ex = Assert.Throws<VariantManifestException>(() => Parse(
                "<Variants><Variant Name=\"1A\"/><Variant Name=\"B\"/><Group Name=\"G\"><Member>X</Member></Group></Variants>"));

            Assert.Equal(2, ex.Problems.Count);
        }
    }
}
