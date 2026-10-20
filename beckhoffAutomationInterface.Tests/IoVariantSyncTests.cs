using System.Collections.Generic;
using BeckhoffAutomationInterface.Sync;
using Xunit;

namespace BeckhoffAutomationInterface.Tests
{
    /// <summary>Per-variant I/O sync, COM-free parts.</summary>
    public class IoVariantSyncTests
    {
        const string El2008 = "TIID^Device 1 (EtherCAT)^Term 1 (EK1100)^Term 3 (EL2008)";

        // Captured from TwinCAT 3.1.4026 in the Task 8 spike (ITcSysManager14.ProjectVariantItems).
        const string RealXml =
            "<?xml version=\"1.0\"?><ProjectVariants>" +
            "<Item Name=\"TIPC^VariantSample\" PvUseDefine=\"true\"><Variant><Name>MachineA</Name><Name>MachineB</Name><Defines/></Variant></Item>" +
            "<Item Name=\"TIPC^VariantSample^VariantSample Instance^PlcTask Outputs^GVL_IO.bOut\" PvUseLink=\"true\"/>" +
            "<Item Name=\"" + El2008 + "\" PvUseDisable=\"true\">" +
            "<Variant><Name>MachineA</Name><Disable>false</Disable><PortABoxInfo>#x01000002</PortABoxInfo></Variant>" +
            "<Variant><Name>MachineB</Name><Disable>true</Disable><PortABoxInfo>#x01000002</PortABoxInfo></Variant></Item>" +
            "</ProjectVariants>";

        static readonly string[] Variants = { "MachineA", "MachineB" };

        [Fact]
        public void ReadState_TakesOnlyPerVariantIoItems()
        {
            var state = IoVariantSync.ReadState(RealXml);

            Assert.Single(state); // the PLC define item and the PvUseLink item are not I/O disable items
            Assert.False(state[El2008]["MachineA"]);
            Assert.True(state[El2008]["MachineB"]);
        }

        [Fact]
        public void ReadState_EmptyProject()
        {
            Assert.Empty(IoVariantSync.ReadState(null));
            Assert.Empty(IoVariantSync.ReadState("<?xml version=\"1.0\"?><ProjectVariants/>"));
        }

        [Fact]
        public void Plan_AlreadyInPlace_ChangesNothing()
        {
            IoVariantPlan plan = IoVariantSync.Plan(IoVariantSync.ReadState(RealXml),
                new[] { new IoVariantItem(El2008, new[] { "MachineA" }) }, Variants);

            Assert.Empty(plan.Changes);
            Assert.Empty(plan.Warnings);
        }

        [Fact]
        public void Plan_NewItem_DisablesItWhereItIsAbsent()
        {
            IoVariantPlan plan = IoVariantSync.Plan(new Dictionary<string, Dictionary<string, bool>>(),
                new[] { new IoVariantItem(El2008, new[] { "MachineA" }) }, Variants);

            IoVariantState change = Assert.Single(plan.Changes);
            Assert.False(change.DisabledByVariant["MachineA"]);
            Assert.True(change.DisabledByVariant["MachineB"]);
        }

        [Fact]
        public void Plan_MovedToTheOtherVariant_IsAChange()
        {
            IoVariantPlan plan = IoVariantSync.Plan(IoVariantSync.ReadState(RealXml),
                new[] { new IoVariantItem(El2008, new[] { "MachineB" }) }, Variants);

            IoVariantState change = Assert.Single(plan.Changes);
            Assert.True(change.DisabledByVariant["MachineA"]);
            Assert.False(change.DisabledByVariant["MachineB"]);
        }

        [Fact]
        public void Plan_NewProjectVariant_IsAChange()
        {
            // A third variant was added to variants.xml: the item must get a state for it.
            IoVariantPlan plan = IoVariantSync.Plan(IoVariantSync.ReadState(RealXml),
                new[] { new IoVariantItem(El2008, new[] { "MachineA" }) }, new[] { "MachineA", "MachineB", "MachineC" });

            Assert.True(Assert.Single(plan.Changes).DisabledByVariant["MachineC"]);
        }

        [Fact]
        public void Export_WritesTheVariantsThatHaveTheItem()
        {
            Dictionary<string, List<string>> map = IoVariantSync.VariantsByPath(IoVariantSync.ReadState(RealXml), Variants);

            Assert.Equal(new[] { "MachineA" }, map[El2008]);
        }

        [Fact]
        public void Export_ItemEnabledEverywhere_NeedsNoAttribute()
        {
            var state = new Dictionary<string, Dictionary<string, bool>>
            {
                [El2008] = new Dictionary<string, bool> { ["MachineA"] = false, ["MachineB"] = false },
            };

            Assert.Empty(IoVariantSync.VariantsByPath(state, Variants));
        }

        [Fact]
        public void Export_Then_Plan_IsANoOp()
        {
            // Round trip on the model: what export writes is exactly what sync would keep.
            var state = IoVariantSync.ReadState(RealXml);
            var exported = IoVariantSync.VariantsByPath(state, Variants);
            var items = new List<IoVariantItem>();
            foreach (var e in exported)
                items.Add(new IoVariantItem(e.Key, e.Value));

            Assert.Empty(IoVariantSync.Plan(state, items, Variants).Changes);
        }

        [Fact]
        public void Plan_PerVariantItemMissingFromTheManifest_IsOnlyWarnedAbout()
        {
            IoVariantPlan plan = IoVariantSync.Plan(IoVariantSync.ReadState(RealXml), new IoVariantItem[0], Variants);

            Assert.Empty(plan.Changes);
            Assert.Contains(plan.Warnings, w => w.Contains(El2008) && w.Contains("left unchanged"));
        }
    }
}
