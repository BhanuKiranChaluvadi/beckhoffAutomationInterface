using System.Collections.Generic;
using BeckhoffAutomationInterface;
using Xunit;

namespace BeckhoffAutomationInterface.Tests
{
    /// <summary>
    /// Shell selection for the Automation Interface (tasks/2026-10-14-variants-closed-loop,
    /// Task 1). The registry is faked, so these tests describe real PC setups without
    /// touching the real registry.
    /// </summary>
    public class DteShellResolverTests
    {
        const string XaeExe = @"C:\Program Files\Beckhoff\TcXaeShell\Common7\IDE\TcXaeShell.exe";
        const string VsExe = @"C:\Program Files\Microsoft Visual Studio\2022\Community\common7\ide\devenv.exe";

        sealed class FakeRegistry : IComRegistry
        {
            public readonly Dictionary<string, string> Clsids = new Dictionary<string, string>();
            public readonly Dictionary<string, string> Servers = new Dictionary<string, string>();
            public string GetClsid(string progId) => Clsids.TryGetValue(progId, out string v) ? v : null;
            public string GetLocalServer(string clsid) => Servers.TryGetValue(clsid, out string v) ? v : null;
        }

        static FakeRegistry Registry(bool xae, bool vs)
        {
            var r = new FakeRegistry();
            if (xae)
            {
                r.Clsids["TcXaeShell.DTE.17.0"] = "{X}";
                r.Servers["{X}"] = XaeExe;
            }
            if (vs)
            {
                r.Clsids["VisualStudio.DTE.17.0"] = "{V}";
                r.Servers["{V}"] = "\"" + VsExe + "\"";
            }
            return r;
        }

        static bool AllExist(string path) => true;

        [Fact]
        public void Default_PrefersXae_WhenBothAreUsable()
        {
            DteShell shell = DteShellResolver.Resolve(null, Registry(xae: true, vs: true), AllExist);

            Assert.Equal("xae", shell.Name);
            Assert.Equal(XaeExe, shell.ExePath);
            Assert.Equal("TcXaeShell", shell.ProcessName);
        }

        [Fact]
        public void Default_FallsBackToVs2022_WhenXaeIsNotRegistered()
        {
            DteShell shell = DteShellResolver.Resolve(null, Registry(xae: false, vs: true), AllExist);

            Assert.Equal("vs2022", shell.Name);
            Assert.Equal(VsExe, shell.ExePath);
            Assert.Equal("devenv", shell.ProcessName);
        }

        [Fact]
        public void XaeOnlyBenchPc_ProgIdWithoutRegisteredClass_IsSkippedNotCrashed()
        {
            // The TODO.md crash: VisualStudio.DTE.17.0 resolves to a CLSID, but that class
            // has no LocalServer32 ("Class not registered {33ABD590-…}").
            FakeRegistry r = Registry(xae: true, vs: false);
            r.Clsids["VisualStudio.DTE.17.0"] = "{33ABD590-0400-4FEF-AF98-5F5A8A99CFC3}";

            DteShell shell = DteShellResolver.Resolve(null, r, AllExist);

            Assert.Equal("xae", shell.Name);
        }

        [Fact]
        public void Requested_Vs2022_WithMissingExe_FailsNamingTheExe()
        {
            FakeRegistry r = Registry(xae: true, vs: true);

            var ex = Assert.Throws<InvalidOperationException>(
                () => DteShellResolver.Resolve("vs2022", r, path => path != VsExe));

            Assert.Contains("vs2022", ex.Message);
            Assert.Contains(VsExe, ex.Message);
        }

        [Fact]
        public void Requested_Xae_IsUsedEvenWhenVsIsAlsoUsable()
        {
            DteShell shell = DteShellResolver.Resolve("XAE", Registry(xae: true, vs: true), AllExist);

            Assert.Equal("xae", shell.Name);
        }

        [Fact]
        public void Requested_UnknownName_FailsListingValidNames()
        {
            var ex = Assert.Throws<InvalidOperationException>(
                () => DteShellResolver.Resolve("vs2019", Registry(xae: true, vs: true), AllExist));

            Assert.Contains("xae", ex.Message);
            Assert.Contains("vs2022", ex.Message);
        }

        [Fact]
        public void NothingUsable_FailsListingEveryCandidate()
        {
            var ex = Assert.Throws<InvalidOperationException>(
                () => DteShellResolver.Resolve(null, Registry(xae: false, vs: false), AllExist));

            Assert.Contains("TcXaeShell.DTE.17.0", ex.Message);
            Assert.Contains("VisualStudio.DTE.17.0", ex.Message);
        }

        [Theory]
        [InlineData("\"C:\\A B\\devenv.exe\"", "C:\\A B\\devenv.exe")]
        [InlineData("\"C:\\A B\\devenv.exe\" /automation", "C:\\A B\\devenv.exe")]
        [InlineData("C:\\X\\TcXaeShell.exe", "C:\\X\\TcXaeShell.exe")]
        [InlineData("C:\\X\\TcXaeShell.exe -Embedding", "C:\\X\\TcXaeShell.exe")]
        [InlineData("  C:\\X\\TcXaeShell.exe  ", "C:\\X\\TcXaeShell.exe")]
        public void ExeFromLocalServer_HandlesQuotesAndArguments(string command, string expected)
        {
            Assert.Equal(expected, DteShellResolver.ExeFromLocalServer(command));
        }

        [Fact]
        public void TakeGlobalOption_RemovesFlagAndValue()
        {
            string[] args = { "build", "C:\\p", "--shell", "xae", "--plc-name", "X" };

            string value = Program.TakeGlobalOption(ref args, "--shell");

            Assert.Equal("xae", value);
            Assert.Equal(new[] { "build", "C:\\p", "--plc-name", "X" }, args);
        }

        [Fact]
        public void TakeGlobalOption_Absent_LeavesArgsUnchanged()
        {
            string[] args = { "build", "C:\\p" };

            Assert.Null(Program.TakeGlobalOption(ref args, "--shell"));
            Assert.Equal(new[] { "build", "C:\\p" }, args);
        }

        [Fact]
        public void RunOptions_ShellFromCommandLine()
        {
            RunOptions options = RunOptions.Parse(new[] { "--source", System.IO.Path.GetTempPath(), "--shell", "vs2022", "--no-config" });

            Assert.Equal("vs2022", options.Shell);
        }

        [Fact]
        public void RunOptions_ShellFromStConfig_WhenNotOnCommandLine()
        {
            string dir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "DteShellResolverTests_" + System.Guid.NewGuid());
            System.IO.Directory.CreateDirectory(dir);
            try
            {
                System.IO.File.WriteAllText(System.IO.Path.Combine(dir, ".stconfig"), "shell=xae\n");

                RunOptions options = RunOptions.Parse(new[] { "--source", dir });

                Assert.Equal("xae", options.Shell);
            }
            finally
            {
                System.IO.Directory.Delete(dir, recursive: true);
            }
        }

        [Fact]
        public void RunOptions_NoShell_IsNull_SoTheResolverPicksTheDefault()
        {
            RunOptions options = RunOptions.Parse(new[] { "--source", System.IO.Path.GetTempPath(), "--no-config" });

            Assert.Null(options.Shell);
        }
    }
}
