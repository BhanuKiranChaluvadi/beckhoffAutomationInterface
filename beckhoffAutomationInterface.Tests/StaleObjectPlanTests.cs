using System;
using System.Collections.Generic;
using System.Linq;
using BeckhoffAutomationInterface.Sync;
using Xunit;

namespace BeckhoffAutomationInterface.Tests
{
    public class StaleObjectPlanTests
    {
        const string Root = "TIPC^P^P Project";

        static StPouSource Top(string name, string folder, PouKind kind = PouKind.FunctionBlock) =>
            new StPouSource(name, kind, null, "decl", null) { RelativeFolder = folder };

        static StPouSource Member(string owner, string name) =>
            new StPouSource(name, PouKind.Method, owner, "decl", null);

        static IReadOnlyDictionary<string, IReadOnlyList<string>> Live(params (string Name, string Path)[] items) =>
            items.GroupBy(i => i.Name, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => (IReadOnlyList<string>)g.Select(i => i.Path).ToList(), StringComparer.OrdinalIgnoreCase);

        static string[] Steps(StaleObjectPlan plan) =>
            plan.Steps.Select(s => s.ParentPath + " | " + s.ChildName).ToArray();

        [Fact]
        public void DeletedObject_IsDeletedAtItsLivePath()
        {
            var plan = StaleObjectPlan.Build(new[] { "FB_A", "FB_Gone" }, new[] { Top("FB_A", "Lib") },
                Live(("FB_A", Root + "^Lib^FB_A"), ("FB_Gone", Root + "^Lib^Old^FB_Gone")), Root);

            Assert.Equal(new[] { Root + "^Lib^Old | FB_Gone" }, Steps(plan));
            Assert.Equal("FB_Gone (gone)", plan.Steps[0].Display);
            Assert.Empty(plan.Unresolved);
        }

        [Fact]
        public void DeletedOwner_DeletesOnlyTheOwner_NotItsMembers()
        {
            var plan = StaleObjectPlan.Build(new[] { "FB_Gone", "FB_Gone.Init", "FB_Gone.Reset" }, new StPouSource[0],
                Live(("FB_Gone", Root + "^FB_Gone")), Root);

            Assert.Equal(new[] { Root + " | FB_Gone" }, Steps(plan));
        }

        [Fact]
        public void DeletedMemberOfAKeptOwner_IsDeletedUnderTheOwner()
        {
            var plan = StaleObjectPlan.Build(new[] { "FB_A", "FB_A.Old", "FB_A.Keep" },
                new[] { Top("FB_A", "Lib"), Member("FB_A", "Keep") },
                Live(("FB_A", Root + "^Lib^FB_A")), Root);

            Assert.Equal(new[] { Root + "^Lib^FB_A | Old" }, Steps(plan));
            Assert.Equal("FB_A.Old (gone)", plan.Steps[0].Display);
        }

        [Fact]
        public void StaleNameThatIsNoLongerInTheProject_IsSkipped()
        {
            var plan = StaleObjectPlan.Build(new[] { "FB_Gone", "FB_A.Old" }, new[] { Top("FB_A", "") },
                Live(("FB_A", Root + "^FB_A")), Root);

            Assert.Equal(new[] { Root + "^FB_A | Old" }, Steps(plan));
            Assert.Empty(plan.Unresolved);
        }

        [Fact]
        public void TwoLiveObjectsWithTheStaleName_AreUnresolved()
        {
            var plan = StaleObjectPlan.Build(new[] { "FB_Gone" }, new StPouSource[0],
                Live(("FB_Gone", Root + "^A^FB_Gone"), ("FB_Gone", Root + "^B^FB_Gone")), Root);

            Assert.Empty(plan.Steps);
            Assert.Equal(new[] { "FB_Gone" }, plan.Unresolved);
        }

        [Fact]
        public void MovedObject_IsDeletedAtTheOldPath_AndItsMembersAreLeftToTheSync()
        {
            var plan = StaleObjectPlan.Build(new[] { "FB_M", "FB_M.Init" },
                new[] { Top("FB_M", "App/B"), Member("FB_M", "Init") },
                Live(("FB_M", Root + "^App^A^FB_M")), Root);

            Assert.Equal(new[] { Root + "^App^A | FB_M" }, Steps(plan));
            Assert.Equal("FB_M (moved from App/A)", plan.Steps[0].Display);
        }

        [Fact]
        public void Program_IsNeverMoved_TheSyncUpdatesItInPlace()
        {
            var plan = StaleObjectPlan.Build(new[] { "MAIN" }, new[] { Top("MAIN", "", PouKind.Program) },
                Live(("MAIN", Root + "^POUs^MAIN")), Root);

            Assert.Empty(plan.Steps);
        }

        [Fact]
        public void MembersAreDeletedBeforeOwners()
        {
            var plan = StaleObjectPlan.Build(new[] { "FB_A", "FB_A.Old", "FB_Gone" }, new[] { Top("FB_A", "") },
                Live(("FB_A", Root + "^FB_A"), ("FB_Gone", Root + "^FB_Gone")), Root);

            Assert.Equal(new[] { Root + "^FB_A | Old", Root + " | FB_Gone" }, Steps(plan));
        }

        [Fact]
        public void NothingChanged_EmptyPlan()
        {
            var plan = StaleObjectPlan.Build(new[] { "FB_A", "FB_A.Init" },
                new[] { Top("FB_A", "Lib"), Member("FB_A", "Init") },
                Live(("FB_A", Root + "^Lib^FB_A")), Root);

            Assert.Empty(plan.Steps);
            Assert.Empty(plan.Unresolved);
        }

        [Fact]
        public void NamesCompareWithoutCase_LikeTwinCat()
        {
            var plan = StaleObjectPlan.Build(new[] { "fb_a" }, new[] { Top("FB_A", "Lib") },
                Live(("FB_A", Root + "^lib^FB_A")), Root);

            Assert.Empty(plan.Steps);
        }
    }
}
