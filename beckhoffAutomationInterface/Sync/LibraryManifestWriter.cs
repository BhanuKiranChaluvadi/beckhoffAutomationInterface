using System.Collections.Generic;
using System.Linq;
using System.Xml.Linq;

namespace BeckhoffAutomationInterface.Sync
{
    /// <summary>
    /// Reverse of LibraryManifestParser: writes a libraries.xml manifest from the library
    /// references read off a live project (see LibrarySyncEngine.ReadReferences). The
    /// output round-trips — LibraryManifestParser.Parse reads it back to the same set.
    ///
    /// References whose display name didn't parse into Name/Version/Company are emitted as
    /// XML comments (not active &lt;Library&gt; elements), so a human can review/fix them
    /// without the forward sync choking on an incomplete reference.
    /// </summary>
    static class LibraryManifestWriter
    {
        public static XDocument Build(IReadOnlyList<LibraryReference> references, IReadOnlyList<string> unparseable)
        {
            var root = new XElement("Libraries");

            // Only references that parsed cleanly become real <Library> entries.
            foreach (LibraryReference lib in references.Where(r => r.Version != null))
            {
                root.Add(new XElement("Library",
                    new XAttribute("Name", lib.Name),
                    new XAttribute("Version", lib.Version),
                    new XAttribute("Company", lib.Company ?? "")));
            }

            foreach (string raw in unparseable)
                root.Add(new XComment($" Unparseable reference (left for manual review): \"{raw}\" "));

            return new XDocument(new XDeclaration("1.0", "utf-8", null), root);
        }

        public static void Write(string manifestPath, IReadOnlyList<LibraryReference> references, IReadOnlyList<string> unparseable)
        {
            Build(references, unparseable).Save(manifestPath);
        }
    }
}
