using System.Xml.Linq;
using BeckhoffAutomationInterface.Sync;
using Xunit;

namespace BeckhoffAutomationInterface.Tests
{
    /// <summary>tasks.xml (BeckhoffLib tasks/2026-10-16-plc-release, M6b).</summary>
    public class TaskManifestTests
    {
        const string Shark =
            "<Tasks>\n" +
            "  <Task Name=\"PlcTask\" CycleTimeUs=\"10000\" Priority=\"20\"><Call>MAIN</Call></Task>\n" +
            "  <Task Name=\"SpectrometerTask\" CycleTimeUs=\"1000\" Priority=\"2\"><Call>PRG_Spectrometer</Call></Task>\n" +
            "</Tasks>";

        static TaskManifest Parse(string xml) => TaskManifestParser.Parse(XDocument.Parse(xml, LoadOptions.SetLineInfo), "tasks.xml");

        static string Problem(string xml) => Assert.Throws<TaskManifestException>(() => Parse(xml)).Message;

        [Fact]
        public void NoFile_KeepsTheTasksOfTheProject()
        {
            Assert.Null(TaskManifestParser.Parse(Path.Combine(Path.GetTempPath(), "no-such-folder-" + Guid.NewGuid(), "tasks.xml")));
        }

        [Fact]
        public void Shark_TwoTasks()
        {
            TaskManifest m = Parse(Shark);

            Assert.Equal(1000, m.BaseTimeUs);
            Assert.Equal(new[] { "PlcTask", "SpectrometerTask" }, m.Tasks.Select(t => t.Name));
            TaskSpec spec = m.Tasks[1];
            Assert.Equal(1000, spec.CycleTimeUs);
            Assert.Equal(10000, spec.CycleTime100ns); // TaskDef/CycleTime is in 100 ns
            Assert.Equal(2, spec.Priority);
            Assert.Equal(new[] { "PRG_Spectrometer" }, spec.Calls);
            Assert.Equal(new[] { "MAIN" }, m.Tasks[0].Calls);
        }

        [Fact]
        public void ATaskMayCallMorePrograms_InOrder()
        {
            TaskManifest m = Parse("<Tasks><Task Name=\"PlcTask\" CycleTimeUs=\"10000\" Priority=\"20\"><Call>MAIN</Call><Call>PRG_Late</Call></Task></Tasks>");

            Assert.Equal(new[] { "MAIN", "PRG_Late" }, m.Tasks[0].Calls);
        }

        [Fact]
        public void WrongRoot()
        {
            Assert.Contains("root element must be <Tasks>", Problem("<Task Name=\"PlcTask\" />"));
        }

        [Fact]
        public void NoTask()
        {
            Assert.Contains("no <Task>", Problem("<Tasks />"));
        }

        [Fact]
        public void DuplicateName_IsCaseInsensitive()
        {
            string p = Problem("<Tasks>\n<Task Name=\"PlcTask\" CycleTimeUs=\"10000\" Priority=\"20\"><Call>MAIN</Call></Task>\n" +
                               "<Task Name=\"plctask\" CycleTimeUs=\"1000\" Priority=\"2\"><Call>PRG_B</Call></Task>\n</Tasks>");

            Assert.Contains("line 3: task 'plctask' is already defined on line 2", p);
        }

        [Fact]
        public void DuplicatePriority()
        {
            string p = Problem("<Tasks><Task Name=\"A\" CycleTimeUs=\"10000\" Priority=\"20\"><Call>MAIN</Call></Task>" +
                               "<Task Name=\"B\" CycleTimeUs=\"1000\" Priority=\"20\"><Call>PRG_B</Call></Task></Tasks>");

            Assert.Contains("Priority 20 is already used by task 'A'", p);
        }

        [Theory]
        [InlineData("CycleTimeUs=\"0\"", "CycleTimeUs '0' must be a whole number > 0")]
        [InlineData("CycleTimeUs=\"-1000\"", "CycleTimeUs '-1000' must be a whole number > 0")]
        [InlineData("CycleTimeUs=\"1.5\"", "CycleTimeUs '1.5' must be a whole number > 0")]
        [InlineData("CycleTimeUs=\"1500\"", "CycleTimeUs 1500 is not a multiple of the base time 1000 µs")]
        [InlineData("", "CycleTimeUs is missing")]
        public void CycleTime(string attribute, string expected)
        {
            Assert.Contains(expected, Problem("<Tasks><Task Name=\"A\" " + attribute + " Priority=\"20\"><Call>MAIN</Call></Task></Tasks>"));
        }

        [Fact]
        public void BaseTime_FromTheRoot()
        {
            TaskManifest m = Parse("<Tasks BaseTimeUs=\"500\"><Task Name=\"A\" CycleTimeUs=\"1500\" Priority=\"20\"><Call>MAIN</Call></Task></Tasks>");

            Assert.Equal(500, m.BaseTimeUs);
            Assert.Equal(1500, m.Tasks[0].CycleTimeUs);
            Assert.Contains("CycleTimeUs 700 is not a multiple of the base time 500 µs",
                Problem("<Tasks BaseTimeUs=\"500\"><Task Name=\"A\" CycleTimeUs=\"700\" Priority=\"20\"><Call>MAIN</Call></Task></Tasks>"));
        }

        [Theory]
        [InlineData("0", "Priority '0' must be a whole number > 0")]
        [InlineData("256", "Priority 256 is out of range 1..255")]
        [InlineData("x", "Priority 'x' must be a whole number > 0")]
        public void Priority(string value, string expected)
        {
            Assert.Contains(expected, Problem("<Tasks><Task Name=\"A\" CycleTimeUs=\"1000\" Priority=\"" + value + "\"><Call>MAIN</Call></Task></Tasks>"));
        }

        [Theory]
        [InlineData("", "Name is missing")]
        [InlineData("Spectrometer Task", "Name 'Spectrometer Task' is not valid")]
        [InlineData("1Task", "Name '1Task' is not valid")]
        public void Name(string name, string expected)
        {
            Assert.Contains(expected, Problem("<Tasks><Task Name=\"" + name + "\" CycleTimeUs=\"1000\" Priority=\"2\"><Call>MAIN</Call></Task></Tasks>"));
        }

        [Fact]
        public void TaskWithoutCall()
        {
            Assert.Contains("task 'A' has no <Call>", Problem("<Tasks><Task Name=\"A\" CycleTimeUs=\"1000\" Priority=\"2\" /></Tasks>"));
        }

        [Fact]
        public void AProgramInTwoTasks_IsRefused()
        {
            // The bug of the PLC_NFL_SHARK_V2 project: PRG_Spectrometer called by PlcTask and SpectrometerTask.
            string p = Problem("<Tasks><Task Name=\"PlcTask\" CycleTimeUs=\"10000\" Priority=\"20\"><Call>MAIN</Call><Call>PRG_Spectrometer</Call></Task>" +
                               "<Task Name=\"SpectrometerTask\" CycleTimeUs=\"1000\" Priority=\"2\"><Call>PRG_Spectrometer</Call></Task></Tasks>");

            Assert.Contains("program 'PRG_Spectrometer' is already called by task 'PlcTask'", p);
        }

        [Fact]
        public void UnknownElements()
        {
            string p = Problem("<Tasks><Program Name=\"A\" /><Task Name=\"B\" CycleTimeUs=\"1000\" Priority=\"2\"><Program>MAIN</Program></Task></Tasks>");

            Assert.Contains("unknown element <Program> (use <Task>)", p);
            Assert.Contains("unknown element <Program> in <Task> (use <Call>)", p);
        }

        [Fact]
        public void AllProblemsAtOnce()
        {
            string p = Problem("<Tasks><Task Name=\"\" CycleTimeUs=\"0\" Priority=\"0\" /></Tasks>");

            Assert.Contains("Name is missing", p);
            Assert.Contains("CycleTimeUs '0'", p);
            Assert.Contains("Priority '0'", p);
            Assert.Contains("has no <Call>", p);
        }

        [Fact]
        public void CheckPrograms_EveryCallMustBeAProgramOfTheSource()
        {
            TaskManifest m = Parse(Shark);

            Assert.Empty(TaskManifestParser.CheckPrograms(m, new[] { "main", "PRG_Spectrometer", "PRG_NFLite" }, "tasks.xml"));
            Assert.Equal(new[] { "tasks.xml: task 'SpectrometerTask' calls 'PRG_Spectrometer', but the source has no PROGRAM 'PRG_Spectrometer'" },
                TaskManifestParser.CheckPrograms(m, new[] { "MAIN" }, "tasks.xml"));
        }
    }
}
