using System.Xml.Linq;
using BeckhoffAutomationInterface.Sync;
using Xunit;

namespace BeckhoffAutomationInterface.Tests
{
    public class CoeStartupTests
    {
        const string Command = "<InitCmd><Transition>PS</Transition><Comment>range</Comment><Timeout>1000</Timeout><Ccs>1</Ccs><Index>#x8000</Index><SubIndex>19</SubIndex><Data>AB00</Data></InitCmd>";
        static XElement Terminal(string content) => XElement.Parse("<Terminal Name='analog' Product='EL3174'>" + content + "</Terminal>");
        static string Native(string commands) => "<TreeItem><EtherCAT><Slave><Mailbox><CoE><InitCmds>" + commands + "</InitCmds></CoE></Mailbox></Slave></EtherCAT></TreeItem>";

        [Fact]
        public void MissingAndEmptyHaveDifferentMeaning()
        {
            Assert.Null(CoeStartup.Parse(Terminal("")));
            Assert.Empty(CoeStartup.Parse(Terminal("<CoE/>")));
            Assert.Null(CoeStartup.Read("<TreeItem/>"));
            Assert.Empty(CoeStartup.Read(Native("")));
        }

        [Fact]
        public void NativeExportOmitsFixedCommandsAndKeepsUserOrder()
        {
            var fixedCommand = Command.Replace("<InitCmd>", "<InitCmd Fixed='true'>");
            var second = Command.Replace("AB00", "CD00");
            var result = CoeStartup.Read(Native(fixedCommand + Command + second));
            Assert.Equal(2, result.Count);
            Assert.Equal("ab00", result[0].Element("Data").Value);
            Assert.Equal("cd00", result[1].Element("Data").Value);
            Assert.Equal("32768", result[0].Element("Index").Value);
            Assert.False(CoeStartup.Equal(result, result.Reverse().ToArray()));
        }

        [Fact]
        public void NativeCdataDecimalAndFieldOrderMatchManifest()
        {
            var expected = CoeStartup.Parse(Terminal("<CoE>" + Command + "</CoE>"));
            var native = Command.Replace("#x8000", "32768").Replace("AB00", "ab00")
                .Replace("<Comment>range</Comment>", "<Comment><![CDATA[range]]></Comment>");
            Assert.True(CoeStartup.Equal(expected, CoeStartup.Read(Native(native))));
        }

        [Fact]
        public void WriterParserRoundTripPreservesExplicitCommandsAndEmptyList()
        {
            var path = Path.GetTempFileName();
            try {
                File.WriteAllText(path, "<IoTree><Device Name='bus'><Terminal Name='a' Product='EL3174'><CoE>" + Command +
                    "</CoE></Terminal><Terminal Name='b' Product='EL3174'><CoE/></Terminal></Device></IoTree>");
                var original = IoManifestParser.ParseManifest(path);
                IoManifestWriter.ToXml(original).Save(path);
                var restored = IoManifestParser.ParseManifest(path);
                Assert.True(CoeStartup.Equal(original.Devices[0].Children[0].CoeCommands, restored.Devices[0].Children[0].CoeCommands));
                Assert.Empty(restored.Devices[0].Children[1].CoeCommands);
            } finally { File.Delete(path); }
        }

        [Theory]
        [InlineData("<InitCmd>", "<InitCmd Fixed='true'>")]
        [InlineData("<InitCmd>", "<InitCmd Fixed='typo'>")]
        [InlineData("#x8000", "65536")]
        [InlineData("<SubIndex>19</SubIndex>", "<SubIndex>256</SubIndex>")]
        [InlineData("<SubIndex>19</SubIndex>", "")]
        [InlineData("<Index>#x8000</Index>", "<Index>1</Index><Index>2</Index>")]
        [InlineData("AB00", "ABC")]
        [InlineData("AB00", "ZZ00")]
        [InlineData("<Transition>PS</Transition>", "")]
        [InlineData("<Transition>PS</Transition>", "<Transition>BOGUS</Transition>")]
        [InlineData("<Ccs>1</Ccs>", "<Ccs>0</Ccs>")]
        [InlineData("<Timeout>1000</Timeout>", "<Timeout>-1</Timeout>")]
        public void InvalidCommandsFailBeforeCom(string from, string to)
        {
            Assert.Throws<FormatException>(() => CoeStartup.Parse(Terminal("<CoE>" + Command.Replace(from, to) + "</CoE>")));
        }

        [Theory]
        [InlineData("<CoE/><CoE/>")]
        [InlineData("<CoE><InitCmds/></CoE>")]
        [InlineData("<CoE Typo='1'/>")]
        public void InvalidSectionsAreNotSilentlyIgnored(string section) =>
            Assert.Throws<FormatException>(() => CoeStartup.Parse(Terminal(section)));
    }
}
