using BeckhoffAutomationInterface.Sync;
using Xunit;

namespace BeckhoffAutomationInterface.Tests
{
    public class IgnoreRulesTests : System.IDisposable
    {
        readonly string _dir;

        public IgnoreRulesTests()
        {
            _dir = Path.Combine(Path.GetTempPath(), "IgnoreRulesTests_" + Guid.NewGuid());
            Directory.CreateDirectory(_dir);
        }

        public void Dispose() => Directory.Delete(_dir, recursive: true);

        [Fact]
        public void NoPatterns_IgnoresNothing()
        {
            IgnoreRules ignore = IgnoreRules.Load(_dir, new string[0]);
            Assert.False(ignore.IsIgnored("FB_Anything.st"));
        }

        [Fact]
        public void PatternWithoutSlash_MatchesFileNameAtAnyDepth()
        {
            IgnoreRules ignore = IgnoreRules.Load(_dir, new[] { "FB_Old.st" });

            Assert.True(ignore.IsIgnored("FB_Old.st"));
            Assert.True(ignore.IsIgnored("Lib/Legacy/FB_Old.st"));
            Assert.False(ignore.IsIgnored("FB_New.st"));
        }

        [Fact]
        public void PatternWithSlash_IsAnchoredToWholeRelativePath()
        {
            IgnoreRules ignore = IgnoreRules.Load(_dir, new[] { "Lib/Legacy/**" });

            Assert.True(ignore.IsIgnored("Lib/Legacy/FB_Old.st"));
            Assert.False(ignore.IsIgnored("Lib/FB_Old.st"));
            Assert.False(ignore.IsIgnored("Other/Lib/Legacy/FB_Old.st"));
        }

        [Fact]
        public void QuestionMark_MatchesExactlyOneNonSlashCharacter()
        {
            IgnoreRules ignore = IgnoreRules.Load(_dir, new[] { "FB_?.st" });

            Assert.True(ignore.IsIgnored("FB_A.st"));
            Assert.False(ignore.IsIgnored("FB_AB.st"));
        }

        [Fact]
        public void Matching_IsCaseInsensitive()
        {
            IgnoreRules ignore = IgnoreRules.Load(_dir, new[] { "fb_old.st" });
            Assert.True(ignore.IsIgnored("FB_Old.st"));
        }

        [Fact]
        public void StIgnoreFile_CommentsAndBlankLinesAreSkipped_AndMergedWithExtraPatterns()
        {
            File.WriteAllText(Path.Combine(_dir, ".stignore"), "# a comment\n\nFB_FromFile.st\n");

            IgnoreRules ignore = IgnoreRules.Load(_dir, new[] { "FB_FromArgs.st" });

            Assert.True(ignore.IsIgnored("FB_FromFile.st"));
            Assert.True(ignore.IsIgnored("FB_FromArgs.st"));
            Assert.False(ignore.IsIgnored("# a comment"));
        }
    }
}
