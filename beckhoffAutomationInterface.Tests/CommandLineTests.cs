using BeckhoffAutomationInterface;
using BeckhoffAutomationInterface.Cli;
using Xunit;

namespace BeckhoffAutomationInterface.Tests
{
    /// <summary>
    /// The subcommand parser (docs/ideas/cli-subcommand-redesign.md, "Final CLI spec";
    /// tasks/2026-10-14-variants-closed-loop, Phase 5). Filesystem fixtures only — no TwinCAT.
    /// </summary>
    public class CommandLineTests : System.IDisposable
    {
        readonly string _dir;
        readonly string _source;      // ST\Shark
        readonly string _tsproj;      // an adopted project: PLC_NFL_SHARK\PLC_NFL_SHARK.tsproj
        readonly string _ownedSln;    // a tool-created project: TwinCAT\Shark\Shark.sln
        readonly string _ownedTsproj; //                          TwinCAT\Shark\Shark\Shark.tsproj

        public CommandLineTests()
        {
            _dir = Path.Combine(Path.GetTempPath(), "CommandLineTests_" + Guid.NewGuid().ToString("N").Substring(0, 8));
            _source = Path.Combine(_dir, "ST", "Shark");
            Directory.CreateDirectory(_source);
            string adopted = Path.Combine(_dir, "PLC_NFL_SHARK");
            Directory.CreateDirectory(adopted);
            _tsproj = Path.Combine(adopted, "PLC_NFL_SHARK.tsproj");
            File.WriteAllText(_tsproj, "<TcSmProject/>");
            string solutionDir = Path.Combine(_dir, "TwinCAT", "Shark");
            Directory.CreateDirectory(Path.Combine(solutionDir, "Shark"));
            _ownedSln = Path.Combine(solutionDir, "Shark.sln");
            File.WriteAllText(_ownedSln, "");
            _ownedTsproj = Path.Combine(solutionDir, "Shark", "Shark.tsproj");
            File.WriteAllText(_ownedTsproj, "<TcSmProject/>");
        }

        public void Dispose() => Directory.Delete(_dir, recursive: true);

        RunOptions Parse(params string[] args) => CommandLine.Parse(args, cwd: _dir);

        static void Rejected(string expectedText, System.Func<RunOptions> parse)
        {
            var ex = Assert.Throws<UsageException>(() => parse());
            Assert.Contains(expectedText, ex.Message);
        }

        // ---------------------------------------------------------------- build

        [Fact]
        public void Build_AdoptsTheProject()
        {
            RunOptions o = Parse("build", _tsproj);

            Assert.Equal(_tsproj, o.ExistingTsprojPath);
            Assert.Equal("PLC_NFL_SHARK", o.PlcProjectName);
            Assert.True(o.BuildOnly);
            Assert.False(o.SourceFolderExplicit);
        }

        [Fact]
        public void Build_FolderWithOneTsproj_AndOptions()
        {
            RunOptions o = Parse("build", Path.GetDirectoryName(_tsproj), "--plc", "PLC_NFL_prj", "--clean", "--variant", "SAS");

            Assert.Equal(_tsproj, o.ExistingTsprojPath);
            Assert.Equal("PLC_NFL_prj", o.PlcProjectName);
            Assert.True(o.Clean);
            Assert.Equal("SAS", o.Variant);
        }

        [Fact]
        public void Build_Errors()
        {
            Rejected("exactly one <project>", () => Parse("build"));
            Rejected("unknown option --plc-name", () => Parse("build", _tsproj, "--plc-name", "X"));      // renamed to --plc
            Rejected("either --variant or --all-variants", () => Parse("build", _tsproj, "--variant", "A", "--all-variants"));
        }

        // ---------------------------------------------------------------- check

        [Theory]
        [InlineData("parse")]
        [InlineData("format")]
        [InlineData("links")]
        public void Check_SourceModes(string mode)
        {
            RunOptions o = Parse("check", mode, _source, "--ignore", "Old*.st", "--ignore", "Tmp*.st");

            Assert.Equal(_source, o.SourceFolder);
            Assert.Equal(mode == "parse", o.ParseOnly);
            Assert.Equal(mode == "format", o.FormatCheck);
            Assert.Equal(mode == "links", o.CheckLinks);
            Assert.Equal(new[] { "Old*.st", "Tmp*.st" }, o.IgnorePatterns);
        }

        [Fact]
        public void CheckEvents_NeedsTheProject()
        {
            RunOptions o = Parse("check", "events", _source, "--project", _tsproj);

            Assert.True(o.CheckEvents);
            Assert.Equal(_tsproj, o.ExistingTsprojPath);
            Rejected("needs --project", () => Parse("check", "events", _source));
            Rejected("unknown 'check' mode", () => Parse("check", "style", _source));
        }

        // ---------------------------------------------------------------- export

        [Fact]
        public void Export_Modes()
        {
            RunOptions io = Parse("export", "io", _source, "--project", _tsproj, "--overwrite");
            RunOptions all = Parse("export", "all", _source, "--project", _tsproj);
            RunOptions links = Parse("export", "links", _source, "--project", _tsproj, "--plc", "PLC_NFL_prj");

            Assert.Equal(ReverseExports.Io, io.ReverseExports);
            Assert.True(io.Overwrite);
            Assert.Equal(ReverseExports.All, all.ReverseExports);
            Assert.True(all.ExportLinks);                     // export all includes links.xml
            Assert.False(links.IsReverseExport);
            Assert.True(links.ExportLinks);
            Assert.Equal("PLC_NFL_prj", links.PlcProjectName);
        }

        [Fact]
        public void ExportObject()
        {
            RunOptions o = Parse("export", "object", "FB_Motor", _source, "--project", _tsproj);

            Assert.Equal("FB_Motor", o.ExportObjectName);
            Assert.False(o.IsReverseExport);
            Rejected("needs --project", () => Parse("export", "io", _source));
            Rejected("needs <ObjectName>", () => Parse("export", "object", "--project", _tsproj));
        }

        // ---------------------------------------------------------------- .stconfig

        [Fact]
        public void StConfig_SuppliesProjectShellClean_AndCommandLineWins()
        {
            File.WriteAllText(Path.Combine(_source, ".stconfig"),
                "project=" + _tsproj + "\nshell=xae\nclean=true\n");

            RunOptions fromConfig = Parse("export", "io", _source);
            RunOptions overridden = CommandLine.Parse(new[] { "export", "io", _source }, shell: "vs2022", cwd: _dir);

            Assert.True(fromConfig.ConfigFileLoaded);
            Assert.Equal(_tsproj, fromConfig.ExistingTsprojPath);
            Assert.Equal("xae", fromConfig.Shell);
            Assert.True(fromConfig.Clean);
            Assert.Equal("vs2022", overridden.Shell);
        }

        [Fact]
        public void StConfig_RemovedKeys_FailWithTheReplacement()
        {
            File.WriteAllText(Path.Combine(_source, ".stconfig"), "dest=C:\\X\nname=Shark\nsync-code=true\nplc-name=P\n");

            var ex = Assert.Throws<UsageException>(() => Parse("check", "parse", _source));

            Assert.Contains("'dest' is no longer supported", ex.Message);
            Assert.Contains("use project=", ex.Message);
            Assert.Contains("'sync-code' is no longer supported", ex.Message);
            Assert.Contains("'plc-name' is no longer supported", ex.Message);
        }

        [Fact]
        public void StConfig_UnknownKey_Fails_NoConfig_Skips()
        {
            File.WriteAllText(Path.Combine(_source, ".stconfig"), "colour=blue\n");

            Rejected("unknown key 'colour'", () => Parse("check", "parse", _source));
            Assert.False(Parse("check", "parse", _source, "--no-config").ConfigFileLoaded);
        }

        [Fact]
        public void StConfig_ConfigFlag_PointsAtAnotherFolder()
        {
            string elsewhere = Path.Combine(_dir, "cfg");
            Directory.CreateDirectory(elsewhere);
            File.WriteAllText(Path.Combine(elsewhere, ".stconfig"), "shell=vs2022\n");

            Assert.Equal("vs2022", Parse("check", "parse", _source, "--config", elsewhere).Shell);
        }

        [Fact]
        public void UnknownCommand_IsRejected()
        {
            Rejected("unknown command", () => Parse("deploy", _source));
        }
    }
}
