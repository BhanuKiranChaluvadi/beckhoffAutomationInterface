using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using BeckhoffAutomationInterface.Sync;
using Interop.TCatSysManager;
using Xunit;

namespace BeckhoffAutomationInterface.Tests
{
    /// <summary>Import of stored object files into an open project: the plan, the COM calls
    /// (on a stub), and the check of the saved project. Nothing here starts TwinCAT.</summary>
    public class PlcObjectImportTests
    {
        static StPouSource Fb(string name, string folder) =>
            new StPouSource(name, PouKind.FunctionBlock, null, "FUNCTION_BLOCK " + name, "") { RelativeFolder = folder };

        static StPouSource Method(string owner, string name) =>
            new StPouSource(name, PouKind.Method, owner, "METHOD " + name, "");

        static PlcObjectImport.Item Item(string name, string folder) =>
            new PlcObjectImport.Item(name, folder, new List<StPouSource> { Fb(name, folder) }, @"C:\store\" + name + ".TcPOU");

        // ------------------------------------------------------------------ the plan

        [Fact]
        public void Plan_ImportsTheStoredObjectsThatAreNotSkippedAndNotInTheProject()
        {
            var sources = new List<StPouSource>
            {
                Fb("FB_New", "Lib/A"), Method("FB_New", "Run"),
                Fb("FB_Skipped", "Lib/A"),
                Fb("FB_InProject", "Lib/A"),
                Fb("FB_NotStored", "Lib/B"),
            };
            var projectFiles = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["fb_inproject.TcPOU"] = @"Lib\A\FB_InProject.TcPOU" };
            string Find(IReadOnlyList<StPouSource> group) => group[0].Name == "FB_NotStored" ? null : @"C:\store\" + group[0].Name + ".TcPOU";

            List<PlcObjectImport.Item> plan = PlcObjectImport.Plan(sources, new[] { "fb_skipped" }, projectFiles, Find);

            PlcObjectImport.Item item = Assert.Single(plan);
            Assert.Equal("FB_New", item.Name);
            Assert.Equal("Lib/A", item.Folder);
            Assert.Equal(new[] { "FB_New", "Run" }, item.Group.Select(s => s.Name));
            Assert.Equal(@"C:\store\FB_New.TcPOU", item.File);
        }

        // ------------------------------------------------------------------ the check after the import

        static SyncManifest.SavedFile Saved(string name, string include) =>
            new SyncManifest.SavedFile(name, new List<StPouSource> { Fb(name, "") }, new byte[0], include);

        [Fact]
        public void Missing_IsEmptyWhenTheSavedProjectHoldsEveryImportedObjectInItsFolder()
        {
            var imported = new[] { Item("FB_A", "Lib/A"), Item("FB_Root", "") };
            var saved = new[] { Saved("FB_A", @"Lib\A\FB_A.TcPOU"), Saved("fb_root", "FB_Root.TcPOU") };

            Assert.Empty(PlcObjectImport.Missing(imported, saved));
        }

        [Fact]
        public void Missing_NamesEachObjectThatXaeDidNotKeepWithItsTexts_OrPutInAnotherFolder()
        {
            var imported = new[] { Item("FB_Dropped", "Lib/A"), Item("FB_Moved", "Lib/A"), Item("FB_Kept", "Lib/A") };
            var saved = new[] { Saved("FB_Moved", "FB_Moved.TcPOU"), Saved("FB_Kept", @"Lib\A\FB_Kept.TcPOU") };

            List<PlcObjectImport.Miss> missing = PlcObjectImport.Missing(imported, saved);

            Assert.Equal(new[] { "FB_Dropped", "FB_Moved" }, missing.Select(m => m.Item.Name));
            Assert.StartsWith("FB_Dropped ", missing[0].Text);
            Assert.StartsWith("FB_Moved ", missing[1].Text);
            Assert.Contains("Lib/A", missing[1].Text);
        }

        // ------------------------------------------------------------------ the COM calls

        [Fact]
        public void Import_CallsCreateChildWithSubtype58_OncePerFolder_WithAllFilesOfThatFolder()
        {
            const string root = "TIPC^NFLite^NFLite Project";
            var calls = new List<(string Path, object[] Args)>();
            ITcSmTreeItem Folder(string path) => ComStub.Make<ITcSmTreeItem>(call =>
            {
                if (call.MethodName == "CreateChild") { calls.Add((path, call.Args)); return null; }
                if (call.MethodName == "get_PathName") return path;
                throw new InvalidOperationException("Unexpected COM call: " + call.MethodName);
            });
            var manager = ComStub.Make<ITcSysManager>(call =>
            {
                if (call.MethodName == "LookupTreeItem") return Folder((string)call.Args[0]);
                throw new InvalidOperationException("Unexpected COM call: " + call.MethodName);
            });

            new PouSyncEngine(manager, root).Import(new[] { Item("FB_A1", "Lib/A"), Item("FB_Root", ""), Item("FB_A2", "Lib/A") });

            Assert.Equal(2, calls.Count);
            (string Path, object[] Args) lib = calls.Single(c => c.Path == root + "^Lib^A");
            Assert.Null(lib.Args[0]);
            Assert.Equal(PlcObjectImport.ImportFiles, lib.Args[1]);
            Assert.Equal(new[] { @"C:\store\FB_A1.TcPOU", @"C:\store\FB_A2.TcPOU" }, Assert.IsType<string[]>(lib.Args[3]));
            Assert.Equal(new[] { @"C:\store\FB_Root.TcPOU" }, Assert.IsType<string[]>(calls.Single(c => c.Path == root).Args[3]));
        }

        [Fact]
        public void Import_OfNothingMakesNoComCall()
        {
            var manager = ComStub.Make<ITcSysManager>(call => call.MethodName == "LookupTreeItem"
                ? ComStub.Make<ITcSmTreeItem>(c => throw new COMException("Unexpected COM call: " + c.MethodName))
                : throw new InvalidOperationException("Unexpected COM call: " + call.MethodName));

            new PouSyncEngine(manager, "TIPC^P^P Project").Import(new PlcObjectImport.Item[0]);
        }
    }
}
