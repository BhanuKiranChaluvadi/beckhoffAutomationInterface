using BeckhoffAutomationInterface.Machines;
using BeckhoffAutomationInterface.Sync;
using Xunit;

namespace BeckhoffAutomationInterface.Tests
{
    /// <summary>The enums of one family's ST sources (Lib + App/&lt;family&gt;): "E_Enum.Member" to its
    /// number. The enum texts are copied from BeckhoffLib (shortened).</summary>
    public class StEnumsTests : IDisposable
    {
        const string ECommand =
            "(* Shared command identifiers; each machine dispatches its supported subset.\r\n" +
            "   Numeric values are part of the external command protocol. *)\r\n" +
            "{attribute 'qualified_only'}\r\n{attribute 'to_string'}\r\n" +
            "TYPE E_Command :\r\n(\r\n" +
            "    None                     := 0,\r\n" +
            "    CancelProcess            := 1,      // Exit from the deposition / System stop\r\n\r\n" +
            "    SetHeaterZone            := 10,     // ST_CmdSetHeatZone — whole zone (zone is multiple channels)\r\n" +
            "    SetValve                 := 50,\r\n" +
            "    // NFPro-specific motion and hardware commands\r\n" +
            "    OzoneGenerator           := 300,    // NFPro PLC-controlled ozone generator\r\n" +
            "    ErrorAcknowledge         := 9999\r\n" +
            ") INT;\r\nEND_TYPE\r\n";

        const string NFProValves =
            "(*\r\n    Valve identifiers of NFPro (VALV_[...] in the telemetry).\r\n*)\r\n" +
            "{attribute 'qualified_only'}\r\n{attribute 'strict'}\r\n{attribute 'to_string'}\r\n" +
            "TYPE E_ValveIdentifier :\r\n    (\r\n" +
            "        VALV_CHK_LZ1           := 26,    // Chuck zone 1\r\n" +
            "        VALV_EX_PUMP           := 27,    // VALVE V4.0 - VACUUM ON\r\n" +
            "        VALV_EX_ISO            := 28     // Direct valve fully open (purge position\r\n" +
            "    )INT;\r\nEND_TYPE\r\n";

        const string NFLiteValves =
            "{attribute 'qualified_only'}\r\n{attribute 'strict'}\r\n" +
            "TYPE E_ValveIdentifier :\r\n    (\r\n" +
            "        VALV_EX_ISO            := 28,\r\n" +
            "        VALV_EX_PUMP           := 29     // Exhaust Pump Valve\r\n" +
            "    )INT;\r\nEND_TYPE\r\n";

        readonly string _root;

        public StEnumsTests()
        {
            _root = TestProjects.NewTempDir("bai_enums");
            Write("Lib/Telemetry/Commands/E_Command.st", ECommand);
            Write("App/NFPro/Valve/E_ValveIdentifier.st", NFProValves);
            Write("App/NFLite/Valve/E_ValveIdentifier.st", NFLiteValves);
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

        StEnums Family(string family) =>
            StEnums.Read(new[] { Path.Combine(_root, "Lib"), Path.Combine(_root, "App", family) });

        static long Resolved(StEnums enums, string name)
        {
            Assert.True(enums.TryResolve(name, out long value, out string problem), problem);
            return value;
        }

        [Fact]
        public void SameName_GivesTheNumberOfTheFamily()
        {
            Assert.Equal(27, Resolved(Family("NFPro"), "E_ValveIdentifier.VALV_EX_PUMP"));
            Assert.Equal(29, Resolved(Family("NFLite"), "E_ValveIdentifier.VALV_EX_PUMP"));
        }

        [Fact]
        public void FamilyOfTheMachineManifest_ReadsItsSources()
        {
            File.WriteAllText(Path.Combine(_root, MachineManifest.FileName),
                "{ \"work\": \"C:/bai/work\", \"lock\": \"C:/bai/lock/umrt.lock\", \"families\": {" +
                "  \"NFPro\": { \"plc\": \"NFPro\", \"sources\": [\"Lib\", \"App/NFPro\"], \"shared\": \"Machines/NFPro\"," +
                "    \"buildInfo\": \"App/NFPro/Version/GVL_BuildInfo.st\", \"machines\": [\"NFPro\"] }," +
                "  \"NFLite\": { \"plc\": \"NFLite\", \"sources\": [\"Lib\", \"App/NFLite\"], \"shared\": \"Machines/NFLite\"," +
                "    \"buildInfo\": \"App/NFLite/Version/GVL_BuildInfo.st\", \"machines\": [\"Shark\", \"Google\"] } } }");
            MachineManifest manifest = MachineManifest.Load(Path.Combine(_root, MachineManifest.FileName));

            Assert.Equal(27, Resolved(manifest.FamilyOf("NFPro").ReadEnums(manifest.Root), "E_ValveIdentifier.VALV_EX_PUMP"));
            Assert.Equal(29, Resolved(manifest.FamilyOf("Google").ReadEnums(manifest.Root), "E_ValveIdentifier.VALV_EX_PUMP"));
        }

        [Theory]
        [InlineData("NFPro")]
        [InlineData("NFLite")]
        public void Command_IsABareMemberOfTheLibCommandEnum(string family)
        {
            StEnums enums = Family(family);
            Assert.True(enums.TryResolveCommand("SetValve", out long value, out string problem), problem);
            Assert.Equal(50, value);
            Assert.Equal(50, Resolved(enums, "E_Command.SetValve"));
        }

        [Fact]
        public void Enums_WithAndWithoutBaseType_CommentsAndAttributes_AreRead()
        {
            Write("App/NFPro/Safety/E_SafetyIdentifier.st",
                "{attribute 'qualified_only'}\r\n{attribute 'strict'}\r\n{attribute 'to_string'}\r\n" +
                "TYPE E_SafetyIdentifier :\r\n(\r\n    SafetyOk := 1,\r\n\r\n    (* Safety Inputs *)\r\n" +
                "    MCR_RESET_PB := 2,\r\n    EXHAUST_OK := 10,\r\n\r\n    (* Safety Outputs *)\r\n" +
                "    DRIVES_HIGH_SPEED_ENABLE := 15\r\n);\r\nEND_TYPE\r\n");
            Write("App/NFLite/Safety/E_SafetyIdentifier.st",
                "(* TwinSAFE signals of the NFLite family, SAFE_[...] in the telemetry (as NFPro's\r\n" +
                "   E_SafetyIdentifier). Shark has no lock feedback: 2 and 3 stay FALSE. *)\r\n" +
                "{attribute 'qualified_only'}\r\n{attribute 'strict'}\r\n{attribute 'to_string'}\r\n" +
                "TYPE E_SafetyIdentifier :\r\n(\r\n" +
                "    SafetyOk        := 1,   // GVL_Safety.SafetyOk (TwinSAFE reports the safe state)\r\n" +
                "    DoorLockStateFL := 2,   // GVL_Safety.DoorLockStateFL (EL1918 Standard Out Var 12)\r\n" +
                "    DoorLockStateFR := 3    // GVL_Safety.DoorLockStateFR (EL1918 Standard Out Var 11)\r\n" +
                ")INT;\r\nEND_TYPE\r\n");
            Write("Lib/Hardware/Types/E_BubblerIndex.st",
                "(* Shared bubbler and direct-gas selectors; availability depends on the machine. *)\r\n" +
                "{attribute 'qualified_only'}\r\n{attribute 'to_string'}\r\n" +
                "TYPE E_BubblerIndex :\r\n    (\r\n        BUBBLER_1 := 1,\r\n        BUBBLER_2 := 2,\r\n" +
                "        GAS_OZONE := 4,\r\n        GAS_NH3   := 5\r\n    )DINT;\r\nEND_TYPE\r\n");
            Write("Lib/Simulation/Physics/E_SimZeroRatePolicy.st",
                "(* What a rate limiter does at zero rate. *)\r\n{attribute 'qualified_only'}\r\n" +
                "TYPE E_SimZeroRatePolicy : (HOLD, SNAP_TO_TARGET := 7);\r\nEND_TYPE\r\n");

            StEnums nfPro = Family("NFPro"), nfLite = Family("NFLite");
            Assert.Equal(10, Resolved(nfPro, "E_SafetyIdentifier.EXHAUST_OK"));
            Assert.Equal(15, Resolved(nfPro, "E_SafetyIdentifier.DRIVES_HIGH_SPEED_ENABLE"));
            Assert.Equal(3, Resolved(nfLite, "E_SafetyIdentifier.DoorLockStateFR"));
            Assert.Equal(5, Resolved(nfLite, "E_BubblerIndex.GAS_NH3"));
            Assert.Equal(7, Resolved(nfPro, "E_SimZeroRatePolicy.SNAP_TO_TARGET"));
        }

        [Theory]
        [InlineData("E_Valve.VALV_EX_PUMP", "unknown enum 'E_Valve'")]
        [InlineData("E_ValveIdentifier.VALV_EX_PMUP", "enum 'E_ValveIdentifier' has no member 'VALV_EX_PMUP'")]
        [InlineData("E_ValveIdentifier.VALV_CHK_LZ1", "enum 'E_ValveIdentifier' has no member 'VALV_CHK_LZ1'")] // NFPro only
        [InlineData("VALV_EX_PUMP", "'VALV_EX_PUMP' is not an enum member name (E_Enum.Member)")]
        [InlineData("E_ValveIdentifier.VALV_EX_PUMP.X", "'E_ValveIdentifier.VALV_EX_PUMP.X' is not an enum member name (E_Enum.Member)")]
        public void UnknownName_IsRefusedWithTheName(string name, string expected)
        {
            Assert.False(Family("NFLite").TryResolve(name, out _, out string problem));
            Assert.Equal(expected, problem);
        }

        [Fact]
        public void UnknownCommand_IsRefusedWithTheName()
        {
            Assert.False(Family("NFPro").TryResolveCommand("SetValv", out _, out string problem));
            Assert.Equal("enum 'E_Command' has no member 'SetValv'", problem);
        }

        [Fact]
        public void Names_IgnoreCase_AsInST()
        {
            Assert.Equal(29, Resolved(Family("NFLite"), "e_valveidentifier.valv_ex_pump"));
        }

        [Fact]
        public void SameEnumTwiceInAFamily_IsRefused()
        {
            Write("Lib/Valve/E_ValveIdentifier.st", NFLiteValves);
            var ex = Assert.Throws<FormatException>(() => Family("NFLite"));
            Assert.Contains("enum 'E_ValveIdentifier' is declared twice", ex.Message);
            Assert.Contains(Path.Combine("Lib", "Valve", "E_ValveIdentifier.st"), ex.Message);
            Assert.Contains(Path.Combine("NFLite", "Valve", "E_ValveIdentifier.st"), ex.Message);
        }

        [Theory]
        [InlineData("    A := 1,\r\n    a := 2", "member 'a' is declared twice")]
        [InlineData("    A := 16#FF", "cannot read the member 'A := 16#FF'")]
        [InlineData("    A := B + 1", "cannot read the member 'A := B + 1'")]
        [InlineData("    A := 1,\r\n    ,B", "cannot read the member ''")]
        public void MemberThatCannotBeRead_IsRefusedWithFileAndText(string members, string expected)
        {
            Write("Lib/E_Bad.st", "TYPE E_Bad :\r\n(\r\n" + members + "\r\n) INT;\r\nEND_TYPE\r\n");
            var ex = Assert.Throws<FormatException>(() => Family("NFPro"));
            Assert.Contains(Path.Combine("Lib", "E_Bad.st"), ex.Message);
            Assert.Contains("E_Bad", ex.Message);
            Assert.Contains(expected, ex.Message);
        }

        [Fact]
        public void MissingSourceFolder_IsRefused()
        {
            var ex = Assert.Throws<DirectoryNotFoundException>(() => StEnums.Read(new[] { Path.Combine(_root, "App", "Nope") }));
            Assert.Contains(Path.Combine("App", "Nope"), ex.Message);
        }

        [Fact]
        public void MembersWithoutValue_CountOnFromTheMemberBefore()
        {
            Write("Lib/Communication/Types/E_ModbusDeviceState.st",
                "(* Modbus device state machine. *)\r\n{attribute 'qualified_only'}\r\n{attribute 'strict'}\r\n" +
                "TYPE E_ModbusDeviceState :\r\n    (\r\n        IDLE,\r\n        START_WRITE,\r\n        START_READ\r\n    );\r\nEND_TYPE\r\n");
            Write("Lib/Controllers/E_Mixed.st",
                "TYPE E_Mixed :\r\n(\r\n    A := 5,\r\n    B,\r\n    C := 20,\r\n    D\r\n) UDINT;\r\nEND_TYPE\r\n");

            StEnums enums = Family("NFPro");
            Assert.Equal(0, Resolved(enums, "E_ModbusDeviceState.IDLE"));
            Assert.Equal(2, Resolved(enums, "E_ModbusDeviceState.START_READ"));
            Assert.Equal(6, Resolved(enums, "E_Mixed.B"));
            Assert.Equal(21, Resolved(enums, "E_Mixed.D"));
        }
    }
}
