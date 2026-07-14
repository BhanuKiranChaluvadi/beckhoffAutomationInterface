using System.Xml.Linq;
using BeckhoffAutomationInterface.Sync;
using Xunit;

namespace BeckhoffAutomationInterface.Tests
{
    public class TsprojPlcDataTypeEditorTests : System.IDisposable
    {
        readonly string _dir;
        readonly string _tsprojPath;

        public TsprojPlcDataTypeEditorTests()
        {
            _dir = Path.Combine(Path.GetTempPath(), "TsprojPlcDataTypeEditorTests_" + Guid.NewGuid());
            Directory.CreateDirectory(Path.Combine(_dir, "plc-data-types"));
            _tsprojPath = Path.Combine(_dir, "Test.tsproj");
        }

        public void Dispose() => Directory.Delete(_dir, recursive: true);

        static readonly string MinimalTsproj = @"
<TcSmProject TcSmVersion=""1.0"">
  <DataTypes>
    <DataType>
      <Name GUID=""{EXISTING}"">SomePreExistingType</Name>
      <BitSize>8</BitSize>
    </DataType>
  </DataTypes>
  <Io>
    <Device Id=""2"" RemoteName=""BH2"">
      <Name>BH2</Name>
      <Box Id=""11"">
        <Name>EL3174_2.1</Name>
        <EtherCAT VendorId=""#x00000002"" Type=""EL3174""/>
      </Box>
    </Device>
  </Io>
</TcSmProject>";

        static readonly string Template = @"
<PlcDataTypeTemplate>
  <DataTypes>
    <DataType>
      <Name GUID=""{D98A3B12-1241-5A12-F4B8-1F0F6D88A6F2}"">MDP5001_300_7E2119CA</Name>
      <BitSize>32</BitSize>
    </DataType>
  </DataTypes>
  <PlcDataTypes>
    <PlcDataType GUID=""{D98A3B12-1241-5A12-F4B8-1F0F6D88A6F2}"">MDP5001_300_7E2119CA</PlcDataType>
    <PlcDataType GUID=""{D98A3B12-1241-5A12-F4B8-1F0F6D88A6F2}"">MDP5001_300_7E2119CA</PlcDataType>
  </PlcDataTypes>
</PlcDataTypeTemplate>";

        void WriteFixture()
        {
            File.WriteAllText(_tsprojPath, MinimalTsproj);
            File.WriteAllText(Path.Combine(_dir, "plc-data-types", "EL3174.xml"), Template);
        }

        [Fact]
        public void NoTargets_DoesNothing()
        {
            WriteFixture();
            string before = File.ReadAllText(_tsprojPath);

            PlcDataTypeEditResult result = TsprojPlcDataTypeEditor.Apply(_tsprojPath, new List<PlcDataTypeTarget>(), _dir);

            Assert.Empty(result.Applied);
            Assert.Empty(result.Warnings);
            Assert.Equal(before, File.ReadAllText(_tsprojPath));
            Assert.False(File.Exists(_tsprojPath + ".bak"));
        }

        [Fact]
        public void MatchingBox_GetsCreateDeviceDataTypeAndMergedDataTypes()
        {
            WriteFixture();
            var targets = new List<PlcDataTypeTarget> { new PlcDataTypeTarget("EL3174_2.1", "EL3174", "Channel") };

            PlcDataTypeEditResult result = TsprojPlcDataTypeEditor.Apply(_tsprojPath, targets, _dir);

            Assert.Equal("EL3174_2.1", Assert.Single(result.Applied));
            Assert.Empty(result.Warnings);
            Assert.True(File.Exists(_tsprojPath + ".bak"));

            XDocument doc = XDocument.Load(_tsprojPath);
            XElement etherCat = doc.Descendants("Box")
                .Single(b => (string)b.Element("Name") == "EL3174_2.1")
                .Element("EtherCAT");
            Assert.True((bool)etherCat.Attribute("CreateDeviceDataType"));
            Assert.True((bool)etherCat.Attribute("DeviceDataTypePerChannel"));
            Assert.Equal(2, etherCat.Element("PlcDataTypes").Elements("PlcDataType").Count());

            // Pre-existing DataTypes pool entry survives, plus the new merged one.
            var dataTypeNames = doc.Root.Element("DataTypes").Elements("DataType")
                .Select(dt => (string)dt.Element("Name")).ToList();
            Assert.Contains("SomePreExistingType", dataTypeNames);
            Assert.Contains("MDP5001_300_7E2119CA", dataTypeNames);
        }

        [Fact]
        public void SecondRun_IsIdempotent_NoDuplicateDataTypesOrRewrite()
        {
            WriteFixture();
            var targets = new List<PlcDataTypeTarget> { new PlcDataTypeTarget("EL3174_2.1", "EL3174", "Channel") };
            TsprojPlcDataTypeEditor.Apply(_tsprojPath, targets, _dir);
            string afterFirstRun = File.ReadAllText(_tsprojPath);

            PlcDataTypeEditResult second = TsprojPlcDataTypeEditor.Apply(_tsprojPath, targets, _dir);

            Assert.Equal("EL3174_2.1 (already set)", Assert.Single(second.Applied));
            Assert.Equal(afterFirstRun, File.ReadAllText(_tsprojPath));

            XDocument doc = XDocument.Load(_tsprojPath);
            int matchingTypeCount = doc.Root.Element("DataTypes").Elements("DataType")
                .Count(dt => (string)dt.Element("Name") == "MDP5001_300_7E2119CA");
            Assert.Equal(1, matchingTypeCount);
        }

        [Fact]
        public void UnknownBoxName_ReportsWarning_NoCrash()
        {
            WriteFixture();
            var targets = new List<PlcDataTypeTarget> { new PlcDataTypeTarget("EL3174_9.9", "EL3174", "Channel") };

            PlcDataTypeEditResult result = TsprojPlcDataTypeEditor.Apply(_tsprojPath, targets, _dir);

            Assert.Empty(result.Applied);
            string warning = Assert.Single(result.Warnings);
            Assert.Contains("EL3174_9.9", warning);
            Assert.Contains("not found", warning);
        }

        [Fact]
        public void MissingTemplate_ReportsWarning_NoCrash()
        {
            File.WriteAllText(_tsprojPath, MinimalTsproj);
            // Deliberately not writing a template file for this product.
            var targets = new List<PlcDataTypeTarget> { new PlcDataTypeTarget("EL3174_2.1", "EL9999", "Channel") };

            PlcDataTypeEditResult result = TsprojPlcDataTypeEditor.Apply(_tsprojPath, targets, _dir);

            Assert.Empty(result.Applied);
            Assert.Contains("no plc-data-types/EL9999.xml template found", Assert.Single(result.Warnings));
        }
    }
}
