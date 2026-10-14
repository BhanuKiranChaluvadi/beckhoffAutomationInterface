using BeckhoffAutomationInterface;
using BeckhoffAutomationInterface.Sync;
using Xunit;

namespace BeckhoffAutomationInterface.Tests
{
    /// <summary>build --variant / --all-variants (tasks/2026-10-14-variants-closed-loop, Task 7).</summary>
    public class VariantBuildPlanTests
    {
        static readonly string[] Two = { "MachineA", "MachineB" };

        [Fact]
        public void NoVariants_NoFlags_IsAPlainBuild()
        {
            Assert.Empty(VariantBuildPlan.Select(new string[0], null, false));
        }

        [Theory]
        [InlineData("MachineA", false)]
        [InlineData(null, true)]
        public void NoVariants_ButVariantRequested_Fails(string variant, bool all)
        {
            var ex = Assert.Throws<ArgumentException>(() => VariantBuildPlan.Select(new string[0], variant, all));

            Assert.Contains("no variants", ex.Message);
        }

        [Fact]
        public void Variants_NoFlags_BuildsAll()
        {
            Assert.Equal(Two, VariantBuildPlan.Select(Two, null, false));
        }

        [Fact]
        public void Variants_AllVariantsFlag_BuildsAll()
        {
            Assert.Equal(Two, VariantBuildPlan.Select(Two, null, true));
        }

        [Fact]
        public void Variant_MatchedCaseInsensitively_ReturnedInProjectSpelling()
        {
            Assert.Equal(new[] { "MachineB" }, VariantBuildPlan.Select(Two, "machineb", false));
        }

        [Fact]
        public void UnknownVariant_FailsListingTheProjectVariants()
        {
            var ex = Assert.Throws<ArgumentException>(() => VariantBuildPlan.Select(Two, "Google", false));

            Assert.Contains("'Google'", ex.Message);
            Assert.Contains("MachineA, MachineB", ex.Message);
        }

        [Fact]
        public void BothFlags_Fail()
        {
            Assert.Throws<ArgumentException>(() => VariantBuildPlan.Select(Two, "MachineA", true));
        }

        [Fact]
        public void Summary_AllPassed()
        {
            string s = VariantBuildPlan.Summary(new[]
            {
                new VariantBuildResult { Variant = "MachineA", Passed = true },
                new VariantBuildResult { Variant = "MachineB", Passed = true },
            });

            Assert.Contains("MachineA  PASSED", s);
            Assert.EndsWith("ALL 2 VARIANT(S) PASSED", s);
        }

        [Fact]
        public void Summary_OneFailedOneTimedOut()
        {
            string s = VariantBuildPlan.Summary(new[]
            {
                new VariantBuildResult { Variant = "MachineA", Passed = false, ErrorCount = 3 },
                new VariantBuildResult { Variant = "MachineB", TimedOut = true },
            });

            Assert.Contains("FAILED (3 error(s))", s);
            Assert.Contains("TIMED OUT", s);
            Assert.EndsWith("2 OF 2 VARIANT(S) FAILED", s);
        }

        [Fact]
        public void PragmaMessages_AreReadFromRealBuildOutput()
        {
            const string output =
                @"C:\D\POUs\MAIN.TcPOU(9) : warning: C0373: VARIANT CHECK: MachineA branch compiled" + "\r\n" +
                @"C:\D\POUs\MAIN.TcPOU(23) : warning: C0373: FEATURE CHECK: PCV_MODBUS driver compiled" + "\r\n" +
                @"C:\D\POUs\MAIN.TcPOU(30) : warning: C0001: some other warning" + "\r\n";

            Assert.Equal(new[] { "VARIANT CHECK: MachineA branch compiled", "FEATURE CHECK: PCV_MODBUS driver compiled" },
                BuildOutputParser.ParsePragmaMessages(output));
        }

        [Fact]
        public void BuildFlags_ReachRunOptions()
        {
            string dir = TestProjects.NewTempDir("BuildFlags");
            try
            {
                string tsproj = TestProjects.Adopted(dir, "P");
                RunOptions o = TestProjects.Parse(dir, "build", tsproj, "--variant", "MachineB", "--no-config");
                RunOptions all = TestProjects.Parse(dir, "build", tsproj, "--all-variants", "--no-config");

                Assert.Equal("MachineB", o.Variant);
                Assert.True(all.AllVariants);
            }
            finally
            {
                System.IO.Directory.Delete(dir, recursive: true);
            }
        }
    }
}
