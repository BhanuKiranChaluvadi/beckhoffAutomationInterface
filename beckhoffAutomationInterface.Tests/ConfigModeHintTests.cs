using System;
using BeckhoffAutomationInterface.Runtime;
using Xunit;

namespace BeckhoffAutomationInterface.Tests
{
    /// <summary>"TwinCAT stays in Config" hint.
    /// The Task 8 spike showed the cause is not always the license.</summary>
    public class ConfigModeHintTests
    {
        static readonly DateTime Now = new DateTime(2026, 10, 14, 16, 0, 0);

        [Fact]
        public void NoLicenseFile_SaysCreateOne()
        {
            Assert.Contains("No license file", RuntimeCommands.ConfigModeHint(null, Now));
        }

        [Fact]
        public void ExpiredLicense_BlamesTheLicense()
        {
            string hint = RuntimeCommands.ConfigModeHint(new DateTime(2026, 10, 14), Now);

            Assert.Contains("expired 2026-10-14 00:00", hint);
            Assert.Contains("Renew", hint);
        }

        [Fact]
        public void ValidLicense_DoesNotBlameTheLicense_ButPointsAtDevices()
        {
            // The spike case: valid trial license, enabled EtherCAT master on UmRT.
            string hint = RuntimeCommands.ConfigModeHint(new DateTime(2026, 10, 22), Now);

            Assert.Contains("valid until 2026-10-22", hint);
            Assert.Contains("EtherCAT master on UmRT", hint);
            Assert.DoesNotContain("Renew", hint);
        }

        [Fact]
        public void PermanentLicense_PointsAtDevices()
        {
            Assert.Contains("has no expiry", RuntimeCommands.ConfigModeHint(DateTime.MaxValue, Now));
        }
    }
}
