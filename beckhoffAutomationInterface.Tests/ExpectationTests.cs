using System.Collections.Generic;
using BeckhoffAutomationInterface.Runtime;
using Xunit;

namespace BeckhoffAutomationInterface.Tests
{
    /// <summary>expect.json and the live-value checks.</summary>
    public class ExpectationTests
    {
        const string Json = @"{
  ""variants"": {
    ""MachineA"": [
      { ""symbol"": ""MAIN.sMachine"", ""type"": ""STRING(16)"", ""equals"": ""MachineA"" },
      { ""symbol"": ""MAIN.rMaxPressure"", ""type"": ""REAL"", ""equals"": 3.0 },
      { ""symbol"": ""MAIN.bDoorsFitted"", ""type"": ""BOOL"", ""equals"": false },
      { ""symbol"": ""PRG_PcvModbus.nCycles"", ""type"": ""UDINT"", ""increases"": true },
      { ""symbol"": ""PRG_PcvEtherCAT.nCycles"", ""type"": ""UDINT"", ""absentOrZero"": true }
    ]
  },
  ""default"": [ { ""symbol"": ""MAIN.x"", ""type"": ""INT"", ""equals"": 1 } ]
}";

        /// <summary>Fake PLC: symbol -> successive raw values; missing symbol = ADS 1808.</summary>
        sealed class FakePlc
        {
            readonly Dictionary<string, Queue<byte[]>> _values = new Dictionary<string, Queue<byte[]>>();

            public FakePlc Set(string symbol, params byte[][] values)
            {
                _values[symbol] = new Queue<byte[]>(values);
                return this;
            }

            public int Read(string symbol, int size, out byte[] data)
            {
                data = null;
                if (!_values.TryGetValue(symbol, out Queue<byte[]> q))
                    return AdsClient.ErrorSymbolNotFound;
                byte[] v = q.Count > 1 ? q.Dequeue() : q.Peek();
                data = new byte[size];
                System.Array.Copy(v, data, System.Math.Min(v.Length, size));
                return 0;
            }
        }

        static byte[] Str(string s) => System.Text.Encoding.ASCII.GetBytes(s + "\0");
        static byte[] U32(uint v) => System.BitConverter.GetBytes(v);
        static byte[] F32(float v) => System.BitConverter.GetBytes(v);

        [Fact]
        public void Parse_ReadsVariantsAndDefault()
        {
            var file = ExpectationFile.Parse(Json);

            Assert.Equal(5, file["MachineA"].Count);
            Assert.Equal(5, file["machinea"].Count); // variant keys are case-insensitive
            Assert.Single(file[ExpectationFile.DefaultKey]);
        }

        [Fact]
        public void AllChecksPass_OnTheExpectedRunningVariant()
        {
            var plc = new FakePlc()
                .Set("MAIN.sMachine", Str("MachineA"))
                .Set("MAIN.rMaxPressure", F32(3.0f))
                .Set("MAIN.bDoorsFitted", new byte[] { 0 })
                .Set("PRG_PcvModbus.nCycles", U32(115), U32(165));
            // PRG_PcvEtherCAT is not linked into MachineA's program: ADS 1808.

            var results = ExpectationChecker.Check(ExpectationFile.Parse(Json)["MachineA"], plc.Read, ms => { });

            Assert.All(results, r => Assert.True(r.Passed, r.Symbol + ": " + r.Actual));
            Assert.Equal("115 -> 165", results[3].Actual);
            Assert.Equal("not in program", results[4].Actual);
        }

        [Fact]
        public void WrongVariantRunning_FailsTheValueChecks()
        {
            // MachineB's program answering MachineA's expectations.
            var plc = new FakePlc()
                .Set("MAIN.sMachine", Str("MachineB"))
                .Set("MAIN.rMaxPressure", F32(10.0f))
                .Set("MAIN.bDoorsFitted", new byte[] { 1 })
                .Set("PRG_PcvEtherCAT.nCycles", U32(5), U32(9));

            var results = ExpectationChecker.Check(ExpectationFile.Parse(Json)["MachineA"], plc.Read, ms => { });

            Assert.False(results[0].Passed);                         // sMachine
            Assert.False(results[1].Passed);                         // 10.0 vs 3.0
            Assert.False(results[2].Passed);                         // doors
            Assert.Equal("not in program", results[3].Actual);       // Modbus driver absent
            Assert.False(results[3].Passed);
            Assert.False(results[4].Passed);                         // EtherCAT driver runs
        }

        [Fact]
        public void CounterThatDoesNotMove_Fails()
        {
            var plc = new FakePlc().Set("PRG_PcvModbus.nCycles", U32(7), U32(7));
            var x = new Expectation { Symbol = "PRG_PcvModbus.nCycles", Type = "UDINT", Increases = true };

            Assert.False(ExpectationChecker.Check(new[] { x }, plc.Read, ms => { })[0].Passed);
        }

        [Fact]
        public void Tolerance_IsUsedForReals()
        {
            var plc = new FakePlc().Set("P", F32(3.0004f));
            var strict = new Expectation { Symbol = "P", Type = "REAL", EqualsValue = 3.0 };
            var loose = new Expectation { Symbol = "P", Type = "REAL", EqualsValue = 3.0, Tolerance = 0.001 };

            Assert.False(ExpectationChecker.Check(new[] { strict }, plc.Read, ms => { })[0].Passed);
            Assert.True(ExpectationChecker.Check(new[] { loose }, plc.Read, ms => { })[0].Passed);
        }

        [Theory]
        [InlineData("STRING(16)", 17)]
        [InlineData("STRING", 81)]
        [InlineData("BOOL", 1)]
        [InlineData("INT", 2)]
        [InlineData("UDINT", 4)]
        [InlineData("REAL", 4)]
        [InlineData("LREAL", 8)]
        public void SizeOf_MatchesTwinCatLayout(string type, int size)
        {
            Assert.Equal(size, PlcValue.SizeOf(type));
        }

        [Theory]
        [InlineData(@"{ ""default"": [ { ""symbol"": ""A"", ""type"": ""INT"" } ] }")]
        [InlineData(@"{ ""default"": [ { ""symbol"": ""A"", ""type"": ""INT"", ""equals"": 1, ""increases"": true } ] }")]
        [InlineData(@"{ ""default"": [ { ""type"": ""INT"", ""equals"": 1 } ] }")]
        [InlineData(@"{ ""default"": [ { ""symbol"": ""A"", ""type"": ""WSTRING"", ""equals"": ""x"" } ] }")]
        public void InvalidEntries_AreRejected(string json)
        {
            Assert.Throws<System.FormatException>(() => ExpectationFile.Parse(json));
        }

        [Theory]
        [InlineData("199.4.42.250.1.1")]
        [InlineData("0.0.0.0.0.0")]
        public void NetId_RoundTrips(string netId)
        {
            Assert.Equal(netId, AdsClient.FormatNetId(AdsClient.ParseNetId(netId)));
        }

        [Theory]
        [InlineData("199.4.42.250.1")]
        [InlineData("199.4.42.250.1.300")]
        [InlineData("umrt")]
        public void NetId_Invalid_IsRejected(string netId)
        {
            Assert.Throws<System.ArgumentException>(() => AdsClient.ParseNetId(netId));
        }
    }
}
