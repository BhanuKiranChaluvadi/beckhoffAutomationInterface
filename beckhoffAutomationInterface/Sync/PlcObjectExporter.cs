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
    /// FUNCTION_BLOCK/PROGRAM/FUNCTION/INTERFACE objects with NO child METHODs/
    /// PROPERTIES are also supported: Declaration + Implementation (if any) + the
    /// correct POU-level terminator re-added (never stored in Declaration/
    /// ImplementationText — see StPouSource.StripPouTerminators). INTERFACE has no
    /// Implementation section at all (StFileParser never sets one), so only its
    /// Declaration + END_INTERFACE are used.
    ///
    /// FB/PROGRAM/INTERFACE objects that DO have child METHODs/PROPERTIES are NOT
    /// yet supported (stitching members back together in the right order/format is
    /// a separate, more involved piece of work) — Export() throws
    /// NotSupportedException for those; check IsSupported first.
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

        /// <summary>POU kinds with a separate Declaration+Implementation (unlike DUTs/
        /// GVLs) and their re-added terminator keyword, but only supported here when
        /// ChildCount == 0 (no METHODs/PROPERTIES to stitch back in).</summary>
        static readonly Dictionary<int, string> PouTerminators = new Dictionary<int, string>
        {
            { (int)TREEITEMTYPES.TREEITEMTYPE_PLCPOUFB, "END_FUNCTION_BLOCK" },
            { (int)TREEITEMTYPES.TREEITEMTYPE_PLCPOUPROG, "END_PROGRAM" },
            { (int)TREEITEMTYPES.TREEITEMTYPE_PLCPOUFUNC, "END_FUNCTION" },
            { (int)TREEITEMTYPES.TREEITEMTYPE_PLCITF, "END_INTERFACE" },
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

        public static bool IsSupported(ITcSmTreeItem item)
        {
            if (DeclarationOnlyKinds.Contains(item.ItemType))
                return true;
            return PouTerminators.ContainsKey(item.ItemType) && item.ChildCount == 0;
        }

        /// <summary>Returns the exact text to write to the .st file. Throws
        /// NotSupportedException if item's kind isn't a currently-supported one — check
        /// IsSupported first.</summary>
        public static string Export(ITcSmTreeItem item)
        {
            if (DeclarationOnlyKinds.Contains(item.ItemType))
                return ((ITcPlcDeclaration)item).DeclarationText;

            if (PouTerminators.TryGetValue(item.ItemType, out string terminator) && item.ChildCount == 0)
            {
                string declaration = ((ITcPlcDeclaration)item).DeclarationText;
                // INTERFACE has no Implementation section at all (StFileParser never sets
                // one for it — see ParseInterfaceFile), so skip reading it for that kind.
                string implementation = item.ItemType == (int)TREEITEMTYPES.TREEITEMTYPE_PLCITF
                    ? null
                    : ((ITcPlcImplementation)item).ImplementationText;

                var text = new System.Text.StringBuilder();
                text.AppendLine(declaration);
                if (!string.IsNullOrEmpty(implementation))
                {
                    text.AppendLine();
                    text.AppendLine(implementation);
                }
                text.AppendLine(terminator);
                return text.ToString();
            }

            throw new NotSupportedException(
                $"Export of '{item.Name}' ({item.ItemSubTypeName}) is not yet supported — " +
                (item.ChildCount > 0
                    ? "it has child METHODs/PROPERTIES, which export doesn't stitch back together yet."
                    : "only DUTs (STRUCT/ENUM/ALIAS), GVLs, and childless FUNCTION_BLOCK/PROGRAM/FUNCTION/INTERFACE objects can be exported so far."));
        }
    }
}
