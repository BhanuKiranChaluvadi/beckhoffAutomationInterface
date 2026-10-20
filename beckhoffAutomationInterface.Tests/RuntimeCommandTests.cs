using System.Collections.Generic;
using BeckhoffAutomationInterface.Runtime;
using Xunit;

namespace BeckhoffAutomationInterface.Tests
{
    /// <summary>Runtime targets and verify's COM/ADS-free parts.</summary>
    public class RuntimeCommandTests
    {
        // Real UmRT_Default TcRegistry.xml shape (TwinCAT 3.1.4026).
        const string TcRegistry =
            "<?xml version=\"1.0\"?><TcRegistry><Key Name=\"HKLM\"><Key Name=\"Software\"><Key Name=\"Beckhoff\">" +
            "<Key Name=\"TwinCAT3\"><Key Name=\"System\"><Value Name=\"RunAsDevice\" Type=\"DW\">1</Value>" +
            "<Value Name=\"AmsNetId\" Type=\"BIN\">C7042AFA0101</Value></Key></Key></Key></Key></Key></TcRegistry>";

        [Fact]
        public void RuntimeStateUsesSystemReconfigCommand()
        {
            Assert.Equal(16, RuntimeStateCommand.Command("config"));
            Assert.Equal(2, RuntimeStateCommand.Command("run"));
            Assert.Throws<System.ArgumentException>(() => RuntimeStateCommand.Command("stop"));
        }

        [Fact]
        public void UmRtNetId_IsReadFromTcRegistry()
        {
            Assert.Equal("199.4.42.250.1.1", RuntimeTargets.NetIdFromTcRegistry(TcRegistry));
        }

        [Fact]
        public void TcRegistry_WithoutAmsNetId_IsRejected()
        {
            Assert.Throws<System.FormatException>(() =>
                RuntimeTargets.NetIdFromTcRegistry("<TcRegistry><Value Name=\"Other\">1</Value></TcRegistry>"));
        }

        [Theory]
        [InlineData("umrt", "umrt")]
        [InlineData("UmRT", "umrt")]
        [InlineData(" local ", "local")]
        [InlineData("199.4.42.250.1.1", "199.4.42.250.1.1")]
        public void Targets_AreUmrtOrLocal(string input, string expected)
        {
            Assert.Equal(expected, RuntimeTargets.Normalize(input));
        }

        [Theory]
        [InlineData("256.4.42.250.1.1")]
        [InlineData("1.2.3.4")]
        [InlineData("1.2.3.4.5.-1")]
        [InlineData("plc42")]
        [InlineData(null)]
        public void OtherTargets_AreRejected(string input)
        {
            var ex = Assert.Throws<System.ArgumentException>(() => RuntimeTargets.Normalize(input));
            Assert.Contains("umrt, local", ex.Message);
        }

        [Fact]
        public void BootProjectInfo_IsDescribed()
        {
            const string json = "{\"project\":{\"name\":\"PLC_NFL_SHARK\",\"platform\":\"TwinCAT OS (x64)\",\"created\":\"2026-10-13T10:14:32\"}}";

            Assert.Equal("PLC_NFL_SHARK (activated 2026-10-13T10:14:32)", RuntimeTargets.DescribeProjectInfo(json));
        }

        static Dictionary<string, List<Expectation>> File(params string[] keys)
        {
            var d = new Dictionary<string, List<Expectation>>(System.StringComparer.OrdinalIgnoreCase);
            foreach (string k in keys)
                d[k] = new List<Expectation> { new Expectation { Symbol = k + ".x", Type = "INT", EqualsValue = 1 } };
            return d;
        }

        [Fact]
        public void SelectExpectations_ByVariant()
        {
            Assert.Equal("MachineB.x", RuntimeCommands.SelectExpectations(File("MachineA", "MachineB"), "machineb")[0].Symbol);
        }

        [Fact]
        public void SelectExpectations_WithoutVariant_UsesDefault()
        {
            Assert.Equal("default.x", RuntimeCommands.SelectExpectations(File("default"), null)[0].Symbol);
        }

        [Fact]
        public void SelectExpectations_WithoutVariant_AndNoDefault_AsksForVariant()
        {
            var ex = Assert.Throws<System.ArgumentException>(() => RuntimeCommands.SelectExpectations(File("MachineA", "MachineB"), null));
            Assert.Contains("choose --variant", ex.Message);
        }

        [Fact]
        public void SelectExpectations_UnknownVariant_ListsTheKnownOnes()
        {
            var ex = Assert.Throws<System.ArgumentException>(() => RuntimeCommands.SelectExpectations(File("MachineA"), "Google"));
            Assert.Contains("MachineA", ex.Message);
        }

        [Fact]
        public void CommandArgs_ParsesOptionsSwitchesAndPositional()
        {
            CommandArgs a = CommandArgs.Parse(new[] { "activate", "C:\\p", "--target", "umrt", "--yes" }, new[] { "--target" }, new[] { "--yes" });

            Assert.Equal("C:\\p", a.Positional);
            Assert.Equal("umrt", a.Get("--target"));
            Assert.True(a.Has("--yes"));
        }

        [Theory]
        [InlineData("--bogus")]
        [InlineData("--target")]   // missing value
        public void CommandArgs_RejectsUnknownOrIncomplete(string arg)
        {
            Assert.Throws<System.ArgumentException>(() =>
                CommandArgs.Parse(new[] { "verify", arg }, new[] { "--target" }, new string[0]));
        }

        [Fact]
        public void BootProject_ShowsAPlcOnlyUpdate()   // SAS after Shark: only Plc/Port_851* rewritten (2026-10-14)
        {
            var info = new System.DateTime(2026, 10, 14, 21, 29, 32);
            Assert.Equal("Shark (activated x)", RuntimeTargets.WithPlcUpdate("Shark (activated x)", info, info.AddSeconds(1)));
            Assert.Equal("Shark (activated x)", RuntimeTargets.WithPlcUpdate("Shark (activated x)", info, null));
            Assert.Equal("Shark (activated x); PLC (port 851) updated 2026-10-14T21:45:57",
                RuntimeTargets.WithPlcUpdate("Shark (activated x)", info, new System.DateTime(2026, 10, 14, 21, 45, 57)));
        }

        [Fact]
        public void ActivateTakesPlc_AnUnknownOptionIsRefused()
        {
            string[] options = { "--target", "--variant", "--plc", "--timeout" };

            Assert.Equal("P", CommandArgs.Parse(new[] { "activate", "x.tsproj", "--plc", "P" }, options, new[] { "--yes" }).Get("--plc"));
            var ex = Assert.Throws<System.ArgumentException>(() =>
                CommandArgs.Parse(new[] { "activate", "x.tsproj", "--plc-name", "P" }, options, new[] { "--yes" }));
            Assert.Contains("unrecognized argument: --plc-name", ex.Message);
        }
    }
}
