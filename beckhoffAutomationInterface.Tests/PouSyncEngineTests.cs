using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using BeckhoffAutomationInterface.Sync;
using Interop.TCatSysManager;
using Xunit;

namespace BeckhoffAutomationInterface.Tests
{
    /// <summary>PouSyncEngine against a COM stub of the PLC tree. Nothing here starts TwinCAT.</summary>
    public class PouSyncEngineTests
    {
        const string Root = "TIPC^P^P Project";

        [Fact]
        public void Sync_ReusesTheTemplateMainInPous_WithoutWalkingTheTree()
        {
            var texts = new Dictionary<string, string> { ["decl"] = "PROGRAM MAIN", ["impl"] = "" };
            ITcSmTreeItem main = ComStub.Make<ITcSmTreeItem>(call =>
            {
                switch (call.MethodName)
                {
                    case "get_DeclarationText": return texts["decl"];
                    case "set_DeclarationText": texts["decl"] = (string)call.Args[0]; return null;
                    case "get_ImplementationText": return texts["impl"];
                    case "set_ImplementationText": texts["impl"] = (string)call.Args[0]; return null;
                    case "get_PathName": return Root + "^POUs^MAIN";
                }
                throw new InvalidOperationException("Unexpected COM call on MAIN: " + call.MethodName);
            });
            // A folder answers only its path and refuses a second MAIN; any other call (a tree walk) fails the test.
            ITcSmTreeItem Folder(string path) => ComStub.Make<ITcSmTreeItem>(call =>
            {
                if (call.MethodName == "get_PathName") return path;
                if (call.MethodName == "CreateChild") throw new COMException("An object with this name already exists.", unchecked((int)0x80004005));
                throw new InvalidOperationException("Unexpected COM call on " + path + ": " + call.MethodName);
            });
            var folders = new HashSet<string> { Root, Root + "^App", Root + "^App^NFLite" };
            var manager = ComStub.Make<ITcSysManager>(call =>
            {
                string path = (string)call.Args[0];
                if (call.MethodName != "LookupTreeItem") throw new InvalidOperationException("Unexpected COM call: " + call.MethodName);
                if (folders.Contains(path)) return Folder(path);
                if (path == Root + "^POUs^MAIN") return main;
                throw new COMException("Tree item not found: " + path);
            });
            var source = new StPouSource("MAIN", PouKind.Program, null, "PROGRAM MAIN\r\nVAR\r\nEND_VAR", "x := 1;") { RelativeFolder = "App/NFLite" };

            SyncReport report = new PouSyncEngine(manager, Root).Sync(new[] { source });

            Assert.Equal("PROGRAM MAIN\r\nVAR\r\nEND_VAR", texts["decl"]);
            Assert.Equal("x := 1;", texts["impl"]);
            Assert.Equal(new[] { "App/NFLite/MAIN" }, report.Updated);
        }
    }
}
