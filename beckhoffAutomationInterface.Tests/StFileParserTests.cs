using BeckhoffAutomationInterface.Sync;
using Xunit;

namespace BeckhoffAutomationInterface.Tests
{
    public class StFileParserTests : IDisposable
    {
        readonly string _dir;

        public StFileParserTests()
        {
            _dir = Path.Combine(Path.GetTempPath(), "StFileParserTests_" + Guid.NewGuid());
            Directory.CreateDirectory(_dir);
        }

        public void Dispose() => Directory.Delete(_dir, recursive: true);

        string WriteFile(string fileNameWithoutExtension, string content)
        {
            string path = Path.Combine(_dir, fileNameWithoutExtension + ".st");
            File.WriteAllText(path, content);
            return path;
        }

        [Fact]
        public void FunctionBlock_WithInlineMethodAndProperty_SplitsIntoThreeSources()
        {
            string path = WriteFile("FB_Motor", @"
FUNCTION_BLOCK FB_Motor
VAR
    _speed : LREAL;
END_VAR

METHOD Init
VAR_INPUT
    initialSpeed : LREAL;
END_VAR
    _speed := initialSpeed;
END_METHOD

PROPERTY Speed : LREAL
    GET
        Speed := _speed;
    END_GET
    SET
        _speed := Speed;
    END_SET
END_PROPERTY
END_FUNCTION_BLOCK
");
            var sources = StFileParser.ParseFile(path);

            Assert.Equal(3, sources.Count);

            StPouSource fb = sources.Single(s => s.Name == "FB_Motor");
            Assert.Equal(PouKind.FunctionBlock, fb.Kind);
            Assert.Null(fb.OwnerName);
            Assert.Contains("VAR", fb.DeclarationText);
            Assert.DoesNotContain("END_FUNCTION_BLOCK", fb.DeclarationText);

            StPouSource init = sources.Single(s => s.Name == "Init");
            Assert.Equal(PouKind.Method, init.Kind);
            Assert.Equal("FB_Motor", init.OwnerName);
            Assert.Contains("_speed := initialSpeed;", init.ImplementationText);

            StPouSource speed = sources.Single(s => s.Name == "Speed");
            Assert.Equal(PouKind.Property, speed.Kind);
            Assert.Equal("FB_Motor", speed.OwnerName);
            Assert.Contains("Speed := _speed;", speed.GetText);
            Assert.Contains("_speed := Speed;", speed.SetText);
        }

        [Fact]
        public void Program_WithInlinePrivateMethod_SplitsIntoTwoSources()
        {
            string path = WriteFile("PRG_Test", @"
PROGRAM PRG_Test
VAR
    x : INT;
END_VAR
    x := _Init();

METHOD PRIVATE _Init : INT
    _Init := 42;
END_METHOD
END_PROGRAM
");
            var sources = StFileParser.ParseFile(path);

            Assert.Equal(2, sources.Count);

            StPouSource prg = sources.Single(s => s.Name == "PRG_Test");
            Assert.Equal(PouKind.Program, prg.Kind);

            StPouSource init = sources.Single(s => s.Name == "_Init");
            Assert.Equal(PouKind.Method, init.Kind);
            Assert.Equal("PRG_Test", init.OwnerName);
            Assert.Contains("_Init := 42;", init.ImplementationText);
        }

        [Fact]
        public void Interface_WithExtends_CapturesBaseTypeAndMethodSignature()
        {
            string path = WriteFile("I_Derived", @"
INTERFACE I_Derived EXTENDS I_Base
METHOD DoWork : BOOL
END_METHOD
END_INTERFACE
");
            var sources = StFileParser.ParseFile(path);

            Assert.Equal(2, sources.Count);

            StPouSource itf = sources.Single(s => s.Name == "I_Derived");
            Assert.Equal(PouKind.Interface, itf.Kind);
            Assert.Equal("I_Base", itf.BaseType);

            StPouSource method = sources.Single(s => s.Name == "DoWork");
            Assert.Equal(PouKind.Method, method.Kind);
            Assert.Equal("I_Derived", method.OwnerName);
        }

        // PouKind is internal, so [Theory] data (a public signature) carries the expected
        // kind as a string and parses it inside the method body, where internal types are
        // usable freely (only the public method *signature* can't reference them).
        [Theory]
        [InlineData("E_State", "TYPE E_State : (Idle, Running, Fault); END_TYPE", "EnumDut")]
        [InlineData("ST_Point", "TYPE ST_Point :\nSTRUCT\n    x : LREAL;\n    y : LREAL;\nEND_STRUCT\nEND_TYPE", "StructDut")]
        [InlineData("T_Speed", "TYPE T_Speed : LREAL; END_TYPE", "AliasDut")]
        public void Dut_Kinds_AreClassifiedCorrectly(string name, string content, string expectedKindName)
        {
            var expectedKind = (PouKind)Enum.Parse(typeof(PouKind), expectedKindName);
            string path = WriteFile(name, content);
            var sources = StFileParser.ParseFile(path);

            StPouSource dut = Assert.Single(sources);
            Assert.Equal(name, dut.Name);
            Assert.Equal(expectedKind, dut.Kind);
            Assert.Null(dut.ImplementationText);
        }

        [Fact]
        public void Alias_CapturesAliasedBaseType()
        {
            string path = WriteFile("T_Speed", "TYPE T_Speed : LREAL; END_TYPE");
            StPouSource dut = Assert.Single(StFileParser.ParseFile(path));
            Assert.Equal("LREAL", dut.BaseType);
        }

        [Fact]
        public void Gvl_IsClassifiedAsGvl()
        {
            string path = WriteFile("GVL_Shark", "VAR_GLOBAL\n    gCounter : INT;\nEND_VAR");
            StPouSource gvl = Assert.Single(StFileParser.ParseFile(path));
            Assert.Equal(PouKind.Gvl, gvl.Kind);
            Assert.True(gvl.IsGvl);
        }

        [Fact]
        public void StandaloneMethodFile_ParsesOwnerAndMethodNameFromFileName()
        {
            string path = WriteFile("FB_Motor.Init", @"
METHOD Init
    _speed := 0;
END_METHOD
");
            StPouSource method = Assert.Single(StFileParser.ParseFile(path));
            Assert.Equal(PouKind.Method, method.Kind);
            Assert.Equal("Init", method.Name);
            Assert.Equal("FB_Motor", method.OwnerName);
        }
    }
}
