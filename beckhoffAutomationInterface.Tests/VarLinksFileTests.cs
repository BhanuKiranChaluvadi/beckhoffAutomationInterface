using System.Collections.Generic;
using BeckhoffAutomationInterface.Sync;
using Xunit;

namespace BeckhoffAutomationInterface.Tests
{
    /// <summary>
    /// VarLinksFile parsing (tasks/plan.md: "Native links.xml (TwinCAT VarLinks format)").
    /// Fixture is trimmed from the real reference project's own exported file
    /// ("Spectrometer Instance Mappings.xml").
    /// </summary>
    public class VarLinksFileTests : System.IDisposable
    {
        readonly string _dir;

        public VarLinksFileTests()
        {
            _dir = Path.Combine(Path.GetTempPath(), "VarLinksFileTests_" + Guid.NewGuid());
            Directory.CreateDirectory(_dir);
        }

        public void Dispose() => Directory.Delete(_dir, recursive: true);

        string WriteLinksXml(string content)
        {
            string path = Path.Combine(_dir, "links.xml");
            File.WriteAllText(path, content);
            return path;
        }

        const string RealTrimmedFixture = @"<?xml version=""1.0""?>
<VarLinks>
	<OwnerA Name=""InputDst"" Prefix=""TIPC^Spectrometer^Spectrometer Instance"" Type=""1"">
		<OwnerB Name=""TIID^Device 6 (EtherCAT)^Box 18 (MARPOSS P3XF STANDARD)"">
			<Link VarA=""MAIN.fbSpec.inLogicSig[1]"" GrpA=""PlcTask Inputs"" TypeA=""USINT"" InOutA=""0"" GuidA=""{18071995-0000-0000-0000-000000000002}"" VarB=""Transmit PDO Mapping^OUT 001""/>
			<Link VarA=""MAIN.fbSpec.stWatchesRaw^AckErr"" GrpA=""PlcTask Inputs"" TypeA=""UDINT"" InOutA=""0"" ParentTypeA=""ST_WatchesRaw"" GuidA=""{18071995-0000-0000-0000-000000000008}"" VarB=""Transmit PDO Mapping^WTC 017""/>
		</OwnerB>
	</OwnerA>
	<OwnerA Name=""OutputSrc"" Prefix=""TIPC^Spectrometer^Spectrometer Instance"" Type=""2"">
		<OwnerB Name=""TIID^Device 6 (EtherCAT)^Box 18 (MARPOSS P3XF STANDARD)"">
			<Link VarA=""MAIN.fbSpec.outLogicSig[1]"" GrpA=""PlcTask Outputs"" TypeA=""USINT"" InOutA=""1"" GuidA=""{18071995-0000-0000-0000-000000000002}"" VarB=""Receive PDO Mapping^IN 001""/>
		</OwnerB>
	</OwnerA>
</VarLinks>
";

        [Fact]
        public void MissingFile_ParseReturnsEmptyList_NotNull()
        {
            List<VarLinkEntry> entries = VarLinksFile.Parse(Path.Combine(_dir, "links.xml"));

            Assert.NotNull(entries);
            Assert.Empty(entries);
        }

        [Fact]
        public void MissingFile_LoadRawXmlReturnsNull()
        {
            Assert.Null(VarLinksFile.LoadRawXml(Path.Combine(_dir, "links.xml")));
        }

        [Fact]
        public void ParsesOwnerAOwnerBLink_Correctly()
        {
            string path = WriteLinksXml(RealTrimmedFixture);

            List<VarLinkEntry> entries = VarLinksFile.Parse(path);

            Assert.Equal(3, entries.Count);
            VarLinkEntry first = entries[0];
            Assert.Equal("TIPC^Spectrometer^Spectrometer Instance", first.PlcInstancePrefix);
            Assert.Equal("PlcTask Inputs", first.Group);
            Assert.Equal("MAIN.fbSpec.inLogicSig[1]", first.VarA);
            Assert.Equal("TIID^Device 6 (EtherCAT)^Box 18 (MARPOSS P3XF STANDARD)", first.IoOwnerName);
            Assert.Equal("Transmit PDO Mapping^OUT 001", first.VarB);
        }

        [Fact]
        public void LoadRawXml_ReturnsFileContentVerbatim()
        {
            string path = WriteLinksXml(RealTrimmedFixture);

            Assert.Equal(RealTrimmedFixture, VarLinksFile.LoadRawXml(path));
        }

        [Fact]
        public void NestedFbInstanceVarA_HasNullSimpleKey()
        {
            string path = WriteLinksXml(RealTrimmedFixture);

            List<VarLinkEntry> entries = VarLinksFile.Parse(path);

            Assert.All(entries, e => Assert.Null(e.SimpleKey));
        }

        [Fact]
        public void StructMemberVarA_WithCaret_HasNullSimpleKey()
        {
            string xml = @"<VarLinks><OwnerA Prefix=""TIPC^X^X Instance""><OwnerB Name=""TIID^Y"">" +
                @"<Link VarA=""MAIN.fbSpec.stWatchesRaw^AckErr"" GrpA=""PlcTask Inputs"" VarB=""B""/>" +
                @"</OwnerB></OwnerA></VarLinks>";
            string path = WriteLinksXml(xml);

            VarLinkEntry entry = VarLinksFile.Parse(path)[0];

            Assert.Null(entry.SimpleKey);
        }

        [Fact]
        public void NoGrpAAttribute_GroupFoldedIntoVarA_IsNormalizedCorrectly()
        {
            // Shape used by Beckhoff's own official CodeGenerationDemo sample
            // (Templates/MachineTypeA/Links.xml): OwnerA has only Name (no Prefix), and
            // Link has no GrpA -- the group is folded directly into VarA instead.
            string xml = @"<VarLinks><OwnerA Name=""TIPC^MachineTypeA^MachineTypeA Instance"">" +
                @"<OwnerB Name=""TIID^EtherCAT Master^Term 1^Term 3"">" +
                @"<Link VarA=""PlcTask Outputs^MAIN.bError"" VarB=""Channel 2^Output""/>" +
                @"</OwnerB></OwnerA></VarLinks>";
            string path = WriteLinksXml(xml);

            VarLinkEntry entry = VarLinksFile.Parse(path)[0];

            Assert.Equal("TIPC^MachineTypeA^MachineTypeA Instance", entry.PlcInstancePrefix);
            Assert.Equal("PlcTask Outputs", entry.Group);
            Assert.Equal("MAIN.bError", entry.VarA);
            Assert.Equal("MAIN.bError", entry.SimpleKey);
        }

        [Fact]
        public void SimpleTopLevelVarA_HasNonNullSimpleKeyMatchingVarA()
        {
            string xml = @"<VarLinks><OwnerA Prefix=""TIPC^Shark^Shark Instance""><OwnerB Name=""TIID^Device 1"">" +
                @"<Link VarA=""GVL_Shark.bMotorRunSensor"" GrpA=""PlcTask Inputs"" VarB=""Device 1^Channel 1^Input""/>" +
                @"</OwnerB></OwnerA></VarLinks>";
            string path = WriteLinksXml(xml);

            VarLinkEntry entry = VarLinksFile.Parse(path)[0];

            Assert.Equal("GVL_Shark.bMotorRunSensor", entry.SimpleKey);
        }
    }
}
