using System.Collections.Generic;
using System.IO;
using System.Linq;
using BeckhoffAutomationInterface.Sync;
using Xunit;

namespace BeckhoffAutomationInterface.Tests
{
    /// <summary>
    /// LibraryManifestWriter (reverse export). The strongest test is a round-trip through
    /// the real parser: what the writer emits, LibraryManifestParser.Parse must read back
    /// to the same set (this is exactly the forward/reverse contract).
    /// </summary>
    public class LibraryManifestWriterTests
    {
        static readonly string[] Empty = new string[0];

        [Fact]
        public void RoundTrips_ThroughLibraryManifestParser()
        {
            var refs = new List<LibraryReference>
            {
                new LibraryReference("Tc2_Standard", "*", "Beckhoff Automation GmbH"),
                new LibraryReference("Tc3_Module", "3.4.22.0", "Beckhoff Automation GmbH"),
            };

            string path = Path.Combine(Path.GetTempPath(), "libwriter_" + System.Guid.NewGuid() + ".xml");
            try
            {
                LibraryManifestWriter.Write(path, refs, Empty);
                List<LibraryReference> parsed = LibraryManifestParser.Parse(path);

                Assert.Equal(2, parsed.Count);
                Assert.Contains(parsed, r => r.Name == "Tc2_Standard" && r.Version == "*" && r.Company == "Beckhoff Automation GmbH");
                Assert.Contains(parsed, r => r.Name == "Tc3_Module" && r.Version == "3.4.22.0");
            }
            finally { File.Delete(path); }
        }

        [Fact]
        public void UnparseableReferences_AreCommentsNotElements()
        {
            var refs = new List<LibraryReference>
            {
                new LibraryReference("Tc2_Standard", "*", "Beckhoff Automation GmbH"),
                new LibraryReference("WeirdRefWithNoVersion", null, null), // unparseable, Name-only
            };

            System.Xml.Linq.XDocument doc = LibraryManifestWriter.Build(refs, new[] { "WeirdRefWithNoVersion" });

            // Only the parseable one is a real <Library> element.
            Assert.Single(doc.Root.Elements("Library"));
            Assert.Equal("Tc2_Standard", (string)doc.Root.Elements("Library").Single().Attribute("Name"));
            // The unparseable one survives as a comment for manual review.
            Assert.Contains(doc.Root.Nodes().OfType<System.Xml.Linq.XComment>(), c => c.Value.Contains("WeirdRefWithNoVersion"));
        }
    }
}
