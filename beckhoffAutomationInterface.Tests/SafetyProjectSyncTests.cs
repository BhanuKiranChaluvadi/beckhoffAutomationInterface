using BeckhoffAutomationInterface.Sync;
using Xunit;

namespace BeckhoffAutomationInterface.Tests
{
    /// <summary>The file side of the safety stage (BeckhoffLib tasks/2026-10-16-plc-release,
    /// S3.2c/d): plan, byte-equal copy, and the Standard In/Out channel check.</summary>
    public class SafetyProjectSyncTests : IDisposable
    {
        readonly string _dir;
        readonly string _source;
        readonly string _safety;
        readonly string _tsproj;

        const string Tsc = "﻿<?xml version=\"1.0\" encoding=\"utf-8\"?>\r\n<TargetSystemConfig Crc=\"4207553396\" Version=\"1.5\">\r\n"
            + "  <TargetSystemObjectId>50462754</TargetSystemObjectId>\r\n  <TargetSystemObjectName>Term 34 (EL1918)</TargetSystemObjectName>\r\n"
            + "  <TakeOverStandardAliasDeviceNamesInProcessImage>false</TakeOverStandardAliasDeviceNamesInProcessImage>\r\n</TargetSystemConfig>";

        static string Standard(int sdsId, string direction) =>
            "<SafetyDeviceDesc><AliasDeviceType><Type>1</Type></AliasDeviceType><SDSID>" + sdsId + "</SDSID><StandardAliasDevice>"
            + "<IO><Name>" + direction + "</Name><DataType>BIT</DataType><BitSize>1</BitSize></IO></StandardAliasDevice></SafetyDeviceDesc>";

        public SafetyProjectSyncTests()
        {
            _dir = Path.Combine(Path.GetTempPath(), "SafetyProjectSyncTests_" + Guid.NewGuid());
            _source = Path.Combine(_dir, "src");
            _safety = Path.Combine(_source, "safety", "M_Safety_prj");
            string alias = Path.Combine(_safety, "TwinSafeGroup1", "Alias Devices");
            Directory.CreateDirectory(alias);
            File.WriteAllText(Path.Combine(_safety, "M_Safety_prj.splcproj"), "<Project />");
            File.WriteAllText(Path.Combine(_safety, "TargetSystemConfig.xml"), Tsc);
            File.WriteAllText(Path.Combine(alias, "ErrorAcknowledgement.sds"), Standard(1, "In"));
            File.WriteAllText(Path.Combine(alias, "bSTOstate.sds"), Standard(8, "Out"));
            File.WriteAllText(Path.Combine(alias, "Term 35 (EL1918) - Module 1 (FSOE).sds"),
                "<SafetyDeviceDesc><SDSID>4</SDSID><SafetyAliasDevice><ObjectId>50921475</ObjectId></SafetyAliasDevice></SafetyDeviceDesc>");

            string projectDir = Path.Combine(_dir, "M", "M");
            Directory.CreateDirectory(projectDir);
            _tsproj = Path.Combine(projectDir, "M.tsproj");
            File.WriteAllText(_tsproj, @"<TcSmProject><Project><Io><Device Id=""1""><Name>BH3</Name>
<Box Id=""34""><Name>Term 33 (EK1100)</Name><EtherCAT PortABoxInfo=""#x00ffffff"" /></Box>
<Box Id=""35""><Name>Term 34 (EL1918)</Name><EtherCAT PortABoxInfo=""#x01000022"" /></Box>
<Box Id=""36""><Name>Term 35 (EL1918)</Name><EtherCAT PortABoxInfo=""#x01000023"" />
<Slot><Module Id=""#x03090003""><Name>Module 1 (FSOE)</Name></Module></Slot></Box>
</Device></Io></Project></TcSmProject>");
        }

        public void Dispose() => Directory.Delete(_dir, recursive: true);

        SafetyPlan Plan() => SafetyProjectSync.Plan(
            SafetyManifestParser.Parse(Write("<Safety Name=\"M_Safety_prj\" Folder=\"safety/M_Safety_prj\" />"), _source), _tsproj);

        string Write(string manifest)
        {
            string path = Path.Combine(_source, "safety.xml");
            File.WriteAllText(path, manifest);
            return path;
        }

        [Fact]
        public void Plan_ReadsIds_AndSeesTheIdChangeAndTheCopyThatAreNeeded()
        {
            SafetyPlan plan = Plan();

            Assert.Equal("Term 34 (EL1918)", plan.LogicTerminal.BoxName);
            Assert.Equal(34, plan.LogicTerminal.Id);
            Assert.Equal(2, plan.ObjectIds.Count); // the box and the FSoE module
            Assert.Equal(Path.Combine(_dir, "M", "M", "M_Safety_prj"), plan.ProjectFolder);
            Assert.Equal(Path.Combine(plan.ProjectFolder, "M_Safety_prj.splcproj"), plan.SplcprojPath);
            Assert.Equal(new[] { "Box 'Term 34 (EL1918)': Id 35 -> 34 ('Term 33 (EK1100)': 34 -> 35)" }, plan.IdChanges.Applied);
            Assert.Equal(5, plan.FilesToCopy.Count);
            Assert.True(plan.NeedsFileWork);
            Assert.Contains("Box Id=\"35\"><Name>Term 34", File.ReadAllText(_tsproj)); // a plan writes nothing
        }

        [Fact]
        public void Copy_IsByteEqual_KeepsWhatTwinCatAdded_AndThenNothingIsLeftToDo()
        {
            SafetyPlan plan = Plan();
            Directory.CreateDirectory(Path.Combine(plan.ProjectFolder, "GVLs"));
            File.WriteAllText(Path.Combine(plan.ProjectFolder, "GVLs", "made-by-xae.sal"), "x");

            List<string> copied = SafetyProjectSync.Copy(plan);
            TsprojObjectIdEditor.Apply(_tsproj, plan.ObjectIds);

            Assert.Equal(5, copied.Count);
            Assert.Empty(SafetyProjectSync.Differences(plan));
            Assert.Equal(File.ReadAllBytes(Path.Combine(_safety, "TargetSystemConfig.xml")),
                File.ReadAllBytes(Path.Combine(plan.ProjectFolder, "TargetSystemConfig.xml"))); // BOM and CRLF kept
            Assert.True(File.Exists(Path.Combine(plan.ProjectFolder, "GVLs", "made-by-xae.sal")));
            SafetyPlan second = Plan();
            Assert.False(second.NeedsFileWork);
            Assert.Empty(SafetyProjectSync.Copy(second));
        }

        [Fact]
        public void Differences_FindsAFileThatChangedAfterTheCopy()
        {
            SafetyPlan plan = Plan();
            SafetyProjectSync.Copy(plan);

            File.WriteAllText(Path.Combine(plan.ProjectFolder, "TargetSystemConfig.xml"), Tsc.Replace("4207553396", "1"));

            Assert.Equal(new[] { "TargetSystemConfig.xml" }, SafetyProjectSync.Differences(plan));
        }

        [Fact]
        public void Plan_WithAReplacementTargetSystemConfig_CopiesThatFile()
        {
            string other = Path.Combine(_source, "safety", "Other", "TargetSystemConfig.xml");
            Directory.CreateDirectory(Path.GetDirectoryName(other));
            File.WriteAllText(other, Tsc.Replace("4207553396", "123"));

            SafetyPlan plan = SafetyProjectSync.Plan(SafetyManifestParser.Parse(
                Write("<Safety Name=\"M_Safety_prj\" Folder=\"safety/M_Safety_prj\" TargetSystemConfig=\"safety/Other/TargetSystemConfig.xml\" />"), _source), _tsproj);
            SafetyProjectSync.Copy(plan);

            Assert.Equal(File.ReadAllBytes(other), File.ReadAllBytes(Path.Combine(plan.ProjectFolder, "TargetSystemConfig.xml")));
            Assert.Empty(SafetyProjectSync.Differences(plan));
        }

        // ------------------------------------------------------------ channels

        [Fact]
        public void ExpectedChannels_ComeFromTheStandardAliasDevices()
        {
            SafetyChannels channels = Plan().Channels;

            Assert.True(channels.NeedsInputs);
            Assert.True(channels.NeedsOutputs);
            Assert.Equal(new[] { "Standard Out Var 8" }, channels.StandardInputs);
            Assert.Equal(new[] { "Standard In Var 1" }, channels.StandardOutputs);
        }

        // ProduceXml of the EL1918 after a good add (spike S3.1 test C), cut down.
        const string GoodBoxXml = "<?xml version=\"1.0\"?><TreeItem><ItemName>Term 34 (EL1918)</ItemName><ObjectId>#x03020022</ObjectId>"
            + "<EtherCAT><ProcessData>"
            + "<TxPdo Sm=\"3\"><Index>#x1bf0</Index><Name>StandardInputs</Name><Entry><Name>Standard Out Var 8</Name></Entry><Entry><Name>Standard Out Var 9</Name></Entry><Entry><BitLen>6</BitLen></Entry></TxPdo>"
            + "<RxPdo Sm=\"2\"><Index>#x17f0</Index><Name>StandardOutputs</Name><Entry><Name>Standard In Var 1</Name></Entry></RxPdo>"
            + "</ProcessData></EtherCAT></TreeItem>";

        [Fact]
        public void CheckChannels_PassesWhenTheChannelsAreThere()
        {
            SafetyPlan plan = Plan();

            List<string> problems = SafetyProjectSync.CheckChannels(GoodBoxXml, plan.LogicTerminal, plan.Channels, out List<string> found);

            Assert.Empty(problems);
            Assert.Equal(new[] { "StandardInputs: Standard Out Var 8, Standard Out Var 9", "StandardOutputs: Standard In Var 1" }, found);
        }

        [Fact]
        public void CheckChannels_FailsWithNoChannels_TheSilentCaseOfTheSpike()
        {
            SafetyPlan plan = Plan();
            string noChannels = "<TreeItem><ObjectId>#x03020022</ObjectId><EtherCAT><ProcessData><TxPdo><Name>ConnectionInputs</Name></TxPdo></ProcessData></EtherCAT></TreeItem>";

            List<string> problems = SafetyProjectSync.CheckChannels(noChannels, plan.LogicTerminal, plan.Channels, out List<string> _);

            Assert.Contains("'Term 34 (EL1918)' has no StandardInputs channels", problems);
            Assert.Contains("'Term 34 (EL1918)' has no StandardOutputs channels", problems);
        }

        [Fact]
        public void CheckChannels_FailsOnAMissingChannelAndOnAWrongObjectId()
        {
            SafetyPlan plan = Plan();
            string xml = GoodBoxXml.Replace("#x03020022", "#x03020023").Replace("Standard In Var 1", "Standard In Var 2");

            List<string> problems = SafetyProjectSync.CheckChannels(xml, plan.LogicTerminal, plan.Channels, out List<string> _);

            Assert.Contains("'Term 34 (EL1918)' has object id #x03020023, the safety project expects #x03020022", problems);
            Assert.Contains("'Term 34 (EL1918)' StandardOutputs has no channel 'Standard In Var 1'", problems);
        }
    }
}
