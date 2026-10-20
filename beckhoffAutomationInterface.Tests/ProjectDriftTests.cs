using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using BeckhoffAutomationInterface.Release;
using Xunit;

namespace BeckhoffAutomationInterface.Tests
{
    /// <summary>Update-gate regressions of 'package drift' (was Tests/Tools/test_project_drift.py):
    /// reject edits without losing code or XML meaning.</summary>
    public sealed class ProjectDriftTests : IDisposable
    {
        readonly string root = TestProjects.NewTempDir("bai_drift");
        public void Dispose() { try { Directory.Delete(root, true); } catch (IOException) { } }

        static string N(string name, string xml) => Encoding.UTF8.GetString(ProjectDrift.Normalized(name, Encoding.UTF8.GetBytes(xml)));
        static Dictionary<string, byte[]> Files(params (string, string)[] files) => files.ToDictionary(f => f.Item1, f => Encoding.UTF8.GetBytes(f.Item2));

        string Zip(params (string Name, string Text)[] entries)
        {
            string path = Path.Combine(root, Guid.NewGuid().ToString("N") + ".zip");
            using (var zip = ZipFile.Open(path, ZipArchiveMode.Create))
                foreach (var e in entries)
                    using (var writer = new StreamWriter(zip.CreateEntry(e.Name).Open())) writer.Write(e.Text);
            return path;
        }

        [Fact]
        public void XmlFormattingOnlyIsNoDrift() =>
            Assert.Equal(N("a.tsproj", "<Root a=\"1\" b=\"2\"><Child>3</Child></Root>"), N("a.tsproj", "<Root b=\"2\" a=\"1\">\r\n  <Child>3</Child>\r\n</Root>"));

        [Theory]
        [InlineData("<ST><![CDATA[x := 1;]]></ST>", "<ST><![CDATA[x := 2;]]></ST>")]
        [InlineData("<ST> </ST>", "<ST>  </ST>")]
        [InlineData("<T><Call>A</Call><Call>B</Call></T>", "<T><Call>B</Call><Call>A</Call></T>")]
        [InlineData("<X><!--before--></X>", "<X><!--after--></X>")]
        [InlineData("<CoE><Data>0100</Data></CoE>", "<CoE><Data>0200</Data></CoE>")]
        public void CodeValuesOrderAndCommentsMatter(string before, string after) => Assert.NotEqual(N("a.xml", before), N("a.xml", after));

        [Fact]
        public void ReportsAddedRemovedAndModified()
        {
            var baseline = Files(("a.tsproj", "a"), ("a.plcproj", "b"), ("old.st", "old"));
            var report = ProjectDrift.Compare(baseline, Files(("a.tsproj", "a"), ("a.plcproj", "changed"), ("new.st", "new")));
            Assert.Equal("Drift", report["status"]);
            Assert.Equal(new[] { "added", "modified", "removed" }, ((List<object>)report["changes"]).Select(c => (string)((Dictionary<string, object>)c)["change"]).OrderBy(c => c));
            Assert.Equal("Passed", ProjectDrift.Compare(baseline, new Dictionary<string, byte[]>(baseline))["status"]);
        }

        [Theory]
        [InlineData("")]
        [InlineData("a.tsproj")]
        [InlineData("a.plcproj")]
        public void MissingProjectFails(string names)
        {
            var files = names.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries).ToDictionary(n => n, n => new byte[] { 1 });
            Assert.Throws<InvalidDataException>(() => ProjectDrift.Compare(files, files));
        }

        [Theory]
        [InlineData("M/_Boot/x.xml", false)] [InlineData("M/_Libraries/x.xml", false)] [InlineData(".vs/x", false)] [InlineData("M/PLC.tmc", false)]
        [InlineData("M/M.tsproj", true)] [InlineData("M/IO/Device.xti", true)] [InlineData("M/Safety/TargetSystemConfig.xml", true)] [InlineData("M/custom.dat", true)]
        public void GeneratedOutputIsExcluded(string name, bool included) => Assert.Equal(included, ProjectDrift.Included(name));

        [Fact]
        public void ArchiveIsPinnedToItsHash()
        {
            string zip = Zip(("a.tsproj", "<X/>"));
            Assert.Single(ProjectDrift.ArchiveFiles(zip, ArtifactFiles.Hash(zip)));
            Assert.Throws<InvalidDataException>(() => ProjectDrift.ArchiveFiles(zip, new string('0', 64)));
        }

        [Theory]
        [InlineData("../outside")] [InlineData("/absolute")] [InlineData("C:/drive")] [InlineData("a/../b")] [InlineData("a./b")] [InlineData("a:stream")]
        public void UnsafeArchivePathsFail(string name)
        {
            string zip = Zip((name, "x"));
            Assert.Throws<InvalidDataException>(() => ProjectDrift.ArchiveFiles(zip, ArtifactFiles.Hash(zip)));
        }

        [Fact]
        public void DuplicateArchivePathFails()
        {
            string zip = Zip(("A.tsproj", "<X/>"), ("a.tsproj", "<X/>"));
            Assert.Contains("Duplicate", Assert.Throws<InvalidDataException>(() => ProjectDrift.ArchiveFiles(zip, ArtifactFiles.Hash(zip))).Message);
        }

        [Fact]
        public void DtdFails() => Assert.Throws<InvalidDataException>(() => N("a.xml", "<!DOCTYPE a [<!ENTITY x \"expanded\">]><a>&x;</a>"));

        [Fact]
        public void RunWritesReportAndExitCodes()
        {
            string project = Path.Combine(root, "service");
            Directory.CreateDirectory(Path.Combine(project, "M", "_Boot"));
            File.WriteAllText(Path.Combine(project, "M.tsproj"), "<TcSmProject>\n  <Plc/>\n</TcSmProject>");
            File.WriteAllText(Path.Combine(project, "M", "M.plcproj"), "<Project/>");
            File.WriteAllText(Path.Combine(project, "M", "_Boot", "ignored.bin"), "build output");
            string zip = Zip(("M.tsproj", "<TcSmProject><Plc/></TcSmProject>"), ("M/M.plcproj", "<Project/>"));
            string report = Path.Combine(root, "drift.json");
            Assert.Equal(0, ProjectDrift.Run(zip, ArtifactFiles.Hash(zip).ToUpperInvariant(), project, report));
            Assert.Equal("Passed", ArtifactFiles.Read(report)["status"]);
            File.WriteAllText(Path.Combine(project, "M", "M.plcproj"), "<Project><Changed/></Project>");
            Assert.Equal(1, ProjectDrift.Run(zip, ArtifactFiles.Hash(zip), project, Path.Combine(root, "drift2.json")));
            Assert.Equal(2, ProjectDrift.Run(zip, new string('0', 64), project, Path.Combine(root, "drift3.json")));
            Assert.Equal("Error", ArtifactFiles.Read(Path.Combine(root, "drift3.json"))["status"]);
            Assert.Throws<ArgumentException>(() => ProjectDrift.Run(zip, ArtifactFiles.Hash(zip), project, Path.Combine(project, "r.json")));
        }
    }
}
