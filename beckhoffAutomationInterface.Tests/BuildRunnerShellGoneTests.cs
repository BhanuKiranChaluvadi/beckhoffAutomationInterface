using System;
using BeckhoffAutomationInterface.Sync;
using Xunit;

namespace BeckhoffAutomationInterface.Tests
{
    /// <summary>After the watchdog closes a hung shell, the build wait must stop at once
    /// instead of polling a dead server until its own time-out.</summary>
    public class BuildRunnerShellGoneTests
    {
        [Theory]
        [InlineData(0x80010007u)] // RPC_E_SERVER_DIED
        [InlineData(0x80010012u)] // RPC_E_SERVER_DIED_DNE
        [InlineData(0x80010108u)] // RPC_E_DISCONNECTED
        [InlineData(0x800706BAu)] // RPC_S_SERVER_UNAVAILABLE
        [InlineData(0x800706BEu)] // RPC_S_CALL_FAILED
        public void DeadServerErrors_MeanTheShellIsGone(uint hresult)
        {
            Assert.True(BuildRunner.IsShellGone(unchecked((int)hresult)));
        }

        [Theory]
        [InlineData(0x8001010Au)] // RPC_E_SERVERCALL_RETRYLATER: busy, keep waiting
        [InlineData(0x80010001u)] // RPC_E_CALL_REJECTED
        [InlineData(0x80004005u)] // E_FAIL
        public void BusyOrOtherErrors_DoNotMeanTheShellIsGone(uint hresult)
        {
            Assert.False(BuildRunner.IsShellGone(unchecked((int)hresult)));
        }

        [Fact]
        public void HangLimit_CoversCleanAndBuildTimeouts()
        {
            Assert.Equal(TimeSpan.FromMinutes(15), BuildRunner.HangLimit);
        }
    }
}
