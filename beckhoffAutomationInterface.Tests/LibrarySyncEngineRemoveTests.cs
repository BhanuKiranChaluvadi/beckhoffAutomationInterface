using System;
using System.Collections.Generic;
using BeckhoffAutomationInterface.Sync;
using Interop.TCatSysManager;
using Xunit;

namespace BeckhoffAutomationInterface.Tests
{
    /// <summary>Removing a library that libraries.xml no longer lists from a kept project.
    /// LIVE FINDING (2026-11-21, BeckhoffLib removed Tc3_JsonXml): a "*" library is a
    /// placeholder reference; RemoveReference("Tc3_JsonXml", "*", company) failed with
    /// "Specified library ... not found!" and stopped every kept project's sync.</summary>
    public class LibrarySyncEngineRemoveTests
    {
        static ITcPlcLibraryManager Manager(ITcPlcLibRef reference, List<object[]> removed) =>
            ComStub.Make<ITcPlcLibraryManager>(call =>
            {
                if (call.MethodName == "get_References")
                    return ComStub.Make<ITcPlcReferences>(c =>
                        c.MethodName == "get_Count" ? (object)1 :
                        c.MethodName == "get_Item" ? reference :
                        throw new NotSupportedException(c.MethodName));
                if (call.MethodName == "RemoveReference")
                {
                    removed.Add(call.Args);
                    return null;
                }
                throw new NotSupportedException(call.MethodName);
            });

        [Fact]
        public void PlaceholderReference_IsRemovedByItsPlaceholderName()
        {
            ITcPlcLibRef placeholder = ComStub.Make<ITcPlcLibRef>(call =>
                call.MethodName == "get_Name" || call.MethodName == "get_PlaceholderName" ? "Tc3_JsonXml" :
                throw new NotSupportedException(call.MethodName),
                type => type != typeof(ITcPlcLibrary));
            var removed = new List<object[]>();

            LibrarySyncReport report = LibrarySyncEngine.Sync(Manager(placeholder, removed), new List<LibraryReference>());

            Assert.Single(removed);
            Assert.Equal(new object[] { "Tc3_JsonXml", "", "" }, removed[0]);
            Assert.Equal(new[] { "Tc3_JsonXml" }, report.Removed);
        }

        [Fact]
        public void FixedVersionReference_IsRemovedWithItsOwnVersionAndDistributor()
        {
            ITcPlcLibRef library = ComStub.Make<ITcPlcLibRef>(call =>
                call.MethodName == "get_Name" || call.MethodName == "get_LibraryName" ? "Tc3_JsonXml" :
                call.MethodName == "get_Version" ? "3.4.9.0" :
                call.MethodName == "get_Distributor" ? "Beckhoff Automation GmbH" :
                throw new NotSupportedException(call.MethodName),
                type => type != typeof(ITcPlcPlaceholderRef));
            var removed = new List<object[]>();

            LibrarySyncEngine.Sync(Manager(library, removed), new List<LibraryReference>());

            Assert.Single(removed);
            Assert.Equal(new object[] { "Tc3_JsonXml", "3.4.9.0", "Beckhoff Automation GmbH" }, removed[0]);
        }

        [Fact]
        public void ImplicitReference_IsNeverRemoved()
        {
            ITcPlcLibRef implicitRef = ComStub.Make<ITcPlcLibRef>(call =>
                call.MethodName == "get_Name" ? "#Tc2_System" : throw new NotSupportedException(call.MethodName));
            var removed = new List<object[]>();

            LibrarySyncEngine.Sync(Manager(implicitRef, removed), new List<LibraryReference>());

            Assert.Empty(removed);
        }
    }
}
