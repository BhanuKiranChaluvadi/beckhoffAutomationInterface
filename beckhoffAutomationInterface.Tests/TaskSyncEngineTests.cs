using System.Collections.Generic;
using System.Xml.Linq;
using BeckhoffAutomationInterface.Sync;
using Xunit;

namespace BeckhoffAutomationInterface.Tests
{
    /// <summary>Tasks stage, COM-free parts (BeckhoffLib tasks/2026-10-16-plc-release, M6b).</summary>
    public class TaskSyncEngineTests
    {
        // Captured from TwinCAT 3.1.4026 in the M6a spike (TIRT^PlcTask ProduceXml; module instance cut).
        const string PlcTaskXml =
            "<?xml version=\"1.0\"?><TreeItem><ItemName>PlcTask</ItemName><PathName>TIRT^PlcTask</PathName><ItemType>1</ItemType>" +
            "<ItemId>3</ItemId><ObjectId>#x02010030</ObjectId><ItemSubType>1</ItemSubType><ChildCount>0</ChildCount><Disabled>false</Disabled>" +
            "<TreeImageId>4</TreeImageId><TaskDef><Priority>20</Priority><AmsPort>350</AmsPort><CycleTime>100000</CycleTime><AutoStart>true</AutoStart>" +
            "<IoAtBegin>false</IoAtBegin><ExceedWarning>0</ExceedWarning><CreateSymbols>false</CreateSymbols><ExtSync>false</ExtSync><TickModulo>0</TickModulo>" +
            "<InputUpdatePreTicks>0</InputUpdatePreTicks><DisableFpExceptions>false</DisableFpExceptions><WatchdogStackCapacity>0</WatchdogStackCapacity>" +
            "<CpuAffinity>#x0</CpuAffinity></TaskDef><TcModuleInstance><DataTypes/></TcModuleInstance></TreeItem>";

        static readonly TaskManifest Shark = TaskManifestParser.Parse(XDocument.Parse(
            "<Tasks><Task Name=\"PlcTask\" CycleTimeUs=\"10000\" Priority=\"20\"><Call>MAIN</Call></Task>" +
            "<Task Name=\"SpectrometerTask\" CycleTimeUs=\"1000\" Priority=\"2\"><Call>PRG_Spectrometer</Call></Task></Tasks>"), "tasks.xml");

        // What the PLC template makes (init): PlcTask, 10 ms, priority 20, calls MAIN.
        static readonly RtTaskState TemplateTask = new RtTaskState("PlcTask", 100000, 20);
        static readonly PlcTaskRefState TemplateRef = new PlcTaskRefState("PlcTask", "TIRT^PlcTask", new[] { "MAIN" });

        static TaskSyncPlan Plan(IEnumerable<RtTaskState> rt, IEnumerable<PlcTaskRefState> refs, TaskManifest m = null) =>
            TaskSyncEngine.Plan(m ?? Shark, rt.ToList(), refs.ToList());

        [Fact]
        public void ReadTaskDef_RealXml_CycleTimeIn100ns()
        {
            (int cycle, int priority) = TaskSyncEngine.ReadTaskDef(PlcTaskXml);

            Assert.Equal(100000, cycle); // 10 ms
            Assert.Equal(20, priority);
        }

        [Fact]
        public void TaskDefXml_IsWhatTheSpikeConsumed()
        {
            Assert.Equal("<TreeItem><TaskDef><CycleTime>10000</CycleTime><Priority>2</Priority></TaskDef></TreeItem>",
                TaskSyncEngine.TaskDefXml(Shark.Tasks[1]));
        }

        [Fact]
        public void FreshProject_CreatesTheSecondTaskAndItsReference_LeavesPlcTask()
        {
            TaskSyncPlan plan = Plan(new[] { TemplateTask }, new[] { TemplateRef });

            Assert.Equal(new[] { TaskActionKind.CreateRtTask, TaskActionKind.CreateTaskRef }, plan.Actions.Select(a => a.Kind));
            Assert.All(plan.Actions, a => Assert.Equal("SpectrometerTask", a.Task.Name));
            Assert.Equal("PRG_Spectrometer", plan.Actions[1].Program);
            Assert.Equal("+ task     TIRT^SpectrometerTask (1000 µs, priority 2)", plan.Actions[0].Description);
            Assert.Empty(plan.Warnings);
        }

        [Fact]
        public void SecondRun_ChangesNothing()
        {
            TaskSyncPlan plan = Plan(
                new[] { TemplateTask, new RtTaskState("SpectrometerTask", 10000, 2) },
                new[] { TemplateRef, new PlcTaskRefState("SpectrometerTask", "TIRT^SpectrometerTask", new[] { "PRG_Spectrometer" }) });

            Assert.Empty(plan.Actions);
            Assert.Empty(plan.Warnings);
        }

        [Fact]
        public void NamesAreCaseInsensitive_AsInTwinCAT()
        {
            TaskSyncPlan plan = Plan(
                new[] { TemplateTask, new RtTaskState("spectrometertask", 10000, 2) },
                new[] { TemplateRef, new PlcTaskRefState("SPECTROMETERTASK", "tirt^spectrometertask", new[] { "prg_spectrometer" }) });

            Assert.Empty(plan.Actions);
        }

        [Fact]
        public void WrongCycleTimeOrPriority_SetsTheTaskDef()
        {
            TaskSyncPlan plan = Plan(
                new[] { TemplateTask, new RtTaskState("SpectrometerTask", 100000, 3) },
                new[] { TemplateRef, new PlcTaskRefState("SpectrometerTask", "TIRT^SpectrometerTask", new[] { "PRG_Spectrometer" }) });

            TaskAction a = Assert.Single(plan.Actions);
            Assert.Equal(TaskActionKind.SetTaskDef, a.Kind);
            Assert.Equal("~ task     TIRT^SpectrometerTask: 10000 µs, priority 3 -> 1000 µs, priority 2", a.Description);
        }

        [Fact]
        public void ReferenceLinkedToAnotherTask_IsRelinked()
        {
            TaskSyncPlan plan = Plan(
                new[] { TemplateTask, new RtTaskState("SpectrometerTask", 10000, 2) },
                new[] { TemplateRef, new PlcTaskRefState("SpectrometerTask", "TIRT^PlcTask", new[] { "PRG_Spectrometer" }) });

            Assert.Equal(TaskActionKind.SetLinkedTask, Assert.Single(plan.Actions).Kind);
        }

        [Fact]
        public void MissingCall_IsAdded_ExtraCall_IsOnlyReported()
        {
            TaskSyncPlan plan = Plan(
                new[] { TemplateTask, new RtTaskState("SpectrometerTask", 10000, 2) },
                new[] { TemplateRef, new PlcTaskRefState("SpectrometerTask", "TIRT^SpectrometerTask", new[] { "PRG_Old" }) });

            TaskAction a = Assert.Single(plan.Actions);
            Assert.Equal(TaskActionKind.AddCall, a.Kind);
            Assert.Equal("PRG_Spectrometer", a.Program);
            Assert.Contains(plan.Warnings, w => w.Contains("task 'SpectrometerTask' also calls 'PRG_Old'") && w.Contains("left unchanged"));
        }

        [Fact]
        public void NewReferenceWithTwoCalls_CreatesWithTheFirst_AddsTheSecond()
        {
            TaskManifest m = TaskManifestParser.Parse(XDocument.Parse(
                "<Tasks><Task Name=\"PlcTask\" CycleTimeUs=\"10000\" Priority=\"20\"><Call>MAIN</Call></Task>" +
                "<Task Name=\"Fast\" CycleTimeUs=\"1000\" Priority=\"2\"><Call>PRG_A</Call><Call>PRG_B</Call></Task></Tasks>"), "tasks.xml");

            TaskSyncPlan plan = Plan(new[] { TemplateTask }, new[] { TemplateRef }, m);

            Assert.Equal(new[] { TaskActionKind.CreateRtTask, TaskActionKind.CreateTaskRef, TaskActionKind.AddCall }, plan.Actions.Select(a => a.Kind));
            Assert.Equal(new[] { null, "PRG_A", "PRG_B" }, plan.Actions.Select(a => a.Program));
        }

        [Fact]
        public void CallOrder_DifferentFromTheManifest_IsReported()
        {
            TaskManifest m = TaskManifestParser.Parse(XDocument.Parse(
                "<Tasks><Task Name=\"PlcTask\" CycleTimeUs=\"10000\" Priority=\"20\"><Call>MAIN</Call><Call>PRG_Late</Call></Task></Tasks>"), "tasks.xml");

            TaskSyncPlan plan = Plan(new[] { TemplateTask }, new[] { new PlcTaskRefState("PlcTask", "TIRT^PlcTask", new[] { "PRG_Late", "MAIN" }) }, m);

            Assert.Empty(plan.Actions);
            Assert.Contains(plan.Warnings, w => w.Contains("in this order: PRG_Late, MAIN") && w.Contains("tasks.xml has MAIN, PRG_Late"));
        }

        [Fact]
        public void TasksNotInTheManifest_AreReported_NeverDeleted()
        {
            // UVM-like manifest with PlcTask only, against a project that still has the 1 ms task.
            TaskManifest m = TaskManifestParser.Parse(XDocument.Parse(
                "<Tasks><Task Name=\"PlcTask\" CycleTimeUs=\"10000\" Priority=\"20\"><Call>MAIN</Call></Task></Tasks>"), "tasks.xml");

            TaskSyncPlan plan = Plan(
                new[] { TemplateTask, new RtTaskState("SpectrometerTask", 10000, 2) },
                new[] { TemplateRef, new PlcTaskRefState("SpectrometerTask", "TIRT^SpectrometerTask", new[] { "PRG_Spectrometer" }) }, m);

            Assert.Empty(plan.Actions);
            Assert.Contains(plan.Warnings, w => w.Contains("real-time task 'TIRT^SpectrometerTask' (1000 µs, priority 2) is not in tasks.xml"));
            Assert.Contains(plan.Warnings, w => w.Contains("PLC task 'SpectrometerTask' (calls PRG_Spectrometer) is not in tasks.xml"));
        }

        [Fact]
        public void ManifestWithoutPlcTask_ReportsTheTemplateTask()
        {
            TaskManifest m = TaskManifestParser.Parse(XDocument.Parse(
                "<Tasks><Task Name=\"SpectrometerTask\" CycleTimeUs=\"1000\" Priority=\"2\"><Call>PRG_Spectrometer</Call></Task></Tasks>"), "tasks.xml");

            TaskSyncPlan plan = Plan(new[] { TemplateTask }, new[] { TemplateRef }, m);

            Assert.Equal(2, plan.Actions.Count);
            Assert.Contains(plan.Warnings, w => w.Contains("'TIRT^PlcTask' (10000 µs, priority 20) is not in tasks.xml"));
        }

        [Fact]
        public void ExtraTaskWithTheSamePriority_IsReported()
        {
            TaskSyncPlan plan = Plan(new[] { TemplateTask, new RtTaskState("Other", 10000, 2) }, new[] { TemplateRef });

            Assert.Contains(plan.Warnings, w => w.Contains("real-time task 'Other' has priority 2, the same as task 'SpectrometerTask'"));
        }
    }
}
