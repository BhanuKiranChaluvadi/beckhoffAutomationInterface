using BeckhoffAutomationInterface.Machines;
using Xunit;

namespace BeckhoffAutomationInterface.Tests
{
    /// <summary>The build identity in the build copy of GVL_BuildInfo.st (port of Tools/build_info.ps1).</summary>
    public class MachineBuildInfoTests
    {
        static BuildIdentity Id(string tag = "v1.2.0-3-gabc1234", bool dirty = false) => new BuildIdentity
        {
            Tag = tag, Commit = "0123456789abcdef0123456789abcdef01234567", Dirty = dirty, Machine = "Google", BuildTime = "2026-10-20T08:15:00Z",
        };

        [Fact]
        public void Apply_ReplacesOnlyTheValues()
        {
            string text = BuildInfo.Apply(MachineRepoFixture.GvlBuildInfo, Id());
            Assert.Equal(MachineRepoFixture.GvlBuildInfo
                .Replace("sGitTag    : STRING(31) := 'dev';", "sGitTag    : STRING(31) := 'v1.2.0-3-gabc1234';")
                .Replace("sGitCommit : STRING(40) := '';", "sGitCommit : STRING(40) := '0123456789abcdef0123456789abcdef01234567';")
                .Replace("bDirty     : BOOL       := TRUE;", "bDirty     : BOOL       := FALSE;")
                .Replace("sMachine   : STRING(31) := '';", "sMachine   : STRING(31) := 'Google';")
                .Replace("sBuildTime : STRING(31) := '';", "sBuildTime : STRING(31) := '2026-10-20T08:15:00Z';"), text);
            Assert.True(BuildInfo.ShowsCleanTag(text, "v1.2.0-3-gabc1234"));
            Assert.False(BuildInfo.ShowsCleanTag(BuildInfo.Apply(MachineRepoFixture.GvlBuildInfo, Id(dirty: true)), "v1.2.0-3-gabc1234"));
        }

        [Fact]
        public void Apply_EscapesCutsAndKeepsAscii()
        {
            string text = BuildInfo.Apply(MachineRepoFixture.GvlBuildInfo, Id(tag: "it's $1 \u00e9" + new string('x', 40)));
            // 31 characters after the non-ASCII character becomes '?', then the IEC escapes.
            Assert.Contains("sGitTag    : STRING(31) := 'it$'s $$1 ?" + new string('x', 22) + "';", text);
        }

        [Fact]
        public void Apply_NeedsEveryDeclaration()
        {
            var ex = Assert.Throws<InvalidOperationException>(() => BuildInfo.Apply("VAR_GLOBAL CONSTANT\r\n    sGitTag : STRING(31) := 'dev';\r\nEND_VAR", Id()));
            Assert.Contains("sGitCommit", ex.Message);
        }

        [Fact]
        public void FromGit_ReadsTagCommitAndDirtyFlag()
        {
            var calls = new List<string>();
            BuildInfo.GitRunner git = (root, args) =>
            {
                calls.Add(string.Join(" ", args));
                switch (args[0])
                {
                    case "rev-parse": return (0, new List<string> { args[1] == "--show-toplevel" ? "C:/repo" : "0123456789abcdef0123456789abcdef01234567" });
                    case "describe": return (0, new List<string> { "v1.0.0" });
                    default: return (0, new List<string> { " M App/NFLite/MAIN.st" });
                }
            };
            BuildIdentity id = BuildInfo.FromGit(@"C:\repo", "Shark", new[] { "Lib", "App" }, git, new DateTime(2026, 10, 20, 8, 15, 0, DateTimeKind.Utc));
            Assert.Equal("v1.0.0", id.Tag);
            Assert.True(id.Dirty);
            Assert.Equal("Shark", id.Machine);
            Assert.Equal("2026-10-20T08:15:00Z", id.BuildTime);
            Assert.Contains("status --porcelain -- Lib App", calls);
            Assert.Contains("describe --tags --always", calls);
        }

        [Fact]
        public void FromGit_KeepsPlaceholders_WhenNotTheTopOfAWorkTree()
        {
            BuildInfo.GitRunner git = (root, args) => (0, new List<string> { "C:/other" });
            Assert.Null(BuildInfo.FromGit(@"C:\repo", "Shark", new[] { "Lib" }, git, DateTime.UtcNow));
            BuildInfo.GitRunner noGit = (root, args) => (-1, new List<string>());
            Assert.Null(BuildInfo.FromGit(@"C:\repo", "Shark", new[] { "Lib" }, noGit, DateTime.UtcNow));
        }

        [Fact]
        public void Write_ChangesTheBuildCopyOnly()
        {
            string dir = TestProjects.NewTempDir("bai_buildinfo");
            string file = Path.Combine(dir, "GVL_BuildInfo.st");
            File.WriteAllText(file, MachineRepoFixture.GvlBuildInfo);
            Assert.False(BuildInfo.Write(file, null));
            Assert.Equal(MachineRepoFixture.GvlBuildInfo, File.ReadAllText(file));
            Assert.True(BuildInfo.Write(file, Id()));
            Assert.Contains("'Google'", File.ReadAllText(file));
            Assert.Throws<InvalidOperationException>(() => BuildInfo.Write(Path.Combine(dir, "none.st"), Id()));
        }

        [Theory]
        [InlineData("abc", "abc")]
        [InlineData("a b", "\"a b\"")]
        [InlineData("", "\"\"")]
        [InlineData("C:\\x y\\", "\"C:\\x y\\\\\"")]
        [InlineData("say \"hi\"", "\"say \\\"hi\\\"\"")]
        public void Quote_FollowsWindowsRules(string arg, string quoted)
        {
            Assert.Equal(quoted, BuildInfo.Quote(arg));
        }
    }
}
