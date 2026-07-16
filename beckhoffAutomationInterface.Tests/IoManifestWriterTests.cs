using BeckhoffAutomationInterface.Sync;
using Xunit;

namespace BeckhoffAutomationInterface.Tests
{
    /// <summary>
    /// The COM-free core of IoManifestWriter — DeriveProduct(name, itemSubTypeName, ...),
    /// the one piece with no exact read-back API. Test data is real device Names taken
    /// directly from the live PLC_NFL_SHARK_V2 production project's on-disk .xti item
    /// names (tasks/2026-07-15-reverse-export-scaffold/, live validation 2026-07-16) —
    /// not synthetic examples — including the case that disproved a pure regex-code
    /// approach: "Box 40 (EX260-SEC1)" would truncate to "EX260" under a bare
    /// "prefix+digits" regex, which is wrong; the whole hyphenated string is the real
    /// Festo catalog product.
    /// </summary>
    public class IoManifestWriterTests
    {
        [Theory]
        [InlineData("Term 13 (EL3174)", "EL3174")]
        [InlineData("Box 1 (CU2508)", "CU2508")]
        [InlineData("Term 12 (EK1100)", "EK1100")]
        [InlineData("Box 40 (EX260-SEC1)", "EX260-SEC1")] // hyphenated non-Beckhoff catalog string, NOT truncated to "EX260"
        [InlineData("Device 1 (RT-Ethernet Adapter)", "RT-Ethernet Adapter")] // multi-word catalog string, used verbatim
        public void DeriveProduct_PrefersTrailingParenthetical_UsedVerbatim_NotHeuristic(string name, string expectedProduct)
        {
            string product = IoManifestWriter.DeriveProduct(name, itemSubTypeName: "irrelevant", out bool heuristic);

            Assert.Equal(expectedProduct, product);
            Assert.False(heuristic); // Name's parenthetical is TwinCAT's own confirmed convention
        }

        [Theory]
        [InlineData("EK1100_1.1", "EK1100")]
        [InlineData("EL2008_1.6", "EL2008")]
        [InlineData("EL9011_1.1", "EL9011")]
        [InlineData("EL9505_1.1", "EL9505")]
        public void DeriveProduct_FallsBackToCodeEmbeddedInName_WhenNoParenthetical(string name, string expectedProduct)
        {
            string product = IoManifestWriter.DeriveProduct(name, itemSubTypeName: "irrelevant", out bool heuristic);

            Assert.Equal(expectedProduct, product);
            Assert.True(heuristic); // no parenthetical — this is a fallback guess, flagged for review
        }

        [Fact]
        public void DeriveProduct_FlagsPureCustomLabelWithNoProductInfo_ForReview()
        {
            // "BH1" — a real purely-organizational label from the production project,
            // carrying no product information at all.
            string product = IoManifestWriter.DeriveProduct("BH1", itemSubTypeName: "", out bool heuristic);

            Assert.True(heuristic);
        }

        [Fact]
        public void DeriveProduct_FallsBackToItemSubTypeName_WhenNameHasNoUsableInfo()
        {
            string product = IoManifestWriter.DeriveProduct("BH2", "EL1008 8Ch. Dig. Input 24V, 3ms", out bool heuristic);

            Assert.Equal("EL1008", product); // code-shape match against ItemSubTypeName
            Assert.True(heuristic);
        }
    }
}
