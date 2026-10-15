using Xunit;

namespace BeckhoffAutomationInterface.Tests
{
    /// <summary>Which shell is ours decides which process the tool may force-kill. With parallel
    /// runs, the process-list diff picked the same pid in all four runs, and one run killed
    /// another run's shell (2026-10-15). A guess must never be killed.</summary>
    public class ShellPidTests
    {
        [Fact]
        public void WindowHandle_Wins_EvenWithSeveralNewShells()
        {
            int pid = VisualStudioSession.ChooseShellPid(4711, new[] { 100, 200, 4711 }, out string source);

            Assert.Equal(4711, pid);
            Assert.Equal("window handle", source);
        }

        [Fact]
        public void NoWindowHandle_OneNewShell_UsesIt()
        {
            int pid = VisualStudioSession.ChooseShellPid(0, new[] { 200 }, out string source);

            Assert.Equal(200, pid);
            Assert.Equal("process list", source);
        }

        [Fact]
        public void NoWindowHandle_SeveralNewShells_IsUnknown()
        {
            // The parallel-build case: the old code took the first one.
            int pid = VisualStudioSession.ChooseShellPid(0, new[] { 47192, 38060 }, out string source);

            Assert.Equal(0, pid);
            Assert.Contains("not sure which", source);
        }

        [Fact]
        public void NoWindowHandle_NoNewShell_IsUnknown()
        {
            Assert.Equal(0, VisualStudioSession.ChooseShellPid(0, new int[0], out _));
        }
    }
}
