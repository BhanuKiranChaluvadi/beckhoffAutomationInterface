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

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public void UpdatingPropertyBodiesPreservesProjectLocalAccessorDeclarations(bool preserveMembers)
        {
            string dir = TestProjects.NewTempDir("bai_accessor_declarations");
            string plc = Path.Combine(dir, "P.plcproj");
            File.WriteAllText(plc, "<Project />");
            var cache = new PlcObjectCache(Path.Combine(dir, "cache"), "converter");
            StPouSource Property(string body) => new StPouSource("Value", PouKind.Property, "FB_Heat",
                "PROPERTY Value : INT", null, "INT", "Value := cached + " + body + ";", "cached := Value;");
            PlcProjectCode.Apply(plc, PlcSourceObject.Group(new[] { Block(), Property("1") }), cache, true);
            string path = Path.Combine(dir, "Lib", "FB_Heat.TcPOU");
            XDocument old = XDocument.Load(path);
            const string getter = "PRIVATE\r\nVAR\r\n    cached : INT;\r\nEND_VAR";
            const string setter = "PROTECTED\r\nVAR\r\n    cached : INT;\r\nEND_VAR";
            old.Descendants("Get").Single().Element("Declaration").ReplaceNodes(new XCData(getter));
            old.Descendants("Set").Single().Element("Declaration").ReplaceNodes(new XCData(setter));
            old.Save(path);
            XDocument saved = XDocument.Load(path);
            string expectedGetter = saved.Descendants("Get").Single().Element("Declaration").Value;
            string expectedSetter = saved.Descendants("Set").Single().Element("Declaration").Value;
            string getId = (string)saved.Descendants("Get").Single().Attribute("Id");
            PlcProjectCode.Apply(plc, PlcSourceObject.Group(new[] { Block(), Property("2") }), cache, preserveMembers);
            XDocument updated = XDocument.Load(path);
            Assert.Equal(expectedGetter, updated.Descendants("Get").Single().Element("Declaration").Value);
            Assert.Equal(expectedSetter, updated.Descendants("Set").Single().Element("Declaration").Value);
            Assert.Equal(getId, (string)updated.Descendants("Get").Single().Attribute("Id"));
            Assert.Equal("Value := cached + 2;", updated.Descendants("Get").Single().Descendants("ST").Single().Value);
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
        public void UnchangedMirrorKeepsTheSessionOpenButMovesAndDeletionsRequireFileWork()
        {
            string dir = TestProjects.NewTempDir("bai_mirror_work");
            string plc = Path.Combine(dir, "P.plcproj");
            File.WriteAllText(plc, "<Project />");
            var cache = new PlcObjectCache(Path.Combine(dir, "cache"), "converter");
            var initial = PlcSourceObject.Group(new[] { Block(), Method("Run") });
            PlcProjectCode.Apply(plc, initial, cache, true);
            string[] tracked = { "FB_Heat", "FB_Heat.Run" };
            Assert.False(PlcProjectCode.NeedsMirror(plc, initial, tracked));
            Assert.True(PlcProjectCode.NeedsMirror(plc, PlcSourceObject.Group(new[] { Block() }), tracked));
            Assert.True(PlcProjectCode.NeedsMirror(plc,
                PlcSourceObject.Group(new[] { Block("Moved"), Method("Run") }), tracked));
            var moved = PlcSourceObject.Group(new[] { Block("Moved"), Method("Run") });
            // Standalone sync can retain a location different from the source folder.
            PlcProjectCode.Apply(plc, moved, cache, true);
            var manifest = SyncManifest.Record("bai", moved.SelectMany(s => s.Sources), plc);
            var skip = SyncManifest.Skippable(manifest, moved.SelectMany(s => s.Sources), plc);
            Assert.Single(skip);
            PlcProjectCode.Apply(plc, moved, cache, true, skip, tracked);
            Assert.True(File.Exists(Path.Combine(dir, "Moved", "FB_Heat.TcPOU")));
            Assert.False(File.Exists(Path.Combine(dir, "Lib", "FB_Heat.TcPOU")));
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

        [Fact]
        public void MirroringRemovesGoneObjectsAndLeavesTaskFilesAlone()
        {
            string dir = TestProjects.NewTempDir("bai_source_delete");
            string plc = Path.Combine(dir, "P.plcproj");
            File.WriteAllText(plc, "<Project><ItemGroup><Compile Include='FB_Task.TcTTO' /></ItemGroup></Project>");
            string task = Path.Combine(dir, "FB_Task.TcTTO");
            File.WriteAllText(task, "task configuration");
            var cache = new PlcObjectCache(Path.Combine(dir, "cache"), "converter");
            PlcProjectCode.Apply(plc, PlcSourceObject.Group(new[] { Block() }), cache, true);
            PlcProjectCode.Apply(plc, PlcSourceObject.Group(new StPouSource[0]), cache, true,
                previousNames: new[] { "FB_Heat", "FB_Task" });
            Assert.False(File.Exists(Path.Combine(dir, "Lib", "FB_Heat.TcPOU")));
            Assert.Equal("task configuration", File.ReadAllText(task));
            Assert.Equal("FB_Task.TcTTO", (string)XDocument.Load(plc).Descendants("Compile").Single().Attribute("Include"));
        }

        [Fact]
        public void InvalidDestinationCannotDeleteAStaleObjectBeforePlanningFinishes()
        {
            string dir = TestProjects.NewTempDir("bai_source_plan");
            string plc = Path.Combine(dir, "P.plcproj");
            File.WriteAllText(plc, "<Project />");
            var cache = new PlcObjectCache(Path.Combine(dir, "cache"), "converter");
            PlcProjectCode.Apply(plc, PlcSourceObject.Group(new[] { Block() }), cache, true);
            byte[] project = File.ReadAllBytes(plc);
            string path = Path.Combine(dir, "Lib", "FB_Heat.TcPOU");
            byte[] original = File.ReadAllBytes(path);
            var invalid = new StPouSource("FB_Other", PouKind.FunctionBlock, null, "FUNCTION_BLOCK FB_Other", "")
                { RelativeFolder = "../outside" };
            Assert.Throws<InvalidOperationException>(() => PlcProjectCode.Apply(plc,
                PlcSourceObject.Group(new[] { invalid }), cache, true, previousNames: new[] { "FB_Heat" }));
            Assert.Equal(project, File.ReadAllBytes(plc));
            Assert.Equal(original, File.ReadAllBytes(path));
        }
    }
}
