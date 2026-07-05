using System.Runtime.InteropServices;
using Interop.TCatSysManager;

namespace BeckhoffAutomationInterface.Sync
{
    /// <summary>
    /// Shared idempotent "check first, only create if missing" helper for TwinCAT tree
    /// items: looks up an existing child at parent.PathName^name, creating it via
    /// CreateChild only if it doesn't exist yet. This is the one place that principle is
    /// implemented — previously duplicated independently in PouSyncEngine and IoSyncEngine.
    /// </summary>
    static class TreeItemFactory
    {
        public static ITcSmTreeItem GetOrCreate(ITcSysManager sysManager, ITcSmTreeItem parent, string name, int type, object vInfo, out bool isNew)
        {
            string path = parent.PathName + "^" + name;
            try
            {
                ITcSmTreeItem existing = sysManager.LookupTreeItem(path);
                isNew = false;
                return existing;
            }
            catch (COMException)
            {
                isNew = true;
                return parent.CreateChild(name, type, "", vInfo);
            }
        }
    }
}
