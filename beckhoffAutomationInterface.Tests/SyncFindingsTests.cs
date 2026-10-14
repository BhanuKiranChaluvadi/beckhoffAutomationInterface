using System.Collections.Generic;
using BeckhoffAutomationInterface.Sync;
using Xunit;

namespace BeckhoffAutomationInterface.Tests
{
    /// <summary>Fixes for problems that the end-to-end run on ST/Shark found (2026-10-14):
    /// a re-sync rewrote all 1261 objects, and two files defined FB_HeatZone.</summary>
    public class SyncFindingsTests
    {
        static StPouSource Src(string name, string folder, string file, string owner = null) =>
            new StPouSource(name, owner == null ? PouKind.FunctionBlock : PouKind.Method, owner, "decl", "impl")
            { RelativeFolder = folder, SourceFileName = file };

        [Fact]
        public void Duplicates_SameObjectInTwoFiles_IsReportedWithBothFiles()
        {
            var sources = new List<StPouSource>
            {
                Src("FB_HeatZone", "Lib/Controllers/Temperature", "FB_HeatZone.st"),
                Src("FB_HeatZone", "Lib/Controllers/Temperature", "FB_HeatJacket.st"),
                Src("FB_Valve", "Lib", "FB_Valve.st"),
            };

            List<string> found = DuplicateObjects.Find(sources);

            Assert.Equal(new[] { "FB_HeatZone: Lib/Controllers/Temperature/FB_HeatJacket.st, Lib/Controllers/Temperature/FB_HeatZone.st" }, found);
        }

        [Fact]
        public void Duplicates_MethodsCountPerOwner_AndOneFileIsNoDuplicate()
        {
            var sources = new List<StPouSource>
            {
                Src("FB_A", "", "FB_A.st"), Src("Init", "", "FB_A.st", owner: "FB_A"),
                Src("FB_B", "", "FB_B.st"), Src("Init", "", "FB_B.st", owner: "FB_B"),   // same method name, other owner
            };

            Assert.Empty(DuplicateObjects.Find(sources));
            Assert.Single(DuplicateObjects.Find(new List<StPouSource>(sources) { Src("Init", "", "FB_A_old.st", owner: "FB_A") }));
        }

        [Theory]
        [InlineData("a := 1;\r\nb := 2;", "a := 1;\nb := 2;", false)]     // line endings only
        [InlineData("a := 1;\r\n", "a := 1;", false)]                      // trailing newline only
        [InlineData(null, "", false)]                                      // empty either way
        [InlineData("a := 1;", "a := 2;", true)]
        [InlineData("a := 1; ", "a  := 1;", true)]                         // inner whitespace counts
        public void TextDiffers_IgnoresOnlyLineEndingsAndTrailingWhitespace(string current, string desired, bool differs)
        {
            Assert.Equal(differs, PouSyncEngine.TextDiffers(current, desired));
        }
    }
}
