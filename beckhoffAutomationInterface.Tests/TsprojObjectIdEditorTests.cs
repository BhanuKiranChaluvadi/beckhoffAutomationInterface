using System.Xml.Linq;
using BeckhoffAutomationInterface.Sync;
using Xunit;

namespace BeckhoffAutomationInterface.Tests
{
    /// <summary>Box/module ids from the safety files (BeckhoffLib tasks/2026-10-16-plc-release,
    /// S3.2b). The fixture is the Shark case of spike S3.1: the generated project gave the
    /// EL1918 logic terminal "Term 34" Box Id 35, the safety project expects 34.</summary>
    public class TsprojObjectIdEditorTests : IDisposable
    {
        readonly string _dir;

        public TsprojObjectIdEditorTests()
        {
            _dir = Path.Combine(Path.GetTempPath(), "TsprojObjectIdEditorTests_" + Guid.NewGuid());
            Directory.CreateDirectory(_dir);
        }

        public void Dispose() => Directory.Delete(_dir, recursive: true);

        // Box ids and topology as bai generated them for Shark (spike test B), cut down.
        const string GeneratedTsproj = @"<TcSmProject>
  <Project>
    <Io>
      <Device Id=""3"">
        <Name>BH3</Name>
        <Box Id=""33"" BoxType=""9099"">
          <Name>Box 32 (CU2508)</Name>
          <EtherCAT Type=""CU2508"" PortABoxInfo=""#x00ffffff"" />
        </Box>
        <Box Id=""34"" BoxType=""9099"">
          <Name>Term 33 (EK1100)</Name>
          <EtherCAT Type=""EK1100"" PortABoxInfo=""#x01000021"" />
        </Box>
        <Box Id=""35"" BoxType=""9099"">
          <Name>Term 34 (EL1918)</Name>
          <EtherCAT Type=""EL1918"" PortABoxInfo=""#x01000022"" />
          <Slot Name=""FSLOGIC""><Module Id=""#x03090002"" Type=""FSLOGIC""><Name>Module 2 (FSLOGIC)</Name></Module></Slot>
        </Box>
        <Box Id=""36"" BoxType=""9099"">
          <Name>Term 35 (EL1918)</Name>
          <EtherCAT Type=""EL1918"" PortABoxInfo=""#x01000023"" />
          <Slot Name=""FSOE-Slot""><Module Id=""#x03090003"" Type=""FSOE""><Name>Module 1 (FSOE)</Name></Module></Slot>
          <Slot Name=""FSLOGIC""><Module Id=""#x03090004"" Type=""FSLOGIC""><Name>Module 2 (FSLOGIC)</Name></Module></Slot>
        </Box>
        <Box Id=""37"" BoxType=""9099"">
          <Name>Term 36 (EL2904)</Name>
          <EtherCAT Type=""EL2904"" PortABoxInfo=""#x01000024"" PortBBoxInfo=""#x02000023"" />
          <Slot Name=""FSOES""><Module Id=""#x03090005"" Type=""FSOES""><Name>Module 1 (FSOES)</Name></Module></Slot>
        </Box>
      </Device>
    </Io>
  </Project>
</TcSmProject>";

        static XDocument Doc() => XDocument.Parse(GeneratedTsproj, LoadOptions.PreserveWhitespace);

        static SafetyObjectId Box(string name, int id) => new SafetyObjectId(SafetyObjectKind.Box, name, null, id, "TargetSystemConfig.xml");

        static SafetyObjectId Module(string box, string module, int id) =>
            new SafetyObjectId(SafetyObjectKind.Module, box, module, id, box + " - " + module + ".sds");

        static string IdOf(XDocument doc, string boxName) =>
            (string)doc.Descendants("Box").Single(b => (string)b.Element("Name") == boxName).Attribute("Id");

        static string PortA(XDocument doc, string boxName) =>
            (string)doc.Descendants("Box").Single(b => (string)b.Element("Name") == boxName).Element("EtherCAT").Attribute("PortABoxInfo");

        static string ModuleId(XDocument doc, string boxName, string moduleName) =>
            (string)doc.Descendants("Box").Single(b => (string)b.Element("Name") == boxName)
                .Descendants("Module").Single(m => (string)m.Element("Name") == moduleName).Attribute("Id");

        // ------------------------------------------------------------ reading the safety files

        [Fact]
        public void Read_TakesTheLogicTerminalFromTargetSystemConfig_AndModulesFromAliasDeviceFileNames()
        {
            string tsc = Path.Combine(_dir, "TargetSystemConfig.xml");
            File.WriteAllText(tsc, "<TargetSystemConfig><TargetSystemObjectId>50462754</TargetSystemObjectId>"
                + "<TargetSystemObjectName>Term 34 (EL1918)</TargetSystemObjectName></TargetSystemConfig>");
            string fsoe = Path.Combine(_dir, "Term 35 (EL1918) - Module 1 (FSOE).sds");
            File.WriteAllText(fsoe, "<SafetyDeviceDesc><SafetyAliasDevice><ObjectId>50921475</ObjectId></SafetyAliasDevice></SafetyDeviceDesc>");
            string cpu = Path.Combine(_dir, "EL1918_CPU.sds");
            File.WriteAllText(cpu, "<SafetyDeviceDesc><SafetyAliasDevice><ObjectId>0</ObjectId></SafetyAliasDevice></SafetyDeviceDesc>");
            string standard = Path.Combine(_dir, "Run.sds");
            File.WriteAllText(standard, "<SafetyDeviceDesc><StandardAliasDevice><IO><Name>In</Name></IO></StandardAliasDevice></SafetyDeviceDesc>");

            List<SafetyObjectId> ids = SafetyObjectIds.Read(tsc, new[] { fsoe, cpu, standard });

            Assert.Equal(2, ids.Count);
            Assert.Equal(SafetyObjectKind.Box, ids[0].Kind);
            Assert.Equal("Term 34 (EL1918)", ids[0].BoxName);
            Assert.Equal(34, ids[0].Id); // 50462754 = #x03020022
            Assert.Equal(SafetyObjectKind.Module, ids[1].Kind);
            Assert.Equal("Term 35 (EL1918)", ids[1].BoxName);
            Assert.Equal("Module 1 (FSOE)", ids[1].ModuleName);
            Assert.Equal(0x03090003, ids[1].Id);
        }

        [Fact]
        public void Read_RefusesATargetSystemObjectIdThatIsNotABox()
        {
            string tsc = Path.Combine(_dir, "TargetSystemConfig.xml");
            File.WriteAllText(tsc, "<TargetSystemConfig><TargetSystemObjectId>50921475</TargetSystemObjectId>"
                + "<TargetSystemObjectName>X</TargetSystemObjectName></TargetSystemConfig>");

            var ex = Assert.Throws<InvalidOperationException>(() => SafetyObjectIds.Read(tsc, new string[0]));

            Assert.Contains("is not an EtherCAT box", ex.Message);
        }

        [Fact]
        public void Read_RefusesAModuleIdInAFileNameWithoutTheModule()
        {
            string tsc = Path.Combine(_dir, "TargetSystemConfig.xml");
            File.WriteAllText(tsc, "<TargetSystemConfig><TargetSystemObjectId>50462754</TargetSystemObjectId>"
                + "<TargetSystemObjectName>Term 34 (EL1918)</TargetSystemObjectName></TargetSystemConfig>");
            string sds = Path.Combine(_dir, "Term 35 (EL1918).sds");
            File.WriteAllText(sds, "<SafetyDeviceDesc><SafetyAliasDevice><ObjectId>50921475</ObjectId></SafetyAliasDevice></SafetyDeviceDesc>");

            var ex = Assert.Throws<InvalidOperationException>(() => SafetyObjectIds.Read(tsc, new[] { sds }));

            Assert.Contains("'<box> - <module>.sds'", ex.Message);
        }

        // ------------------------------------------------------------ boxes

        [Fact]
        public void Box_GetsTheId_TheBoxThatHadItTakesTheFreedId_AndPortReferencesFollow()
        {
            XDocument doc = Doc();

            ObjectIdEditResult result = TsprojObjectIdEditor.Edit(doc, new[] { Box("Term 34 (EL1918)", 34) });

            Assert.Empty(result.Problems);
            Assert.Equal(new[] { "Box 'Term 34 (EL1918)': Id 35 -> 34 ('Term 33 (EK1100)': 34 -> 35)" }, result.Applied);
            Assert.Equal("34", IdOf(doc, "Term 34 (EL1918)"));
            Assert.Equal("35", IdOf(doc, "Term 33 (EK1100)"));
            Assert.Equal("#x01000023", PortA(doc, "Term 34 (EL1918)"));  // still its EK1100, now 35
            Assert.Equal("#x01000022", PortA(doc, "Term 35 (EL1918)"));  // still Term 34, now 34
            Assert.Equal("#x01000021", PortA(doc, "Term 33 (EK1100)"));  // the CU2508 did not move
            Assert.Equal("#x00ffffff", PortA(doc, "Box 32 (CU2508)"));   // "no box" stays
            Assert.Equal("#x01000024", PortA(doc, "Term 36 (EL2904)"));
            Assert.Equal("#x02000022", (string)doc.Descendants("Box").Single(b => (string)b.Element("Name") == "Term 36 (EL2904)")
                .Element("EtherCAT").Attribute("PortBBoxInfo")); // other ports follow too (35 -> 34)
        }

        [Fact]
        public void Box_WithAFreeId_IsRenumberedAlone()
        {
            XDocument doc = Doc();

            ObjectIdEditResult result = TsprojObjectIdEditor.Edit(doc, new[] { Box("Term 34 (EL1918)", 200) });

            Assert.Equal(new[] { "Box 'Term 34 (EL1918)': Id 35 -> 200" }, result.Applied);
            Assert.Equal("200", IdOf(doc, "Term 34 (EL1918)"));
            Assert.Equal("34", IdOf(doc, "Term 33 (EK1100)"));
            Assert.Equal("#x010000c8", PortA(doc, "Term 35 (EL1918)"));
        }

        [Fact]
        public void SecondRun_ChangesNothing()
        {
            XDocument doc = Doc();
            SafetyObjectId[] wanted = { Box("Term 34 (EL1918)", 34), Module("Term 36 (EL2904)", "Module 1 (FSOES)", 0x03090007) };
            TsprojObjectIdEditor.Edit(doc, wanted);
            string once = doc.ToString();

            ObjectIdEditResult again = TsprojObjectIdEditor.Edit(doc, wanted);

            Assert.False(again.Changed);
            Assert.Empty(again.Problems);
            Assert.Equal(once, doc.ToString());
        }

        [Fact]
        public void TwoBoxes_InAChain_BothGetTheirIds()
        {
            XDocument doc = Doc();

            ObjectIdEditResult result = TsprojObjectIdEditor.Edit(doc, new[] { Box("Term 34 (EL1918)", 34), Box("Term 35 (EL1918)", 35) });

            Assert.Empty(result.Problems);
            Assert.Equal("34", IdOf(doc, "Term 34 (EL1918)"));
            Assert.Equal("35", IdOf(doc, "Term 35 (EL1918)"));
            Assert.Equal("36", IdOf(doc, "Term 33 (EK1100)"));
            Assert.Equal("#x01000024", PortA(doc, "Term 34 (EL1918)")); // the EK1100, now 36
            Assert.Equal("#x01000022", PortA(doc, "Term 35 (EL1918)")); // Term 34
            Assert.Equal("#x01000023", PortA(doc, "Term 36 (EL2904)")); // Term 35
        }

        [Fact]
        public void UnknownBox_IsAProblem()
        {
            ObjectIdEditResult result = TsprojObjectIdEditor.Edit(Doc(), new[] { Box("Term 99 (EL1918)", 99) });

            Assert.Contains(result.Problems, p => p.Contains("0 box(es) named 'Term 99 (EL1918)'"));
            Assert.False(result.Changed);
        }

        [Fact]
        public void TwoFilesAskingForTheSameId_IsAProblem()
        {
            ObjectIdEditResult result = TsprojObjectIdEditor.Edit(Doc(), new[] { Box("Term 34 (EL1918)", 34), Box("Term 35 (EL1918)", 34) });

            Assert.Contains(result.Problems, p => p.Contains("ask for the same id"));
        }

        // ------------------------------------------------------------ modules

        [Fact]
        public void Module_GetsTheId_InsideItsBox_AndTheHolderSwaps()
        {
            XDocument doc = Doc();

            ObjectIdEditResult result = TsprojObjectIdEditor.Edit(doc, new[] { Module("Term 36 (EL2904)", "Module 1 (FSOES)", 0x03090003) });

            Assert.Empty(result.Problems);
            Assert.Equal("#x03090003", ModuleId(doc, "Term 36 (EL2904)", "Module 1 (FSOES)"));
            Assert.Equal("#x03090005", ModuleId(doc, "Term 35 (EL1918)", "Module 1 (FSOE)"));
            Assert.Equal("#x03090004", ModuleId(doc, "Term 35 (EL1918)", "Module 2 (FSLOGIC)"));
        }

        [Fact]
        public void Module_IsFoundInItsOwnBoxOnly()
        {
            // "Module 2 (FSLOGIC)" exists in Term 34 and Term 35: the box name decides.
            XDocument doc = Doc();

            TsprojObjectIdEditor.Edit(doc, new[] { Module("Term 35 (EL1918)", "Module 2 (FSLOGIC)", 0x03090010) });

            Assert.Equal("#x03090010", ModuleId(doc, "Term 35 (EL1918)", "Module 2 (FSLOGIC)"));
            Assert.Equal("#x03090002", ModuleId(doc, "Term 34 (EL1918)", "Module 2 (FSLOGIC)"));
        }

        [Fact]
        public void UnknownModule_IsAProblem()
        {
            ObjectIdEditResult result = TsprojObjectIdEditor.Edit(Doc(), new[] { Module("Term 36 (EL2904)", "Module 9 (X)", 0x03090003) });

            Assert.Contains(result.Problems, p => p.Contains("0 module(s) named 'Module 9 (X)' in box 'Term 36 (EL2904)'"));
        }

        // ------------------------------------------------------------ the file

        [Fact]
        public void Apply_WritesTheFileWithABackup_DryRunAndNoChangeDoNot()
        {
            string tsproj = Path.Combine(_dir, "M.tsproj");
            File.WriteAllText(tsproj, GeneratedTsproj);
            SafetyObjectId[] wanted = { Box("Term 34 (EL1918)", 34) };

            ObjectIdEditResult dry = TsprojObjectIdEditor.Apply(tsproj, wanted, dryRun: true);
            Assert.True(dry.Changed);
            Assert.Equal(GeneratedTsproj, File.ReadAllText(tsproj));
            Assert.False(File.Exists(tsproj + ".bak"));

            TsprojObjectIdEditor.Apply(tsproj, wanted);
            Assert.Equal("34", IdOf(XDocument.Load(tsproj), "Term 34 (EL1918)"));
            Assert.Equal(GeneratedTsproj, File.ReadAllText(tsproj + ".bak"));

            string written = File.ReadAllText(tsproj);
            ObjectIdEditResult again = TsprojObjectIdEditor.Apply(tsproj, wanted);
            Assert.False(again.Changed);
            Assert.Equal(written, File.ReadAllText(tsproj));
        }

        [Fact]
        public void Apply_WithAProblem_WritesNothing()
        {
            string tsproj = Path.Combine(_dir, "M.tsproj");
            File.WriteAllText(tsproj, GeneratedTsproj);

            ObjectIdEditResult result = TsprojObjectIdEditor.Apply(tsproj, new[] { Box("Term 34 (EL1918)", 34), Box("Term 99 (EL1918)", 99) });

            Assert.NotEmpty(result.Problems);
            Assert.Equal(GeneratedTsproj, File.ReadAllText(tsproj));
        }
    }
}
