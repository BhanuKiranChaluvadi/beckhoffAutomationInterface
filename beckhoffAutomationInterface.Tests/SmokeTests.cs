using BeckhoffAutomationInterface.Sync;
using Xunit;

namespace BeckhoffAutomationInterface.Tests
{
    public class SmokeTests
    {
        [Fact]
        public void InternalTypesAreVisibleToTests()
        {
            var ignore = IgnoreRules.Load(".", new string[0]);
            Assert.False(ignore.IsIgnored("FB_Anything.st"));
        }
    }
}
