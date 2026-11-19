using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using static BeckhoffAutomationInterface.Clock;
using static BeckhoffAutomationInterface.ComInterop;
using static BeckhoffAutomationInterface.ConsoleReport;

namespace BeckhoffAutomationInterface.Sync
{
    /// <summary>Owns source validation, update selection, persistence, and successful-sync bookkeeping.</summary>
    sealed class PlcSourceUpdate
    {
        readonly TwinCatSession _session;
        readonly RunOptions _options;
        readonly IgnoreRules _ignore;
        readonly PipelineOutcome Outcome;
        readonly bool DeleteStaleObjects;

        public PlcSourceUpdate(TwinCatSession session, RunOptions options, IgnoreRules ignore,
            PipelineOutcome outcome, bool mirror)
        {
            _session = session;
            _options = options;
            _ignore = ignore;
            Outcome = outcome;
            DeleteStaleObjects = mirror;
        }
        /// <summary>Code stage: .st files -> PLC POUs (create/update/delete), plus the
        /// warn-only drift detection, the naming lint, and the known-names baseline.</summary>
        public void Apply()
        {
            Console.WriteLine("{0}: Parsing .st sources from '{1}'...", Now(), _options.SourceFolder);
            List<StPouSource> desiredPous = StFileParser.ParseFolder(_options.SourceFolder, _ignore);

            // Two files for one object: refuse before anything is written (see DuplicateObjects).
            List<string> duplicates = DuplicateObjects.Find(desiredPous);
            if (duplicates.Count > 0)
                throw new InvalidOperationException(string.Format(
                    "{0} object(s) are defined in more than one .st file; nothing was synced. Delete or rename the stale file:\n    {1}",
                    duplicates.Count, string.Join("\n    ", duplicates)));

            bool useManifest = _options.ExistingTsprojPath == null && _session.Shadow == null;

            // Drift: a previously-synced name missing from the current parse means its .st
            // source was renamed/deleted, but the PLC object still compiles. With
            // DeleteStaleObjects it is deleted (and a moved object too); otherwise only reported.
            List<string> currentNames = KnownNamesTracker.CollectNames(desiredPous);
            List<string> previousNames = KnownNamesTracker.Read(_options.KnownNamesPath);
            List<string> deletedObjects = new List<string>();
            if (DeleteStaleObjects)
            {
                StaleObjectPlan plan = null;
                RetryOnBusy(() => plan = StaleObjectPlan.Build(previousNames, desiredPous,
                    StaleObjectDeleter.IndexTopLevel(_session.SysManager.LookupTreeItem(_options.ProjectRootPath)),
                    _options.ProjectRootPath), "indexing PLC objects");
                RetryOnBusy(() => deletedObjects = StaleObjectDeleter.Execute(_session.SysManager, plan), "deleting stale PLC objects");
                Outcome.StaleNames = plan.Unresolved.Count;
                if (plan.Unresolved.Count > 0)
                {
                    Console.WriteLine("{0}: [drift] {1} object(s) could not be deleted in the kept project:", Now(), plan.Unresolved.Count);
                    PrintLines("! stale    ", plan.Unresolved);
                }
            }
            else if (!DeleteStaleObjects && previousNames != null)
            {
                List<string> staleNames = KnownNamesTracker.DiffFull(previousNames, currentNames);
                Outcome.StaleNames = staleNames.Count;
                if (staleNames.Count > 0)
                {
                    Console.WriteLine("{0}: [drift] {1} previously-synced name(s) no longer in .st source (renamed or deleted?) — still compiling in the PLC project:", Now(), staleNames.Count);
                    PrintLines("! stale    ", staleNames);
                }
            }

            List<string> lintIssues = StLinter.Lint(desiredPous);
            if (lintIssues.Count > 0)
            {
                Console.WriteLine("{0}: [lint] {1} naming convention warning(s):", Now(), lintIssues.Count);
                PrintLines("! ", lintIssues);
            }

            // The sync manifest: objects whose source and project file are as recorded are not
            // read over COM. Not for an adopted project (a temporary copy, never saved).
            List<string> skip = useManifest
                ? SyncManifest.Skippable(SyncManifest.Load(_options.SyncManifestPath, SyncManifest.CurrentBai), desiredPous, _options.PlcprojFilePath)
                : new List<string>();

            Console.WriteLine("{0}: Syncing {1} PLC object(s)...", Now(), desiredPous.Count);
            var syncEngine = new PouSyncEngine(_session.SysManager, _options.ProjectRootPath);
            PlcObjectFileStore store = useManifest ? OpenStore() : null;
            List<PlcObjectImport.Item> imported = store == null ? new List<PlcObjectImport.Item>() : ImportStored(store, syncEngine, desiredPous, skip);
            var importedNames = new HashSet<string>(imported.Select(i => i.Name), PlcSourceObject.Names);
            // An object unchanged in the sync manifest is not read over COM; an imported one is proven already.
            SyncReport syncReport = syncEngine.Sync(desiredPous.Where(s => !importedNames.Contains(s.OwnerName ?? s.Name)).ToList(), skip);
            syncReport.Created.AddRange(imported.Select(i => i.Folder.Length > 0 ? i.Folder + "/" + i.Name : i.Name));
            syncReport.Deleted.AddRange(deletedObjects);

            PrintLines("+ created  ", syncReport.Created);
            PrintLines("~ updated  ", syncReport.Updated);
            PrintLines("- deleted  ", syncReport.Deleted);

            _session.Project.Save();
            Console.WriteLine("{0}: Sync complete ({1} created, {2} updated, {3} deleted, {4} unchanged{5}).",
                Now(), syncReport.Created.Count, syncReport.Updated.Count, syncReport.Deleted.Count, syncReport.Unchanged.Count,
                useManifest ? string.Format(", of which {0} not read: unchanged in the sync manifest", syncReport.Skipped) : "");
            if (useManifest)
                RecordSyncManifest(desiredPous);
            if (store != null)
                StoreSavedFiles(store, desiredPous);

            // Record the known-names drift baseline (see the [drift] warning block above).
            KnownNamesTracker.Write(_options.KnownNamesPath, currentNames);
        }

        /// <summary>The shared object files for the TwinCAT version that saved this project; null
        /// when the project names none (then nothing is imported or stored).</summary>
        PlcObjectFileStore OpenStore()
        {
            string twinCat = PlcObjectFileStore.ProjectTwinCatVersion(_options.TsprojFilePath);
            return twinCat == null ? null : new PlcObjectFileStore(PlcObjectFileStore.DefaultRoot, twinCat, SyncManifest.CurrentBai);
        }

        /// <summary>Imports the stored files of the objects the project lacks (PlcObjectImport),
        /// then saves the project and proves each object is in its folder with all its texts.
        /// Throws, naming the objects, when XAE did not keep one.</summary>
        List<PlcObjectImport.Item> ImportStored(PlcObjectFileStore store, PouSyncEngine engine, List<StPouSource> desired, List<string> skip)
        {
            var none = new List<PlcObjectImport.Item>();
            var noFiles = new Dictionary<string, string>();
            if (PlcObjectImport.Plan(desired, skip, noFiles, store.Find).Count == 0)
                return none;
            _session.Project.Save(); // the .plcproj on disk must name every object the project has
            Dictionary<string, string> projectFiles = SyncManifest.ReadProjectFiles(_options.PlcprojFilePath);
            if (projectFiles == null)
                return none;
            List<PlcObjectImport.Item> plan = PlcObjectImport.Plan(desired, skip, projectFiles, store.Find);
            if (plan.Count == 0)
                return none;
            Console.WriteLine("{0}: Importing {1} PLC object(s) from shared object files ({2})...", Now(), plan.Count, PlcObjectFileStore.DefaultRoot);
            engine.Import(plan);
            _session.Project.Save();
            List<PlcObjectImport.Miss> missing = PlcObjectImport.Missing(plan, SyncManifest.SavedFiles(plan.SelectMany(i => i.Group), _options.PlcprojFilePath));
            if (missing.Count > 0)
            {
                foreach (PlcObjectImport.Miss miss in missing)
                    store.Remove(miss.Item.Group);
                throw new InvalidOperationException(string.Format(
                    "XAE did not keep {0} of {1} imported PLC object(s) as their source says. Their shared files are removed;" +
                    " the next build of this project makes them over COM:\n    {2}",
                    missing.Count, plan.Count, string.Join("\n    ", missing.Select(m => m.Text))));
            }
            Console.WriteLine("{0}: Import complete: {1} object(s) in their folders with all their texts.", Now(), plan.Count);
            return plan;
        }

        /// <summary>After the save: keeps each object file that holds its texts for other projects.
        /// A store that cannot be written only loses the reuse.</summary>
        void StoreSavedFiles(PlcObjectFileStore store, List<StPouSource> desired)
        {
            try
            {
                int stored = store.PutSaved(desired, _options.PlcprojFilePath);
                Console.WriteLine("{0}: Shared object files: {1} of {2} object(s) stored.", Now(), stored, SyncManifest.Groups(desired).Count);
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                Console.WriteLine("{0}: WARNING: shared object files not stored ({1}); other projects build these objects over COM.", Now(), ex.Message);
            }
        }

        /// <summary>After the code sync saved the project: records each object whose file the
        /// .plcproj names and which holds the texts bai sent (see SyncManifest). A manifest that
        /// cannot be written is removed, so the next sync compares every object.</summary>
        void RecordSyncManifest(List<StPouSource> desiredPous)
        {
            string path = _options.SyncManifestPath;
            try
            {
                SyncManifest manifest = SyncManifest.Record(SyncManifest.CurrentBai, desiredPous, _options.PlcprojFilePath);
                manifest.Save(path);
                Console.WriteLine("{0}: Sync manifest: {1} of {2} object(s) recorded.", Now(), manifest.Entries.Count, SyncManifest.Groups(desiredPous).Count);
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                Console.WriteLine("{0}: WARNING: sync manifest not written ({1}); the next sync compares every object.", Now(), ex.Message);
                try { File.Delete(path); }
                catch (Exception e) when (e is IOException || e is UnauthorizedAccessException) { /* it is from another bai or older: Load then compares */ }
            }
        }

    }
}
