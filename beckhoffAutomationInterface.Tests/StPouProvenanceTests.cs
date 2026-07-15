using System.Linq;
using BeckhoffAutomationInterface.Sync;
using Xunit;

namespace BeckhoffAutomationInterface.Tests
{
    /// <summary>
    /// Provenance tracking (tasks/todo.md Task 5): every StPouSource carries its
    /// originating .st file name and 1-based section start lines, so build errors
    /// can be mapped back to real source locations (Task 6).
    /// </summary>
    public class StPouProvenanceTests : IDisposable
    {
        readonly string _dir;

        public StPouProvenanceTests()
        {
            _dir = Path.Combine(Path.GetTempPath(), "StPouProvenanceTests_" + Guid.NewGuid());
            Directory.CreateDirectory(_dir);
        }

        public void Dispose() => Directory.Delete(_dir, recursive: true);

        string WriteFile(string fileNameWithoutExtension, string content)
        {
            string path = Path.Combine(_dir, fileNameWithoutExtension + ".st");
            File.WriteAllText(path, content);
            return path;
        }

        // Line numbers below are 1-based and refer to this exact literal: the leading
        // newline after the verbatim quote makes "FUNCTION_BLOCK FB_Multi" line 2.
        const string MultiMethodFb = @"
FUNCTION_BLOCK FB_Multi
VAR
    _x : LREAL;
END_VAR
_x := _x + 1.0;

METHOD Init
VAR_INPUT
    v : LREAL;
END_VAR
_x := v;
END_METHOD

{attribute 'hide'}
METHOD Reset
_x := 0.0;
END_METHOD
END_FUNCTION_BLOCK";

        [Fact]
        public void MultiMethodFb_EachSectionCarriesItsKeywordLine()
        {
            string path = WriteFile("FB_Multi", MultiMethodFb);

            var sources = StFileParser.ParseFile(path);

            Assert.All(sources, s => Assert.Equal("FB_Multi.st", s.SourceFileName));

            StPouSource fb = sources.Single(s => s.Name == "FB_Multi");
            Assert.Equal(2, fb.DeclarationStartLine);            // FUNCTION_BLOCK keyword
            Assert.Equal(6, fb.ImplementationStartLine);         // "_x := _x + 1.0;"

            StPouSource init = sources.Single(s => s.Name == "Init");
            Assert.Equal(8, init.DeclarationStartLine);          // METHOD Init keyword
            Assert.Equal(12, init.ImplementationStartLine);      // "_x := v;"

            // Reset's METHOD keyword is line 16 — the {attribute} pragma on line 15 is
            // absorbed into the segment but must NOT shift the reported keyword line.
            StPouSource reset = sources.Single(s => s.Name == "Reset");
            Assert.Equal(16, reset.DeclarationStartLine);
            Assert.Equal(17, reset.ImplementationStartLine);     // "_x := 0.0;"
        }

        [Fact]
        public void StandaloneMethodFile_CarriesFileNameAndKeywordLine()
        {
            string path = WriteFile("FB_Owner.DoWork", @"// helper method, defined in its own file
METHOD DoWork : BOOL
VAR
    ok : BOOL;
END_VAR
ok := TRUE;
DoWork := ok;
END_METHOD");

            StPouSource method = Assert.Single(StFileParser.ParseFile(path));

            Assert.Equal("FB_Owner.DoWork.st", method.SourceFileName);
            Assert.Equal("FB_Owner", method.OwnerName);
            Assert.Equal(2, method.DeclarationStartLine);        // METHOD keyword (line 1 is a comment)
            Assert.Equal(6, method.ImplementationStartLine);     // "ok := TRUE;"
        }

        [Fact]
        public void Dut_WholeFileIsDeclaration_LineOneNoImplementation()
        {
            string path = WriteFile("ST_Point", @"TYPE ST_Point :
STRUCT
    x : LREAL;
    y : LREAL;
END_STRUCT
END_TYPE");

            StPouSource dut = Assert.Single(StFileParser.ParseFile(path));

            Assert.Equal("ST_Point.st", dut.SourceFileName);
            Assert.Equal(1, dut.DeclarationStartLine);
            Assert.Null(dut.ImplementationStartLine);
        }

        [Fact]
        public void SourceRelativePath_JoinsFolderAndFileName()
        {
            var src = new StPouSource("X", PouKind.StructDut, null, "TYPE X : STRUCT END_STRUCT END_TYPE", null)
            {
                SourceFileName = "X.st",
            };
            Assert.Equal("X.st", src.SourceRelativePath);

            src.RelativeFolder = "App/Types";
            Assert.Equal("App/Types/X.st", src.SourceRelativePath);
        }

        [Fact]
        public void ParseFolder_SetsProvenanceOnNestedFiles()
        {
            Directory.CreateDirectory(Path.Combine(_dir, "Lib"));
            File.WriteAllText(Path.Combine(_dir, "Lib", "F_Add.st"), @"FUNCTION F_Add : LREAL
VAR_INPUT
    a : LREAL;
    b : LREAL;
END_VAR
F_Add := a + b;");

            var sources = StFileParser.ParseFolder(_dir, IgnoreRules.Load(_dir, new string[0]));

            StPouSource fn = Assert.Single(sources);
            Assert.Equal("F_Add.st", fn.SourceFileName);
            Assert.Equal("Lib/F_Add.st", fn.SourceRelativePath);
            Assert.Equal(6, fn.ImplementationStartLine);         // "F_Add := a + b;"
        }
    }
}
