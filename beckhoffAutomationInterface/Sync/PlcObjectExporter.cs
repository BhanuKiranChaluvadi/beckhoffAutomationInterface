using System;
using System.Collections.Generic;
using Interop.TCatSysManager;

namespace BeckhoffAutomationInterface.Sync
{
    /// <summary>
    /// Exports a live PLC object's current text back to a .st file — the read-side
    /// counterpart of PouSyncEngine's write side, using the same
    /// ITcPlcDeclaration/ITcPlcImplementation properties (see the ProduceXml() spike
    /// findings in docs/ideas/st-plc-bidirectional-sync.md: ProduceXml() only returns
    /// metadata, DeclarationText/ImplementationText are the real source).
    ///
    /// Currently supports DUTs (STRUCT/ENUM/ALIAS) and GVLs only — for these,
    /// DeclarationText already IS the complete file content (the whole "TYPE X : ...
    /// END_TYPE"/"VAR_GLOBAL ... END_VAR" text), matching exactly what
    /// StFileParser.ParseFile stores as DeclarationText for these kinds, so no
    /// reconstruction is needed: read it, write it, done.
    ///
    /// FUNCTION_BLOCK/PROGRAM/INTERFACE/FUNCTION (which have a separate Implementation
    /// section, and FB/INTERFACE may also have child METHODs/PROPERTIES) need their
    /// POU-level terminator re-added and their members stitched back together in the
    /// right order — NOT yet implemented; ExportSupportedKinds lists what's currently
    /// exportable and Export() throws NotSupportedException for anything else.
    /// </summary>
    static class PlcObjectExporter
    {
        static readonly HashSet<int> DeclarationOnlyKinds = new HashSet<int>
        {
            (int)TREEITEMTYPES.TREEITEMTYPE_PLCDUTENUM,
            (int)TREEITEMTYPES.TREEITEMTYPE_PLCDUTSTRUCT,
            (int)TREEITEMTYPES.TREEITEMTYPE_PLCDUTALIAS,
            (int)TREEITEMTYPES.TREEITEMTYPE_PLCGVL,
        };

        /// <summary>Recursively finds every descendant tree item whose Name matches
        /// (case-insensitive) — TwinCAT object names are unique per project, but this
        /// returns a list rather than assuming that, so callers can detect/report the
        /// (should-be-impossible) case of more than one match.</summary>
        public static List<ITcSmTreeItem> FindByName(ITcSmTreeItem parent, string name)
        {
            var matches = new List<ITcSmTreeItem>();
            FindByNameRecursive(parent, name, matches);
            return matches;
        }

        static void FindByNameRecursive(ITcSmTreeItem parent, string name, List<ITcSmTreeItem> matches)
        {
            for (int i = 1; i <= parent.ChildCount; i++)
            {
                ITcSmTreeItem child = parent.get_Child(i);
                if (string.Equals(child.Name, name, StringComparison.OrdinalIgnoreCase))
                    matches.Add(child);
                FindByNameRecursive(child, name, matches);
            }
        }

        /// <summary>Source-relative folder for item (forward-slash, no leading/trailing
        /// slash, "" if directly under the project root), mirroring PouSyncEngine's own
        /// folder-mirroring convention in reverse.</summary>
        public static string GetRelativeFolder(ITcSmTreeItem item, string projectRootPath)
        {
            string fullPath = item.PathName;
            string prefix = projectRootPath + "^";
            string remainder = fullPath.StartsWith(prefix, StringComparison.Ordinal)
                ? fullPath.Substring(prefix.Length)
                : fullPath;
            string[] segments = remainder.Split('^');
            return string.Join("/", segments, 0, segments.Length - 1);
        }

        public static bool IsSupported(ITcSmTreeItem item) => DeclarationOnlyKinds.Contains(item.ItemType);

        /// <summary>Returns the exact text to write to the .st file. Throws
        /// NotSupportedException if item's kind isn't a currently-supported one — check
        /// IsSupported first.</summary>
        public static string Export(ITcSmTreeItem item)
        {
            if (!IsSupported(item))
                throw new NotSupportedException(
                    $"Export of '{item.Name}' ({item.ItemSubTypeName}) is not yet supported — only DUTs (STRUCT/ENUM/ALIAS) and GVLs can be exported so far.");

            return ((ITcPlcDeclaration)item).DeclarationText;
        }
    }
}
