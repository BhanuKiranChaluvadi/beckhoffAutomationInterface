using System;
using System.Runtime.InteropServices;
using BeckhoffAutomationInterface.Sync;
using Interop.TCatSysManager;
using Xunit;

namespace BeckhoffAutomationInterface.Tests
{
    public class TreeItemFactoryTests
    {
        [Theory]
        [InlineData(unchecked((int)0x800706BE))] // lost server
        [InlineData(unchecked((int)0x80004005))] // e.g. existing MAIN in the template
        public void CreationFailureRetainsComRecoveryAndAddsTheObjectContext(int hresult)
        {
            var failure = new COMException("Native creation failure.", hresult);
            var manager = ComStub.Make<ITcSysManager>(call => throw new COMException("Item not found"));
            var parent = ComStub.Make<ITcSmTreeItem>(call =>
            {
                if (call.MethodName == "get_PathName") return "TIPC^NFPro^FB_Test";
                if (call.MethodName == "CreateChild") throw failure;
                throw new InvalidOperationException("Unexpected COM call: " + call.MethodName);
            });

            var error = Assert.Throws<COMException>(() =>
                TreeItemFactory.GetOrCreate(manager, parent, "Execute", 609, null, out _));

            Assert.Same(failure, error);
            Assert.Equal(hresult, error.HResult);
            var context = Assert.IsType<string>(error.Data[TreeItemFactory.CreationContextKey]);
            Assert.Contains("TIPC^NFPro^FB_Test^Execute", context);
            Assert.Contains($"0x{hresult:X8}", context);
        }
    }
}
