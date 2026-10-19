using System.Xml.Linq;
using BeckhoffAutomationInterface.Sync;
using Xunit;

namespace BeckhoffAutomationInterface.Tests
{
    public class PdoConfigurationTests
    {
        const string Configuration = "<ProcessData><Sm3><Pdo>6912</Pdo></Sm3><TxPdo Sm='3'><Index>#x1b00</Index><Name>TxPDO</Name><Entry><Index>#x2001</Index><SubIndex>1</SubIndex><BitLen>16</BitLen><Name>Measure</Name><DataType>UINT</DataType></Entry><Entry><Index>#x2001</Index><SubIndex>2</SubIndex><BitLen>16</BitLen><Name>Setpoint</Name><DataType>UINT</DataType></Entry></TxPdo></ProcessData>";

        [Fact]
        public void RoundTripRetainsEntryOrderAndNativeFields()
        {
            var parsed = PdoConfiguration.Parse(XElement.Parse("<Box>" + Configuration + "</Box>"));
            Assert.Equal(Configuration, parsed.ToString(SaveOptions.DisableFormatting).Replace('"', '\''));
            var read = PdoConfiguration.Read("<TreeItem><EtherCAT><Slave>" + Configuration + "</Slave></EtherCAT></TreeItem>");
            Assert.True(PdoConfiguration.Matches(parsed, read));
            read.Element("TxPdo").Element("Index").Value = "6912";
            Assert.True(PdoConfiguration.Matches(parsed, read));
            var entries = read.Element("TxPdo").Elements("Entry").ToArray();
            entries[0].Remove();
            read.Element("TxPdo").Add(entries[0]);
            Assert.False(PdoConfiguration.Matches(parsed, read));
        }

        [Theory]
        [InlineData("<ProcessData/>")]
        [InlineData("<ProcessData><TxPdo><Index>-1</Index></TxPdo></ProcessData>")]
        [InlineData("<ProcessData><TxPdo><Index>6912</Index><Entry><Index>8193</Index><SubIndex>256</SubIndex><BitLen>16</BitLen></Entry></TxPdo></ProcessData>")]
        [InlineData("<ProcessData><TxPdo><Index>6912</Index></TxPdo><TxPdo><Index>#x1b00</Index></TxPdo></ProcessData>")]
        public void InvalidMappingRejected(string xml) => Assert.Throws<FormatException>(() => PdoConfiguration.Parse(XElement.Parse("<Box>" + xml + "</Box>")));

        [Fact]
        public void NativePaddingCanOmitSubindexButDataCannot()
        {
            var node = XElement.Parse("<Terminal><ProcessData><TxPdo><Index>#x1a00</Index><Entry><Index>#x0</Index><BitLen>5</BitLen></Entry></TxPdo></ProcessData></Terminal>");
            Assert.NotNull(PdoConfiguration.Parse(node));
            node.Descendants("Entry").Single().Element("Index").Value = "#x6000";
            Assert.Throws<FormatException>(() => PdoConfiguration.Parse(node));
            node.Descendants("Entry").Single().Element("Index").Value = "0";
            node.Descendants("BitLen").Single().Value = "0";
            Assert.Throws<FormatException>(() => PdoConfiguration.Parse(node));
        }

        [Fact]
        public void ExportOmitsOnlyDerivedProcessImageAddresses()
        {
            var section = XElement.Parse(Configuration);
            section.AddFirst(new XElement("Send", new XElement("BitStart", "312")));
            section.Element("TxPdo").Element("Entry").Add(new XElement("AdsInfo", new XElement("IndexOffset", "123")));
            var read = PdoConfiguration.Read("<TreeItem><EtherCAT><Slave>" + section + "</Slave></EtherCAT></TreeItem>");
            Assert.True(PdoConfiguration.Matches(XElement.Parse(Configuration), read));
            Assert.Empty(read.Descendants("AdsInfo"));
            Assert.Null(read.Element("Send"));
        }

        [Fact]
        public void ExtraMappingOrWrongSizeCannotPassReadback()
        {
            var expected = XElement.Parse(Configuration);
            var actual = XElement.Parse(Configuration);
            actual.Element("TxPdo").Element("Entry").Element("BitLen").Value = "32";
            Assert.False(PdoConfiguration.Matches(expected, actual));
            actual = XElement.Parse(Configuration);
            actual.Add(new XElement("RxPdo", new XElement("Index", "5888")));
            Assert.False(PdoConfiguration.Matches(expected, actual));
        }
    }
}
