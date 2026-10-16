using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using BeckhoffAutomationInterface.Runtime;
using BeckhoffAutomationInterface.Sync;
using Xunit;

namespace BeckhoffAutomationInterface.Tests
{
    /// <summary>Scenario files for 'test': the parser, the filters and the JUnit report
    /// (BeckhoffLib tasks/2026-10-16-plc-release, T1.1 and T1.3).</summary>
    public class ScenarioFileTests
    {
        const string Example = @"{
  ""name"": ""Purge precursor, change material"",
  ""tags"": [""sequence"", ""smoke""],
  ""requires"": [""PCV_ETHERCAT""],
  ""timeoutS"": 300,
  ""steps"": [
    { ""do"": ""send"", ""command"": 200, ""parameters"": { ""mode"": 1, ""side"": 1, ""bubbler"": 1 }, ""expect"": ""accepted"", ""timeoutS"": 3 },
    { ""do"": ""waitUntil"", ""symbol"": ""PRG_Sequence.fbOrchestrator.eActiveSequence"", ""type"": ""INT"", ""equals"": 7, ""timeoutS"": 5 },
    { ""do"": ""check"", ""symbol"": ""PRG_Sequence.fbOrchestrator.bError"", ""equals"": false },
    { ""do"": ""write"", ""symbol"": ""GVL_Simulation.bSafetyOk"", ""type"": ""BOOL"", ""value"": false },
    { ""do"": ""wait"", ""seconds"": 2.5, ""comment"": ""let the interlock trip"" },
    { ""do"": ""check"", ""symbol"": ""PRG_Sequence.fbOrchestrator.rProgress"", ""type"": ""REAL"", ""greaterThan"": 0, ""lessThan"": 100 },
    { ""do"": ""check"", ""symbol"": ""PRG_Sequence.fbOrchestrator.sStatus"", ""type"": ""STRING(255)"", ""notEquals"": ""Ready"" },
    { ""do"": ""send"", ""command"": 9999 }
  ]
}";

        static Scenario Parse(string json) => ScenarioFile.Parse(json, "f.json");

        [Fact]
        public void Parse_ReadsEveryStepType()
        {
            Scenario s = Parse(Example);

            Assert.Equal("Purge precursor, change material", s.Name);
            Assert.Equal(new[] { "sequence", "smoke" }, s.Tags);
            Assert.Equal(new[] { "PCV_ETHERCAT" }, s.Requires);
            Assert.Equal(300, s.TimeoutS);
            Assert.Equal(8, s.Steps.Count);
            Assert.Equal(Enumerable.Range(1, 8), s.Steps.Select(x => x.Number));

            ScenarioStep send = s.Steps[0];
            Assert.Equal(StepKind.Send, send.Kind);
            Assert.Equal(200, send.Command);
            Assert.Equal(1, send.Parameters["mode"]);
            Assert.True(send.ExpectAccepted);
            Assert.Equal(3, send.TimeoutS);

            ScenarioStep wait = s.Steps[1];
            Assert.Equal(StepKind.WaitUntil, wait.Kind);
            Assert.Equal("INT", wait.Type);
            Assert.Equal(7.0, wait.Condition.EqualsValue);
            Assert.Equal(5, wait.TimeoutS);

            ScenarioStep check = s.Steps[2];
            Assert.Equal(StepKind.Check, check.Kind);
            Assert.Equal("BOOL", check.Type);   // a true/false value may leave out "type"
            Assert.Equal(false, check.Condition.EqualsValue);

            ScenarioStep write = s.Steps[3];
            Assert.Equal(StepKind.Write, write.Kind);
            Assert.Equal("GVL_Simulation.bSafetyOk", write.Symbol);
            Assert.Equal(false, write.Value);

            Assert.Equal(StepKind.Wait, s.Steps[4].Kind);
            Assert.Equal(2.5, s.Steps[4].Seconds);

            Assert.Equal(0, s.Steps[5].Condition.GreaterThan);
            Assert.Equal(100, s.Steps[5].Condition.LessThan);
            Assert.Equal("Ready", s.Steps[6].Condition.NotEqualsValue);

            Assert.Null(s.Steps[7].Parameters);
            Assert.Null(s.Steps[7].TimeoutS);
        }

        [Fact]
        public void Parse_Defaults()
        {
            Scenario s = Parse(@"{ ""name"": ""n"", ""steps"": [ { ""do"": ""send"", ""command"": 51, ""expect"": ""rejected"" } ] }");

            Assert.Equal(Scenario.DefaultTimeoutS, s.TimeoutS);
            Assert.Empty(s.Tags);
            Assert.Empty(s.Requires);
            Assert.False(s.Steps[0].ExpectAccepted);
        }

        [Theory]
        [InlineData(@"{ ""symbol"": ""A.b"", ""equals"": 1 }", "step 2: \"do\" is missing")]
        [InlineData(@"{ ""do"": ""poll"" }", "step 2: unknown \"do\": 'poll' (use send, waitUntil, check, write, wait)")]
        [InlineData(@"{ ""do"": ""check"", ""equals"": 1, ""type"": ""INT"" }", "step 2: \"symbol\" is missing")]
        [InlineData(@"{ ""do"": ""check"", ""symbol"": ""A.b"", ""equals"": 1 }", "step 2: \"type\" is missing")]
        [InlineData(@"{ ""do"": ""check"", ""symbol"": ""A.b"", ""type"": ""FLOAT"", ""equals"": 1 }", "step 2: \"type\": unsupported PLC type 'FLOAT'")]
        [InlineData(@"{ ""do"": ""check"", ""symbol"": ""A.b"", ""type"": ""BOOL"", ""equals"": 1 }", "step 2: \"equals\" must be true or false for type BOOL")]
        [InlineData(@"{ ""do"": ""check"", ""symbol"": ""A.b"", ""type"": ""INT"", ""equals"": ""7"" }", "step 2: \"equals\" must be a number for type INT")]
        [InlineData(@"{ ""do"": ""check"", ""symbol"": ""A.b"", ""type"": ""STRING(80)"", ""equals"": 7 }", "step 2: \"equals\" must be a text for type STRING(80)")]
        [InlineData(@"{ ""do"": ""check"", ""symbol"": ""A.b"", ""type"": ""INT"", ""equal"": 7 }", "step 2: unknown field \"equal\"")]
        [InlineData(@"{ ""do"": ""check"", ""symbol"": ""A.b"", ""type"": ""INT"" }", "step 2: needs a condition")]
        [InlineData(@"{ ""do"": ""check"", ""symbol"": ""A.b"", ""type"": ""INT"", ""equals"": 1, ""lessThan"": 3 }", "step 2: use \"equals\" or \"notEquals\" alone")]
        [InlineData(@"{ ""do"": ""check"", ""symbol"": ""A.b"", ""type"": ""BOOL"", ""lessThan"": 3 }", "step 2: \"lessThan\" and \"greaterThan\" need a number type, not BOOL")]
        [InlineData(@"{ ""do"": ""check"", ""symbol"": ""A.b"", ""type"": ""INT"", ""greaterThan"": 1, ""tolerance"": 1 }", "step 2: \"tolerance\" goes only with")]
        [InlineData(@"{ ""do"": ""check"", ""symbol"": ""A.b"", ""type"": ""INT"", ""equals"": 1, ""timeoutS"": 1 }", "step 2: unknown field \"timeoutS\"")]
        [InlineData(@"{ ""do"": ""waitUntil"", ""symbol"": ""A.b"", ""type"": ""INT"", ""equals"": 1, ""timeoutS"": 0 }", "step 2: \"timeoutS\" must be more than 0")]
        [InlineData(@"{ ""do"": ""send"" }", "step 2: \"command\" is missing")]
        [InlineData(@"{ ""do"": ""send"", ""command"": 1.5 }", "step 2: \"command\" must be a whole number from -32768 to 32767")]
        [InlineData(@"{ ""do"": ""send"", ""command"": 65546 }", "step 2: \"command\" must be a whole number from -32768 to 32767")]
        [InlineData(@"{ ""do"": ""send"", ""command"": 1, ""expect"": ""ok"" }", "step 2: \"expect\" must be \"accepted\" or \"rejected\"")]
        [InlineData(@"{ ""do"": ""send"", ""command"": 1, ""parameters"": [1] }", "step 2: \"parameters\" must be an object")]
        [InlineData(@"{ ""do"": ""write"", ""symbol"": ""A.b"", ""type"": ""INT"" }", "step 2: \"value\" is missing")]
        [InlineData(@"{ ""do"": ""write"", ""symbol"": ""A.b"", ""type"": ""INT"", ""value"": 40000 }", "step 2: \"value\": 40000 is outside the INT range")]
        [InlineData(@"{ ""do"": ""write"", ""symbol"": ""A.b"", ""type"": ""INT"", ""value"": 1.5 }", "step 2: \"value\": a INT value must be a whole number")]
        [InlineData(@"{ ""do"": ""write"", ""symbol"": ""A.b"", ""type"": ""STRING(3)"", ""value"": ""abcd"" }", "step 2: \"value\": 'abcd' is longer than 3 characters")]
        [InlineData(@"{ ""do"": ""wait"" }", "step 2: \"seconds\" is missing")]
        [InlineData(@"{ ""do"": ""wait"", ""seconds"": -1 }", "step 2: \"seconds\" must not be negative")]
        [InlineData(@"{ ""do"": ""wait"", ""seconds"": ""2"" }", "step 2: \"seconds\" must be a number")]
        [InlineData(@"""wait""", "step 2: a step must be an object")]
        public void Parse_InvalidStep_NamesFileAndStep(string step, string expected)
        {
            string json = @"{ ""name"": ""n"", ""steps"": [ { ""do"": ""wait"", ""seconds"": 1 }, " + step + " ] }";

            var ex = Assert.Throws<ScenarioFormatException>(() => Parse(json));
            Assert.StartsWith("f.json: ", ex.Message);
            Assert.Contains(expected, ex.Message);
        }

        [Theory]
        [InlineData(@"{ ""name"": ""n"", ""steps"": [ ", "f.json: not valid JSON")]
        [InlineData(@"[ 1 ]", "f.json: the root must be an object")]
        [InlineData(@"{ ""steps"": [ { ""do"": ""wait"", ""seconds"": 1 } ] }", "f.json: \"name\" is missing")]
        [InlineData(@"{ ""name"": """", ""steps"": [ { ""do"": ""wait"", ""seconds"": 1 } ] }", "f.json: \"name\" must be a text that is not empty")]
        [InlineData(@"{ ""name"": ""n"" }", "f.json: \"steps\" is missing")]
        [InlineData(@"{ ""name"": ""n"", ""steps"": [] }", "f.json: \"steps\" is empty")]
        [InlineData(@"{ ""name"": ""n"", ""steps"": { } }", "f.json: \"steps\" must be a list")]
        [InlineData(@"{ ""name"": ""n"", ""tags"": ""smoke"", ""steps"": [ { ""do"": ""wait"", ""seconds"": 1 } ] }", "f.json: \"tags\" must be a list of names")]
        [InlineData(@"{ ""name"": ""n"", ""requires"": [ 1 ], ""steps"": [ { ""do"": ""wait"", ""seconds"": 1 } ] }", "f.json: \"requires\" must be a list of names")]
        [InlineData(@"{ ""name"": ""n"", ""timeoutS"": -5, ""steps"": [ { ""do"": ""wait"", ""seconds"": 1 } ] }", "f.json: \"timeoutS\" must be more than 0")]
        [InlineData(@"{ ""name"": ""n"", ""machine"": ""Shark"", ""steps"": [ { ""do"": ""wait"", ""seconds"": 1 } ] }", "f.json: unknown field \"machine\"")]
        public void Parse_InvalidFile_NamesFile(string json, string expected)
        {
            var ex = Assert.Throws<ScenarioFormatException>(() => Parse(json));
            Assert.StartsWith(expected, ex.Message);
        }

        [Fact]
        public void Parse_CommandTooLongForAdsStream_IsRejected()
        {
            string big = new string('x', 4100);
            string json = @"{ ""name"": ""n"", ""steps"": [ { ""do"": ""send"", ""command"": 1, ""parameters"": { ""s"": """ + big + @""" } } ] }";

            var ex = Assert.Throws<ScenarioFormatException>(() => Parse(json));
            Assert.Contains("step 1: the command JSON is", ex.Message);
        }

        [Fact]
        public void LoadAll_ReadsAFolderInPathOrder_AndCollectsProblems()
        {
            string root = Path.Combine(Path.GetTempPath(), "bai-scenarios-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path.Combine(root, "sequences"));
            try
            {
                File.WriteAllText(Path.Combine(root, "sequences", "b.json"), @"{ ""name"": ""B"", ""steps"": [ { ""do"": ""wait"", ""seconds"": 1 } ] }");
                File.WriteAllText(Path.Combine(root, "a.json"), @"{ ""name"": ""A"", ""steps"": [ { ""do"": ""wait"", ""seconds"": 1 } ] }");
                File.WriteAllText(Path.Combine(root, "bad.json"), @"{ ""name"": ""Bad"", ""steps"": [ { ""do"": ""jump"" } ] }");
                File.WriteAllText(Path.Combine(root, "notes.txt"), "not a scenario");

                var problems = new List<string>();
                List<Scenario> scenarios = ScenarioFile.LoadAll(root, problems);

                Assert.Equal(new[] { "A", "B" }, scenarios.Select(s => s.Name));
                Assert.Single(problems);
                Assert.Contains("bad.json: step 1: unknown \"do\": 'jump'", problems[0]);

                Assert.Single(ScenarioFile.LoadAll(Path.Combine(root, "a.json"), new List<string>()));
                Assert.Throws<ArgumentException>(() => ScenarioFile.LoadAll(Path.Combine(root, "missing"), new List<string>()));
            }
            finally
            {
                Directory.Delete(root, true);
            }
        }

        // ------------------------------------------------------------------ filters

        static Scenario Make(string name, string[] tags = null, string[] requires = null) =>
            new Scenario { Name = name, Tags = (tags ?? new string[0]).ToList(), Requires = (requires ?? new string[0]).ToList() };

        [Fact]
        public void ByTags_SelectsScenariosWithAnyTag()
        {
            var all = new[] { Make("a", new[] { "smoke" }), Make("b", new[] { "full", "fault" }), Make("c") };

            Assert.Equal(new[] { "a", "b", "c" }, ScenarioFilter.ByTags(all, new string[0]).Select(s => s.Name));
            Assert.Equal(new[] { "a" }, ScenarioFilter.ByTags(all, new[] { "SMOKE" }).Select(s => s.Name));
            Assert.Equal(new[] { "a", "b" }, ScenarioFilter.ByTags(all, new[] { "smoke", "fault" }).Select(s => s.Name));
        }

        [Fact]
        public void SkipReason_RequiresMustBeASubsetOfTheGroups()
        {
            Scenario plain = Make("plain");
            Scenario ethercat = Make("ethercat", requires: new[] { "PCV_ETHERCAT", "DOOR_SENSORS" });

            Assert.Null(ScenarioFilter.SkipReason(plain, null));
            Assert.Null(ScenarioFilter.SkipReason(plain, new string[0]));
            Assert.Null(ScenarioFilter.SkipReason(ethercat, new[] { "door_sensors", "PCV_ETHERCAT", "PRESSURE_10BAR" }));
            Assert.Equal("requires PCV_ETHERCAT (not a feature group of this machine)",
                ScenarioFilter.SkipReason(ethercat, new[] { "DOOR_SENSORS", "PCV_MODBUS" }));
            Assert.Contains("the feature groups are not known", ScenarioFilter.SkipReason(ethercat, null));
        }

        [Fact]
        public void GroupsOfVariant_ReadsTheVariantsGroups()
        {
            var manifest = new VariantManifest(new[] { "UVM", "SAS" }, new Dictionary<string, IReadOnlyList<string>>
            {
                ["PCV_ETHERCAT"] = new[] { "UVM", "SAS" },
                ["PRESSURE_10BAR"] = new[] { "SAS" },
            });

            string variant = "sas";
            Assert.Equal(new[] { "PCV_ETHERCAT", "PRESSURE_10BAR" }, RuntimeCommands.GroupsOfVariant(manifest, "v.xml", ref variant));
            Assert.Equal("SAS", variant);

            variant = null;
            Assert.Contains("choose --variant", Assert.Throws<ArgumentException>(() => RuntimeCommands.GroupsOfVariant(manifest, "v.xml", ref variant)).Message);
            variant = "Google";
            Assert.Contains("no variant 'Google'", Assert.Throws<ArgumentException>(() => RuntimeCommands.GroupsOfVariant(manifest, "v.xml", ref variant)).Message);
            variant = null;
            Assert.Throws<ArgumentException>(() => RuntimeCommands.GroupsOfVariant(null, "v.xml", ref variant));
        }

        [Fact]
        public void GroupsOfVariant_OneVariantNeedsNoName()
        {
            var manifest = new VariantManifest(new[] { "Shark" }, new Dictionary<string, IReadOnlyList<string>>
            {
                ["PCV_MODBUS"] = new[] { "Shark" },
                ["SPECTROMETER_PLC"] = new[] { "Shark" },
            });

            string variant = null;
            Assert.Equal(new[] { "PCV_MODBUS", "SPECTROMETER_PLC" }, RuntimeCommands.GroupsOfVariant(manifest, "v.xml", ref variant));
            Assert.Equal("Shark", variant);
        }

        [Fact]
        public void SplitList_TrimsAndDropsEmptyNames()
        {
            Assert.Equal(new[] { "a", "b", "c" }, RuntimeCommands.SplitList(" a, b,,c "));
            Assert.Empty(RuntimeCommands.SplitList(null));
            Assert.Empty(RuntimeCommands.SplitList(""));
        }

        // ------------------------------------------------------------------ values and JSON

        [Fact]
        public void CommandJson_HasTheClientFormat_AndIsZeroPadded()
        {
            string json = CommandJson.Build(200, 7, new Dictionary<string, object> { ["mode"] = 1, ["name"] = "a'b" });
            Assert.Equal("{\"command\":200,\"index\":7,\"parameters\":{\"mode\":1,\"name\":\"a\\u0027b\"}}", json);
            Assert.Equal("{\"command\":9999,\"index\":8,\"parameters\":{}}", CommandJson.Build(9999, 8, null));

            byte[] stream = CommandJson.ToStream(json);
            Assert.Equal(4096, stream.Length);
            Assert.Equal((byte)'{', stream[0]);
            Assert.All(stream.Skip(json.Length), b => Assert.Equal(0, b));
            Assert.Throws<FormatException>(() => CommandJson.ToStream(new string('x', 4096)));
        }

        [Theory]
        [InlineData("INT", -2.0, new byte[] { 0xFE, 0xFF })]
        [InlineData("UINT", 65535.0, new byte[] { 0xFF, 0xFF })]
        [InlineData("DINT", 70000.0, new byte[] { 0x70, 0x11, 0x01, 0x00 })]
        [InlineData("BYTE", 255.0, new byte[] { 0xFF })]
        [InlineData("SINT", -1.0, new byte[] { 0xFF })]
        public void Encode_Integers(string type, double value, byte[] expected)
        {
            Assert.Equal(expected, PlcValue.Encode(type, value));
            Assert.Equal(value, PlcValue.Decode(type, expected));
        }

        [Fact]
        public void Encode_OtherTypes()
        {
            Assert.Equal(new byte[] { 1 }, PlcValue.Encode("BOOL", true));
            Assert.Equal(BitConverter.GetBytes(1.5f), PlcValue.Encode("REAL", 1.5));
            Assert.Equal(BitConverter.GetBytes(1.25), PlcValue.Encode("LREAL", 1.25));
            Assert.Equal(new byte[] { (byte)'a', (byte)'b', 0, 0, 0, 0 }, PlcValue.Encode("STRING(5)", "ab"));
            Assert.Equal(81, PlcValue.Encode("STRING", "x").Length);

            Assert.Throws<FormatException>(() => PlcValue.Encode("INT", 32768.0));
            Assert.Throws<FormatException>(() => PlcValue.Encode("UDINT", -1.0));
            Assert.Throws<FormatException>(() => PlcValue.Encode("DINT", 0.5));
            Assert.Throws<FormatException>(() => PlcValue.Encode("BOOL", 1.0));
            Assert.Throws<FormatException>(() => PlcValue.Encode("INT", "1"));
        }

        [Fact]
        public void SymbolEntry_GivesSizeAndType()
        {
            var entry = new List<byte>();
            foreach (uint v in new uint[] { 0, 0x4040, 0x10, 4, 3, 8 })
                entry.AddRange(BitConverter.GetBytes(v));
            string name = "PRG_Telemetry.fbCommandReceiver.nLastCommandIndex", type = "DINT";
            entry.AddRange(BitConverter.GetBytes((ushort)name.Length));
            entry.AddRange(BitConverter.GetBytes((ushort)type.Length));
            entry.AddRange(BitConverter.GetBytes((ushort)0));
            entry.AddRange(System.Text.Encoding.ASCII.GetBytes(name + "\0" + type + "\0\0"));

            Assert.True(AdsClient.ParseSymbolEntry(entry.ToArray(), entry.Count, out int size, out string typeName));
            Assert.Equal(4, size);
            Assert.Equal("DINT", typeName);
            Assert.False(AdsClient.ParseSymbolEntry(entry.ToArray(), 20, out _, out _));
        }

        // ------------------------------------------------------------------ JUnit

        [Fact]
        public void JUnit_HasOneTestcasePerScenario()
        {
            string root = Path.Combine(Path.GetTempPath(), "tests");
            var results = new List<ScenarioResult>
            {
                new ScenarioResult { Scenario = new Scenario { Name = "Heating", File = Path.Combine(root, "sequences", "heating.json") }, Status = ScenarioStatus.Passed, Seconds = 12.5 },
                new ScenarioResult { Scenario = new Scenario { Name = "Purge <A>", File = Path.Combine(root, "sequences", "purge.json") }, Status = ScenarioStatus.Failed, Seconds = 3,
                    Message = "step 2 (waitUntil) X: expected = 7, actual 0" },
                new ScenarioResult { Scenario = new Scenario { Name = "Doors", File = Path.Combine(root, "doors.json") }, Status = ScenarioStatus.Skipped,
                    Message = "requires DOOR_SENSORS (not a feature group of this machine)" },
            };
            results[1].Details.Add("PRG_Sequence.fbOrchestrator.sStatus = 'Invalid purge mode'");

            XDocument doc = XDocument.Parse(JUnitReport.Build("bai test Shark", results, root, new DateTime(2026, 10, 16, 9, 0, 0)).ToString());

            XElement suite = doc.Root.Element("testsuite");
            Assert.Equal("testsuites", doc.Root.Name.LocalName);
            Assert.Equal("3", suite.Attribute("tests").Value);
            Assert.Equal("1", suite.Attribute("failures").Value);
            Assert.Equal("1", suite.Attribute("skipped").Value);
            Assert.Equal("15.500", suite.Attribute("time").Value);

            List<XElement> cases = suite.Elements("testcase").ToList();
            Assert.Equal(new[] { "sequences.heating", "sequences.purge", "doors" }, cases.Select(c => c.Attribute("classname").Value));
            Assert.Empty(cases[0].Elements());
            XElement failure = cases[1].Element("failure");
            Assert.Equal("step 2 (waitUntil) X: expected = 7, actual 0", failure.Attribute("message").Value);
            Assert.Contains("Invalid purge mode", failure.Value);
            Assert.Equal("Purge <A>", cases[1].Attribute("name").Value);
            Assert.NotNull(cases[2].Element("skipped"));
        }

        [Fact]
        public void JUnit_ClassNameOfASingleFile_IsItsName()
        {
            string file = Path.Combine(Path.GetTempPath(), "heating.json");
            Assert.Equal("heating", JUnitReport.ClassName(file, file));
        }
    }
}
