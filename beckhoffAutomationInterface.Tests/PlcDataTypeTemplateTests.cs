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

        [Theory]
        [InlineData("EL3214-0000-0017", "EL3214")]
        [InlineData("EL3214", "EL3214")]
        [InlineData("EX260-SEC1", "EX260-SEC1")]
        public void WithoutRevision_StripsOnlyTheRevision(string product, string expected)
        {
            Assert.Equal(expected, PlcDataTypeTemplate.WithoutRevision(product));
        }

        [Fact]
        public void ProductWithRevision_UsesTheProductTemplate_UnlessItHasItsOwn()
        {
            string real = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "TestData", "plc-data-types");
            foreach (string f in Directory.GetFiles(real))
                File.Copy(f, Path.Combine(_dir, "plc-data-types", Path.GetFileName(f)));

            Assert.NotNull(PlcDataTypeTemplate.Load(Path.Combine(_dir, "plc-data-types"), "EL3214-0000-0017"));
            Assert.Null(PlcDataTypeTemplate.Load(Path.Combine(_dir, "plc-data-types"), "EL9999-0000-0017"));
        }

        [Fact]
        public void RealTemplates_ParseWithoutError()
        {
            // Real TwinCAT-generated templates (EL3174/EL3214, from the Shark project) — a
            // regression check that the loader reads real files, not only hand-made ones.
            string plcDataTypesDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "TestData", "plc-data-types");

            foreach (string product in new[] { "EL3174", "EL3214" })
            {
                PlcDataTypeTemplate template = PlcDataTypeTemplate.Load(plcDataTypesDir, product);
                Assert.NotNull(template);
                Assert.NotEmpty(template.DataTypes);
                Assert.Equal(5, template.PlcDataTypes.Count);
            }
        }
    }
}
