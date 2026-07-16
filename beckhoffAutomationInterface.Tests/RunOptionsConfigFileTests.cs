using BeckhoffAutomationInterface;
using Xunit;

namespace BeckhoffAutomationInterface.Tests
{
    /// <summary>
    /// .stconfig defaults-file wiring in RunOptions.Parse (tasks/plan.md: ".stconfig —
    /// Default CLI Options File"). Discovery order: --config's folder, else --source's
    /// folder (same place .stignore already lives), else the process's current
    /// directory (the `cwd` parameter below stands in for it so these tests never touch
    /// the real working directory).
    /// </summary>
    public class RunOptionsConfigFileTests : System.IDisposable
    {
        readonly string _dir;

        public RunOptionsConfigFileTests()
        {
            _dir = Path.Combine(Path.GetTempPath(), "RunOptionsConfigFileTests_" + Guid.NewGuid());
            Directory.CreateDirectory(_dir);
        }

        public void Dispose() => Directory.Delete(_dir, recursive: true);

        static void WriteConfig(string dir, string content) => File.WriteAllText(Path.Combine(dir, ".stconfig"), content);

        string NewSubDir(string name)
        {
            string dir = Path.Combine(_dir, name);
            Directory.CreateDirectory(dir);
            return dir;
        }

        [Fact]
        public void NoConfigFile_BehavesExactlyLikeBeforeThisFeature()
        {
            RunOptions options = RunOptions.Parse(new[] { "--source", _dir });

            Assert.False(options.ConfigFileLoaded);
            Assert.Equal(SyncStages.All, options.Stages);
        }

        [Fact]
        public void ConfigFile_DiscoveredAtTopLevelOfSource_WhenSourceIsGiven()
        {
            string project = NewSubDir("project");
            WriteConfig(project, $@"
name=FromConfig
dest={_dir}\dest
");
            RunOptions options = RunOptions.Parse(new[] { "--source", project, "--parse-only" });

            Assert.True(options.ConfigFileLoaded);
            Assert.Equal(Path.GetFullPath(project), options.SourceFolder);
            Assert.Equal("FromConfig", options.ProjectName);
            Assert.Equal(Path.GetFullPath($@"{_dir}\dest"), options.DestinationFolder);
        }

        [Fact]
        public void ExplicitCliValue_OverridesConfigValue()
        {
            string project = NewSubDir("project");
            WriteConfig(project, "name=FromConfig");

            RunOptions options = RunOptions.Parse(new[] { "--source", project, "--name", "FromCli" });

            Assert.Equal("FromCli", options.ProjectName);
        }

        [Fact]
        public void ConfigFlag_PointsDiscoveryAtAnArbitraryDirectory_NotSource()
        {
            string configDir = NewSubDir("somewhere-else");
            string sourceDir = NewSubDir("src");
            WriteConfig(configDir, $@"
source={sourceDir}
name=FromConfig
");
            RunOptions options = RunOptions.Parse(new[] { "--config", configDir, "--parse-only" });

            Assert.True(options.ConfigFileLoaded);
            Assert.Equal(Path.GetFullPath(sourceDir), options.SourceFolder);
            Assert.Equal("FromConfig", options.ProjectName);
        }

        [Fact]
        public void NeitherSourceNorConfigGiven_FallsBackToCwd()
        {
            WriteConfig(_dir, "name=FromCwdConfig");

            RunOptions options = RunOptions.Parse(new[] { "--parse-only" }, cwd: _dir);

            Assert.True(options.ConfigFileLoaded);
            Assert.Equal("FromCwdConfig", options.ProjectName);
        }

        [Fact]
        public void CliStageFlag_IgnoresConfigStagesEntirely_NoSurpriseExtraStage()
        {
            WriteConfig(_dir, "build=true");

            RunOptions options = RunOptions.Parse(new[] { "--sync-code" }, cwd: _dir);

            Assert.Equal(SyncStages.Code, options.Stages); // NOT Code | Build
        }

        [Fact]
        public void ConfigStages_UsedOnlyWhenCliNamesNoStageAtAll()
        {
            WriteConfig(_dir, "sync-io=true\nsync-events=true");

            RunOptions options = RunOptions.Parse(new[] { "--parse-only" }, cwd: _dir);

            Assert.Equal(SyncStages.Io | SyncStages.Events, options.Stages);
        }

        [Fact]
        public void NeitherCliNorConfigNamesAnyStage_DefaultsToAll()
        {
            WriteConfig(_dir, "incremental=true"); // present, but no stage keys

            RunOptions options = RunOptions.Parse(new[] { "--parse-only" }, cwd: _dir);

            Assert.Equal(SyncStages.All, options.Stages);
        }

        [Fact]
        public void SafetyFlags_NeverReadFromConfig_EvenWhenPresent()
        {
            WriteConfig(_dir, "init=true\nconfirm-delete=true\nconfirm-delete-io=true\noverwrite=true");

            RunOptions options = RunOptions.Parse(new[] { "--parse-only" }, cwd: _dir);

            Assert.False(options.Init);
            Assert.False(options.ConfirmDelete);
            Assert.False(options.ConfirmDeleteIo);
            Assert.False(options.Overwrite);
        }

        [Fact]
        public void ReverseExports_NeverComeFromConfig_OnlyCli()
        {
            WriteConfig(_dir, "export-code=true\nexport-all=true");

            RunOptions options = RunOptions.Parse(new[] { "--parse-only" }, cwd: _dir);

            Assert.Equal(ReverseExports.None, options.ReverseExports);
            Assert.False(options.IsReverseExport);
        }

        [Fact]
        public void TsprojAndPlcNameOverrides_NeverComeFromConfig_OnlyCli()
        {
            WriteConfig(_dir, @"tsproj=C:\FromConfig.tsproj" + "\nplc-name=FromConfig");

            RunOptions options = RunOptions.Parse(new[] { "--parse-only", "--name", "Real" }, cwd: _dir);

            Assert.Null(options.ExistingTsprojPath);
            Assert.Equal("Real", options.PlcProjectName);
        }

        [Fact]
        public void NoConfigFlag_SuppressesFileEvenWhenOnePresent()
        {
            WriteConfig(_dir, "name=FromConfig");

            RunOptions options = RunOptions.Parse(new[] { "--parse-only", "--no-config" }, cwd: _dir);

            Assert.False(options.ConfigFileLoaded);
            Assert.NotEqual("FromConfig", options.ProjectName);
        }

        [Fact]
        public void BooleanFlags_DefaultFromConfig()
        {
            WriteConfig(_dir, "incremental=true\nformat-check=true\ncheck-events=true\ncheck-links=true");

            RunOptions options = RunOptions.Parse(new[] { "--parse-only" }, cwd: _dir);

            Assert.True(options.Incremental);
            Assert.True(options.FormatCheck);
            Assert.True(options.CheckEvents);
            Assert.True(options.CheckLinks);
        }

        [Fact]
        public void ExportObjectName_DefaultsFromConfig()
        {
            WriteConfig(_dir, "export=FB_Motor");

            RunOptions options = RunOptions.Parse(new[] { "--parse-only" }, cwd: _dir);

            Assert.Equal("FB_Motor", options.ExportObjectName);
        }

        [Fact]
        public void ExportLinks_DefaultsFromConfig()
        {
            WriteConfig(_dir, "export-links=true");

            RunOptions options = RunOptions.Parse(new[] { "--parse-only" }, cwd: _dir);

            Assert.True(options.ExportLinks);
        }

        [Fact]
        public void IgnorePatterns_NeverComeFromConfig_OnlyCliAndStignore()
        {
            WriteConfig(_dir, "ignore=SomethingThatShouldBeIgnored.st");

            RunOptions options = RunOptions.Parse(new[] { "--parse-only" }, cwd: _dir);

            Assert.Empty(options.IgnorePatterns);
        }
    }
}
