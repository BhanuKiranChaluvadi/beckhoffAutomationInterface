using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Xml.Linq;
using Interop.TCatSysManager;
using static BeckhoffAutomationInterface.Clock;

namespace BeckhoffAutomationInterface.Sync
{
    /// <summary>
    /// init only: creates all PLC objects of a FRESH project with ONE PlcOpenImport call (see
    /// PlcOpenWriter) instead of one CreateChild per object (about 0.27 s each: about 7 min for
    /// 1600 objects). The normal code sync runs right after it and writes every text that still
    /// differs (method/property/interface declarations), so the result is the same as without
    /// the import. Anything unexpected (not a fresh project, import error) only prints a note:
    /// the normal sync then creates the objects one by one, as before.
    /// </summary>
    static class PlcOpenBulkImport
    {
        const int TREEITEMTYPE_PLCFOLDER = 601;

        /// <summary>A fresh project holds only the template objects (MAIN, a GVL, ...). More than
        /// this many objects means the project is not fresh: the import is skipped.</summary>
        internal const int FreshProjectObjectLimit = 50;

        /// <summary>Returns the number of objects imported (0 = skipped or failed).</summary>
        public static int Run(ITcSysManager sysManager, string projectRootPath, IReadOnlyList<StPouSource> desired)
        {
            ITcSmTreeItem root = sysManager.LookupTreeItem(projectRootPath);
            var existing = new List<string>();
            if (!CollectObjectNames(root, existing))
            {
                Console.WriteLine("{0}: Bulk import skipped: the project already has more than {1} objects (not fresh).", Now(), FreshProjectObjectLimit);
                return 0;
            }

            List<StPouSource> toImport = ToImport(desired, existing);
            if (existing.Count > 0)
                Console.WriteLine("{0}: Bulk import: {1} template object(s) already in the project, left to the normal sync: {2}",
                    Now(), existing.Count, string.Join(", ", existing));
            if (toImport.Count == 0)
                return 0;

            string file = Path.Combine(Path.GetTempPath(), "bai_bulk_" + Guid.NewGuid().ToString("N") + ".xml");
            var clock = Stopwatch.StartNew();
            try
            {
                PlcOpenWriter.Write(toImport).Save(file, SaveOptions.DisableFormatting);
                Console.WriteLine("{0}: Bulk import of {1} PLC object(s) (one PLCopen file)...", Now(), toImport.Count);
                ((ITcPlcIECProject)root).PlcOpenImport(file, (int)PLCIMPORTOPTIONS.PLCIMPORTOPTIONS_NONE, "", true);
            }
            catch (Exception ex) when (ex is COMException || ex is InvalidCastException || ex is ArgumentOutOfRangeException)
            {
                Console.WriteLine("{0}: NOTE: bulk import failed ({1}); the objects are created one by one.", Now(), ex.Message.Trim());
                return 0;
            }
            finally
            {
                try { File.Delete(file); } catch (IOException) { /* temp file: not important */ }
            }
            Console.WriteLine("{0}: Bulk import complete ({1} object(s) in {2:F1} s).", Now(), toImport.Count, clock.Elapsed.TotalSeconds);
            return toImport.Count;
        }

        /// <summary>The sources to import: all except objects whose name is already in the
        /// project (and the members of such an owner), which the normal sync updates in place
        /// (pure, unit-tested).</summary>
        internal static List<StPouSource> ToImport(IReadOnlyList<StPouSource> desired, IEnumerable<string> existingNames)
        {
            var existing = new HashSet<string>(existingNames, StringComparer.OrdinalIgnoreCase);
            return desired.Where(s => !existing.Contains(s.IsMethod || s.IsProperty ? s.OwnerName : s.Name)).ToList();
        }

        /// <summary>Names of the PLC objects under the root, walking PLC folders only (never
        /// References or task items). False when there are more than FreshProjectObjectLimit.</summary>
        static bool CollectObjectNames(ITcSmTreeItem parent, List<string> names)
        {
            for (int i = 1; i <= parent.ChildCount; i++)
            {
                ITcSmTreeItem child = parent.get_Child(i);
                if (child.ItemType == TREEITEMTYPE_PLCFOLDER)
                {
                    if (!CollectObjectNames(child, names))
                        return false;
                }
                else if (child is ITcPlcDeclaration)
                {
                    names.Add(child.Name);
                    if (names.Count > FreshProjectObjectLimit)
                        return false;
                }
            }
            return true;
        }
    }
}
