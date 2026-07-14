using BeckhoffAutomationInterface.Sync;
using Xunit;

namespace BeckhoffAutomationInterface.Tests
{
    public class StLinterTests
    {
        [Fact]
        public void FunctionBlock_WithoutExpectedPrefix_ReportsOneIssue()
        {
            var sources = new List<StPouSource>
            {
                new StPouSource("Motor", PouKind.FunctionBlock, null, "FUNCTION_BLOCK Motor", ""),
            };

            List<string> issues = StLinter.Lint(sources);

            string issue = Assert.Single(issues);
            Assert.Contains("FB_", issue);
        }

        [Fact]
        public void FunctionBlock_WithExpectedPrefix_ReportsNoIssues()
        {
            var sources = new List<StPouSource>
            {
                new StPouSource("FB_Motor", PouKind.FunctionBlock, null, "FUNCTION_BLOCK FB_Motor", ""),
            };

            Assert.Empty(StLinter.Lint(sources));
        }

        [Fact]
        public void MethodsAndProperties_AreNeverLinted_RegardlessOfName()
        {
            var sources = new List<StPouSource>
            {
                new StPouSource("init", PouKind.Method, "FB_Motor", "METHOD init", ""),
                new StPouSource("speed", PouKind.Property, "FB_Motor", "PROPERTY speed : LREAL", null),
            };

            Assert.Empty(StLinter.Lint(sources));
        }
    }
}
