using BeckhoffAutomationInterface.Sync;
using Xunit;

namespace BeckhoffAutomationInterface.Tests
{
    /// <summary>safety.xml (BeckhoffLib tasks/2026-10-16-plc-release, S3.2a).</summary>
    public class SafetyManifestTests : IDisposable
    {
        readonly string _source;
        readonly string _project;

        public SafetyManifestTests()
        {
            _source = Path.Combine(Path.GetTempPath(), "SafetyManifestTests_" + Guid.NewGuid());
            _project = Path.Combine(_source, "safety", "M_Safety_prj");
            Directory.CreateDirectory(Path.Combine(_project, "TwinSafeGroup1", "Alias Devices"));
            File.WriteAllText(Path.Combine(_project, "M_Safety_prj.splcproj"), "<Project />");
            File.WriteAllText(Path.Combine(_project, "TargetSystemConfig.xml"), "<TargetSystemConfig />");
            File.WriteAllText(Path.Combine(_project, "TwinSafeGroup1", "Alias Devices", "Run.sds"), "<SafetyDeviceDesc />");
        }

        public void Dispose() => Directory.Delete(_source, recursive: true);

        SafetyManifest Parse(string xml)
        {
            string path = Path.Combine(_source, "safety.xml");
            File.WriteAllText(path, xml);
            return SafetyManifestParser.Parse(path, _source);
        }

        string Problem(string xml) => Assert.Throws<SafetyManifestException>(() => Parse(xml)).Message;

        [Fact]
        public void NoFile_IsNoSafetyProject()
        {
            Assert.Null(SafetyManifestParser.Parse(Path.Combine(_source, "safety.xml"), _source));
        }

        [Fact]
        public void Valid_ResolvesTheFolderAgainstTheSourceFolder()
        {
            SafetyManifest m = Parse("<Safety Name=\"M_Safety_prj\" Folder=\"safety/M_Safety_prj\" />");

            Assert.Equal("M_Safety_prj", m.Name);
            Assert.Equal(_project, m.Folder);
            Assert.Null(m.TargetSystemConfig);
            Assert.Equal(Path.Combine(_project, "TargetSystemConfig.xml"), m.EffectiveTargetSystemConfig);
            Assert.Equal("M_Safety_prj.splcproj", m.SplcprojFileName);
        }

        [Fact]
        public void SourceFiles_AreAllFilesOfTheFolder_ByRelativePath()
        {
            SafetyManifest m = Parse("<Safety Name=\"M_Safety_prj\" Folder=\"safety/M_Safety_prj\" />");

            SortedDictionary<string, string> files = m.SourceFiles();

            Assert.Equal(new[] { "M_Safety_prj.splcproj", "TargetSystemConfig.xml", @"TwinSafeGroup1\Alias Devices\Run.sds" }, files.Keys);
            Assert.Equal(Path.Combine(_project, "TargetSystemConfig.xml"), files["TargetSystemConfig.xml"]);
        }

        [Fact]
        public void TargetSystemConfig_ReplacesTheProjectsFileInTheCopy()
        {
            Directory.CreateDirectory(Path.Combine(_source, "safety", "Google"));
            string other = Path.Combine(_source, "safety", "Google", "TargetSystemConfig.xml");
            File.WriteAllText(other, "<TargetSystemConfig />");

            SafetyManifest m = Parse("<Safety Name=\"M_Safety_prj\" Folder=\"safety/M_Safety_prj\" TargetSystemConfig=\"safety/Google/TargetSystemConfig.xml\" />");

            Assert.Equal(other, m.TargetSystemConfig);
            Assert.Equal(other, m.EffectiveTargetSystemConfig);
            Assert.Equal(other, m.SourceFiles()["TargetSystemConfig.xml"]);
            Assert.Equal(3, m.SourceFiles().Count);
        }

        [Fact]
        public void OtherAttributes_AreIgnored()
        {
            // A build script may keep its own data in the file (e.g. where it took the folder from).
            SafetyManifest m = Parse("<Safety Name=\"M_Safety_prj\" Folder=\"safety/M_Safety_prj\" Source=\"Safety/NFLite/M\" />");

            Assert.Equal("M_Safety_prj", m.Name);
        }

        [Fact]
        public void MissingFolder_IsAClearError()
        {
            string message = Problem("<Safety Name=\"M_Safety_prj\" Folder=\"safety/Nope\" />");

            Assert.Contains("Folder 'safety/Nope' does not exist", message);
        }

        [Fact]
        public void MissingSplcproj_IsAClearError()
        {
            string message = Problem("<Safety Name=\"Other_prj\" Folder=\"safety/M_Safety_prj\" />");

            Assert.Contains("has no Other_prj.splcproj", message);
        }

        [Fact]
        public void MissingTargetSystemConfig_IsAClearError()
        {
            File.Delete(Path.Combine(_project, "TargetSystemConfig.xml"));

            Assert.Contains("has no TargetSystemConfig.xml", Problem("<Safety Name=\"M_Safety_prj\" Folder=\"safety/M_Safety_prj\" />"));
        }

        [Fact]
        public void MissingReplacementTargetSystemConfig_IsAClearError()
        {
            string message = Problem("<Safety Name=\"M_Safety_prj\" Folder=\"safety/M_Safety_prj\" TargetSystemConfig=\"safety/none.xml\" />");

            Assert.Contains("TargetSystemConfig 'safety/none.xml' does not exist", message);
        }

        [Fact]
        public void MissingNameAndFolder_AreBothReported()
        {
            string message = Problem("<Safety />");

            Assert.Contains("Name is missing", message);
            Assert.Contains("Folder is missing", message);
        }

        [Fact]
        public void WrongRoot_IsRefused()
        {
            Assert.Contains("root element must be <Safety>", Problem("<Safeties />"));
        }
    }
}
