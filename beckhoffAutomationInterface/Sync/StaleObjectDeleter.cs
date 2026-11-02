using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using Interop.TCatSysManager;

namespace BeckhoffAutomationInterface.Sync
{
    /// <summary>
    /// The COM side of StaleObjectPlan: index the kept project's top-level code objects, and
    /// delete the planned ones with ITcSmTreeItem.DeleteChild. Idempotent: an object that is
    /// already gone is skipped, so a retry after a busy shell, or a second run, ends the same.
    /// </summary>
    static class StaleObjectDeleter
    {
        const int PLCFOLDER = 601;

        /// <summary>Top-level code object name (any case) to its tree paths. Walks PLC folders
        /// only; never goes into an object (its members) or into References.</summary>
        public static Dictionary<string, IReadOnlyList<string>> IndexTopLevel(ITcSmTreeItem projectRoot)
        {
            var index = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
            Walk(projectRoot, index);
            return index.ToDictionary(e => e.Key, e => (IReadOnlyList<string>)e.Value, StringComparer.OrdinalIgnoreCase);
        }

        static void Walk(ITcSmTreeItem parent, Dictionary<string, List<string>> index)
        {
            for (int i = 1; i <= parent.ChildCount; i++)
            {
                ITcSmTreeItem child = parent.get_Child(i);
                if (child.ItemType == PLCFOLDER)
                    Walk(child, index);
                else if (PlcObjectExporter.IsExportableKind(child))
                {
                    if (!index.TryGetValue(child.Name, out List<string> paths))
                        index[child.Name] = paths = new List<string>();
                    paths.Add(child.PathName);
                }
            }
        }

        /// <summary>Deletes the plan's steps in order. Returns the Display of each deleted
        /// object. A step TwinCAT refuses stops the run: it and the steps after it go to
        /// plan.Unresolved, so the caller builds fresh. A busy shell is left to the caller's retry.</summary>
        public static List<string> Execute(ITcSysManager sysManager, StaleObjectPlan plan)
        {
            var deleted = new List<string>();
            for (int i = 0; i < plan.Steps.Count; i++)
            {
                DeleteStep step = plan.Steps[i];
                ITcSmTreeItem parent;
                try
                {
                    sysManager.LookupTreeItem(step.ParentPath + "^" + step.ChildName);
                    parent = sysManager.LookupTreeItem(step.ParentPath);
                }
                catch (COMException ex) when ((uint)ex.HResult != ComInterop.ServerCallRetryLater)
                {
                    continue; // already gone
                }
                try
                {
                    parent.DeleteChild(step.ChildName);
                }
                catch (COMException ex) when ((uint)ex.HResult != ComInterop.ServerCallRetryLater)
                {
                    plan.Unresolved.AddRange(plan.Steps.Skip(i).Select(s => s.Display + ": " + ex.Message));
                    break;
                }
                deleted.Add(step.Display);
            }
            return deleted;
        }
    }
}
