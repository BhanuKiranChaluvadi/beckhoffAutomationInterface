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
    }
}
