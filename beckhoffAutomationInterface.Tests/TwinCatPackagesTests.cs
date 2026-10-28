using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BeckhoffAutomationInterface.Machines;
using BeckhoffAutomationInterface.Release;
using Xunit;

namespace BeckhoffAutomationInterface.Tests
{
    /// <summary>'toolchain check|install' with a fake tcpkg: what is missing is installed after a dry
    /// run and a typed 'y'; an installed version is never changed.</summary>
    public sealed class TwinCatPackagesTests
    {
        static readonly TwinCatPackage[] Packages =
        {
            new TwinCatPackage { Name = "TwinCAT.Standard.XAE", Version = "4026.27.0", Why = "XAE" },
            new TwinCatPackage { Name = "TF4100.ControllerToolbox.XAE", Version = "3.6.5", Why = "toolbox", Lib = "TwinCAT.XAE.PLC.Lib.Tc2_ControllerToolbox" },
            new TwinCatPackage { Name = "TwinCAT.XAR.NCPTP.UM", Version = "3.1.5049", Why = "NC", Alias = "TwinCAT.XARUM.NCPTP" },
            new TwinCatPackage { Name = "TF6255.ModbusRTU.XAE", Version = "3.6.3", Why = "Modbus" },
        };

        /// <summary>A tcpkg that knows 'installed' and records each call.</summary>
        sealed class FakeTcPkg
        {
            public readonly Dictionary<string, string> Installed = new Dictionary<string, string>();
            public readonly List<string> Calls = new List<string>();
            public bool Shell64, SignedIn = true;

            public int Run(string[] arguments, bool echo, List<string> output)
            {
                string call = string.Join(" ", arguments);
                Calls.Add(call);
                if (call == "source list") output.Add("Beckhoff Stable Feed  https://public.tcpkg.beckhoff-cloud.com  " + (SignedIn ? "Authenticated" : "Anonymous"));
                else if (call == "config list") output.Add("UseTcXaeShell64: " + (Shell64 ? "True" : "False"));
                else if (call == "config set -n useTcXaeShell64 -y") Shell64 = true;
                else if (call == "list -i") output.AddRange(Installed.Select(p => p.Key + "  " + p.Value));
                else if (arguments[0] == "install" && !arguments.Contains("--dry-run"))
                    foreach (string p in arguments.Skip(1).Where(a => a.Contains("="))) Installed[p.Split('=')[0]] = p.Split('=')[1];
                return 0;
            }
        }

        [Fact]
        public void CompareFindsOkMissingDifferentAliasAndLibrary()
        {
            var installed = TwinCatPackages.Installed(new[] { "Name  Version", "TwinCAT.Standard.XAE  4026.24.0",
                "TwinCAT.XAE.PLC.Lib.Tc2_ControllerToolbox  3.4.1", "TwinCAT.XARUM.NCPTP  3.1.5049", "garbage" });
            var rows = TwinCatPackages.Compare(Packages, installed);
            Assert.Equal(new[] { "DIFFERENT", "OK", "OK", "MISSING" }, rows.Select(r => r.Status));
            Assert.Equal("TwinCAT.XARUM.NCPTP", rows[2].Found);
        }

        [Fact]
        public void CheckChangesNothingAndFailsOnAMissingPackage()
        {
            var tcpkg = new FakeTcPkg { Shell64 = true };
            foreach (var p in Packages) tcpkg.Installed[p.Name] = p.Version;
            Assert.Equal(0, TwinCatPackages.Run(false, Packages, tcpkg.Run, new StringReader("")));
            tcpkg.Installed.Remove("TF6255.ModbusRTU.XAE");
            Assert.Equal(1, TwinCatPackages.Run(false, Packages, tcpkg.Run, new StringReader("y")));
            Assert.DoesNotContain(tcpkg.Calls, c => c.StartsWith("install") || c.StartsWith("config set"));
        }

        [Fact]
        public void InstallAddsOnlyMissingPackagesAfterADryRunAndY()
        {
            var tcpkg = new FakeTcPkg();
            tcpkg.Installed["TwinCAT.Standard.XAE"] = "4026.24.0";   // another version: kept
            Assert.Equal(0, TwinCatPackages.Run(true, Packages, tcpkg.Run, new StringReader("y\n")));
            Assert.True(tcpkg.Shell64);
            Assert.Equal("4026.24.0", tcpkg.Installed["TwinCAT.Standard.XAE"]);
            string dry = tcpkg.Calls.Single(c => c.Contains("--dry-run"));
            Assert.DoesNotContain("TwinCAT.Standard.XAE", dry);
            Assert.Contains("TF6255.ModbusRTU.XAE=3.6.3", dry);
            Assert.Contains("TwinCAT.XAR.NCPTP.UM=3.1.5049", tcpkg.Calls.Single(c => c.StartsWith("install") && !c.Contains("--dry-run")));
        }

        [Fact]
        public void InstallStopsWithoutYOrWithoutSignIn()
        {
            var tcpkg = new FakeTcPkg { Shell64 = true };
            Assert.Contains("Nothing installed", Assert.Throws<InvalidOperationException>(() => TwinCatPackages.Run(true, Packages, tcpkg.Run, new StringReader("n\n"))).Message);
            Assert.Contains("Nothing installed", Assert.Throws<InvalidOperationException>(() => TwinCatPackages.Run(true, Packages, tcpkg.Run, new StringReader(""))).Message);
            tcpkg.SignedIn = false;
            Assert.Contains("Sign in", Assert.Throws<InvalidOperationException>(() => TwinCatPackages.Run(true, Packages, tcpkg.Run, new StringReader("y\n"))).Message);
            Assert.DoesNotContain(tcpkg.Calls, c => c.StartsWith("install") && !c.Contains("--dry-run"));
        }

        [Fact]
        public void ManifestReadsThePackageList()
        {
            var r = MachineManifest.Parse("{ \"work\": \"C:/w\", \"lock\": \"C:/l\", \"release\": { \"packages\": [ { \"name\": \"A\", \"version\": \"1.0\", \"why\": \"w\", \"lib\": \"L\" } ] }," +
                " \"families\": { \"F\": { \"plc\": \"F\", \"sources\": [\"Lib\"], \"shared\": \"M\", \"buildInfo\": \"Lib/B.st\", \"machines\": [\"A1\"] } } }", @"C:\repo\bai.machines.json").Release;
            Assert.Equal("A", r.Packages.Single().Name);
            Assert.Equal("L", r.Packages.Single().Lib);
            Assert.Contains("unknown key 'size'", Assert.Throws<FormatException>(() => MachineManifest.Parse("{ \"work\": \"C:/w\", \"lock\": \"C:/l\", \"release\": { \"packages\": [ { \"name\": \"A\", \"version\": \"1\", \"size\": 1 } ] }," +
                " \"families\": { \"F\": { \"plc\": \"F\", \"sources\": [\"Lib\"], \"shared\": \"M\", \"buildInfo\": \"Lib/B.st\", \"machines\": [\"A1\"] } } }", @"C:\repo\bai.machines.json")).Message);
        }
    }
}
