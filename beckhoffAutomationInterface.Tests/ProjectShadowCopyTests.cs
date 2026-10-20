using BeckhoffAutomationInterface;
using Xunit;

namespace BeckhoffAutomationInterface.Tests
{
    /// <summary>Adopted projects are attached as a temp copy, because TwinCAT saves the project by
    /// itself during variant builds and activation.</summary>
    public class ProjectShadowCopyTests : System.IDisposable
    {
        readonly string _dir;
        readonly string _tsproj;

        public ProjectShadowCopyTests()
        {
            _dir = Path.Combine(Path.GetTempPath(), "ShadowTests_" + Guid.NewGuid().ToString("N").Substring(0, 8));
            string project = Path.Combine(_dir, "Shark");
            Directory.CreateDirectory(Path.Combine(project, "PLC_NFL_prj", "POUs"));
            Directory.CreateDirectory(Path.Combine(project, "_Config", "IO"));
            Directory.CreateDirectory(Path.Combine(project, "_Boot", "TwinCAT OS (x64)"));
            Directory.CreateDirectory(Path.Combine(project, "PLC_NFL_prj", "_CompileInfo"));
            _tsproj = Path.Combine(project, "Shark.tsproj");
            File.WriteAllText(_tsproj, "<TcSmProject/>");
            File.WriteAllText(Path.Combine(project, "PLC_NFL_prj", "POUs", "MAIN.TcPOU"), "main");
            File.WriteAllText(Path.Combine(project, "_Config", "IO", "Device 1.xti"), "io");
            File.WriteAllText(Path.Combine(project, "_Boot", "TwinCAT OS (x64)", "CurrentConfig.xml"), "boot");
            File.WriteAllText(Path.Combine(project, "PLC_NFL_prj", "_CompileInfo", "x.compileinfo"), "ci");
        }

        public void Dispose() => Directory.Delete(_dir, recursive: true);

        [Fact]
        public void Copy_HasTheProjectFiles_ButNotBuildOutput()
        {
            using (ProjectShadowCopy shadow = ProjectShadowCopy.Create(_tsproj, _dir))
            {
                Assert.True(File.Exists(shadow.TsprojPath));
                Assert.EndsWith(Path.Combine("Shark", "Shark.tsproj"), shadow.TsprojPath);
                Assert.True(File.Exists(Path.Combine(shadow.ShadowDir, "PLC_NFL_prj", "POUs", "MAIN.TcPOU")));
                Assert.True(File.Exists(Path.Combine(shadow.ShadowDir, "_Config", "IO", "Device 1.xti")));
                Assert.False(Directory.Exists(Path.Combine(shadow.ShadowDir, "_Boot")));
                Assert.False(Directory.Exists(Path.Combine(shadow.ShadowDir, "PLC_NFL_prj", "_CompileInfo")));
            }
        }

        [Fact]
        public void WritingTheCopy_LeavesTheOriginalUntouched()
        {
            using (ProjectShadowCopy shadow = ProjectShadowCopy.Create(_tsproj, _dir))
                File.WriteAllText(shadow.TsprojPath, "<TcSmProject TcProjectVariant=\"MachineB\"/>"); // what TwinCAT did

            Assert.Equal("<TcSmProject/>", File.ReadAllText(_tsproj));
        }

        [Fact]
        public void Dispose_DeletesTheCopy()
        {
            ProjectShadowCopy shadow = ProjectShadowCopy.Create(_tsproj, _dir);
            string copy = shadow.ShadowDir;

            shadow.Dispose();

            Assert.False(Directory.Exists(copy));
        }

        [Fact]
        public void ErrorPathsInTheCopy_MapBackToTheProject()
        {
            using (ProjectShadowCopy shadow = ProjectShadowCopy.Create(_tsproj, _dir))
            {
                string inCopy = Path.Combine(shadow.ShadowDir, "PLC_NFL_prj", "POUs", "MAIN.TcPOU");

                Assert.Equal(Path.Combine(Path.GetDirectoryName(_tsproj), "PLC_NFL_prj", "POUs", "MAIN.TcPOU"), shadow.ToOriginalPath(inCopy));
                Assert.Equal(@"C:\elsewhere\X.TcPOU", shadow.ToOriginalPath(@"C:\elsewhere\X.TcPOU"));
                Assert.Null(shadow.ToOriginalPath(null));
            }
        }
    }
}
