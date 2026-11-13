using System.Collections.Generic;
using System.IO;
using System.Linq;
using BeckhoffAutomationInterface.Sync;
using Xunit;

namespace BeckhoffAutomationInterface.Tests
{
    /// <summary>The sync manifest: which objects a sync may skip without reading them over COM.
    /// Pure decisions and files only; nothing here starts TwinCAT.</summary>
    public class SyncManifestTests
    {
        static StPouSource Fb(string name, string decl = "FUNCTION_BLOCK FB_X\r\nVAR\r\nEND_VAR", string impl = "", string folder = "Lib/A") =>
            new StPouSource(name, PouKind.FunctionBlock, null, decl, impl) { RelativeFolder = folder };

        static StPouSource Method(string owner, string name, string decl = "METHOD PUBLIC Run : BOOL", string impl = "Run := TRUE;") =>
            new StPouSource(name, PouKind.Method, owner, decl, impl);

        static StPouSource Gvl(string name, string decl = "VAR_GLOBAL\r\n    n : INT;\r\nEND_VAR", string folder = "Lib/A") =>
            new StPouSource(name, PouKind.Gvl, null, decl, null) { RelativeFolder = folder };

        // ------------------------------------------------------------------ the decision

        [Theory]
        [InlineData("s", "d", "s", "d", true)]     // source and project file as recorded
        [InlineData("s", "d", "s2", "d", false)]   // the source changed
        [InlineData("s", "d", "s", "d2", false)]   // the project file changed (edited in XAE, or saved again)
        [InlineData("s", "d", "s", null, false)]   // no project file for the object
        [InlineData("s", "d", null, "d", false)]
        [InlineData(null, null, "s", "d", false)]  // no entry
        public void CanSkip_OnlyWhenSourceAndDiskHashBothMatchTheEntry(string entrySource, string entryDisk, string source, string disk, bool skip)
        {
            SyncManifest.Entry entry = entrySource == null ? null : new SyncManifest.Entry(entrySource, entryDisk);
            Assert.Equal(skip, SyncManifest.CanSkip(entry, source, disk));
        }

        // ------------------------------------------------------------------ groups and source hashes

        [Fact]
        public void Groups_PutMembersWithTheirOwner_AndLeaveOutMembersWithoutOne()
        {
            var sources = new List<StPouSource> { Fb("FB_A"), Method("FB_A", "Run"), Gvl("GVL_B"), Method("FB_Missing", "Run") };
            Dictionary<string, List<StPouSource>> groups = SyncManifest.Groups(sources);
            Assert.Equal(new[] { "FB_A", "GVL_B" }, groups.Keys.OrderBy(k => k));
            Assert.Equal(new[] { "FB_A", "Run" }, groups["FB_A"].Select(s => s.Name));
        }

        [Fact]
        public void SourceHash_ChangesWithEveryTextBaiSendsAndWithTheFolder()
        {
            string Hash(params StPouSource[] group) => SyncManifest.SourceHash(group.ToList());
            string baseline = Hash(Fb("FB_A"), Method("FB_A", "Run"));
            Assert.Equal(baseline, Hash(Fb("FB_A"), Method("FB_A", "Run")));
            Assert.NotEqual(baseline, Hash(Fb("FB_A", decl: "FUNCTION_BLOCK FB_A\r\nVAR\r\n  n : INT;\r\nEND_VAR"), Method("FB_A", "Run")));
            Assert.NotEqual(baseline, Hash(Fb("FB_A", impl: "x := 1;"), Method("FB_A", "Run")));
            Assert.NotEqual(baseline, Hash(Fb("FB_A", folder: "Lib/B"), Method("FB_A", "Run")));
            Assert.NotEqual(baseline, Hash(Fb("FB_A"), Method("FB_A", "Run", impl: "Run := FALSE;")));
            Assert.NotEqual(baseline, Hash(Fb("FB_A")));
            Assert.NotEqual(baseline, Hash(Fb("FB_A"), Method("FB_A", "Run"), Method("FB_A", "Stop")));
            // Two texts that run into each other: the separator keeps them apart.
            Assert.NotEqual(Hash(Fb("FB_A", decl: "ab", impl: "c")), Hash(Fb("FB_A", decl: "a", impl: "bc")));
        }

        [Fact]
        public void SourceHash_ChangesWithAPropertyAccessor()
        {
            StPouSource Prop(string get) => new StPouSource("Value", PouKind.Property, "FB_A", "PROPERTY Value : INT", null, "INT", get, null);
            Assert.NotEqual(SyncManifest.SourceHash(new List<StPouSource> { Fb("FB_A"), Prop("Value := 1;") }),
                SyncManifest.SourceHash(new List<StPouSource> { Fb("FB_A"), Prop("Value := 2;") }));
        }

        // ------------------------------------------------------------------ the project file of an object

        const string Plcproj = @"<?xml version=""1.0"" encoding=""utf-8""?>
<Project DefaultTargets=""Build"" xmlns=""http://schemas.microsoft.com/developer/msbuild/2003"">
  <ItemGroup>
    <Compile Include=""Lib\A\FB_A.TcPOU""><SubType>Code</SubType></Compile>
    <Compile Include=""Lib\A\GVL_B.TcGVL""><SubType>Code</SubType></Compile>
    <Compile Include=""POUs\MAIN.TcPOU""><SubType>Code</SubType></Compile>
    <Compile Include=""Lib\A\FB_Twice.TcPOU"" />
    <Compile Include=""Lib\B\FB_Twice.TcPOU"" />
    <Compile Include=""PlcTask.TcTTO"" />
  </ItemGroup>
</Project>";

        [Fact]
        public void ProjectFiles_AreTheFilesThePlcProjectNames_ByObjectFileName()
        {
            Dictionary<string, string> files = SyncManifest.ProjectFiles(Plcproj);
            Assert.Equal(@"Lib\A\FB_A.TcPOU", files["fb_a.tcpou"]);
            Assert.Equal(@"POUs\MAIN.TcPOU", files["MAIN.TcPOU"]);   // the template MAIN, wherever MAIN.st is
            Assert.False(files.ContainsKey("FB_Twice.TcPOU"));        // two files: not known
            Assert.False(files.ContainsKey("PlcTask.TcTTO"));
        }

        [Theory]
        [InlineData("FunctionBlock", ".TcPOU")]
        [InlineData("Program", ".TcPOU")]
        [InlineData("Function", ".TcPOU")]
        [InlineData("Interface", ".TcIO")]
        [InlineData("EnumDut", ".TcDUT")]
        [InlineData("StructDut", ".TcDUT")]
        [InlineData("AliasDut", ".TcDUT")]
        [InlineData("Gvl", ".TcGVL")]
        public void FileExtension_ByKind(string kind, string extension)
        {
            Assert.Equal(extension, SyncManifest.FileExtension((PouKind)System.Enum.Parse(typeof(PouKind), kind)));
        }

        // ------------------------------------------------------------------ the project file holds the texts

        const string FbFile = "﻿<?xml version=\"1.0\" encoding=\"utf-8\"?>\r\n<TcPlcObject Version=\"1.1.0.1\">\r\n" +
            "  <POU Name=\"FB_A\" Id=\"{1}\" SpecialFunc=\"None\">\r\n" +
            "    <Declaration><![CDATA[FUNCTION_BLOCK FB_A\r\nVAR\r\nEND_VAR]]></Declaration>\r\n" +
            "    <Implementation>\r\n      <ST><![CDATA[]]></ST>\r\n    </Implementation>\r\n" +
            "    <Method Name=\"Run\" Id=\"{2}\">\r\n      <Declaration><![CDATA[METHOD PUBLIC Run : BOOL]]></Declaration>\r\n" +
            "      <Implementation>\r\n        <ST><![CDATA[Run := TRUE;\r\nn := n + 1;\r\n]]></ST>\r\n      </Implementation>\r\n    </Method>\r\n" +
            "  </POU>\r\n</TcPlcObject>";

        [Fact]
        public void FileHoldsTexts_WhenEveryTextIsAWholeSection()
        {
            var group = new List<StPouSource> { Fb("FB_A", decl: "FUNCTION_BLOCK FB_A\r\nVAR\r\nEND_VAR"), Method("FB_A", "Run", impl: "Run := TRUE;\r\nn := n + 1;") };
            Assert.True(SyncManifest.FileHoldsTexts(FbFile, group));
        }

        [Fact]
        public void FileHoldsTexts_NotWhenATextDiffers_OrIsOnlyTheStartOfASection()
        {
            var changed = new List<StPouSource> { Fb("FB_A", decl: "FUNCTION_BLOCK FB_A\r\nVAR\r\nEND_VAR"), Method("FB_A", "Run", impl: "Run := FALSE;") };
            Assert.False(SyncManifest.FileHoldsTexts(FbFile, changed));
            // The new text is the old one without its last line: the file is stale.
            var shorter = new List<StPouSource> { Fb("FB_A", decl: "FUNCTION_BLOCK FB_A\r\nVAR\r\nEND_VAR"), Method("FB_A", "Run", impl: "Run := TRUE;") };
            Assert.False(SyncManifest.FileHoldsTexts(FbFile, shorter));
        }

        // ------------------------------------------------------------------ the file

        [Fact]
        public void Manifest_RoundTrips_AndIsIgnoredWhenMissingBrokenOrFromAnotherBai()
        {
            string file = Path.Combine(TestProjects.NewTempDir("bai_syncmanifest"), "bai-sync-manifest.json");
            Assert.Null(SyncManifest.Load(file, "bai 1"));

            var m = new SyncManifest("bai 1");
            m.Entries["FB_A"] = new SyncManifest.Entry("s1", "d1");
            m.Save(file);
            SyncManifest back = SyncManifest.Load(file, "bai 1");
            Assert.Equal("s1", back.Entries["FB_A"].Source);
            Assert.Equal("d1", back.Entries["FB_A"].Disk);
            Assert.True(back.Entries.ContainsKey("fb_a"));

            Assert.Null(SyncManifest.Load(file, "bai 2"));
            File.WriteAllText(file, File.ReadAllText(file).Replace("\"format\":" + SyncManifest.Format, "\"format\":" + (SyncManifest.Format + 1)));
            Assert.Null(SyncManifest.Load(file, "bai 1"));
            File.WriteAllText(file, "{ not json");
            Assert.Null(SyncManifest.Load(file, "bai 1"));
            File.WriteAllText(file, "[1, 2]");
            Assert.Null(SyncManifest.Load(file, "bai 1"));
        }

        [Fact]
        public void Save_ReplacesTheOldFileWhole()
        {
            string file = Path.Combine(TestProjects.NewTempDir("bai_syncmanifest"), "bai-sync-manifest.json");
            var first = new SyncManifest("bai 1");
            first.Entries["FB_A"] = new SyncManifest.Entry("s1", "d1");
            first.Save(file);
            var second = new SyncManifest("bai 1");
            second.Entries["FB_B"] = new SyncManifest.Entry("s2", "d2");
            second.Save(file);
            Assert.Equal(new[] { "FB_B" }, SyncManifest.Load(file, "bai 1").Entries.Keys);
            Assert.False(File.Exists(file + ".tmp"));
        }

        // ------------------------------------------------------------------ the whole plan on files

        [Fact]
        public void Plan_SkipsAnObjectOnlyAfterItWasRecorded_AndComparesItAgainWhenItsFileChanges()
        {
            string plc = TestProjects.NewTempDir("bai_syncplc");
            Directory.CreateDirectory(Path.Combine(plc, @"Lib\A"));
            string plcproj = Path.Combine(plc, "P.plcproj");
            File.WriteAllText(plcproj, Plcproj);
            string fbFile = Path.Combine(plc, @"Lib\A\FB_A.TcPOU");
            File.WriteAllText(fbFile, FbFile);
            var sources = new List<StPouSource> { Fb("FB_A", decl: "FUNCTION_BLOCK FB_A\r\nVAR\r\nEND_VAR"), Method("FB_A", "Run", impl: "Run := TRUE;\r\nn := n + 1;"), Gvl("GVL_B") };

            // No manifest: nothing is skipped.
            Assert.Empty(SyncManifest.Skippable(null, sources, plcproj));

            // After the sync: FB_A is recorded (its file holds the texts); GVL_B has no file.
            SyncManifest recorded = SyncManifest.Record("bai 1", sources, plcproj);
            Assert.Equal(new[] { "FB_A" }, recorded.Entries.Keys);
            Assert.Equal(new[] { "FB_A" }, SyncManifest.Skippable(recorded, sources, plcproj));

            // The source changed: compare.
            var changed = new List<StPouSource> { Fb("FB_A", decl: "FUNCTION_BLOCK FB_A\r\nVAR\r\nEND_VAR"), Method("FB_A", "Run", impl: "Run := FALSE;"), Gvl("GVL_B") };
            Assert.Empty(SyncManifest.Skippable(recorded, changed, plcproj));

            // The project file changed (an edit in XAE): compare.
            File.WriteAllText(fbFile, FbFile.Replace("n := n + 1;", "n := n + 2;"));
            Assert.Empty(SyncManifest.Skippable(recorded, sources, plcproj));
            // ... and a file that does not hold the texts is not recorded.
            Assert.Empty(SyncManifest.Record("bai 1", sources, plcproj).Entries);
        }
    }
}
