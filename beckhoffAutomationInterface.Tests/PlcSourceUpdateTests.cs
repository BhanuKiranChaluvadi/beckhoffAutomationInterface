using System;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using BeckhoffAutomationInterface.Sync;
using Xunit;

namespace BeckhoffAutomationInterface.Tests
{
    public class PlcSourceUpdateTests
    {
        static StPouSource Block(string folder = "Lib") => new StPouSource("FB_Heat", PouKind.FunctionBlock,
            null, "FUNCTION_BLOCK FB_Heat", "") { RelativeFolder = folder };
        static StPouSource Method(string name, string body = "", string owner = "FB_Heat") =>
            new StPouSource(name, PouKind.Method, owner, "METHOD " + name, body);

        [Fact]
        public void MixedCaseOwnerMethodEditInvalidatesManifestAndUpdatesFile()
        {
            string dir = TestProjects.NewTempDir("bai_source_identity");
            string plc = Path.Combine(dir, "P.plcproj");
            File.WriteAllText(plc, "<Project />");
            var cache = new PlcObjectCache(Path.Combine(dir, "cache"), "converter");
            var sources = new[] { Block(), Method("Run", "x := 1;", "fb_heat") };
            PlcProjectCode.Apply(plc, PlcSourceObject.Group(sources), cache, true);
            var manifest = SyncManifest.Record("bai", sources, plc);
            Assert.Single(SyncManifest.Skippable(manifest, sources, plc));
            sources[1] = Method("Run", "x := 2;", "fb_heat");
            var skip = SyncManifest.Skippable(manifest, sources, plc);
            Assert.Empty(skip);
            PlcProjectCode.Apply(plc, PlcSourceObject.Group(sources), cache, true, skip);
            Assert.Contains("x := 2;", File.ReadAllText(Path.Combine(dir, "Lib", "FB_Heat.TcPOU")));
        }

        [Theory]
        [InlineData("Method")]
        [InlineData("Property")]
        public void CaseOnlyMemberRenamePreservesIdentity(string element)
        {
            var kind = (PouKind)Enum.Parse(typeof(PouKind), element);
            string dir = TestProjects.NewTempDir("bai_member_identity");
            string plc = Path.Combine(dir, "P.plcproj");
            File.WriteAllText(plc, "<Project />");
            var cache = new PlcObjectCache(Path.Combine(dir, "cache"), "converter");
            StPouSource Member(string name) => new StPouSource(name, kind, "FB_Heat",
                kind == PouKind.Method ? "METHOD " + name : "PROPERTY " + name + " : INT", "", "INT");
            PlcProjectCode.Apply(plc, PlcSourceObject.Group(new[] { Block(), Member("Run") }), cache, true);
            string path = Path.Combine(dir, "Lib", "FB_Heat.TcPOU");
            string id = (string)XDocument.Load(path).Descendants(element).Single().Attribute("Id");
            PlcProjectCode.Apply(plc, PlcSourceObject.Group(new[] { Block(), Member("run") }), cache, true);
            XElement member = XDocument.Load(path).Descendants(element).Single();
            Assert.Equal("run", (string)member.Attribute("Name"));
            Assert.Equal(id, (string)member.Attribute("Id"));
        }

        [Fact]
        public void MirroringMovesObjectsAndRemovesOnlyPreviouslyTrackedMembers()
        {
            string dir = TestProjects.NewTempDir("bai_mirror_files");
            string plc = Path.Combine(dir, "P.plcproj");
            File.WriteAllText(plc, "<Project />");
            var cache = new PlcObjectCache(Path.Combine(dir, "cache"), "converter");
            PlcProjectCode.Apply(plc, PlcSourceObject.Group(new[] { Block(), Method("Gone"), Method("Handwritten") }), cache, true);
            var report = PlcProjectCode.Apply(plc, PlcSourceObject.Group(new[] { Block("Moved") }), cache, true,
                previousNames: new[] { "FB_Heat", "fb_heat.gone" });
            Assert.False(File.Exists(Path.Combine(dir, "Lib", "FB_Heat.TcPOU")));
            XElement member = XDocument.Load(Path.Combine(dir, "Moved", "FB_Heat.TcPOU")).Descendants("Method").Single();
            Assert.Equal("Handwritten", (string)member.Attribute("Name"));
            Assert.Single(XDocument.Load(plc).Descendants("Compile"));
            Assert.Contains(report.Deleted, s => s.Contains("gone"));
        }

        [Fact]
        public void FailedProjectWriteRestoresReplacedAndDeletedObjectFiles()
        {
            string dir = TestProjects.NewTempDir("bai_source_rollback");
            string plc = Path.Combine(dir, "P.plcproj");
            File.WriteAllText(plc, "<Project />");
            var cache = new PlcObjectCache(Path.Combine(dir, "cache"), "converter");
            PlcProjectCode.Apply(plc, PlcSourceObject.Group(new[] { Block(), Method("Gone") }), cache, true);
            string oldPath = Path.Combine(dir, "Lib", "FB_Heat.TcPOU");
            byte[] oldProject = File.ReadAllBytes(plc), oldObject = File.ReadAllBytes(oldPath);
            bool failed = false;
            void Write(string path, byte[] bytes)
            {
                if (path == plc && !failed) { failed = true; throw new IOException("disk failure"); }
                ProjectFileBatch.Write(path, bytes);
            }
            Assert.Throws<IOException>(() => PlcProjectCode.Apply(plc, PlcSourceObject.Group(new[] { Block("Moved") }),
                cache, true, previousNames: new[] { "FB_Heat", "FB_Heat.Gone" }, write: Write));
            Assert.Equal(oldProject, File.ReadAllBytes(plc));
            Assert.Equal(oldObject, File.ReadAllBytes(oldPath));
            Assert.False(File.Exists(Path.Combine(dir, "Moved", "FB_Heat.TcPOU")));
        }
    }
}
