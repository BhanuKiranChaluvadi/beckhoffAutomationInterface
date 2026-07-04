using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml.Linq;

namespace BeckhoffAutomationInterface.Sync
{
    /// <summary>
    /// Parses a small XML manifest listing the PLC library references a project
    /// needs (Name/Version/Company), e.g.:
    /// <code>
    /// &lt;Libraries&gt;
    ///   &lt;Library Name="Tc2_Standard" Version="*" Company="Beckhoff Automation GmbH" /&gt;
    /// &lt;/Libraries&gt;
    /// </code>
    /// Library references are not IEC 61131-3 source code, so they don't fit the
    /// .st convention used for POUs/DUTs/GVLs \u2014 they're config data, synced the
    /// same way the ELT/soup01 tutorial samples in example/ do it via
    /// ITcPlcLibraryManager.
    /// </summary>
    static class LibraryManifestParser
    {
        public static List<LibraryReference> Parse(string manifestFilePath)
        {
            if (!File.Exists(manifestFilePath))
                return new List<LibraryReference>();

            XDocument doc = XDocument.Load(manifestFilePath);
            return doc.Root
                .Elements("Library")
                .Select(e => new LibraryReference(
                    (string)e.Attribute("Name"),
                    (string)e.Attribute("Version"),
                    (string)e.Attribute("Company")))
                .ToList();
        }
    }
}
