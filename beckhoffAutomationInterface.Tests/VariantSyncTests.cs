using System.Collections.Generic;
using BeckhoffAutomationInterface;
using BeckhoffAutomationInterface.Sync;
using Xunit;

namespace BeckhoffAutomationInterface.Tests
{
    /// <summary>Variants stage (tasks/2026-10-14-variants-closed-loop, Task 6) — the COM-free parts.</summary>
    public class VariantSyncTests
    {
        static VariantManifest M(string[] variants, params (string group, string[] members)[] groups)
        {
            var g = new Dictionary<string, IReadOnlyList<string>>();
            foreach (var (group, members) in groups)
                g[group] = members;
            return new VariantManifest(variants, g);
        }

        [Fact]
        public void EmptyProject_AddsEverything()
        {
            VariantSyncReport r = VariantSyncEngine.Plan(M(new string[0]), M(new[] { "A", "B" }, ("PCV_MODBUS", new[] { "A" })));

            Assert.True(r.ConfigChanged);
            Assert.Equal(new[] { "A", "B" }, r.Added);
            Assert.Empty(r.Removed);
        }

        [Fact]
        public void SameConfig_NoChange()
        {
            VariantManifest m = M(new[] { "A", "B" }, ("PCV_MODBUS", new[] { "A" }));

            VariantSyncReport r = VariantSyncEngine.Plan(m, M(new[] { "B", "A" }, ("PCV_MODBUS", new[] { "A" })));

            Assert.False(r.ConfigChanged);
            Assert.True(r.NoChange);
        }

        [Fact]
        public void VariantRemovedFromManifest_IsReportedAsRemoved()
        {
            VariantSyncReport r = VariantSyncEngine.Plan(M(new[] { "A", "B", "C" }), M(new[] { "A", "B" }));

            Assert.True(r.ConfigChanged);
            Assert.Equal(new[] { "C" }, r.Removed);
        }

        [Fact]
        public void GroupMembershipChange_ChangesConfig_WithoutAddedOrRemovedVariants()
        {
            VariantSyncReport r = VariantSyncEngine.Plan(
                M(new[] { "A", "B" }, ("OZONE", new[] { "A" })),
                M(new[] { "A", "B" }, ("OZONE", new[] { "B" })));

            Assert.True(r.ConfigChanged);
            Assert.Empty(r.Added);
            Assert.Empty(r.Removed);
        }

        [Theory]
        [InlineData("<TreeItem><PlcProjectDef><EnableImplicitDefines>true</EnableImplicitDefines></PlcProjectDef></TreeItem>", true)]
        [InlineData("<TreeItem><PlcProjectDef><EnableImplicitDefines>false</EnableImplicitDefines></PlcProjectDef></TreeItem>", false)]
        [InlineData("<TreeItem><PlcProjectDef></PlcProjectDef></TreeItem>", false)]
        [InlineData(null, false)]
        public void ImplicitDefinesEnabled_ReadsThePlcItemXml(string xml, bool expected)
        {
            Assert.Equal(expected, VariantSyncEngine.ImplicitDefinesEnabled(xml));
        }

        [Fact]
        public void SyncVariants_SelectsOnlyTheVariantsStage_AndThePlcItemPath()
        {
            string dir = TestProjects.NewTempDir("SyncVariants");
            try
            {
                string sln = TestProjects.Owned(dir, "Shark");
                RunOptions o = TestProjects.Parse(dir, "sync", "variants", TestProjects.Source(dir, "src"), "--project", sln, "--no-config");

                Assert.Equal(SyncStages.Variants, o.Stages);
                Assert.Equal("TIPC^Shark", o.PlcItemPath);
            }
            finally
            {
                System.IO.Directory.Delete(dir, recursive: true);
            }
        }

        [Fact]
        public void FullRun_IncludesTheVariantsStage()
        {
            Assert.True(SyncStages.All.HasFlag(SyncStages.Variants));
        }
    }
}
