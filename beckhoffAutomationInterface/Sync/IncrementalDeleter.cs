using System.Collections.Generic;
using System.IO;
using Interop.TCatSysManager;

namespace BeckhoffAutomationInterface.Sync
{
    /// <summary>One outcome of an attempted deletion for a single git-deleted .st file.</summary>
    class DeleteResult
    {
        public string RelativePath { get; }
        public string ObjectName { get; }
        public bool Deleted { get; }

        /// <summary>Null when Deleted is true; otherwise explains why it was skipped.</summary>
        public string SkipReason { get; }

        public DeleteResult(string relativePath, string objectName, bool deleted, string skipReason)
        {
            RelativePath = relativePath;
            ObjectName = objectName;
            Deleted = deleted;
            SkipReason = skipReason;
        }
    }

    /// <summary>
    /// Deletes the PLC object(s) corresponding to .st files git reports as deleted, for
    /// --incremental --confirm-delete (opt-in: --incremental alone only warns, per the
    /// user's explicit choice of the safer default). Conservative by design: only acts
    /// on an EXACT, unambiguous name match (the deleted file's own name, without
    /// extension) found under the project root using ITcSmTreeItem.DeleteChild() (the
    /// official API — confirmed against example/.../PlcArchives.cs:
    /// pousItem.DeleteChild("POUProgram")). Anything else (zero or multiple matches, or
    /// a standalone "&lt;Owner&gt;.&lt;Method&gt;.st" file) is skipped and reported, never guessed at.
    /// </summary>
    static class IncrementalDeleter
    {
        public static List<DeleteResult> Delete(ITcSysManager sysManager, string projectRootPath, IReadOnlyList<string> deletedRelativePaths)
        {
            var results = new List<DeleteResult>();
            ITcSmTreeItem root = sysManager.LookupTreeItem(projectRootPath);

            foreach (string relativePath in deletedRelativePaths)
            {
                string objectName = Path.GetFileNameWithoutExtension(relativePath);

                // Standalone method files ("<Owner>.<Method>.st") have no top-level object of
                // their own — the part after the first dot names a METHOD, a child of another
                // object, not something FindByName's project-root search resolves on its own.
                // Same rare pattern already noted as a known --incremental limitation; skip
                // rather than guess which owner's child to remove.
                if (objectName.Contains("."))
                {
                    results.Add(new DeleteResult(relativePath, objectName, false,
                        "looks like a '<Owner>.<Method>.st' file — deleting individual methods isn't supported, remove manually if needed"));
                    continue;
                }

                List<ITcSmTreeItem> matches = PlcObjectExporter.FindByName(root, objectName);
                if (matches.Count == 0)
                {
                    results.Add(new DeleteResult(relativePath, objectName, false, "no matching PLC object found (already gone?)"));
                    continue;
                }
                if (matches.Count > 1)
                {
                    results.Add(new DeleteResult(relativePath, objectName, false, $"{matches.Count} objects share this name — ambiguous, skipped"));
                    continue;
                }

                ITcSmTreeItem item = matches[0];
                ITcSmTreeItem parent = sysManager.LookupTreeItem(ParentPath(item.PathName));
                parent.DeleteChild(item.Name);
                results.Add(new DeleteResult(relativePath, objectName, true, null));
            }

            return results;
        }

        static string ParentPath(string pathName)
        {
            int lastCaret = pathName.LastIndexOf('^');
            return lastCaret >= 0 ? pathName.Substring(0, lastCaret) : pathName;
        }
    }
}
