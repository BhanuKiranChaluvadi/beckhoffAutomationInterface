using System.Collections.Generic;
using BeckhoffAutomationInterface.Sync;
using Xunit;

namespace BeckhoffAutomationInterface.Tests
{
    /// <summary>
    /// LinkChecker.Check (tasks/plan.md: "--check-links — %I/%Q ↔ <Links> Cross-Reference
    /// Checker"). Pure in-memory fixtures — no file I/O, no COM.
    /// </summary>
    public class LinkCheckerTests
    {
        static StPouSource Gvl(string name, string declarationText) =>
            new StPouSource(name, PouKind.Gvl, null, declarationText, null) { SourceFileName = name + ".st" };

        static StPouSource Program(string name, string declarationText) =>
            new StPouSource(name, PouKind.Program, null, declarationText, "") { SourceFileName = name + ".st" };

        static StPouSource FunctionBlock(string name, string declarationText) =>
            new StPouSource(name, PouKind.FunctionBlock, null, declarationText, "") { SourceFileName = name + ".st" };

        static LinkSpec Link(string plcVar, string ioChannel = "Device 1^Term 1^Term 2^Channel 1^Input") =>
            new LinkSpec(plcVar, ioChannel);

        static VarLinkEntry VarLink(string varA, string group = "PlcTask Inputs", string varB = "Device 1^Channel 1^Input") =>
            new VarLinkEntry("TIPC^Shark^Shark Instance", group, varA, "TIID^Device 1", varB);

        [Fact]
        public void LinkedGvlVariable_IsNotReportedUnlinked()
        {
            var sources = new[] { Gvl("GVL_Shark", "VAR_GLOBAL\n\tbMotorRunSensor AT %I* : BOOL;\nEND_VAR") };
            var links = new[] { Link("Shark Instance^PlcTask Inputs^GVL_Shark.bMotorRunSensor") };

            LinkCheckReport report = LinkChecker.Check(sources, links);

            Assert.Single(report.Linked);
            Assert.Empty(report.Unlinked);
            Assert.Empty(report.OrphanedLinks);
        }

        [Fact]
        public void UnlinkedProgramVariable_IsReported()
        {
            var sources = new[] { Program("PRG_DIGITAL_INPUT", "VAR\n\tIO_DoorLT AT %I* : BOOL;\nEND_VAR") };
            var links = new List<LinkSpec>();

            LinkCheckReport report = LinkChecker.Check(sources, links);

            DeclaredIoVariable unlinked = Assert.Single(report.Unlinked);
            Assert.Equal("PRG_DIGITAL_INPUT", unlinked.SourceName);
            Assert.Equal("IO_DoorLT", unlinked.VariableName);
            Assert.Equal("%I", unlinked.Direction);
        }

        [Fact]
        public void FunctionBlockOwnedAtDeclaration_IsExcludedEntirely()
        {
            var sources = new[] { FunctionBlock("FB_EL73x2_DcMotorNoEncoder", "VAR\n\tbEnable AT %Q* : BOOL;\nEND_VAR") };

            LinkCheckReport report = LinkChecker.Check(sources, new List<LinkSpec>());

            Assert.Empty(report.Linked);
            Assert.Empty(report.Unlinked);
        }

        [Fact]
        public void CommentedOutDeclaration_IsIgnored()
        {
            var sources = new[] { Gvl("GVL_X", "VAR_GLOBAL\n\t// bOld AT %I* : BOOL;\nEND_VAR") };

            LinkCheckReport report = LinkChecker.Check(sources, new List<LinkSpec>());

            Assert.Empty(report.Linked);
            Assert.Empty(report.Unlinked);
        }

        [Fact]
        public void Direction_IsCaseInsensitive()
        {
            var sources = new[] { Gvl("GVL_X", "VAR_GLOBAL\n\tbFlag at %i* : BOOL;\nEND_VAR") };

            LinkCheckReport report = LinkChecker.Check(sources, new List<LinkSpec>());

            DeclaredIoVariable found = Assert.Single(report.Unlinked);
            Assert.Equal("%I", found.Direction);
        }

        [Fact]
        public void StaleLinkWithNoMatchingDeclaration_IsReportedAsOrphaned()
        {
            var sources = new[] { Gvl("GVL_X", "VAR_GLOBAL\n\tbCurrent AT %I* : BOOL;\nEND_VAR") };
            var links = new[]
            {
                Link("Shark Instance^PlcTask Inputs^GVL_X.bCurrent"),
                Link("Shark Instance^PlcTask Inputs^GVL_X.bRenamedAway"),
            };

            LinkCheckReport report = LinkChecker.Check(sources, links);

            Assert.Empty(report.Unlinked);
            LinkSpec orphan = Assert.Single(report.OrphanedLinks);
            Assert.EndsWith("bRenamedAway", orphan.PlcVar);
        }

        [Fact]
        public void MultipleSourcesAndLinks_CombineCorrectly()
        {
            var sources = new[]
            {
                Gvl("GVL_Safety", "VAR_GLOBAL\n\tSafetyOk AT %I* : BOOL;\n\tSafetyRunning AT %Q* : BOOL;\nEND_VAR"),
                Program("PRG_VALVE", "VAR\n\tIO_ChuckLZ1 AT %Q* : BOOL;\nEND_VAR"),
            };
            var links = new[] { Link("Shark Instance^PlcTask Inputs^GVL_Safety.SafetyOk") };

            LinkCheckReport report = LinkChecker.Check(sources, links);

            Assert.Single(report.Linked);
            Assert.Equal(2, report.Unlinked.Count);
            Assert.Contains(report.Unlinked, v => v.Key == "GVL_Safety.SafetyRunning");
            Assert.Contains(report.Unlinked, v => v.Key == "PRG_VALVE.IO_ChuckLZ1");
        }

        [Fact]
        public void LinksXmlEntryWithSimpleKey_CountsAsLinked()
        {
            var sources = new[] { Gvl("GVL_Shark", "VAR_GLOBAL\n\tbMotorRunSensor AT %I* : BOOL;\nEND_VAR") };
            var varLinks = new[] { VarLink("GVL_Shark.bMotorRunSensor") };

            LinkCheckReport report = LinkChecker.Check(sources, new List<LinkSpec>(), varLinks);

            Assert.Single(report.Linked);
            Assert.Empty(report.Unlinked);
        }

        [Fact]
        public void LinksXmlEntry_NestedFbInstanceVarA_IsUnresolvable_NotOrphanedOrUnlinked()
        {
            var sources = new[] { Program("MAIN", "VAR\n\tfbSpec : FB_Spectrometer;\nEND_VAR") };
            var varLinks = new[] { VarLink("MAIN.fbSpec.inLogicSig[1]") };

            LinkCheckReport report = LinkChecker.Check(sources, new List<LinkSpec>(), varLinks);

            VarLinkEntry unresolvable = Assert.Single(report.Unresolvable);
            Assert.Equal("MAIN.fbSpec.inLogicSig[1]", unresolvable.VarA);
            Assert.Empty(report.OrphanedVarLinks);
            Assert.Empty(report.Unlinked);
        }

        [Fact]
        public void LinksXmlEntry_ResolvableButNoMatchingDeclaration_IsOrphanedVarLink()
        {
            var sources = new List<StPouSource>();
            var varLinks = new[] { VarLink("GVL_Shark.bRenamedAway") };

            LinkCheckReport report = LinkChecker.Check(sources, new List<LinkSpec>(), varLinks);

            VarLinkEntry orphan = Assert.Single(report.OrphanedVarLinks);
            Assert.Equal("GVL_Shark.bRenamedAway", orphan.VarA);
        }

        [Fact]
        public void NoLinksXmlEntries_BehavesExactlyLikeBeforeThisFeature()
        {
            var sources = new[] { Gvl("GVL_Shark", "VAR_GLOBAL\n\tbMotorRunSensor AT %I* : BOOL;\nEND_VAR") };

            LinkCheckReport report = LinkChecker.Check(sources, new List<LinkSpec>());

            Assert.Single(report.Unlinked);
            Assert.Empty(report.Unresolvable);
            Assert.Empty(report.OrphanedVarLinks);
        }
    }
}
