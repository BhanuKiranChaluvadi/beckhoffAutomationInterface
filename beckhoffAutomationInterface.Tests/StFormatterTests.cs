using BeckhoffAutomationInterface.Sync;
using Xunit;

namespace BeckhoffAutomationInterface.Tests
{
    public class StFormatterTests : System.IDisposable
    {
        readonly string _dir;
        readonly IgnoreRules _noIgnore;

        public StFormatterTests()
        {
            _dir = Path.Combine(Path.GetTempPath(), "StFormatterTests_" + Guid.NewGuid());
            Directory.CreateDirectory(_dir);
            _noIgnore = IgnoreRules.Load(_dir, new string[0]);
        }

        public void Dispose() => Directory.Delete(_dir, recursive: true);

        List<FormatIssue> CheckSingleFile(string content)
        {
            File.WriteAllText(Path.Combine(_dir, "FB_X.st"), content);
            return StFormatter.CheckFolder(_dir, _noIgnore);
        }

        [Fact]
        public void CleanFile_ReportsNoIssues()
        {
            List<FormatIssue> issues = CheckSingleFile("FUNCTION_BLOCK FB_X\r\nEND_FUNCTION_BLOCK\r\n");
            Assert.Empty(issues);
        }

        [Fact]
        public void MixedLineEndings_AreReported()
        {
            List<FormatIssue> issues = CheckSingleFile("FUNCTION_BLOCK FB_X\r\nVAR\nEND_VAR\r\nEND_FUNCTION_BLOCK\r\n");
            Assert.Contains(issues, i => i.Description.Contains("mixed line endings"));
        }

        [Fact]
        public void TrailingWhitespace_IsCountedPerLine()
        {
            List<FormatIssue> issues = CheckSingleFile("FUNCTION_BLOCK FB_X \r\nEND_FUNCTION_BLOCK\r\n");
            FormatIssue issue = Assert.Single(issues);
            Assert.Contains("1 line(s) with trailing whitespace", issue.Description);
        }

        [Fact]
        public void MissingTrailingNewline_IsReported()
        {
            List<FormatIssue> issues = CheckSingleFile("FUNCTION_BLOCK FB_X\r\nEND_FUNCTION_BLOCK");
            FormatIssue issue = Assert.Single(issues);
            Assert.Equal("missing trailing newline at end of file", issue.Description);
        }

        [Fact]
        public void ExtraBlankLinesAtEof_AreCounted()
        {
            List<FormatIssue> issues = CheckSingleFile("FUNCTION_BLOCK FB_X\r\nEND_FUNCTION_BLOCK\r\n\r\n\r\n");
            FormatIssue issue = Assert.Single(issues);
            Assert.Contains("2 extra blank line(s) at end of file", issue.Description);
        }

        [Fact]
        public void EmptyFile_ReportsNoIssues()
        {
            Assert.Empty(CheckSingleFile(""));
        }
    }
}
