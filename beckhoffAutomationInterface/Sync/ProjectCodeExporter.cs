using System;
using System.Collections.Generic;
using System.IO;
using Interop.TCatSysManager;

namespace BeckhoffAutomationInterface.Sync
{
    /// <summary>Outcome of a whole-project code export (see ProjectCodeExporter).</summary>
    class ProjectExportReport
    {
        /// <summary>Source-relative .st paths actually written (forward-slash), in walk order.</summary>
        public List<string> Written { get; } = new List<string>();

        /// <summary>Code-bearing objects that couldn't be exported (e.g. an FB with a child
        /// that isn't a recognized METHOD/PROPERTY) — reported, never silently skipped.</summary>
        public List<string> Unsupported { get; } = new List<string>();
    }

    /// <summary>
    /// Reverse of PouSyncEngine: walks a live PLC project's tree and writes EVERY
    /// supported POU/DUT/GVL back out as an .st file under the source folder, mirroring
    /// its tree location as a source-relative folder (the read-side counterpart of
    /// PouSyncEngine's "source folders map directly under the project root" convention).
    ///
    /// This is the multi-object walk the single-object --export never had: it reuses
    /// PlcObjectExporter.Export/IsSupported/GetRelativeFolder verbatim, so per-object
    /// fidelity (terminators re-added, methods/properties stitched, INTERFACE special
    /// cases, the known non-ASCII lossy caveat) is exactly the same as --export.
    ///
    /// Members (METHODs/PROPERTIES) are NOT written as standalone files — they're stitched
    /// into their owner's file by PlcObjectExporter, so the walk never descends into an
    /// exported object. Folders, the References node, and visualizations aren't exportable
    /// kinds, so the walk simply recurses through them without writing anything.
    /// </summary>
    static class ProjectCodeExporter
    {
        public static ProjectExportReport ExportAll(ITcSmTreeItem projectRoot, string projectRootPath, string sourceFolder)
        {
            var report = new ProjectExportReport();
            Walk(projectRoot, projectRootPath, sourceFolder, report);
            return report;
        }

        static void Walk(ITcSmTreeItem parent, string projectRootPath, string sourceFolder, ProjectExportReport report)
        {
            for (int i = 1; i <= parent.ChildCount; i++)
            {
                ITcSmTreeItem child = parent.get_Child(i);

                if (PlcObjectExporter.IsExportableKind(child))
                {
                    // A code-bearing object: export it (don't recurse — its children are
                    // members already stitched into this file). If its specific shape isn't
                    // supported yet, report it rather than descending or crashing.
                    if (PlcObjectExporter.IsSupported(child))
                        WriteObject(child, projectRootPath, sourceFolder, report);
                    else
                        report.Unsupported.Add($"{child.Name} ({child.ItemSubTypeName})");
                    continue;
                }

                // A folder / References / visualization container: recurse through it.
                Walk(child, projectRootPath, sourceFolder, report);
            }
        }

        static void WriteObject(ITcSmTreeItem item, string projectRootPath, string sourceFolder, ProjectExportReport report)
        {
            string text = PlcObjectExporter.Export(item);
            string relativeFolder = PlcObjectExporter.GetRelativeFolder(item, projectRootPath);
            string folderPath = string.IsNullOrEmpty(relativeFolder)
                ? sourceFolder
                : Path.Combine(sourceFolder, relativeFolder.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(folderPath);
            File.WriteAllText(Path.Combine(folderPath, item.Name + ".st"), text);

            report.Written.Add(string.IsNullOrEmpty(relativeFolder) ? item.Name + ".st" : relativeFolder + "/" + item.Name + ".st");
        }
    }
}
