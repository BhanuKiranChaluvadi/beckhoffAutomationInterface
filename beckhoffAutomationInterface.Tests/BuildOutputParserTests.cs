using BeckhoffAutomationInterface.Sync;
using Xunit;

namespace BeckhoffAutomationInterface.Tests
{
    /// <summary>Output-pane fallback for empty error texts. Sample lines are real
    /// TwinCAT 3.1.4026 build output.</summary>
    public class BuildOutputParserTests
    {
        [Fact]
        public void TailLines_LastNonEmptyLines()   // a failed build with 0 errors shows these (2026-10-14)
        {
            Assert.Equal(new[] { "b", "c" }, BuildRunner.TailLines("a\r\n\r\nb\n  \nc\n", 2));
            Assert.Empty(BuildRunner.TailLines(null, 5));
        }

        const string RealOutput =
            "------ Build started: Application: VariantDemo.DemoPlc -------\r\n" +
            "Typify code...\r\n" +
            "Generate code...\r\n" +
            @"C:\Demo\DemoPlc\POUs\MAIN.TcPOU(13) : error: No machine variant selected (MachineA or MachineB)" + "\r\n" +
            @"C:\Demo\DemoPlc\POUs\MAIN.TcPOU(17) : error: C0032: Cannot convert type 'Unknown type' to type 'STRING(16)'" + "\r\n" +
            @"C:\Demo\DemoPlc\GVLs\GVL_MachineConfig.TcGVL : : C0181: Related position" + "\r\n" +
            @"C:\Demo\DemoPlc\POUs\MAIN.TcPOU(9) : warning: C0373: VARIANT CHECK: MachineA branch compiled" + "\r\n" +
            "Build complete -- 2 errors, 1 warnings : No download possible\r\n";

        [Fact]
        public void ParsesErrorLines_WithFileLineAndText()
        {
            var errors = BuildOutputParser.ParseErrors(RealOutput);

            Assert.Equal(2, errors.Count);
            Assert.Equal(@"C:\Demo\DemoPlc\POUs\MAIN.TcPOU", errors[0].FileName);
            Assert.Equal(13, errors[0].Line);
            Assert.Equal("No machine variant selected (MachineA or MachineB)", errors[0].Description);
            Assert.Equal(17, errors[1].Line);
            Assert.StartsWith("C0032: Cannot convert type", errors[1].Description);
        }

        [Fact]
        public void IgnoresWarningsAndRelatedPositionLines()
        {
            var errors = BuildOutputParser.ParseErrors(RealOutput);

            Assert.DoesNotContain(errors, e => e.Description.Contains("VARIANT CHECK"));
            Assert.DoesNotContain(errors, e => e.Description.Contains("Related position"));
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("Build complete -- 0 errors, 0 warnings : Ready for download")]
        public void NoErrorLines_ReturnsEmpty(string text)
        {
            Assert.Empty(BuildOutputParser.ParseErrors(text));
        }

        [Fact]
        public void FallbackIsUsed_WhenFailedBuildHasOnlyBlankDescriptions()
        {
            var report = new BuildReport { Succeeded = false };
            report.Errors.Add(new BuildError { Description = "" });
            report.Errors.Add(new BuildError { Description = "  " });

            Assert.True(BuildRunner.ShouldUseOutputPane(report));
        }

        [Fact]
        public void FallbackIsUsed_WhenFailedBuildHasNoErrorItems()
        {
            Assert.True(BuildRunner.ShouldUseOutputPane(new BuildReport { Succeeded = false }));
        }

        [Fact]
        public void FallbackIsNotUsed_WhenErrorsHaveText()
        {
            var report = new BuildReport { Succeeded = false };
            report.Errors.Add(new BuildError { Description = "Identifier 'x' not defined" });

            Assert.False(BuildRunner.ShouldUseOutputPane(report));
        }

        [Fact]
        public void FallbackIsNotUsed_WhenBuildSucceeded()
        {
            Assert.False(BuildRunner.ShouldUseOutputPane(new BuildReport { Succeeded = true }));
        }

        const string PassedWithWarnings =
            "------ Build started: Application: Unit.UnitPlc -------\r\n" +
            @"C:\Unit\UnitPlc\POUs\FB_Writer.TcPOU(41) : warning: C0195: Implicit conversion from signed Type 'INT' to unsigned Type 'UDINT' : Possible change of sign" + "\r\n" +
            @"C:\Unit\UnitPlc\POUs\MAIN.TcPOU(9) : warning: C0373: VARIANT CHECK: MachineA branch compiled" + "\r\n" +
            @"C:\Unit\UnitPlc\POUs\F_Text.TcPOU(7) : warning: C0130: Lost information in conversion from 'LREAL' to 'REAL'" + "\r\n" +
            "Build complete -- 0 errors, 3 warnings : Ready for download\r\n";

        [Fact]
        public void ParsesWarningLines_WithoutPragmaMessages()   // the error list can be empty when XAE is hidden
        {
            var warnings = BuildOutputParser.ParseWarnings(PassedWithWarnings);

            Assert.Equal(2, warnings.Count);
            Assert.Equal(@"C:\Unit\UnitPlc\POUs\FB_Writer.TcPOU", warnings[0].FileName);
            Assert.Equal(41, warnings[0].Line);
            Assert.StartsWith("C0195: Implicit conversion", warnings[0].Description);
            Assert.Equal("Medium", warnings[0].Level);
            Assert.Equal("C0130: Lost information in conversion from 'LREAL' to 'REAL'", warnings[1].Description);
            Assert.DoesNotContain(warnings, w => w.Description.Contains("VARIANT CHECK"));
        }

        [Fact]
        public void ParsesNoWarnings_FromErrorsOrEmptyText()
        {
            Assert.Empty(BuildOutputParser.ParseWarnings(null));
            Assert.Empty(BuildOutputParser.ParseWarnings(RealOutput));   // its only warning is a pragma message
        }

        [Fact]
        public void Summary_IsTheLastBuildCompleteLine()
        {
            Assert.Equal("Build complete -- 0 errors, 3 warnings : Ready for download", BuildOutputParser.ParseSummary(PassedWithWarnings));
            Assert.Equal("Build complete -- 2 errors, 1 warnings : No download possible", BuildOutputParser.ParseSummary(RealOutput));
            Assert.Null(BuildOutputParser.ParseSummary("Typify code...\r\n"));
            Assert.Null(BuildOutputParser.ParseSummary(null));
        }

        [Fact]
        public void PaneWarnings_ReplaceTheErrorList_WhenThePaneHasAny()
        {
            var fromList = new System.Collections.Generic.List<BuildError> { new BuildError { Description = "" } };
            var fromPane = BuildOutputParser.ParseWarnings(PassedWithWarnings);

            Assert.Same(fromPane, BuildRunner.ChooseWarnings(fromList, fromPane));
            Assert.Same(fromList, BuildRunner.ChooseWarnings(fromList, new System.Collections.Generic.List<BuildError>()));
        }
    }
}
