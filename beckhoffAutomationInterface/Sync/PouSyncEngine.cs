using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using Interop.TCatSysManager;

namespace BeckhoffAutomationInterface.Sync
{
    /// <summary>
    /// Reconciles a folder tree of .st files against a TwinCAT PLC project. The
    /// source folder hierarchy is MIRRORED as PLC folders directly under the PLC
    /// project root (e.g. ST/Shark/App/Shark/FunctionBlocks/FB_X.st ->
    /// "&lt;Project&gt;^App^Shark^FunctionBlocks^FB_X"), so POUs, DUTs and GVLs can
    /// live together in feature folders exactly as organised on disk. METHODs are
    /// created as children of their owning FUNCTION_BLOCK / INTERFACE.
    ///
    /// Folders are created via CreateChild(name, TREEITEMTYPE_PLCFOLDER=601, "",
    /// null) (pattern from example/.../GeneratePlcProject.cs). Objects are created
    /// with CreateChild(name, TREEITEMTYPE_*, "", vInfo) and their text injected
    /// via ITcPlcDeclaration/ITcPlcImplementation.
    ///
    /// Orphan deletion is intentionally NOT performed at the mirrored-folder level:
    /// it would risk deleting the standard template's default POUs/DUTs/GVLs/MAIN
    /// items. Only METHODs are pruned (per owner). Deleted .st files are therefore
    /// left in the project until manually removed.
    /// </summary>
    class PouSyncEngine
    {
        const int TREEITEMTYPE_PLCFOLDER = 601;

        readonly ITcSysManager _sysManager;
        readonly string _projectRootPath;
        readonly Dictionary<string, ITcSmTreeItem> _folderCache = new Dictionary<string, ITcSmTreeItem>();
        readonly Dictionary<string, ITcSmTreeItem> _ownerItems = new Dictionary<string, ITcSmTreeItem>();
        readonly HashSet<string> _interfaceOwners = new HashSet<string>();

        public PouSyncEngine(ITcSysManager sysManager, string projectRootPath)
        {
            _sysManager = sysManager;
            _projectRootPath = projectRootPath;
            _folderCache[projectRootPath] = sysManager.LookupTreeItem(projectRootPath);
        }

        public SyncReport Sync(IReadOnlyList<StPouSource> desiredSources)
        {
            var report = new SyncReport();

            List<StPouSource> nonMethods = desiredSources.Where(s => !s.IsMethod && !s.IsProperty).ToList();
            List<StPouSource> methods = desiredSources.Where(s => s.IsMethod).ToList();
            List<StPouSource> properties = desiredSources.Where(s => s.IsProperty).ToList();

            // Two passes so cross-type references (FB EXTENDS/IMPLEMENTS, INTERFACE EXTENDS,
            // ALIAS base type) always resolve regardless of file order:
            //   Pass 1: create every object as a base-less shell in its mirrored folder
            //           (CreateChild's vInfo must reference a type that already exists, so we
            //            never pass a base at creation \u2014 EXTENDS/IMPLEMENTS come from the text).
            //   Pass 2: inject Declaration/Implementation text (which carries the real
            //           EXTENDS/IMPLEMENTS/base). By now every referenced type exists, and
            //           TwinCAT only validates the references at build time anyway.
            var placed = new List<(StPouSource Source, ITcSmTreeItem Item, bool IsNew)>();
            foreach (StPouSource source in nonMethods)
            {
                ITcSmTreeItem folder = GetOrCreateFolder(source.RelativeFolder);
                ITcSmTreeItem item = CreateOrGet(folder, source.Name, KindToTreeItemType(source.Kind), CreationVInfo(source.Kind), out bool isNew);
                placed.Add((source, item, isNew));

                // PROGRAM is a valid method/property owner too — PROGRAMs commonly carry
                // inline private helper METHODs (e.g. "METHOD PRIVATE _Init").
                if (source.Kind == PouKind.FunctionBlock || source.Kind == PouKind.Interface || source.Kind == PouKind.Program)
                {
                    _ownerItems[source.Name] = item;
                    if (source.Kind == PouKind.Interface)
                        _interfaceOwners.Add(source.Name);
                }
            }

            // Create methods and properties BEFORE setting the owner FBs' declaration text.
            // A FUNCTION_BLOCK's "IMPLEMENTS I_X" declaration is only consistent once its
            // methods/properties exist; setting IMPLEMENTS on a member-less FB makes TwinCAT
            // try to auto-generate the interface members and can crash devenv (RPC failed).
            SyncMethods(methods, report);
            SyncProperties(properties, report);

            foreach (var p in placed)
            {
                try
                {
                    ((ITcPlcDeclaration)p.Item).DeclarationText = p.Source.DeclarationText;
                    if (p.Source.ImplementationText != null)
                        ((ITcPlcImplementation)p.Item).ImplementationText = p.Source.ImplementationText;
                }
                catch (Exception ex)
                {
                    Console.WriteLine("    !! failed setting text for {0} '{1}': {2}",
                        p.Source.Kind, p.Source.RelativeFolder + "/" + p.Source.Name, ex.Message);
                    throw;
                }

                (p.IsNew ? report.Created : report.Updated).Add(p.Source.RelativeFolder.Length > 0
                    ? p.Source.RelativeFolder + "/" + p.Source.Name
                    : p.Source.Name);
            }

            return report;
        }
        /// <summary>
        /// The vInfo to pass to CreateChild when creating a base-less shell. We never pass a
        /// user-defined base type here (it may not exist yet); the real base comes from the
        /// declaration text set in pass 2. Rules that are structurally required by TwinCAT:
        ///   - INTERFACE: needs a string ("" = no base); null throws "Base class not specified!".
        ///   - ALIAS: needs a base type; we seed a primitive placeholder ("INT") and let the
        ///     declaration text set the real (possibly user-defined) base.
        ///   - FUNCTION_BLOCK and everything else: null.
        /// </summary>
        static object CreationVInfo(PouKind kind)
        {
            switch (kind)
            {
                case PouKind.Interface: return "";
                case PouKind.AliasDut: return "INT";
                default: return null;
            }
        }

        /// <summary>Walks/creates the PLC folder chain for a source-relative folder path,
        /// caching each level. Returns the project root item for an empty path.</summary>
        ITcSmTreeItem GetOrCreateFolder(string relativeFolder)
        {
            ITcSmTreeItem current = _folderCache[_projectRootPath];
            if (string.IsNullOrEmpty(relativeFolder))
                return current;

            string path = _projectRootPath;
            foreach (string segment in relativeFolder.Split('/'))
            {
                string childPath = path + "^" + segment;
                if (!_folderCache.TryGetValue(childPath, out ITcSmTreeItem child))
                {
                    try
                    {
                        child = _sysManager.LookupTreeItem(childPath);
                    }
                    catch (COMException)
                    {
                        child = current.CreateChild(segment, TREEITEMTYPE_PLCFOLDER, "", null);
                    }
                    _folderCache[childPath] = child;
                }
                current = child;
                path = childPath;
            }
            return current;
        }

        void SyncMethods(List<StPouSource> desiredMethods, SyncReport report)
        {
            var methodsByOwner = desiredMethods
                .GroupBy(m => m.OwnerName)
                .ToDictionary(g => g.Key, g => g.ToList());

            foreach (string unknownOwner in methodsByOwner.Keys.Except(_ownerItems.Keys))
                throw new InvalidOperationException(
                    $"Method file(s) reference unknown FUNCTION_BLOCK/INTERFACE owner '{unknownOwner}' \u2014 ensure '{unknownOwner}.st' exists.");

            foreach (var kv in methodsByOwner)
            {
                string ownerName = kv.Key;
                ITcSmTreeItem ownerItem = _ownerItems[ownerName];
                bool isInterfaceOwner = _interfaceOwners.Contains(ownerName);
                TREEITEMTYPES methodType = isInterfaceOwner
                    ? TREEITEMTYPES.TREEITEMTYPE_PLCITFMETH
                    : TREEITEMTYPES.TREEITEMTYPE_PLCMETHOD;

                foreach (StPouSource method in kv.Value)
                {
                    ITcSmTreeItem item = CreateOrGet(ownerItem, method.Name, methodType, null, out bool isNew);

                    ((ITcPlcDeclaration)item).DeclarationText = method.DeclarationText;
                    try
                    {
                        ((ITcPlcImplementation)item).ImplementationText = method.ImplementationText;
                    }
                    catch (InvalidCastException) when (isInterfaceOwner)
                    {
                        // Interface method signatures have no implementation body.
                    }

                    (isNew ? report.Created : report.Updated).Add($"{ownerName}.{method.Name}");
                }

                // NOTE: no orphan pruning of an owner's children here \u2014 a FUNCTION_BLOCK's
                // children include both METHODs and PROPERTYs, so pruning "everything not in
                // the method set" would delete the properties (and vice-versa).
            }
        }

        /// <summary>
        /// Creates/updates PROPERTYs under their owning FUNCTION_BLOCK / INTERFACE. Each
        /// property is a tree item (PLCPROP=611 for FBs, PLCITFPROP=612 for interfaces) whose
        /// DeclarationText is the "PROPERTY Name : Type" header, with Get (613/654) and Set
        /// (614/655) accessor children carrying the getter/setter bodies as their
        /// ImplementationText (interface accessors have no body).
        /// </summary>
        void SyncProperties(List<StPouSource> desiredProperties, SyncReport report)
        {
            var propsByOwner = desiredProperties
                .GroupBy(p => p.OwnerName)
                .ToDictionary(g => g.Key, g => g.ToList());

            foreach (string unknownOwner in propsByOwner.Keys.Except(_ownerItems.Keys))
                throw new InvalidOperationException(
                    $"Property file(s) reference unknown FUNCTION_BLOCK/INTERFACE owner '{unknownOwner}' \u2014 ensure '{unknownOwner}.st' exists.");

            foreach (var kv in propsByOwner)
            {
                string ownerName = kv.Key;
                ITcSmTreeItem ownerItem = _ownerItems[ownerName];
                bool isItf = _interfaceOwners.Contains(ownerName);
                var propType = (TREEITEMTYPES)(isItf ? 612 : 611);
                var getType = (TREEITEMTYPES)(isItf ? 654 : 613);
                var setType = (TREEITEMTYPES)(isItf ? 655 : 614);

                foreach (StPouSource prop in kv.Value)
                {
                    // A property's return type (carried in BaseType) is required as vInfo.
                    ITcSmTreeItem propItem = CreateOrGet(ownerItem, prop.Name, propType, prop.BaseType, out bool isNew);
                    ((ITcPlcDeclaration)propItem).DeclarationText = prop.DeclarationText;

                    if (prop.GetText != null)
                    {
                        ITcSmTreeItem getItem = CreateOrGet(propItem, "Get", getType, null, out _);
                        SetAccessorBody(getItem, prop.GetText, isItf);
                    }
                    if (prop.SetText != null)
                    {
                        ITcSmTreeItem setItem = CreateOrGet(propItem, "Set", setType, null, out _);
                        SetAccessorBody(setItem, prop.SetText, isItf);
                    }

                    (isNew ? report.Created : report.Updated).Add($"{ownerName}.{prop.Name}");
                }
            }
        }

        static void SetAccessorBody(ITcSmTreeItem accessor, string body, bool isInterface)
        {
            try
            {
                ((ITcPlcImplementation)accessor).ImplementationText = body;
            }
            catch (InvalidCastException) when (isInterface)
            {
                // Interface property accessors have no implementation body.
            }
        }

        ITcSmTreeItem CreateOrGet(ITcSmTreeItem parent, string name, TREEITEMTYPES kind, object vInfo, out bool isNew)
        {
            try
            {
                return TreeItemFactory.GetOrCreate(_sysManager, parent, name, (int)kind, vInfo, out isNew);
            }
            catch (COMException) when (kind == TREEITEMTYPES.TREEITEMTYPE_PLCPOUPROG)
            {
                // PLC object names are globally unique, so a create can collide with an
                // object of the same name that already exists elsewhere — most commonly
                // the standard template's "MAIN" (in the POUs folder), which a root-level
                // MAIN.st would duplicate. Find and reuse the existing one so its text is
                // updated in place instead of failing.
                ITcSmTreeItem existing = FindDescendantByName(_folderCache[_projectRootPath], name);
                if (existing == null) throw;
                isNew = false;
                return existing;
            }
        }

        /// <summary>Depth-first search for a descendant tree item with the given name.</summary>
        static ITcSmTreeItem FindDescendantByName(ITcSmTreeItem parent, string name)
        {
            for (int i = 1; i <= parent.ChildCount; i++)
            {
                ITcSmTreeItem child = parent.get_Child(i);
                if (string.Equals(child.Name, name, StringComparison.OrdinalIgnoreCase))
                    return child;
                ITcSmTreeItem found = FindDescendantByName(child, name);
                if (found != null) return found;
            }
            return null;
        }

        static TREEITEMTYPES KindToTreeItemType(PouKind kind)
        {
            switch (kind)
            {
                case PouKind.Program: return TREEITEMTYPES.TREEITEMTYPE_PLCPOUPROG;
                case PouKind.FunctionBlock: return TREEITEMTYPES.TREEITEMTYPE_PLCPOUFB;
                case PouKind.Function: return TREEITEMTYPES.TREEITEMTYPE_PLCPOUFUNC;
                case PouKind.Interface: return TREEITEMTYPES.TREEITEMTYPE_PLCITF;
                case PouKind.EnumDut: return TREEITEMTYPES.TREEITEMTYPE_PLCDUTENUM;
                case PouKind.StructDut: return TREEITEMTYPES.TREEITEMTYPE_PLCDUTSTRUCT;
                case PouKind.AliasDut: return TREEITEMTYPES.TREEITEMTYPE_PLCDUTALIAS;
                case PouKind.Gvl: return TREEITEMTYPES.TREEITEMTYPE_PLCGVL;
                default: throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unsupported top-level PouKind.");
            }
        }
    }
}
