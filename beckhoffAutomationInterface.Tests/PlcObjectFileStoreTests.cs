using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using BeckhoffAutomationInterface.Sync;
using Xunit;

namespace BeckhoffAutomationInterface.Tests
{
    /// <summary>The store of object files that XAE saved: one project stores them, another imports
    /// them. Files only; nothing here starts TwinCAT.</summary>
    public class PlcObjectFileStoreTests
    {
        const string TwinCat = "3.1.4026.24";
        const string Bai = "1.15.0 abc";

        static StPouSource Fb(string folder = "Lib/A", string impl = "n := n + 1;") =>
            new StPouSource("FB_A", PouKind.FunctionBlock, null, "FUNCTION_BLOCK FB_A\r\nVAR\r\n  n : INT;\r\nEND_VAR", impl) { RelativeFolder = folder };

        static StPouSource Method() => new StPouSource("Run", PouKind.Method, "FB_A", "METHOD PUBLIC Run : BOOL", "Run := TRUE;");

        static List<StPouSource> Group(string folder = "Lib/A", string impl = "n := n + 1;") =>
            new List<StPouSource> { Fb(folder, impl), Method() };

        /// <summary>An object file as XAE saves it: every text in its own CDATA section.</summary>
        static byte[] SavedFile(IReadOnlyList<StPouSource> group)
        {
            string Cdata(string text) => "<![CDATA[" + text + "]]>";
            StPouSource fb = group[0];
            var xml = new StringBuilder("<?xml version=\"1.0\" encoding=\"utf-8\"?>\r\n<TcPlcObject Version=\"1.1.0.1\" ProductVersion=\"3.1.4026.24\">\r\n");
            xml.Append("  <POU Name=\"" + fb.Name + "\" Id=\"{11111111-2222-3333-4444-555555555555}\" SpecialFunc=\"None\">\r\n");
            xml.Append("    <Declaration>" + Cdata(fb.DeclarationText) + "</Declaration>\r\n");
            xml.Append("    <Implementation><ST>" + Cdata(fb.ImplementationText) + "</ST></Implementation>\r\n");
            foreach (StPouSource m in group.Skip(1))
                xml.Append("    <Method Name=\"" + m.Name + "\" Id=\"{66666666-2222-3333-4444-555555555555}\"><Declaration>" + Cdata(m.DeclarationText)
                    + "</Declaration><Implementation><ST>" + Cdata(m.ImplementationText) + "</ST></Implementation></Method>\r\n");
            xml.Append("  </POU>\r\n</TcPlcObject>");
            return Encoding.UTF8.GetBytes(xml.ToString());
        }

        static PlcObjectFileStore Store(string root, string twinCat = TwinCat, string bai = Bai) => new PlcObjectFileStore(root, twinCat, bai);

        [Fact]
        public void AFileStoredByOneProjectIsFoundForTheSameObjectInAnotherFolder_UnderTheObjectFileName()
        {
            string root = TestProjects.NewTempDir("bai_object_files");
            byte[] saved = SavedFile(Group("Lib/A"));

            Assert.True(Store(root).Put(Group("Lib/A"), saved));
            string found = Store(root).Find(Group("App/Other"));

            Assert.NotNull(found);
            Assert.Equal("FB_A.TcPOU", Path.GetFileName(found));
            Assert.Equal(saved, File.ReadAllBytes(found));
        }

        [Theory]
        [InlineData("3.1.4026.27", Bai)]
        [InlineData(TwinCat, "1.16.0 def")]
        public void AFileIsNotFoundForAnotherTwinCatOrBai(string twinCat, string bai)
        {
            string root = TestProjects.NewTempDir("bai_object_files");
            Assert.True(Store(root).Put(Group(), SavedFile(Group())));

            Assert.Null(Store(root, twinCat, bai).Find(Group()));
        }

        [Fact]
        public void AFileThatDoesNotHoldTheSourceTextsIsRefused()
        {
            string root = TestProjects.NewTempDir("bai_object_files");

            Assert.False(Store(root).Put(Group(impl: "n := 0;"), SavedFile(Group())));
            Assert.Null(Store(root).Find(Group(impl: "n := 0;")));
        }

        [Fact]
        public void AFileIsNotFoundAfterTheSourceChanged()
        {
            string root = TestProjects.NewTempDir("bai_object_files");
            Assert.True(Store(root).Put(Group(), SavedFile(Group())));

            Assert.Null(Store(root).Find(Group(impl: "n := 0;")));
        }

        [Fact]
        public void ADamagedStoredFileIsNotFound()
        {
            string root = TestProjects.NewTempDir("bai_object_files");
            Assert.True(Store(root).Put(Group(), SavedFile(Group())));
            File.WriteAllText(Store(root).Find(Group()), "<TcPlcObject/>");

            Assert.Null(Store(root).Find(Group()));
        }

        [Fact]
        public void StoringTheSameObjectAgainKeepsOneFile()
        {
            string root = TestProjects.NewTempDir("bai_object_files");
            Assert.True(Store(root).Put(Group(), SavedFile(Group())));
            Assert.True(Store(root).Put(Group(), SavedFile(Group())));

            Assert.Single(Directory.GetFiles(root, "*.TcPOU", SearchOption.AllDirectories));
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        public void AStoreNeedsTheTwinCatVersion(string twinCat)
        {
            Assert.Throws<ArgumentException>(() => new PlcObjectFileStore(TestProjects.NewTempDir("bai_object_files"), twinCat, Bai));
        }
    }
}
