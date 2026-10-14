using System;
using System.Linq;
using BeckhoffAutomationInterface;
using BeckhoffAutomationInterface.Runtime;
using Xunit;

namespace BeckhoffAutomationInterface.Tests
{
    /// <summary>doctor's license and shell reporting (tasks/2026-10-14-variants-closed-loop, Task 15).</summary>
    public class DoctorTests
    {
        // Shape of a real TwinCAT trial license file (TrialLicense.tclrs), shortened.
        const string Trial =
            "<?xml version=\"1.0\"?><TcLicenseInfo><LicenseInfo><SystemId>{32B6176E}</SystemId>" +
            "<IssueTime>2026-10-14T11:55:00</IssueTime><ExpireTime>2026-10-22T00:00:00</ExpireTime>" +
            "</LicenseInfo></TcLicenseInfo>";

        [Fact]
        public void LicenseExpiry_IsReadFromTheTclrsFile()
        {
            Assert.Equal(new DateTime(2026, 10, 22, 0, 0, 0), Doctor.LicenseExpiry(Trial));
        }

        [Fact]
        public void LicenseWithoutExpiry_IsPermanent()
        {
            Assert.Null(Doctor.LicenseExpiry("<TcLicenseInfo><LicenseInfo /></TcLicenseInfo>"));
        }

        [Fact]
        public void ExpiredLicense_SaysConfigModeAndHowToRenew()
        {
            // The real case from 2026-10-14: the trial expired at midnight, UmRT stayed in Config.
            string text = Doctor.DescribeExpiry(new DateTime(2026, 10, 14), new DateTime(2026, 10, 14, 13, 50, 0));

            Assert.StartsWith("EXPIRED", text);
            Assert.Contains("Config", text);
            Assert.Contains("7 Days Trial License", text);
        }

        [Fact]
        public void LicenseExpiringWithinTwoDays_SaysRenewSoon()
        {
            Assert.Contains("renew soon", Doctor.DescribeExpiry(new DateTime(2026, 10, 22), new DateTime(2026, 10, 21, 12, 0, 0)));
        }

        [Fact]
        public void ValidLicense_ShowsDaysLeft()
        {
            Assert.Equal("valid until 2026-10-22 00:00 (7 days left)",
                Doctor.DescribeExpiry(new DateTime(2026, 10, 22), new DateTime(2026, 10, 14, 15, 30, 0)));
        }

        sealed class Registry : IComRegistry
        {
            public string GetClsid(string progId) => progId == "TcXaeShell.DTE.17.0" ? "{X}" : null;
            public string GetLocalServer(string clsid) => clsid == "{X}" ? @"C:\X\TcXaeShell.exe" : null;
        }

        [Fact]
        public void ShellStatus_ListsEveryShellWithExeOrProblem()
        {
            var status = DteShellResolver.Status(new Registry(), p => true).ToList();

            Assert.Equal(2, status.Count);
            Assert.True(status[0].Usable);
            Assert.Equal(@"C:\X\TcXaeShell.exe", status[0].Detail);
            Assert.False(status[1].Usable);
            Assert.Equal("ProgID is not registered", status[1].Detail);
        }
    }
}
