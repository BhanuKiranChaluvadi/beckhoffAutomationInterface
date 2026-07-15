using BeckhoffAutomationInterface.Sync;
using Xunit;

namespace BeckhoffAutomationInterface.Tests
{
    public class PlcDataTypeTemplateTests : System.IDisposable
    {
        readonly string _dir;

        public PlcDataTypeTemplateTests()
        {
            _dir = Path.Combine(Path.GetTempPath(), "PlcDataTypeTemplateTests_" + Guid.NewGuid());
            Directory.CreateDirectory(Path.Combine(_dir, "plc-data-types"));
        }

        public void Dispose() => Directory.Delete(_dir, recursive: true);

        [Fact]
        public void MissingTemplate_ReturnsNull()
        {
            Assert.Null(PlcDataTypeTemplate.Load(Path.Combine(_dir, "plc-data-types"), "EL9999"));
        }

        [Fact]
        public void Load_ParsesDataTypesAndPlcDataTypes()
        {
            File.WriteAllText(Path.Combine(_dir, "plc-data-types", "EL3174.xml"), @"
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
</PlcDataTypeTemplate>");

            PlcDataTypeTemplate template = PlcDataTypeTemplate.Load(Path.Combine(_dir, "plc-data-types"), "EL3174");

            Assert.NotNull(template);
            Assert.Single(template.DataTypes);
            Assert.Equal("MDP5001_300_7E2119CA", (string)template.DataTypes[0].Element("Name"));
            Assert.Equal(2, template.PlcDataTypes.Count);
        }

        [Fact]
        public void RealShippedTemplates_ParseWithoutError()
        {
            // The templates actually shipped for ST/Shark (see io-devices.xml Task 3) --
            // a lightweight regression check that hand-edits keep them well-formed.
            string repoRoot = FindRepoRoot();
            string plcDataTypesDir = Path.Combine(repoRoot, "ST", "Shark", "plc-data-types");

            foreach (string product in new[] { "EL3174", "EL3214" })
            {
                PlcDataTypeTemplate template = PlcDataTypeTemplate.Load(plcDataTypesDir, product);
                Assert.NotNull(template);
                Assert.NotEmpty(template.DataTypes);
                Assert.Equal(5, template.PlcDataTypes.Count);
            }
        }

        static string FindRepoRoot()
        {
            string dir = AppDomain.CurrentDomain.BaseDirectory;
            while (dir != null && !Directory.Exists(Path.Combine(dir, "ST", "Shark")))
                dir = Path.GetDirectoryName(dir.TrimEnd(Path.DirectorySeparatorChar));
            return dir ?? throw new DirectoryNotFoundException("Could not locate repo root (ST/Shark) from " + AppDomain.CurrentDomain.BaseDirectory);
        }
    }
}
