using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using Interop.TCatSysManager;

namespace BeckhoffAutomationInterface.Sync
{
    /// <summary>
    /// Reconciles a folder of .st files against a TwinCAT PLC project: creates
    /// missing PLC objects, updates existing ones, and deletes any that no
    /// longer have a corresponding .st file. Handles four tiers:
    ///   1. Top-level POUs (PROGRAM/FUNCTION_BLOCK/FUNCTION) under the POUs folder.
    ///   2. Top-level DUTs (ENUM/STRUCT) under the DUTs folder.
    ///   3. Top-level GVLs under the GVLs folder.
    ///   4. METHODs, which are children of their owning FUNCTION_BLOCK (not the
    ///      POUs folder directly), synced/pruned per-FB after the FBs themselves
    ///      have been created/updated.
    ///
    /// Validated end-to-end in docs/ideas/st-source-twincat-sync.md (2026-07-04):
    /// text injection via ITcPlcDeclaration/ITcPlcImplementation, object creation
    /// via CreateChild(name, TREEITEMTYPE_*, "", null), and deletion via
    /// DeleteChild(name) all work as expected against a real TwinCAT/VS instance.
    /// </summary>
    class PouSyncEngine
    {
        readonly ITcSysManager _sysManager;
        readonly string _pousTreePath;
        readonly string _dutsTreePath;
        readonly string _gvlsTreePath;

        public PouSyncEngine(ITcSysManager sysManager, string pousTreePath, string dutsTreePath, string gvlsTreePath)
        {
            _sysManager = sysManager;
            _pousTreePath = pousTreePath;
            _dutsTreePath = dutsTreePath;
            _gvlsTreePath = gvlsTreePath;
        }

        public SyncReport Sync(IReadOnlyList<StPouSource> desiredSources)
        {
            var report = new SyncReport();

            List<StPouSource> topLevelPous = desiredSources.Where(s => !s.IsDut && !s.IsMethod && !s.IsGvl).ToList();
            List<StPouSource> duts = desiredSources.Where(s => s.IsDut).ToList();
            List<StPouSource> gvls = desiredSources.Where(s => s.IsGvl).ToList();

            SyncTopLevel(_pousTreePath, topLevelPous, report);
            SyncTopLevel(_dutsTreePath, duts, report);
            SyncTopLevel(_gvlsTreePath, gvls, report);
            SyncMethods(topLevelPous, desiredSources.Where(s => s.IsMethod).ToList(), report);

            return report;
        }

        void SyncTopLevel(string parentTreePath, List<StPouSource> desired, SyncReport report)
        {
            ITcSmTreeItem folder = _sysManager.LookupTreeItem(parentTreePath);
            if (folder == null)
                throw new InvalidOperationException($"Could not find tree folder at '{parentTreePath}'.");

            var desiredNames = new HashSet<string>(desired.Select(s => s.Name));

            foreach (StPouSource source in desired)
            {
                string path = $"{parentTreePath}^{source.Name}";
                ITcSmTreeItem item = CreateOrGet(folder, path, source.Name, KindToTreeItemType(source.Kind), out bool isNew);

                ((ITcPlcDeclaration)item).DeclarationText = source.DeclarationText;
                if (source.ImplementationText != null)
                    ((ITcPlcImplementation)item).ImplementationText = source.ImplementationText;

                (isNew ? report.Created : report.Updated).Add(source.Name);
            }

            DeleteOrphans(folder, desiredNames, report);
        }

        void SyncMethods(List<StPouSource> desiredFunctionBlocks, List<StPouSource> desiredMethods, SyncReport report)
        {
            var fbNames = new HashSet<string>(
                desiredFunctionBlocks.Where(s => s.Kind == PouKind.FunctionBlock).Select(s => s.Name));

            var methodsByOwner = desiredMethods
                .GroupBy(m => m.OwnerName)
                .ToDictionary(g => g.Key, g => g.ToList());

            foreach (string unknownOwner in methodsByOwner.Keys.Except(fbNames))
                throw new InvalidOperationException(
                    $"Method file(s) reference unknown FUNCTION_BLOCK owner '{unknownOwner}' \u2014 ensure '{unknownOwner}.st' exists.");

            foreach (string fbName in fbNames)
            {
                string ownerPath = $"{_pousTreePath}^{fbName}";
                ITcSmTreeItem ownerItem = _sysManager.LookupTreeItem(ownerPath);

                List<StPouSource> methodsForFb = methodsByOwner.TryGetValue(fbName, out var list)
                    ? list
                    : new List<StPouSource>();
                var desiredMethodNames = new HashSet<string>(methodsForFb.Select(m => m.Name));

                foreach (StPouSource method in methodsForFb)
                {
                    string path = $"{ownerPath}^{method.Name}";
                    ITcSmTreeItem item = CreateOrGet(ownerItem, path, method.Name, TREEITEMTYPES.TREEITEMTYPE_PLCMETHOD, out bool isNew);

                    ((ITcPlcDeclaration)item).DeclarationText = method.DeclarationText;
                    ((ITcPlcImplementation)item).ImplementationText = method.ImplementationText;

                    (isNew ? report.Created : report.Updated).Add($"{fbName}.{method.Name}");
                }

                DeleteOrphans(ownerItem, desiredMethodNames, report, prefix: fbName + ".");
            }
        }

        ITcSmTreeItem CreateOrGet(ITcSmTreeItem parent, string path, string name, TREEITEMTYPES kind, out bool isNew)
        {
            try
            {
                ITcSmTreeItem existing = _sysManager.LookupTreeItem(path);
                isNew = false;
                return existing;
            }
            catch (COMException)
            {
                isNew = true;
                return parent.CreateChild(name, (int)kind, "", null);
            }
        }

        static void DeleteOrphans(ITcSmTreeItem parent, HashSet<string> desiredNames, SyncReport report, string prefix = "")
        {
            int childCount = parent.ChildCount;
            var orphaned = new List<string>();
            for (int i = 1; i <= childCount; i++)
            {
                string existingName = parent.get_Child(i).Name;
                if (!desiredNames.Contains(existingName))
                    orphaned.Add(existingName);
            }

            foreach (string name in orphaned)
            {
                parent.DeleteChild(name);
                report.Deleted.Add(prefix + name);
            }
        }

        static TREEITEMTYPES KindToTreeItemType(PouKind kind)
        {
            switch (kind)
            {
                case PouKind.Program: return TREEITEMTYPES.TREEITEMTYPE_PLCPOUPROG;
                case PouKind.FunctionBlock: return TREEITEMTYPES.TREEITEMTYPE_PLCPOUFB;
                case PouKind.Function: return TREEITEMTYPES.TREEITEMTYPE_PLCPOUFUNC;
                case PouKind.EnumDut: return TREEITEMTYPES.TREEITEMTYPE_PLCDUTENUM;
                case PouKind.StructDut: return TREEITEMTYPES.TREEITEMTYPE_PLCDUTSTRUCT;
                case PouKind.Gvl: return TREEITEMTYPES.TREEITEMTYPE_PLCGVL;
                default: throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unsupported top-level PouKind.");
            }
        }
    }
}
