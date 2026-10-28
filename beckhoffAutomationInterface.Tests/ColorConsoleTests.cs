using System;
using BeckhoffAutomationInterface.Cli;
using Xunit;

namespace BeckhoffAutomationInterface.Tests
{
    /// <summary>The line colors of a console run (the console itself is not needed).</summary>
    public sealed class ColorConsoleTests
    {
        [Theory]
        [InlineData("PASS: package NFPro (ReleaseCandidate, TwinCAT RT (x64), v0.26.0-rc.7).", ConsoleColor.Green)]
        [InlineData("  OK   GVL_BuildInfo.sGitTag     = v0.26.0-rc.7  (expected v0.26.0-rc.7)", ConsoleColor.Green)]
        [InlineData("READY: nothing changed.", ConsoleColor.Green)]
        [InlineData("2026-10-28 11:50:27: VERIFY PASSED - NFPro: all 6 check(s) as expected.", ConsoleColor.Green)]
        [InlineData("FAIL: The target has no boot project.", ConsoleColor.Red)]
        [InlineData("  FAIL TwinCAT_SystemInfoVarList._AppInfo.OnlineChangeCnt  = 1  (expected 0)", ConsoleColor.Red)]
        [InlineData("ERROR: unrecognized argument: --expect", ConsoleColor.Red)]
        [InlineData("  MISSING  TC3 Controller Toolbox       missing", ConsoleColor.Red)]
        [InlineData("NFPro    fresh  PASS   FAIL   -   14:18", ConsoleColor.Red)]
        [InlineData("WARNING: could not keep a copy of the installed package.", ConsoleColor.Yellow)]
        [InlineData("SKIP: no saved service project (--saved-project): no drift check.", ConsoleColor.Yellow)]
        public void ResultWordsGetTheirColor(string line, ConsoleColor color) => Assert.Equal(color, ColorConsole.ColorOf(line));

        [Theory]
        [InlineData("Current package: none installed by bai on this target.")]
        [InlineData("Target local: Config")]
        [InlineData("If a step fails, bai puts the old image back.")]
        [InlineData("")]
        public void OtherLinesKeepTheDefaultColor(string line) => Assert.Null(ColorConsole.ColorOf(line));
    }
}
