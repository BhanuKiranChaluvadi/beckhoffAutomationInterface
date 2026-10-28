using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using BeckhoffAutomationInterface.Runtime;
using Xunit;

namespace BeckhoffAutomationInterface.Tests
{
    /// <summary>'license check|trial' without XAE: the license selection, the .tclrs parser, the check
    /// and the license-only project.</summary>
    public sealed class LicenseCommandsTests : IDisposable
    {
        readonly string work = TestProjects.NewTempDir("bai_license");
        public void Dispose() { try { Directory.Delete(work, true); } catch (IOException) { } }

        static string Tclrs(string expires, params string[] names) =>
            "<?xml version=\"1.0\"?><TcLicenseInfo><LicenseInfo><SystemId>{32B6176E-4272-217F-E0A4-FA13C07B1713}</SystemId>" +
            (expires == null ? "" : "<ExpireTime>" + expires + "</ExpireTime>") +
            string.Concat(names.Select(n => "<License><LicenseId>" + LicenseCommands.Known[n].ToLowerInvariant() + "</LicenseId><Name>" + n + "</Name>" +
                (n == "TC3 NC PTP Axis" ? "<Instances>10</Instances>" : "") + "</License>")) +
            "</LicenseInfo></TcLicenseInfo>";

        [Fact]
        public void SelectTakesKnownNamesAnyCaseAndIds()
        {
            var selected = LicenseCommands.Select(" tc3 plc , TC3 NC PTP,{11111111-2222-3333-4444-555555555555}");
            Assert.Equal(new[] { "TC3 PLC", "TC3 NC PTP", "{11111111-2222-3333-4444-555555555555}" }, selected.Select(s => s.Key));
            Assert.Equal(LicenseCommands.Known["TC3 PLC"], selected[0].Value);
            Assert.Contains("TC3 Controller Toolbox", Assert.Throws<ArgumentException>(() => LicenseCommands.Select("TC3 Unknown")).Message);
            Assert.Throws<ArgumentException>(() => LicenseCommands.Select(" , "));
        }

        [Fact]
        public void ParseReadsExpiryInstancesAndPermanentLicenses()
        {
            var trial = LicenseCommands.Parse("t.tclrs", Tclrs("2026-11-04T00:00:00", "TC3 NC PTP Axis", "TC3 PLC")).Single();
            Assert.Equal(new DateTime(2026, 11, 4), trial.Expires);
            Assert.Equal(10, trial.Licenses.Single(l => l.Name == "TC3 NC PTP Axis").Instances);
            Assert.Equal(LicenseCommands.Known["TC3 PLC"], trial.Licenses.Single(l => l.Name == "TC3 PLC").Id);
            var permanent = LicenseCommands.Parse("p.tclrs", Tclrs(null, "TC3 IO")).Single();
            Assert.Null(permanent.Expires);
            Assert.True(permanent.ValidAt(DateTime.MaxValue.AddDays(-1)));
        }

        [Fact]
        public void CheckFindsValidExpiredAndMissing()
        {
            File.WriteAllText(Path.Combine(work, "old.tclrs"), Tclrs("2026-10-01T00:00:00", "TC3 Temperature-Controller", "TC3 PLC"));
            File.WriteAllText(Path.Combine(work, "new.tclrs"), Tclrs("2026-11-04T00:00:00", "TC3 PLC"));
            File.WriteAllText(Path.Combine(work, "broken.tclrs"), "not xml");
            var problems = new List<string>();
            var files = LicenseCommands.Read(new[] { work, Path.Combine(work, "absent") }, problems);
            Assert.Single(problems);
            var check = LicenseCommands.Check(files, LicenseCommands.Select("TC3 PLC,TC3 Temperature-Controller,TC3 NC PTP"), new DateTime(2026, 10, 28));
            Assert.Equal("new.tclrs", Path.GetFileName(check[0].File.Path));
            Assert.Null(check[1].File);
            Assert.StartsWith("expired 2026-10-01", check[1].Problem);
            Assert.Equal("missing", check[2].Problem);
        }

        [Fact]
        public void PackageLicensesComeFromTheBootDriversAndThePlcTmc()
        {
            string package = Path.Combine(work, "NFPro.zip");
            using (var zip = System.IO.Compression.ZipFile.Open(package, System.IO.Compression.ZipArchiveMode.Create))
            {
                void Entry(string name, string text) { using (var w = new StreamWriter(zip.CreateEntry(name).Open())) w.Write(text); }
                Entry("boot/CurrentConfig.xml", "<TcBootProject><Drivers><Driver><ServiceName>TcIo</ServiceName></Driver><Driver><ServiceName>TcNc</ServiceName></Driver>" +
                    "<Driver><ServiceName>TcPlc30</ServiceName></Driver></Drivers></TcBootProject>");
                Entry("boot/Plc/Port_851.tmc", "<TcModuleClass><Licenses><License><LicenseId>{1eebbaaa-51e1-4750-9d6a-7b04116ea32b}</LicenseId><Comment>TF4110</Comment></License>" +
                    "<License><LicenseId>{1EEBBAAA-51E1-4750-9D6A-7B04116EA32B}</LicenseId></License><License><LicenseId>{12345678-0000-0000-0000-000000000000}</LicenseId><Comment>TF9999 Other</Comment></License></Licenses></TcModuleClass>");
            }
            var required = LicenseCommands.FromPackage(package);
            Assert.Equal(new[] { "TC3 ADS", "TC3 IO", "TC3 PLC", "TC3 NC PTP", "TC3 NC PTP Axis", "TC3 Temperature-Controller", "TF9999 Other" }, required.Select(r => r.Key));
            Assert.Equal("{12345678-0000-0000-0000-000000000000}", required.Last().Value);
        }

        [Fact]
        public void ProjectSelectsEachLicenseOnce()
        {
            string xml = LicenseCommands.ProjectXml(new[] { LicenseCommands.Known["TC3 NC PTP"], LicenseCommands.Known["TC3 PLC"] });
            var selected = XDocument.Parse(xml).Descendants("ManualSelect").Select(e => e.Value).ToList();
            Assert.Equal(new[] { LicenseCommands.Known["TC3 NC PTP"], LicenseCommands.Known["TC3 PLC"] }, selected);
            Assert.Equal("true", (string)XDocument.Parse(xml).Root.Element("Project").Attribute("Target64Bit"));
        }

        [Fact]
        public void CommandLineRefusesRemoteTargetsAndTrialWithoutLicenses()
        {
            Assert.Equal(2, LicenseCommands.Run(new[] { "license", "check", "--target", "1.2.3.4.1.1" }, null));
            Assert.Equal(2, LicenseCommands.Run(new[] { "license", "trial", "--target", "umrt" }, null));
            Assert.Equal(2, LicenseCommands.Run(new[] { "license", "check", "--wait", "5" }, null));
            Assert.Equal(2, LicenseCommands.Run(new[] { "license", "renew" }, null));
        }
    }
}
