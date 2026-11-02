using BeckhoffAutomationInterface.Machines;
using Xunit;

namespace BeckhoffAutomationInterface.Tests
{
    public class DevPlanTests
    {
        static readonly string[] Sources = { "Lib", "App/NFLite" };
        const string Tests = "Tests/Integration/NFLite";
        static readonly string[] Manifests = { "Machines/NFLite", "bai.machines.json" };

        static DevStep For(params string[] changed) => DevPlan.For(changed, Sources, Tests, Manifests);

        [Fact]
        public void ChangedStFile_Rebuilds()
        {
            Assert.Equal(DevStep.Rebuild, For("Lib/Sequences/FB_GasStartSequence.st"));
            Assert.Equal(DevStep.Rebuild, For(@"App\NFLite\PRG_NFLite.st"));
        }

        [Fact]
        public void ChangedScenarioOnly_RunsTheTestsAgain()
        {
            Assert.Equal(DevStep.TestsOnly, For("Tests/Integration/NFLite/simulation/ozone-level.json"));
        }

        [Fact]
        public void ChangedManifest_Stops_EvenWithCode()
        {
            Assert.Equal(DevStep.Stop, For("Lib/A.st", "Machines/NFLite/Google/io-devices.xml"));
            Assert.Equal(DevStep.Stop, For("bai.machines.json"));
        }

        [Fact]
        public void CodeAndScenario_Rebuilds()
        {
            Assert.Equal(DevStep.Rebuild, For("Tests/Integration/NFLite/a.json", "Lib/A.st"));
        }

        [Fact]
        public void OtherFiles_DoNothing()
        {
            Assert.Equal(DevStep.None, For());
            Assert.Equal(DevStep.None, For("Lib/README.md", "Documentation/x.st", "App/NFLiteOld/X.st",
                "Tests/Integration/NFPro/a.json", ".metrics/crap.edn"));
        }
    }
}
