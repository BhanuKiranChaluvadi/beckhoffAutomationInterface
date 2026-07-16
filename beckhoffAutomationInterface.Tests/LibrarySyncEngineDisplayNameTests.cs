using BeckhoffAutomationInterface.Sync;
using Xunit;

namespace BeckhoffAutomationInterface.Tests
{
    /// <summary>
    /// LibrarySyncEngine.TryParseDisplayName. The bare-name test cases are the REAL,
    /// empirically-observed ITcPlcLibRef.Name values from a live scratch TwinCAT project
    /// (tasks/2026-07-15-reverse-export-scaffold/, live validation 2026-07-16) — the
    /// originally-assumed combined "Name, Version (Company)" display format was never
    /// actually produced; disproving that assumption is exactly what reverse-export's
    /// round-trip testing was for.
    ///
    /// The isImplicit flag is a SECOND live finding from the same session: attempting to
    /// RemoveReference an implicit ("#"-prefixed) reference not listed in a desired
    /// manifest crashes with a real COMException ("Specified library '...' not found!")
    /// against a live project — Sync's orphan-removal loop must skip these.
    /// </summary>
    public class LibrarySyncEngineDisplayNameTests
    {
        [Theory]
        [InlineData("Tc2_Standard", false)]
        [InlineData("#Tc2_Standard", true)]
        [InlineData("#Tc2_System", true)]
        [InlineData("#Tc3_Module", true)]
        [InlineData("#Tc3_GlobalTypes", true)]
        public void BareBeckhoffNamespaceName_ParsesWithDefaultVersionAndCompany_AndCorrectImplicitFlag(string rawDisplayName, bool expectedImplicit)
        {
            bool parsed = LibrarySyncEngine.TryParseDisplayName(rawDisplayName, out string name, out string version, out string company, out bool isImplicit);

            Assert.True(parsed);
            Assert.Equal(rawDisplayName.TrimStart('#'), name);
            Assert.Equal("*", version);
            Assert.Equal("Beckhoff Automation GmbH", company);
            Assert.Equal(expectedImplicit, isImplicit);
        }

        [Fact]
        public void CombinedDisplayFormat_StillParses_IfEverActuallyProduced()
        {
            bool parsed = LibrarySyncEngine.TryParseDisplayName("Tc2_Standard, * (Beckhoff Automation GmbH)",
                out string name, out string version, out string company, out bool isImplicit);

            Assert.True(parsed);
            Assert.Equal("Tc2_Standard", name);
            Assert.Equal("*", version);
            Assert.Equal("Beckhoff Automation GmbH", company);
            Assert.False(isImplicit);
        }

        [Fact]
        public void BareNonBeckhoffName_IsLeftUnparsed_NoGuessedCompany()
        {
            bool parsed = LibrarySyncEngine.TryParseDisplayName("SomeThirdPartyLib",
                out string name, out string version, out string company, out bool isImplicit);

            Assert.False(parsed);
            Assert.Equal("SomeThirdPartyLib", name);
            Assert.Null(version);
            Assert.Null(company);
            Assert.False(isImplicit);
        }

        [Fact]
        public void ImplicitNonBeckhoffName_IsLeftUnparsed_ButStillFlaggedImplicit()
        {
            bool parsed = LibrarySyncEngine.TryParseDisplayName("#SomeThirdPartyLib",
                out string name, out string version, out string company, out bool isImplicit);

            Assert.False(parsed);
            Assert.True(isImplicit); // even when unparseable, the "#" marker itself is still detected
        }
    }
}
