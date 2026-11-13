using System;
using System.Collections.Generic;
using System.IO;
using BeckhoffAutomationInterface.Machines;
using BeckhoffAutomationInterface.Runtime;
using BeckhoffAutomationInterface.Sync;
using Xunit;

namespace BeckhoffAutomationInterface.Tests
{
    /// <summary>Scenario files name enum members instead of numbers (BeckhoffLib
    /// tasks/scenario-enum-names, ticket 02): the scenario text and the enums of the family under
    /// test go in; steps with the numbers, or a load error with file, step and name, come out.</summary>
    public class ScenarioNamesTests : IDisposable
    {
        readonly string _root;

        public ScenarioNamesTests()
        {
            _root = TestProjects.NewTempDir("bai_names");
            Write("Lib/Telemetry/Commands/E_Command.st",
                "{attribute 'qualified_only'}\r\nTYPE E_Command :\r\n(\r\n    None := 0,\r\n    CancelProcess := 1,\r\n" +
                "    SetValve := 50,    // ST_CmdSetValve\r\n    ErrorAcknowledge := 9999\r\n) INT;\r\nEND_TYPE\r\n");
            Write("Lib/Sequences/Types/E_Sequence.st",
                "{attribute 'qualified_only'}\r\nTYPE E_Sequence :\r\n(\r\n    None := 0,\r\n    Purge := 7\r\n) INT;\r\nEND_TYPE\r\n");
            Write("App/NFPro/Valve/E_ValveIdentifier.st",
                "{attribute 'qualified_only'}\r\nTYPE E_ValveIdentifier :\r\n(\r\n    VALV_CHK_LZ1 := 26,\r\n    VALV_EX_PUMP := 27\r\n) INT;\r\nEND_TYPE\r\n");
            Write("App/NFPro/DigitalIO/E_DigitalOutputIdentifier.st",
                "{attribute 'qualified_only'}\r\nTYPE E_DigitalOutputIdentifier :\r\n(\r\n    PUMP_AB_EXH := 4\r\n) INT;\r\nEND_TYPE\r\n");
            Write("App/NFPro/Safety/E_SafetyIdentifier.st",
                "{attribute 'qualified_only'}\r\nTYPE E_SafetyIdentifier :\r\n(\r\n    SafetyOk := 1,\r\n    EXHAUST_OK := 10\r\n);\r\nEND_TYPE\r\n");
            Write("App/NFPro/Pressure/E_PressureSensorIdentifier.st",
                "{attribute 'qualified_only'}\r\nTYPE E_PressureSensorIdentifier :\r\n(\r\n    PSNS_AB_EXH := 8\r\n) INT;\r\nEND_TYPE\r\n");
            Write("App/NFLite/Valve/E_ValveIdentifier.st",
                "{attribute 'qualified_only'}\r\nTYPE E_ValveIdentifier :\r\n(\r\n    VALV_EX_ISO := 28,\r\n    VALV_EX_PUMP := 29\r\n) INT;\r\nEND_TYPE\r\n");
        }

        public void Dispose()
        {
            try { Directory.Delete(_root, recursive: true); } catch (IOException) { }
        }

        void Write(string relative, string text)
        {
            string path = Path.Combine(_root, relative.Replace('/', '\\'));
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllText(path, text);
        }

        ScenarioNames Family(string family) =>
            ScenarioNames.Of(family, () => StEnums.Read(new[] { Path.Combine(_root, "Lib"), Path.Combine(_root, "App", family) }));

        static string OneStep(string step) => @"{ ""name"": ""n"", ""steps"": [ { ""do"": ""wait"", ""seconds"": 1 }, " + step + " ] }";

        static ScenarioStep Second(string step, ScenarioNames names) => ScenarioFile.Parse(OneStep(step), "f.json", names).Steps[1];

        [Fact]
        public void Command_BareMemberOfECommand_SendsItsNumber()
        {
            ScenarioStep step = Second(@"{ ""do"": ""send"", ""command"": ""SetValve"" }", Family("NFPro"));

            Assert.Equal(50, step.Command);
            Assert.Equal("SetValve (50)", step.CommandText);
        }

        [Fact]
        public void Command_Number_StillWorks_WithoutAFamily()
        {
            ScenarioStep step = Second(@"{ ""do"": ""send"", ""command"": 50, ""parameters"": { ""valveId"": 27, ""name"": ""text"" } }", null);

            Assert.Equal(50, step.Command);
            Assert.Equal("50", step.CommandText);
            Assert.Equal(@"{""command"":50,""index"":3,""parameters"":{""valveId"":27,""name"":""text""}}",
                CommandJson.Build(step.Command, 3, step.Parameters));
        }

        [Theory]
        [InlineData("NFPro", 27)]
        [InlineData("NFLite", 29)]
        public void Parameter_QualifiedName_SendsTheNumberOfTheFamily(string family, int expected)
        {
            ScenarioStep step = Second(@"{ ""do"": ""send"", ""command"": ""SetValve"",
                ""parameters"": { ""valveId"": ""E_ValveIdentifier.VALV_EX_PUMP"", ""enabled"": true, ""recipe"": ""Al2O3.v2"" } }", Family(family));

            Assert.Equal("{\"command\":50,\"index\":1,\"parameters\":{\"valveId\":" + expected + ",\"enabled\":true,\"recipe\":\"Al2O3.v2\"}}",
                CommandJson.Build(step.Command, 1, step.Parameters));
        }

        [Fact]
        public void SymbolIndex_Name_ReadsTheNumberedElement_AndShowsTheTextAsWritten()
        {
            const string written = "PRG_Telemetry.fbTelemetryPublisher.stTelemetry.SAFE_[E_SafetyIdentifier.EXHAUST_OK].State";
            ScenarioStep step = Second(@"{ ""do"": ""check"", ""symbol"": """ + written + @""", ""equals"": true }", Family("NFPro"));

            Assert.Equal("PRG_Telemetry.fbTelemetryPublisher.stTelemetry.SAFE_[10].State", step.Symbol);
            Assert.Equal(written, step.SymbolText);
        }

        [Fact]
        public void SymbolIndex_InWaitUntil_AndInASimulationFaultWrite()
        {
            Scenario s = ScenarioFile.Parse(@"{ ""name"": ""n"", ""steps"": [
                { ""do"": ""waitUntil"", ""symbol"": ""T.PSNS_[E_PressureSensorIdentifier.PSNS_AB_EXH].Pressure"", ""type"": ""REAL"", ""lessThan"": 0.5 } ],
                ""simulationFault"": { ""write"": [ { ""symbol"": ""GVL_Simulation.aPressureOffset[ E_PressureSensorIdentifier.PSNS_AB_EXH ]"", ""type"": ""REAL"", ""value"": 1.0 } ],
                                       ""failsAtStep"": 1 } }", "f.json", Family("NFPro"));

            Assert.Equal("T.PSNS_[8].Pressure", s.Steps[0].Symbol);
            Assert.Equal("GVL_Simulation.aPressureOffset[8]", s.SimulationFault.Writes[0].Symbol);
            Assert.Equal("GVL_Simulation.aPressureOffset[ E_PressureSensorIdentifier.PSNS_AB_EXH ]", s.SimulationFault.Writes[0].SymbolText);
        }

        [Fact]
        public void ComparedValue_Name_ComparesWithTheNumber()
        {
            ScenarioStep step = Second(@"{ ""do"": ""check"", ""symbol"": ""PRG_Sequence.fbOrchestrator.eActiveSequence"", ""type"": ""INT"",
                ""notEquals"": ""E_Sequence.Purge"" }", Family("NFPro"));
            ScenarioStep equals = Second(@"{ ""do"": ""waitUntil"", ""symbol"": ""X.e"", ""type"": ""INT"", ""equals"": ""E_Sequence.None"" }", Family("NFLite"));
            ScenarioStep range = Second(@"{ ""do"": ""check"", ""symbol"": ""X.e"", ""type"": ""INT"",
                ""greaterThan"": ""E_Sequence.None"", ""lessThan"": ""E_Sequence.Purge"" }", Family("NFLite"));

            Assert.True(step.Condition.Matches(0.0));
            Assert.False(step.Condition.Matches(7.0));
            Assert.Equal("<> E_Sequence.Purge (7)", step.Condition.Text);
            Assert.True(equals.Condition.Matches(0.0));
            Assert.Equal("= E_Sequence.None (0)", equals.Condition.Text);
            Assert.True(range.Condition.Matches(3.0));
            Assert.Equal("> E_Sequence.None (0) and < E_Sequence.Purge (7)", range.Condition.Text);
        }

        [Fact]
        public void Names_IgnoreCase_AsInST()
        {
            ScenarioStep step = Second(@"{ ""do"": ""send"", ""command"": ""setvalve"", ""parameters"": { ""valveId"": ""e_valveidentifier.valv_ex_pump"" } }", Family("NFLite"));
            Assert.Equal("{\"command\":50,\"index\":1,\"parameters\":{\"valveId\":29}}", CommandJson.Build(step.Command, 1, step.Parameters));
        }

        [Fact]
        public void TextOfAnotherForm_StaysAText()
        {
            ScenarioStep step = Second(@"{ ""do"": ""check"", ""symbol"": ""X.s"", ""type"": ""STRING(80)"", ""equals"": ""Ready.Done"" }", Family("NFPro"));
            Assert.Equal("Ready.Done", step.Condition.EqualsValue);
        }

        [Theory]
        [InlineData(@"{ ""do"": ""send"", ""command"": ""SetValv"" }",
            "f.json: step 2: \"command\": 'SetValv': enum 'E_Command' has no member 'SetValv' (family NFPro)")]
        [InlineData(@"{ ""do"": ""send"", ""command"": 50, ""parameters"": { ""valveId"": ""E_Valve.VALV_EX_PUMP"" } }",
            "f.json: step 2: \"parameters\": \"valveId\": 'E_Valve.VALV_EX_PUMP': unknown enum 'E_Valve' (family NFPro)")]
        [InlineData(@"{ ""do"": ""check"", ""symbol"": ""T.DO_[E_DigitalOutputIdentifier.PUMP_AB_EXHH].State"", ""equals"": true }",
            "f.json: step 2: \"symbol\": 'E_DigitalOutputIdentifier.PUMP_AB_EXHH': enum 'E_DigitalOutputIdentifier' has no member 'PUMP_AB_EXHH' (family NFPro)")]
        [InlineData(@"{ ""do"": ""check"", ""symbol"": ""X.e"", ""type"": ""INT"", ""equals"": ""E_Sequence.Nothing"" }",
            "f.json: step 2: \"equals\": 'E_Sequence.Nothing': enum 'E_Sequence' has no member 'Nothing' (family NFPro)")]
        public void UnknownName_StopsTheLoad_WithFileStepAndName(string step, string expected)
        {
            var ex = Assert.Throws<ScenarioFormatException>(() => ScenarioFile.Parse(OneStep(step), "f.json", Family("NFPro")));
            Assert.Equal(expected, ex.Message);
        }

        [Fact]
        public void NameOfTheOtherFamilyOnly_IsRefused()
        {
            // VALV_CHK_LZ1 is an NFPro valve; a step copied to an NFLite file must not run.
            var ex = Assert.Throws<ScenarioFormatException>(() => ScenarioFile.Parse(@"{ ""name"": ""n"", ""steps"": [ { ""do"": ""wait"", ""seconds"": 1 } ],
                ""cleanup"": [ { ""do"": ""send"", ""command"": ""SetValve"", ""parameters"": { ""valveId"": ""E_ValveIdentifier.VALV_CHK_LZ1"" } } ] }",
                "f.json", Family("NFLite")));
            Assert.Equal("f.json (cleanup): step 1: \"parameters\": \"valveId\": 'E_ValveIdentifier.VALV_CHK_LZ1': " +
                "enum 'E_ValveIdentifier' has no member 'VALV_CHK_LZ1' (family NFLite)", ex.Message);
        }

        [Fact]
        public void Name_WithoutAFamily_AsksForIt()
        {
            var ex = Assert.Throws<ScenarioFormatException>(() => Second(@"{ ""do"": ""send"", ""command"": ""SetValve"" }",
                ScenarioNames.Without("no family name (NFPro, NFLite) in the scenario path; give --family <name>")));
            Assert.Equal("f.json: step 2: \"command\": 'SetValve': no family name (NFPro, NFLite) in the scenario path; give --family <name>", ex.Message);
        }

        [Fact]
        public void EnumsThatCannotBeRead_AreALoadProblem()
        {
            Write("Lib/E_Bad.st", "TYPE E_Bad :\r\n(\r\n    A := 16#FF\r\n) INT;\r\nEND_TYPE\r\n");
            var ex = Assert.Throws<ScenarioFormatException>(() => Second(@"{ ""do"": ""send"", ""command"": ""SetValve"" }", Family("NFPro")));
            Assert.StartsWith("f.json: step 2: \"command\": 'SetValve': cannot read the enums of family NFPro: ", ex.Message);
            Assert.Contains("E_Bad.st", ex.Message);
        }

        // ------------------------------------------------------------------ the family under test

        string Manifest()
        {
            string path = Path.Combine(_root, MachineManifest.FileName);
            File.WriteAllText(path,
                "{ \"work\": \"C:/bai/work\", \"lock\": \"C:/bai/lock/umrt.lock\", \"families\": {" +
                "  \"NFPro\": { \"plc\": \"NFPro\", \"sources\": [\"Lib\", \"App/NFPro\"], \"shared\": \"Machines/NFPro\"," +
                "    \"buildInfo\": \"App/NFPro/Version/GVL_BuildInfo.st\", \"machines\": [\"NFPro\"] }," +
                "  \"NFLite\": { \"plc\": \"NFLite\", \"sources\": [\"Lib\", \"App/NFLite\"], \"shared\": \"Machines/NFLite\"," +
                "    \"buildInfo\": \"App/NFLite/Version/GVL_BuildInfo.st\", \"machines\": [\"Shark\", \"Google\"] } } }");
            return path;
        }

        static long Valve(ScenarioNames names)
        {
            Assert.True(names.TryResolve("E_ValveIdentifier.VALV_EX_PUMP", out long value, out string problem), problem);
            return value;
        }

        static string Refusal(ScenarioNames names)
        {
            Assert.False(names.TryResolve("E_ValveIdentifier.VALV_EX_PUMP", out _, out string problem));
            return problem;
        }

        [Fact]
        public void Family_IsTheFamilyNamedInTheScenarioPath()
        {
            Manifest();
            Write("Tests/Acceptance/NFPro/smoke/03-exhaust.json", "{}");

            Assert.Equal(27, Valve(ScenarioNames.ForScenarios(Path.Combine(_root, "Tests", "Acceptance", "NFPro"), null, null)));
            Assert.Equal(27, Valve(ScenarioNames.ForScenarios(Path.Combine(_root, "Tests", "Acceptance", "nfpro", "smoke", "03-exhaust.json"), null, null)));
            Directory.CreateDirectory(Path.Combine(_root, "Tests", "Integration", "NFLite"));
            Assert.Equal(29, Valve(ScenarioNames.ForScenarios(Path.Combine(_root, "Tests", "Integration", "NFLite"), null, null)));
        }

        [Fact]
        public void Family_Option_WinsOverThePath_AndMustBeAFamily()
        {
            string manifest = Manifest();
            Directory.CreateDirectory(Path.Combine(_root, "Tests", "Integration", "NFLite"));
            string scenarios = Path.Combine(_root, "Tests", "Integration", "NFLite");

            Assert.Equal(27, Valve(ScenarioNames.ForScenarios(scenarios, "nfpro", null)));
            Assert.Equal(27, Valve(ScenarioNames.ForScenarios(Path.GetTempPath(), "NFPro", manifest)));
            var ex = Assert.Throws<ArgumentException>(() => ScenarioNames.ForScenarios(scenarios, "Google", null));
            Assert.Equal("--family Google: not a family of " + manifest + " (NFPro, NFLite).", ex.Message);
        }

        [Fact]
        public void Family_NoneOrTwoInThePath_NamesAreRefused_NumbersStillLoad()
        {
            Manifest();
            Directory.CreateDirectory(Path.Combine(_root, "Tests", "Shared"));
            Directory.CreateDirectory(Path.Combine(_root, "NFPro", "NFLite"));

            ScenarioNames none = ScenarioNames.ForScenarios(Path.Combine(_root, "Tests", "Shared"), null, null);
            ScenarioNames two = ScenarioNames.ForScenarios(Path.Combine(_root, "NFPro", "NFLite"), null, null);

            Assert.Equal("'E_ValveIdentifier.VALV_EX_PUMP': the scenario path names no family (NFPro, NFLite); give --family <name>", Refusal(none));
            Assert.Equal("'E_ValveIdentifier.VALV_EX_PUMP': the scenario path names more than one family (NFPro, NFLite); give --family <name>", Refusal(two));
            Assert.Equal(50, ScenarioFile.Parse(OneStep(@"{ ""do"": ""send"", ""command"": 50 }"), "f.json", none).Steps[1].Command);
        }

        [Fact]
        public void Family_WithoutAManifest_NumbersStillLoad()
        {
            string folder = TestProjects.NewTempDir("bai_names_nomanifest");
            try
            {
                ScenarioNames names = ScenarioNames.ForScenarios(folder, null, null);
                Assert.Equal(1, ScenarioFile.Parse(OneStep(@"{ ""do"": ""send"", ""command"": 1 }"), "f.json", names).Steps[1].Command);
                Assert.Contains("--family <name>", Refusal(names));
                Assert.Throws<ArgumentException>(() => ScenarioNames.ForScenarios(folder, "NFPro", null));
            }
            finally
            {
                Directory.Delete(folder, true);
            }
        }
    }
}
