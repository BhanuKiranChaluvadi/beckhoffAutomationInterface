using BeckhoffAutomationInterface.Sync;
using Xunit;

namespace BeckhoffAutomationInterface.Tests
{
    public class StConfigFileTests : System.IDisposable
    {
        readonly string _dir;

        public StConfigFileTests()
        {
            _dir = Path.Combine(Path.GetTempPath(), "StConfigFileTests_" + Guid.NewGuid());
            Directory.CreateDirectory(_dir);
        }

        public void Dispose() => Directory.Delete(_dir, recursive: true);

        void WriteConfig(string content) => File.WriteAllText(Path.Combine(_dir, ".stconfig"), content);

        [Fact]
        public void MissingFile_ReturnsEmptyDictionary_NotNull()
        {
            var config = StConfigFile.Load(_dir);

            Assert.NotNull(config);
            Assert.Empty(config);
        }

        [Fact]
        public void ParsesKeyValuePairs()
        {
            WriteConfig(@"
source=C:\path\to\ST\Shark
dest=C:\path\to\TwinCAT
name=Shark
");
            var config = StConfigFile.Load(_dir);

            Assert.Equal(@"C:\path\to\ST\Shark", config.GetString("source"));
            Assert.Equal(@"C:\path\to\TwinCAT", config.GetString("dest"));
            Assert.Equal("Shark", config.GetString("name"));
        }

        [Fact]
        public void SkipsCommentsAndBlankLines()
        {
            WriteConfig(@"
# this is a comment
source=C:\path

# another comment

name=Shark
");
            var config = StConfigFile.Load(_dir);

            Assert.Equal(2, config.Count);
            Assert.Equal(@"C:\path", config.GetString("source"));
            Assert.Equal("Shark", config.GetString("name"));
        }

        [Fact]
        public void SkipsMalformedLinesWithNoEquals()
        {
            WriteConfig(@"
not a valid line
source=C:\path
");
            var config = StConfigFile.Load(_dir);

            Assert.Single(config);
            Assert.Equal(@"C:\path", config.GetString("source"));
        }

        [Fact]
        public void KeyLookup_IsCaseInsensitive()
        {
            WriteConfig("Source=C:\\path\r\n");

            var config = StConfigFile.Load(_dir);

            Assert.Equal(@"C:\path", config.GetString("source"));
            Assert.Equal(@"C:\path", config.GetString("SOURCE"));
        }

        [Fact]
        public void GetString_ReturnsNull_WhenKeyMissing()
        {
            var config = StConfigFile.Load(_dir);

            Assert.Null(config.GetString("source"));
        }

        [Theory]
        [InlineData("true")]
        [InlineData("TRUE")]
        [InlineData("1")]
        [InlineData("yes")]
        [InlineData("Yes")]
        public void GetBool_TruthyValues(string value)
        {
            WriteConfig($"incremental={value}");

            var config = StConfigFile.Load(_dir);

            Assert.True(config.GetBool("incremental"));
        }

        [Theory]
        [InlineData("false")]
        [InlineData("0")]
        [InlineData("no")]
        [InlineData("")]
        [InlineData("nonsense")]
        public void GetBool_FalsyValues(string value)
        {
            WriteConfig($"incremental={value}");

            var config = StConfigFile.Load(_dir);

            Assert.False(config.GetBool("incremental"));
        }

        [Fact]
        public void GetBool_FalseWhenKeyMissing()
        {
            var config = StConfigFile.Load(_dir);

            Assert.False(config.GetBool("incremental"));
        }
    }
}
