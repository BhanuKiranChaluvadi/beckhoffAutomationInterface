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
    /// Supports DUTs (STRUCT/ENUM/ALIAS) and GVLs — for these, DeclarationText already
    /// IS the complete file content (the whole "TYPE X : ... END_TYPE"/"VAR_GLOBAL ...
    /// END_VAR" text), matching exactly what StFileParser.ParseFile stores as
    /// DeclarationText for these kinds, so no reconstruction is needed: read it, write
    /// it, done.
    ///
    /// Also supports FUNCTION_BLOCK/PROGRAM/FUNCTION/INTERFACE objects, with or without
    /// child METHODs/PROPERTIES: Declaration + Implementation (if any) + the correct
    /// POU-level terminator re-added (never stored in Declaration/ImplementationText —
    /// see StPouSource.StripPouTerminators), followed by each child METHOD/PROPERTY
    /// stitched back in as its own "METHOD ... END_METHOD" or "PROPERTY ... GET ...
    /// END_GET SET ... END_SET END_PROPERTY" section, in tree (creation) order — the
    /// exact reverse of PouSyncEngine.SyncMethods/SyncProperties. INTERFACE has no
    /// Implementation section at all (StFileParser never sets one for it), and
    /// interface members have no Implementation body (casting throws
    /// InvalidCastException, matching the write side's own catch pattern) — both
    /// handled by TryGetImplementationText.
    /// </summary>
    static class PlcObjectExporter
    {
        // Property kinds have no named TREEITEMTYPES members in this interop assembly
        // (PouSyncEngine.cs casts these same raw ints) — see repo memory
        // "Tree types: PLCPROP=611, PLCITFPROP=612 (accessors: 613/614, itf 654/655)".
        // Accessors themselves are identified by name ("Get"/"Set"), matching exactly how
        // PouSyncEngine.SyncProperties creates them, so their raw type ints aren't needed here.
        const int PLCPROP = 611, PLCITFPROP = 612;

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
            if (!PouTerminators.ContainsKey(item.ItemType))
                return false;

            for (int i = 1; i <= item.ChildCount; i++)
                if (!IsMemberKind(item.get_Child(i).ItemType))
                    return false;
            return true;
        }

        static bool IsMemberKind(int itemType) =>
            itemType == (int)TREEITEMTYPES.TREEITEMTYPE_PLCMETHOD ||
            itemType == (int)TREEITEMTYPES.TREEITEMTYPE_PLCITFMETH ||
            itemType == PLCPROP || itemType == PLCITFPROP;

        /// <summary>Returns the exact text to write to the .st file. Throws
        /// NotSupportedException if item's kind isn't a currently-supported one — check
        /// IsSupported first.</summary>
        public static string Export(ITcSmTreeItem item)
        {
            if (DeclarationOnlyKinds.Contains(item.ItemType))
                return ((ITcPlcDeclaration)item).DeclarationText;

            if (!IsSupported(item))
                throw new NotSupportedException(
                    $"Export of '{item.Name}' ({item.ItemSubTypeName}) is not yet supported — " +
                    "only DUTs (STRUCT/ENUM/ALIAS), GVLs, and FUNCTION_BLOCK/PROGRAM/FUNCTION/INTERFACE " +
                    "objects (whose children are all recognized METHOD/PROPERTY members) can be exported so far.");

            string terminator = PouTerminators[item.ItemType];
            bool isInterface = item.ItemType == (int)TREEITEMTYPES.TREEITEMTYPE_PLCITF;

            var text = new System.Text.StringBuilder();
            text.AppendLine(((ITcPlcDeclaration)item).DeclarationText);

            // INTERFACE has no Implementation section at all (StFileParser never sets one
            // for it — see ParseInterfaceFile), so skip reading it for that kind.
            if (!isInterface)
            {
                string baseImplementation = ((ITcPlcImplementation)item).ImplementationText;
                if (!string.IsNullOrEmpty(baseImplementation))
                {
                    text.AppendLine();
                    text.AppendLine(baseImplementation);
                }
            }

            for (int i = 1; i <= item.ChildCount; i++)
            {
                text.AppendLine();
                AppendMember(text, item.get_Child(i));
            }

            text.AppendLine(terminator);
            return text.ToString();
        }

        /// <summary>Appends one child METHOD or PROPERTY section, mirroring the exact shape
        /// StFileParser.ParseMethodSegments/ParseProperty expect on re-import.</summary>
        static void AppendMember(System.Text.StringBuilder text, ITcSmTreeItem member)
        {
            if (member.ItemType == (int)TREEITEMTYPES.TREEITEMTYPE_PLCMETHOD ||
                member.ItemType == (int)TREEITEMTYPES.TREEITEMTYPE_PLCITFMETH)
            {
                text.AppendLine(((ITcPlcDeclaration)member).DeclarationText);
                string implementation = TryGetImplementationText(member);
                if (!string.IsNullOrEmpty(implementation))
                {
                    text.AppendLine();
                    text.AppendLine(implementation);
                }
                text.AppendLine("END_METHOD");
                return;
            }

            if (member.ItemType == PLCPROP || member.ItemType == PLCITFPROP)
            {
                text.AppendLine(((ITcPlcDeclaration)member).DeclarationText);
                for (int i = 1; i <= member.ChildCount; i++)
                {
                    ITcSmTreeItem accessor = member.get_Child(i);
                    string body = TryGetImplementationText(accessor) ?? "";
                    if (string.Equals(accessor.Name, "Get", StringComparison.OrdinalIgnoreCase))
                    {
                        text.AppendLine("GET");
                        if (body.Length > 0) text.AppendLine(body);
                        text.AppendLine("END_GET");
                    }
                    else if (string.Equals(accessor.Name, "Set", StringComparison.OrdinalIgnoreCase))
                    {
                        text.AppendLine("SET");
                        if (body.Length > 0) text.AppendLine(body);
                        text.AppendLine("END_SET");
                    }
                }
                text.AppendLine("END_PROPERTY");
                return;
            }

            throw new NotSupportedException($"Export of member '{member.Name}' ({member.ItemSubTypeName}) is not supported.");
        }

        /// <summary>Interface members have no Implementation body — casting throws
        /// InvalidCastException, matching PouSyncEngine's own write-side catch pattern.</summary>
        static string TryGetImplementationText(ITcSmTreeItem item)
        {
            try { return ((ITcPlcImplementation)item).ImplementationText; }
            catch (InvalidCastException) { return null; }
        }
    }
}
