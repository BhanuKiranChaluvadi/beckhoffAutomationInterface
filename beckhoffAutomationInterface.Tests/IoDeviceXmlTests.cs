using BeckhoffAutomationInterface.Sync;
using Xunit;

namespace BeckhoffAutomationInterface.Tests
{
    /// <summary>
    /// The COM-free part of the adapter/port layout. The XML is ProduceXml output captured in
    /// the R2.1 spike (TwinCAT 3.1.4026, scratch Shark project, 2026-10-17), shortened to the
    /// elements that matter.
    /// </summary>
    public class IoDeviceXmlTests
    {
        // Adapter after CreateChild(name, 109) + the ConsumeXml of IoDeviceXml.AdapterConsumeXml.
        const string AdapterXml =
            "<?xml version=\"1.0\"?><TreeItem><ItemName>Device 1 (RT-Ethernet Adapter)</ItemName><PathName>TIID^Device 1 (RT-Ethernet Adapter)</PathName>" +
            "<ItemType>2</ItemType><ItemId>1</ItemId><ObjectId>#x03010010</ObjectId><ItemSubType>109</ItemSubType>" +
            "<ItemSubTypeName>Real-Time Ethernet Adapter (Multiple Protocol Handler)</ItemSubTypeName><ChildCount>0</ChildCount><Disabled>false</Disabled>" +
            "<DeviceDef><AmsPort>0</AmsPort><AddressInfo><Pnp><DeviceDesc>Ethernet 5 (TwinCAT-Intel PCI Ethernet Adapter (Gig</DeviceDesc>" +
            "<DeviceName>\\DEVICE\\{B031B203-E423-45B3-89B7-350F7AB392F9}</DeviceName><DeviceData>000105992c32</DeviceData></Pnp></AddressInfo></DeviceDef>" +
            "<EthernetAdapter><PortSelectionType>Esl</PortSelectionType><Ports><Count>8</Count>" +
            "<Port><Name>Device 1 (RT-Ethernet Adapter) - Port 1</Name><Oid>50397201</Oid></Port>" +
            "<Port><Name>Device 1 (RT-Ethernet Adapter) - Port 2</Name><Oid>50397202</Oid></Port>" +
            "<Port><Name>Device 1 (RT-Ethernet Adapter) - Port 3</Name><Oid>50397203</Oid></Port>" +
            "<Port><Name>Device 1 (RT-Ethernet Adapter) - Port 4</Name><Oid>50397204</Oid></Port>" +
            "<Port><Name>Device 1 (RT-Ethernet Adapter) - Port 5</Name><Oid>50397205</Oid></Port>" +
            "<Port><Name>Device 1 (RT-Ethernet Adapter) - Port 6</Name><Oid>50397206</Oid></Port>" +
            "<Port><Name>Device 1 (RT-Ethernet Adapter) - Port 7</Name><Oid>50397207</Oid></Port>" +
            "<Port><Name>Device 1 (RT-Ethernet Adapter) - Port 8</Name><Oid>50397208</Oid></Port>" +
            "</Ports><TcpIpPort>0</TcpIpPort><PromiscuousMode>false</PromiscuousMode><EslSyncMaster/><EslSyncSlave/></EthernetAdapter></TreeItem>";

        // A new adapter, before the ConsumeXml.
        const string NewAdapterXml =
            "<?xml version=\"1.0\"?><TreeItem><ItemName>Device 1 (RT-Ethernet Adapter)</ItemName><ItemType>2</ItemType><ItemSubType>109</ItemSubType>" +
            "<DeviceDef><AmsPort>0</AmsPort><AddressInfo><Pnp><DeviceDesc></DeviceDesc><DeviceName></DeviceName><DeviceData>000000000000</DeviceData></Pnp></AddressInfo></DeviceDef>" +
            "<EthernetAdapter><PortSelectionType>None</PortSelectionType><Ports><Count>1</Count><Port><Name>Device 1 (RT-Ethernet Adapter) - Port 1</Name><Oid>50397248</Oid></Port></Ports>" +
            "<TcpIpPort>1</TcpIpPort></EthernetAdapter></TreeItem>";

        // BH1 after CreateChild("BH1", 111) + PortConsumeXml(port 1).
        const string OnPortXml =
            "<?xml version=\"1.0\"?><TreeItem><ItemName>BH1</ItemName><PathName>TIID^BH1</PathName><ItemType>2</ItemType><ObjectId>#x03010020</ObjectId>" +
            "<ItemSubType>111</ItemSubType><ItemSubTypeName>EtherCAT Master</ItemSubTypeName>" +
            "<DeviceDef><AmsPort>28674</AmsPort><AmsNetId>192.168.1.81.3.1</AmsNetId><AddressInfo><TcCom><ObjectId>#x03010011</ObjectId></TcCom></AddressInfo><ScanBoxes>false</ScanBoxes></DeviceDef></TreeItem>";

        // A new SubType 111 device, not on a port yet.
        const string NewOnPortXml =
            "<?xml version=\"1.0\"?><TreeItem><ItemName>BHX</ItemName><ItemType>2</ItemType><ItemSubType>111</ItemSubType><ItemSubTypeName>EtherCAT Master</ItemSubTypeName>" +
            "<DeviceDef><AmsPort>28677</AmsPort><AddressInfo><Pnp><DeviceDesc></DeviceDesc><DeviceName></DeviceName><DeviceData>000000000000</DeviceData></Pnp></AddressInfo><ScanBoxes>false</ScanBoxes></DeviceDef></TreeItem>";

        // A DevType 94 master made by bai with a test adapter (its COM ItemSubType says 111).
        const string MasterXml =
            "<?xml version=\"1.0\"?><TreeItem><ItemName>BH1</ItemName><PathName>TIID^BH1</PathName><ItemType>2</ItemType><ObjectId>#x03010010</ObjectId><ItemSubType>94</ItemSubType>" +
            "<DeviceDef><AmsPort>28673</AmsPort><AmsNetId>192.168.1.81.2.1</AmsNetId><AddressInfo><Pnp><DeviceDesc></DeviceDesc><DeviceName></DeviceName><DeviceData>02000000b101</DeviceData></Pnp></AddressInfo><ScanBoxes>false</ScanBoxes></DeviceDef></TreeItem>";

        [Fact]
        public void KindOf_TellsAdapterMasterAndPortDeviceApart()
        {
            Assert.Equal(IoDeviceKind.Adapter, IoDeviceXml.KindOf(AdapterXml));
            Assert.Equal(IoDeviceKind.Adapter, IoDeviceXml.KindOf(NewAdapterXml));
            Assert.Equal(IoDeviceKind.OnPort, IoDeviceXml.KindOf(OnPortXml));
            Assert.Equal(IoDeviceKind.OnPort, IoDeviceXml.KindOf(NewOnPortXml));   // Pnp address, but SubType 111
            Assert.Equal(IoDeviceKind.Master, IoDeviceXml.KindOf(MasterXml));
            Assert.Equal(IoDeviceKind.Other, IoDeviceXml.KindOf("<TreeItem><ItemSubType>3</ItemSubType></TreeItem>"));
            Assert.Equal(IoDeviceKind.Other, IoDeviceXml.KindOf(""));
        }

        [Fact]
        public void PortOids_AreReadFromTheAdapter_PortOneFirst()
        {
            List<uint> oids = IoDeviceXml.PortOidsOf(AdapterXml);

            Assert.Equal(8, oids.Count);
            Assert.Equal("#x03010011", IoDeviceXml.FormatObjectId(oids[0]));
            Assert.Equal("#x03010018", IoDeviceXml.FormatObjectId(oids[7]));
            Assert.Equal(8, IoDeviceXml.PortCountOf(AdapterXml));
            Assert.Empty(IoDeviceXml.PortOidsOf(MasterXml));
        }

        [Fact]
        public void TcComObjectId_IsReadFromAPortDevice_AndNullForAMaster()
        {
            Assert.Equal(0x03010011u, IoDeviceXml.TcComObjectIdOf(OnPortXml));
            Assert.Null(IoDeviceXml.TcComObjectIdOf(NewOnPortXml));
            Assert.Null(IoDeviceXml.TcComObjectIdOf(MasterXml));
            Assert.Equal(50397201u, IoDeviceXml.ParseObjectId("50397201"));
            Assert.Null(IoDeviceXml.ParseObjectId("#xZZ"));
        }

        [Fact]
        public void AdapterMatches_OnlyWhenMacPortsEslAndTcpIpPortAreAsDeclared()
        {
            var spec = new IoAdapterSpec("Device 1 (RT-Ethernet Adapter)", "000105992C32", 8);
            Assert.True(IoDeviceXml.AdapterMatches(spec, AdapterXml));         // a second sync changes nothing
            Assert.False(IoDeviceXml.AdapterMatches(spec, NewAdapterXml));
            Assert.False(IoDeviceXml.AdapterMatches(new IoAdapterSpec("A", "000105992C33", 8), AdapterXml));
            Assert.False(IoDeviceXml.AdapterMatches(new IoAdapterSpec("A", "000105992C32", 4), AdapterXml));
            Assert.True(IoDeviceXml.AdapterMatches(new IoAdapterSpec("A", "000105992C32", 8, "Ethernet 5 (TwinCAT-Intel PCI Ethernet Adapter (Gig"), AdapterXml));
            Assert.False(IoDeviceXml.AdapterMatches(new IoAdapterSpec("A", "000105992C32", 8, "Ethernet 6"), AdapterXml));
            Assert.Equal("Ethernet 5 (TwinCAT-Intel PCI Ethernet Adapter (Gig", IoDeviceXml.DeviceDescOf(AdapterXml));
            Assert.Null(IoDeviceXml.DeviceDescOf(NewAdapterXml));
        }

        [Fact]
        public void ConsumeXml_IsTheVerifiedSpikeXml()
        {
            Assert.Equal(
                "<TreeItem><DeviceDef><AddressInfo><Pnp><DeviceData>000105992c32</DeviceData></Pnp></AddressInfo></DeviceDef>" +
                "<EthernetAdapter><PortSelectionType>Esl</PortSelectionType><Ports><Count>8</Count></Ports><TcpIpPort>0</TcpIpPort></EthernetAdapter></TreeItem>",
                IoDeviceXml.AdapterConsumeXml(new IoAdapterSpec("A", "000105992C32", 8)));
            Assert.Contains("<Pnp><DeviceDesc>Ethernet &amp; 5</DeviceDesc><DeviceData>",
                IoDeviceXml.AdapterConsumeXml(new IoAdapterSpec("A", "000105992C32", 8, "Ethernet & 5")));
            Assert.Equal("<TreeItem><DeviceDef><AddressInfo><TcCom><ObjectId>#x03010011</ObjectId></TcCom></AddressInfo></DeviceDef></TreeItem>",
                IoDeviceXml.PortConsumeXml(50397201));
        }
    }
}
