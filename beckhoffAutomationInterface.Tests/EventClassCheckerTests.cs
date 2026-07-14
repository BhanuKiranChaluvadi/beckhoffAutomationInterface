using BeckhoffAutomationInterface.Sync;
using Xunit;

namespace BeckhoffAutomationInterface.Tests
{
    public class EventClassCheckerTests : System.IDisposable
    {
        readonly string _dir;
        readonly string _tsprojPath;

        public EventClassCheckerTests()
        {
            _dir = Path.Combine(Path.GetTempPath(), "EventClassCheckerTests_" + Guid.NewGuid());
            Directory.CreateDirectory(_dir);
            _tsprojPath = Path.Combine(_dir, "Test.tsproj");
        }

        public void Dispose() => Directory.Delete(_dir, recursive: true);

        List<EventClassDefinition> OneDesiredClass(string name) =>
            new List<EventClassDefinition> { new EventClassDefinition(name, "{00000000-0000-0000-0000-000000000000}", new List<EventDefinition>()) };

        [Fact]
        public void MissingTsproj_ReportsEverythingMissing()
        {
            EventClassCheckReport report = EventClassChecker.Check(
                Path.Combine(_dir, "does-not-exist.tsproj"), OneDesiredClass("BeckhoffLibEvents"));

            Assert.Empty(report.Present);
            Assert.Equal("BeckhoffLibEvents", Assert.Single(report.Missing));
        }

        [Fact]
        public void EventClass_InTopLevelDataTypesPool_IsDetectedAsPresent()
        {
            // Real projects store Event Classes as ordinary <DataType> entries in the
            // TOP-LEVEL <DataTypes> pool -- NOT nested under <Project>/<System>, despite
            // the "SYSTEM > Type System > Event Classes" UI path suggesting otherwise
            // (confirmed against a real reference project, see tasks/todo.md Task 3).
            File.WriteAllText(_tsprojPath, @"
<TcSmProject TcSmVersion=""1.0"">
  <DataTypes>
    <DataType>
      <Name GUID=""{70AB1C3F-056A-429D-8212-5E248E2724AB}"" PersistentType=""true"">BeckhoffLibEvents</Name>
    </DataType>
  </DataTypes>
  <Project>
    <System>
      <Settings MaxCpus=""6"" />
    </System>
  </Project>
</TcSmProject>");

            EventClassCheckReport report = EventClassChecker.Check(_tsprojPath, OneDesiredClass("BeckhoffLibEvents"));

            Assert.Equal("BeckhoffLibEvents", Assert.Single(report.Present));
            Assert.Empty(report.Missing);
        }

        [Fact]
        public void EventClass_NotInDataTypesPool_IsReportedMissing()
        {
            File.WriteAllText(_tsprojPath, @"
<TcSmProject TcSmVersion=""1.0"">
  <DataTypes />
</TcSmProject>");

            EventClassCheckReport report = EventClassChecker.Check(_tsprojPath, OneDesiredClass("BeckhoffLibEvents"));

            Assert.Empty(report.Present);
            Assert.Equal("BeckhoffLibEvents", Assert.Single(report.Missing));
        }
    }
}
