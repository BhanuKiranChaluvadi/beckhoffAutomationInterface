using BeckhoffAutomationInterface.Sync;
using Xunit;

namespace BeckhoffAutomationInterface.Tests
{
    /// <summary>
    /// The COM-free core of IoManifestWriter — the product-code heuristic (the one piece
    /// with no exact API; see the class doc comment and the open Task 2 spike). The tree
    /// walk itself needs a live ITcSmTreeItem and is verified via the round-trip smoke.
    /// </summary>
    public class IoManifestWriterTests
    {
        [Theory]
        [InlineData("EL1008 8Ch. Dig. Input 24V, 3ms", "EL1008")]
        [InlineData("EK1100 EtherCAT-Koppler (2A E-Bus)", "EK1100")]
        [InlineData("CU2508 (Realtime Ethernet Port Multiplier)", "CU2508")]
        [InlineData("EL3174-0002 4Ch. Ana. Input", "EL3174-0002")]
        public void ExtractProductCode_PullsCleanBeckhoffCode(string subTypeName, string expected)
        {
            string product = IoManifestWriter.ExtractProductCode(subTypeName, out bool heuristic);

            Assert.Equal(expected, product);
            Assert.False(heuristic); // a clean product-code match is NOT flagged for review
        }

        [Theory]
        [InlineData("Some Custom Device")]
        [InlineData("")]
        public void ExtractProductCode_FlagsUnrecognizedForReview(string subTypeName)
        {
            IoManifestWriter.ExtractProductCode(subTypeName, out bool heuristic);

            Assert.True(heuristic);
        }
    }
}
