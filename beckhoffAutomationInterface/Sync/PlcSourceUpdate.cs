using System;
using System.Collections.Generic;
using System.IO;
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
        readonly bool KeepSessionOpen;

        public PlcSourceUpdate(TwinCatSession session, RunOptions options, IgnoreRules ignore,
            PipelineOutcome outcome, bool mirror, bool keepSessionOpen)
        {
            _session = session;
            _options = options;
            _ignore = ignore;
            Outcome = outcome;
            DeleteStaleObjects = mirror;
            KeepSessionOpen = keepSessionOpen;
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

            List<PlcSourceObject> sourceObjects = PlcSourceObject.Group(desiredPous);
            bool useManifest = _options.ExistingTsprojPath == null && _session.Shadow == null;
            bool useFiles = useManifest && !KeepSessionOpen;

            // Drift: a previously-synced name missing from the current parse means its .st
            // source was renamed/deleted, but the PLC object still compiles. With
            // DeleteStaleObjects it is deleted (and a moved object too); otherwise only reported.
            List<string> currentNames = KnownNamesTracker.CollectNames(desiredPous);
            List<string> previousNames = KnownNamesTracker.Read(_options.KnownNamesPath);
            List<string> deletedObjects = new List<string>();
            if (DeleteStaleObjects && !useFiles)
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
            SyncReport syncReport;
            if (useFiles && (skip.Count < sourceObjects.Count || DeleteStaleObjects))
            {
                _session.Project.Save();
                _session.EnsureClosed();
                var cache = PlcObjectCache.Open();
                syncReport = PlcProjectCode.Apply(_options.PlcprojFilePath, sourceObjects, cache,
                    preserveMembers: true, skip: skip, previousNames: DeleteStaleObjects ? previousNames ?? new List<string>() : null);
                Console.WriteLine("{0}: Conversion cache: {1} reused, {2} converted ({3}).",
                    Now(), cache.Reused, cache.Converted, PlcObjectCache.DefaultDirectory);
                _session.EnsureOpen();
            }
            else
            {
                // Adopted projects retain their shadow-copy/COM path. An unchanged owned project has no COM text reads.
                var syncEngine = new PouSyncEngine(_session.SysManager, _options.ProjectRootPath);
                syncReport = syncEngine.Sync(desiredPous, skip);
            }
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

            // Record the known-names drift baseline (see the [drift] warning block above).
            KnownNamesTracker.Write(_options.KnownNamesPath, currentNames);
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
