using System.Collections.Generic;
using BeckhoffAutomationInterface.Sync;
using Xunit;

namespace BeckhoffAutomationInterface.Tests
{
    /// <summary>Per-variant I/O sync, COM-free parts (tasks/2026-10-14-variants-closed-loop, Task 10).</summary>
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
        public void Plan_PerVariantItemMissingFromTheManifest_IsOnlyWarnedAbout()
        {
            IoVariantPlan plan = IoVariantSync.Plan(IoVariantSync.ReadState(RealXml), new IoVariantItem[0], Variants);

            Assert.Empty(plan.Changes);
            Assert.Contains(plan.Warnings, w => w.Contains(El2008) && w.Contains("left unchanged"));
        }
    }
}
