using System.Security.Cryptography;
using BeckhoffAutomationInterface.Machines;
using Xunit;

namespace BeckhoffAutomationInterface.Tests
{
    /// <summary>Source assembly of one machine build: what goes into &lt;work&gt;\&lt;family&gt;\&lt;M&gt;\src.</summary>
    public class MachineSourcesTests
    {
        static readonly MachineSources.CommandRunner NoCommand = (c, w) => throw new InvalidOperationException("no command expected");

        [Fact]
        public void Layout_PutsSimulationBuildsInTheirOwnFolder()
        {
            var repo = new MachineRepoFixture();
            MachineLayout normal = repo.Layout("m1"), sim = repo.Layout("M1", simulation: true);
            Assert.Equal("M1", normal.Machine);
            Assert.Equal(Path.Combine(repo.Work, "Fam", "M1"), normal.Dir);
            Assert.Equal(Path.Combine(repo.Work, "Fam", "M1-sim"), sim.Dir);
            Assert.Equal(Path.Combine(normal.Dir, "M1", "M1", "M1.tsproj"), normal.Tsproj);
            Assert.Equal(Path.Combine(normal.Dir, "M1", "M1.sln"), normal.Sln);
            Assert.Equal(normal.Dir + ".log", normal.Log);
        }

        [Fact]
        public void Assemble_CopiesSourcesSharedAndMachineManifests()
        {
            var repo = new MachineRepoFixture();
            MachineLayout l = repo.Layout("M1");
            MachineSources.Assemble(l, NoCommand);
            string src = l.Src;
            Assert.True(File.Exists(Path.Combine(src, @"Lib\Math\F_Add.st")));
            Assert.True(File.Exists(Path.Combine(src, @"App\Fam\MAIN.st")));
            Assert.False(Directory.Exists(Path.Combine(src, @"App\Other")));
            foreach (string f in new[] { "libraries.xml", "events.xml", "variants.xml", "tasks.xml", "safety.xml", @"event-classes\E.xml", @"plc-data-types\EL3174.xml" })
                Assert.True(File.Exists(Path.Combine(src, f)), f);
            Assert.Equal(IoOverlay.ResolveText(repo.Path_("Machines/Fam/M1/io-devices.xml")), File.ReadAllText(Path.Combine(src, "io-devices.xml")));
            Assert.Contains("Fam Instance^GVL.x", File.ReadAllText(Path.Combine(src, "io-devices.xml")));
            // TwinSAFE files byte for byte, and the target system config at its build path.
            Assert.Equal(File.ReadAllBytes(repo.Path_("Safety/Fam/S/S.splcproj")), File.ReadAllBytes(Path.Combine(src, @"safety\S\S.splcproj")));
            Assert.True(File.Exists(Path.Combine(src, @"safety\S\sub\a.sal")));
            Assert.True(File.Exists(Path.Combine(src, @"safety\t.xml")));
            Assert.False(File.Exists(Path.Combine(src, "expect.json")));
        }

        [Fact]
        public void Assemble_Again_MirrorsDeletesAndRemovedOptionalManifests()
        {
            var repo = new MachineRepoFixture();
            MachineLayout l = repo.Layout("M1");
            MachineSources.Assemble(l, NoCommand);
            DateTime unchanged = File.GetLastWriteTimeUtc(Path.Combine(l.Src, @"Lib\Math\F_Add.st"));
            File.Delete(repo.Path_("App/Fam/MAIN.st"));
            File.Delete(repo.Path_("Machines/Fam/M1/tasks.xml"));
            File.Delete(repo.Path_("Machines/Fam/M1/safety.xml"));
            File.Delete(repo.Path_("Safety/Fam/S/sub/a.sal"));

            Assert.Equal(new[] { "App/Fam/MAIN.st" }, MachineSources.GoneStFiles(repo.Root, l.Src, l.Family.Sources));
            System.Threading.Thread.Sleep(20);
            MachineSources.Assemble(l, NoCommand);
            Assert.False(File.Exists(Path.Combine(l.Src, @"App\Fam\MAIN.st")));
            Assert.False(File.Exists(Path.Combine(l.Src, "tasks.xml")));
            Assert.False(File.Exists(Path.Combine(l.Src, "safety.xml")));
            Assert.Equal(unchanged, File.GetLastWriteTimeUtc(Path.Combine(l.Src, @"Lib\Math\F_Add.st"))); // same bytes: not written
            Assert.Empty(MachineSources.GoneStFiles(repo.Root, l.Src, l.Family.Sources));
        }

        [Fact]
        public void Stamp_ChangesWithManifests_NotWithCode()
        {
            var repo = new MachineRepoFixture();
            MachineLayout l = repo.Layout("M1");
            MachineSources.Assemble(l, NoCommand);
            string first = MachineSources.Stamp(l.Src);

            repo.Write("App/Fam/MAIN.st", "PROGRAM MAIN\nEND_PROGRAM\n");
            MachineSources.Assemble(l, NoCommand);
            Assert.Equal(first, MachineSources.Stamp(l.Src));

            repo.Write("Machines/Fam/Platform/io-devices.xml", "<IoTree><Adapter Name=\"A1\" Mac=\"000105A63802\" Ports=\"8\" /></IoTree>");
            MachineSources.Assemble(l, NoCommand);
            string io = MachineSources.Stamp(l.Src);
            Assert.NotEqual(first, io);

            repo.Write("Safety/Fam/S/S.splcproj", "other");
            MachineSources.Assemble(l, NoCommand);
            Assert.NotEqual(io, MachineSources.Stamp(l.Src));
        }

        [Fact]
        public void BadSafetyManifest_IsRefused()
        {
            var repo = new MachineRepoFixture();
            repo.Write("Machines/Fam/M1/safety.xml", "<Safety Name=\"S\" Folder=\"safety/S\" />");
            var ex = Assert.Throws<InvalidOperationException>(() => MachineSources.Assemble(repo.Layout("M1"), NoCommand));
            Assert.Contains("needs <Safety", ex.Message);
        }

        [Fact]
        public void GasModel_IsCheckedCopiedAndGenerated()
        {
            var repo = new MachineRepoFixture();
            string model = "{\"nodes\":[]}";
            repo.Write("Machines/Fam/M1/simulation-gas.json", model);
            repo.Write("Machines/Fam/M1/tasks.xml",
                "<Tasks><Task Name=\"PlcTask\" CycleTimeUs=\"10000\" Priority=\"20\"><Call>MAIN</Call></Task>" +
                "<Task Name=\"Gas\" CycleTimeUs=\"100000\" Priority=\"30\"><Call>PRG_GAS_SIMULATION</Call></Task></Tasks>");
            string hash;
            using (var sha = SHA256.Create())
                hash = BitConverter.ToString(sha.ComputeHash(File.ReadAllBytes(repo.Path_("Machines/Fam/M1/simulation-gas.json")))).Replace("-", "");
            repo.Write("Machines/provenance.json", "{ \"schemaVersion\": 1, \"resolvedModels\": { \"Machines/Fam/M1/simulation-gas.json\": \"" + hash + "\" } }");

            MachineLayout l = repo.Layout("M1");
            IReadOnlyList<string> ran = null;
            string folder = null;
            MachineSources.Assemble(l, (c, w) => { ran = c; folder = w; return 0; });
            Assert.True(File.Exists(Path.Combine(l.Src, "simulation-gas.json")));
            Assert.Equal(repo.Root, folder);
            Assert.Equal(new[] { "python", "Tools/gen.py", repo.Path_("Machines/Fam/M1/simulation-gas.json"), "App/Fam", l.Src + "/App/Fam/GVL_Gas.st" }, ran);

            var failed = Assert.Throws<InvalidOperationException>(() => MachineSources.Assemble(l, (c, w) => 1));
            Assert.Contains("binding failed", failed.Message);

            repo.Write("Machines/provenance.json", "{ \"schemaVersion\": 1, \"resolvedModels\": { \"Machines/Fam/M1/simulation-gas.json\": \"00\" } }");
            var mismatch = Assert.Throws<InvalidOperationException>(() => MachineSources.Assemble(l, (c, w) => 0));
            Assert.Contains("provenance", mismatch.Message);
        }

        [Theory]
        [InlineData(true, "", "neither can be omitted")]                                            // model without task
        [InlineData(false, "<Task Name=\"Gas\" CycleTimeUs=\"100000\"><Call>PRG_GAS_SIMULATION</Call></Task>", "neither can be omitted")] // task without model
        [InlineData(true, "<Task Name=\"Gas\" CycleTimeUs=\"50000\"><Call>PRG_GAS_SIMULATION</Call></Task>", "validated 100000")]
        [InlineData(true, "<Task Name=\"Gas\" CycleTimeUs=\"100000\"><Call>PRG_GAS_SIMULATION</Call><Call>MAIN</Call></Task>", "dedicated task")]
        public void GasModelRules_AreEnforced(bool model, string task, string message)
        {
            var repo = new MachineRepoFixture();
            if (model)
                repo.Write("Machines/Fam/M1/simulation-gas.json", "{}");
            repo.Write("Machines/Fam/M1/tasks.xml", "<Tasks>" + task + "</Tasks>");
            repo.Write("Machines/provenance.json", "{ \"schemaVersion\": 1, \"resolvedModels\": {} }");
            var ex = Assert.Throws<InvalidOperationException>(() => MachineSources.Assemble(repo.Layout("M1"), (c, w) => 0));
            Assert.Contains(message, ex.Message);
        }

        [Fact]
        public void Provenance_NeedsSchema1AndTheHash()
        {
            byte[] model = { 1, 2, 3 };
            string hash;
            using (var sha = SHA256.Create())
                hash = BitConverter.ToString(sha.ComputeHash(model)).Replace("-", "").ToLowerInvariant();
            Assert.True(MachineSources.ProvenanceMatches("{\"schemaVersion\":1,\"resolvedModels\":{\"a/m.json\":\"" + hash.ToUpperInvariant() + "\"}}", "a/m.json", model));
            Assert.False(MachineSources.ProvenanceMatches("{\"schemaVersion\":2,\"resolvedModels\":{\"a/m.json\":\"" + hash + "\"}}", "a/m.json", model));
            Assert.False(MachineSources.ProvenanceMatches("{\"schemaVersion\":1,\"resolvedModels\":{}}", "a/m.json", model));
        }

        [Theory]
        [InlineData(@"C:\repo", @"C:\bai\work", null)]
        [InlineData(@"C:\repo", @"C:\repo\work", "outside the source tree")]
        [InlineData(@"C:\repo", @"C:\repo", "outside the source tree")]
        [InlineData(@"C:\repo\sub", @"C:\repo", "outside the source tree")]
        [InlineData(@"C:\repo", @"C:\", "drive root")]
        [InlineData(@"C:\repo", @"C:\repository-work", null)]
        public void WorkFolder_MustBeOutsideTheSource(string source, string work, string problem)
        {
            string result = BuildLocation.Problem(source, work);
            if (problem == null)
                Assert.Null(result);
            else
                Assert.Contains(problem, result);
        }

        [Fact]
        public void FailedBuild_IsKeptBeforeAFreshBuild()
        {
            var repo = new MachineRepoFixture();
            MachineLayout l = repo.Layout("M1");
            MachineSources.Assemble(l, NoCommand);
            string archive = BuildLocation.SaveFailedEvidence(l.FamilyWork, l.Dir, l.Machine);
            Assert.StartsWith(Path.Combine(l.FamilyWork, "_failed", "M1-"), archive);
            Assert.True(File.Exists(Path.Combine(archive, @"src\variants.xml")));
            BuildLocation.Delete(l.Dir);
            Assert.False(Directory.Exists(l.Dir));
        }
    }
}
