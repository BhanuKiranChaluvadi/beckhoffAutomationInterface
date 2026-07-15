using System.Xml.Linq;
using BeckhoffAutomationInterface.Sync;
using Xunit;

namespace BeckhoffAutomationInterface.Tests
{
    public class TsprojEventClassEditorTests : System.IDisposable
    {
        readonly string _dir;
        readonly string _templatesFolder;
        readonly string _tsprojPath;

        public TsprojEventClassEditorTests()
        {
            _dir = Path.Combine(Path.GetTempPath(), "TsprojEventClassEditorTests_" + Guid.NewGuid());
            _templatesFolder = Path.Combine(_dir, "event-classes");
            Directory.CreateDirectory(_templatesFolder);
            _tsprojPath = Path.Combine(_dir, "Test.tsproj");
        }

        public void Dispose() => Directory.Delete(_dir, recursive: true);

        static readonly string MinimalTsproj = @"
<TcSmProject TcSmVersion=""1.0"">
  <DataTypes>
    <DataType>
      <Name GUID=""{EXISTING}"">SomePreExistingType</Name>
    </DataType>
  </DataTypes>
  <Project>
    <System>
      <Settings MaxCpus=""6"" />
    </System>
  </Project>
</TcSmProject>";

        static readonly string Template = @"
<PlcDataTypeTemplate>
  <DataTypes>
    <DataType>
      <Name GUID=""{70AB1C3F-056A-429D-8212-5E248E2724AB}"" PersistentType=""true"">BeckhoffLibEvents</Name>
      <EventId>
        <Name Id=""1"">Verbose</Name>
        <Severity>Verbose</Severity>
      </EventId>
    </DataType>
  </DataTypes>
</PlcDataTypeTemplate>";

        void WriteFixture()
        {
            File.WriteAllText(_tsprojPath, MinimalTsproj);
            File.WriteAllText(Path.Combine(_templatesFolder, "BeckhoffLibEvents.xml"), Template);
        }

        [Fact]
        public void NoMissingClasses_DoesNothing()
        {
            WriteFixture();
            string before = File.ReadAllText(_tsprojPath);

            EventClassEditResult result = TsprojEventClassEditor.Apply(_tsprojPath, new List<string>(), _templatesFolder);

            Assert.Empty(result.Applied);
            Assert.Empty(result.Warnings);
            Assert.Equal(before, File.ReadAllText(_tsprojPath));
            Assert.False(File.Exists(_tsprojPath + ".bak"));
        }

        [Fact]
        public void MissingClass_IsAddedToTopLevelDataTypesPool_NotUnderSystem()
        {
            WriteFixture();

            EventClassEditResult result = TsprojEventClassEditor.Apply(
                _tsprojPath, new List<string> { "BeckhoffLibEvents" }, _templatesFolder);

            Assert.Equal("BeckhoffLibEvents", Assert.Single(result.Applied));
            Assert.Empty(result.Warnings);
            Assert.True(File.Exists(_tsprojPath + ".bak"));

            XDocument doc = XDocument.Load(_tsprojPath);
            var dataTypeNames = doc.Root.Element("DataTypes").Elements("DataType")
                .Select(dt => (string)dt.Element("Name")).ToList();
            Assert.Contains("SomePreExistingType", dataTypeNames);
            Assert.Contains("BeckhoffLibEvents", dataTypeNames);

            // Confirms it did NOT go under Project/System (the four previously-failed
            // placements documented in docs/ideas/st-plc-bidirectional-sync.md).
            XElement systemEl = doc.Root.Element("Project").Element("System");
            Assert.Empty(systemEl.Elements("DataType"));
        }

        [Fact]
        public void SecondRun_IsIdempotent()
        {
            WriteFixture();
            var names = new List<string> { "BeckhoffLibEvents" };
            TsprojEventClassEditor.Apply(_tsprojPath, names, _templatesFolder);
            string afterFirstRun = File.ReadAllText(_tsprojPath);

            EventClassEditResult second = TsprojEventClassEditor.Apply(_tsprojPath, names, _templatesFolder);

            Assert.Equal("BeckhoffLibEvents (already present)", Assert.Single(second.Applied));
            Assert.Equal(afterFirstRun, File.ReadAllText(_tsprojPath));
        }

        [Fact]
        public void MissingTemplate_ReportsWarning_NoCrash()
        {
            File.WriteAllText(_tsprojPath, MinimalTsproj);

            EventClassEditResult result = TsprojEventClassEditor.Apply(
                _tsprojPath, new List<string> { "NoSuchEventClass" }, _templatesFolder);

            Assert.Empty(result.Applied);
            Assert.Contains("no event-classes/NoSuchEventClass.xml template found", Assert.Single(result.Warnings));
        }
    }
}
