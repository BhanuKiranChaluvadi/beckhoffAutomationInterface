using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BeckhoffAutomationInterface.Sync;
using Xunit;

namespace BeckhoffAutomationInterface.Tests
{
    /// <summary>
    /// EventManifestWriter (reverse export). Uses a fixture .tsproj — the reversal is a
    /// pure file read, no COM. Verifies the event-class discriminator (&lt;EventId&gt; vs a
    /// plain data type) and that both artifacts round-trip through the FORWARD readers
    /// (EventManifestParser for events.xml, PlcDataTypeTemplate for the template file).
    /// </summary>
    public class EventManifestWriterTests : IDisposable
    {
        readonly string _dir;
        readonly string _tsproj;

        public EventManifestWriterTests()
        {
            _dir = Path.Combine(Path.GetTempPath(), "EventWriterTests_" + Guid.NewGuid());
            Directory.CreateDirectory(_dir);
            _tsproj = Path.Combine(_dir, "Test.tsproj");
            File.WriteAllText(_tsproj, Fixture);
        }

        public void Dispose() => Directory.Delete(_dir, recursive: true);

        const string Fixture = @"<?xml version=""1.0"" encoding=""utf-8""?>
<TcSmProject>
  <DataTypes>
    <DataType>
      <Name GUID=""{70AB1C3F-056A-429D-8212-5E248E2724AB}"" PersistentType=""true"">BeckhoffLibEvents</Name>
      <DisplayName TxtId=""""><![CDATA[Beckhoff Library Events]]></DisplayName>
      <EventId>
        <Name Id=""1"">Verbose</Name>
        <DisplayName TxtId=""""><![CDATA[{0} - {1}]]></DisplayName>
        <Severity>Verbose</Severity>
      </EventId>
      <EventId>
        <Name Id=""4"">Error</Name>
        <DisplayName TxtId=""""><![CDATA[something failed]]></DisplayName>
        <Severity>Error</Severity>
      </EventId>
    </DataType>
    <DataType>
      <Name GUID=""{11111111-1111-1111-1111-111111111111}"">ST_NotAnEventClass</Name>
      <SubItem><Name>a</Name><Type>INT</Type></SubItem>
    </DataType>
  </DataTypes>
</TcSmProject>";

        [Fact]
        public void ReadFromTsproj_SelectsOnlyEventClasses_NotPlainDataTypes()
        {
            List<ExportedEventClass> classes = EventManifestWriter.ReadFromTsproj(_tsproj);

            ExportedEventClass ec = Assert.Single(classes);
            Assert.Equal("BeckhoffLibEvents", ec.Name);
            Assert.Equal("{70AB1C3F-056A-429D-8212-5E248E2724AB}", ec.Guid);
            Assert.Equal(2, ec.Events.Count);
            Assert.Contains(ec.Events, e => e.Name == "Verbose" && e.Id == 1 && e.Severity == "Verbose" && e.Message == "{0} - {1}");
            Assert.Contains(ec.Events, e => e.Name == "Error" && e.Id == 4 && e.Message == "something failed");
        }

        [Fact]
        public void Export_ProducesEventsXml_RoundTrippingThroughEventManifestParser()
        {
            string eventsXml = Path.Combine(_dir, "events.xml");
            string classesFolder = Path.Combine(_dir, "event-classes");

            EventExportReport report = EventManifestWriter.Export(_tsproj, eventsXml, classesFolder);

            Assert.True(report.EventsXmlWritten);
            List<EventClassDefinition> parsed = EventManifestParser.Parse(eventsXml);
            EventClassDefinition ec = Assert.Single(parsed);
            Assert.Equal("BeckhoffLibEvents", ec.Name);
            Assert.Equal("{70AB1C3F-056A-429D-8212-5E248E2724AB}", ec.Guid);
            Assert.Equal(2, ec.Events.Count);
        }

        [Fact]
        public void Export_ProducesTemplate_RoundTrippingThroughPlcDataTypeTemplate()
        {
            string eventsXml = Path.Combine(_dir, "events.xml");
            string classesFolder = Path.Combine(_dir, "event-classes");

            EventManifestWriter.Export(_tsproj, eventsXml, classesFolder);

            // The forward editor loads exactly this shape and copies the <DataType> verbatim.
            PlcDataTypeTemplate template = PlcDataTypeTemplate.Load(classesFolder, "BeckhoffLibEvents");
            Assert.NotNull(template);
            System.Xml.Linq.XElement dataType = Assert.Single(template.DataTypes);
            // GUID (the crucial, must-be-real value) survives verbatim.
            Assert.Equal("{70AB1C3F-056A-429D-8212-5E248E2724AB}", (string)dataType.Element("Name").Attribute("GUID"));
            Assert.Equal(2, dataType.Elements("EventId").Count());
        }

        [Fact]
        public void Export_NoEventClasses_WritesNothing()
        {
            string emptyTsproj = Path.Combine(_dir, "Empty.tsproj");
            File.WriteAllText(emptyTsproj, "<?xml version=\"1.0\"?><TcSmProject><DataTypes /></TcSmProject>");
            string eventsXml = Path.Combine(_dir, "events2.xml");

            EventExportReport report = EventManifestWriter.Export(emptyTsproj, eventsXml, Path.Combine(_dir, "ec2"));

            Assert.False(report.EventsXmlWritten);
            Assert.False(File.Exists(eventsXml));
        }
    }
}
