using System;
using System.Collections.Generic;
using BeckhoffAutomationInterface.Sync;
using Xunit;

namespace BeckhoffAutomationInterface.Tests
{
    public class SyncProgressTests
    {
        static readonly DateTime T0 = new DateTime(2026, 11, 6, 14, 0, 0);

        [Fact]
        public void Line_ShowsPercentStepCounterTimeUsedAndTimeLeft()
        {
            string line = SyncProgress.Line(300, 1000, "methods", 120, 980, TimeSpan.FromSeconds(90), T0);

            Assert.Equal("2026-11-06 14:00:00:   sync  30%  methods 120/980  1:30 used, about 3:30 left", line);
        }

        [Fact]
        public void Line_HasNoTimeLeftWhenDone()
        {
            string line = SyncProgress.Line(1000, 1000, "objects (text)", 700, 700, TimeSpan.FromMinutes(18), T0);

            Assert.Equal("2026-11-06 14:00:00:   sync 100%  objects (text) 700/700  18:00 used", line);
        }

        [Theory]
        [InlineData(59, "0:59")]
        [InlineData(1085, "18:05")]
        [InlineData(3725, "1:02:05")]
        public void Duration_IsMinutesSecondsOrHoursMinutesSeconds(int seconds, string expected) =>
            Assert.Equal(expected, SyncProgress.Duration(TimeSpan.FromSeconds(seconds)));

        [Fact]
        public void Step_WritesAtEachTenPercent()
        {
            var lines = new List<string>();
            DateTime now = T0;
            var progress = new SyncProgress(100, lines.Add, () => now);

            for (int i = 1; i <= 100; i++)
            {
                now = now.AddMilliseconds(100);
                progress.Step("objects (text)", i, 100);
            }

            Assert.Equal(10, lines.Count);
            Assert.Contains(" 10%", lines[0]);
            Assert.Contains("100%", lines[9]);
        }

        [Fact]
        public void Step_WritesAfterThirtySecondsWithoutALine()
        {
            var lines = new List<string>();
            DateTime now = T0;
            var progress = new SyncProgress(1000, lines.Add, () => now);

            now = now.AddSeconds(10);
            progress.Step("methods", 1, 500);   // 0 %, 10 s: quiet
            now = now.AddSeconds(25);
            progress.Step("methods", 2, 500);   // 0 %, 35 s: a line

            Assert.Single(lines);
            Assert.Contains("  0%  methods 2/500  0:35 used", lines[0]);
        }

        [Fact]
        public void Step_WithNothingToDoWritesNothing()
        {
            var lines = new List<string>();
            new SyncProgress(0, lines.Add, () => T0).Step("objects (place)", 1, 0);

            Assert.Empty(lines);
        }
    }
}
