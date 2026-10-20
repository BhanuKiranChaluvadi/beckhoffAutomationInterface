using System.Collections.Generic;
using BeckhoffAutomationInterface.Sync;
using Xunit;

namespace BeckhoffAutomationInterface.Tests
{
    public class KnownNamesTrackerTests
    {
        static StPouSource Pou(string name, string owner = null) =>
            new StPouSource(name, owner == null ? PouKind.FunctionBlock : PouKind.Method, owner, "decl", null);

        [Fact]
        public void CollectNames_UsesOwnerDotMemberForMembers()
        {
            var names = KnownNamesTracker.CollectNames(new[]
            {
                Pou("FB_Motor"),
                Pou("Init", owner: "FB_Motor"),
                Pou("Reset", owner: "FB_Motor"),
            });

            Assert.Equal(new[] { "FB_Motor", "FB_Motor.Init", "FB_Motor.Reset" }, names);
        }

        [Fact]
        public void DiffFull_ReportsEveryDisappearedName()
        {
            var previous = new[] { "FB_A", "FB_A.Old", "FB_B", "GVL_X" };
            var current = new[] { "FB_A", "FB_A.New", "FB_B" };

            Assert.Equal(new[] { "FB_A.Old", "GVL_X" }, KnownNamesTracker.DiffFull(previous, current));
        }

        [Fact]
        public void ReadWrite_RoundTripsAndReadReturnsNullWhenMissing()
        {
            string path = Path.Combine(Path.GetTempPath(), "KnownNamesTrackerTests_" + Guid.NewGuid() + ".txt");
            try
            {
                Assert.Null(KnownNamesTracker.Read(path));

                KnownNamesTracker.Write(path, new[] { "FB_A", "FB_A.Init" });
                Assert.Equal(new List<string> { "FB_A", "FB_A.Init" }, KnownNamesTracker.Read(path));
            }
            finally
            {
                File.Delete(path);
            }
        }
    }
}
