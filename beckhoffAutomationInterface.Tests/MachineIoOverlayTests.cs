using System.Diagnostics;
using System.Text;
using BeckhoffAutomationInterface.Machines;
using Xunit;

namespace BeckhoffAutomationInterface.Tests
{
    /// <summary>io-devices.xml Base/Set/Replace/Add overlays and ${PLC_INSTANCE}: the same result
    /// as Read-IoManifest of the PLC repository's Tools/build_checks.ps1.</summary>
    public class MachineIoOverlayTests
    {
        const string Base =
            "<?xml version='1.0' encoding='utf-8'?>\n<IoTree>\n  <!-- base comment -->\n" +
            "  <Adapter Name=\"A1\" Mac=\"000105A63801\" Ports=\"8\" />\n" +
            "  <Device Name=\"BH 1\" Disabled=\"false\" Via=\"A1\" Port=\"1\">\n    <Box Name=\"Box 1\" RevisionNo=\"#x00000000\" />\n  </Device>\n" +
            "  <Links>\n    <Link PlcVar=\"${PLC_INSTANCE}^GVL.x\" IoPath=\"TIID^BH 1^Box 1^In\" />\n  </Links>\n</IoTree>\n";
        const string Mid =
            "<?xml version='1.0' encoding='utf-8'?>\n<IoTree Base=\"base.xml\">\n  <Add Path=\"/IoTree\">\n" +
            "    <Device Name=\"BH 7\" Disabled=\"false\" Via=\"A1\" Port=\"7\">\n      <Box Name=\"Box 48\" RevisionNo=\"#x00000016\" />\n    </Device>\n" +
            "  </Add>\n</IoTree>\n";
        const string Machine =
            "<?xml version='1.0' encoding='utf-8'?>\n<IoTree Base=\"../Platform/mid.xml\" PlcInstance=\"NFLite Instance\">\n  <!-- machine comment -->\n" +
            "  <Replace Path=\"/IoTree/Adapter[@Name='A1']\">\n    <Adapter Name=\"A1\" Mac=\"000105B0417E\" Ports=\"8\" DeviceName=\"\\DEVICE\\{AB}\" />\n  </Replace>\n" +
            "  <Set Path=\"/IoTree/Device[@Name='BH 1']/Box[@Name='Box 1']\" Attribute=\"RevisionNo\" Value=\"#x00000016\" />\n" +
            "  <Add Path=\"/IoTree/Links\">\n    <Link PlcVar=\"${PLC_INSTANCE}^GVL.y\" IoPath=\"TIID^BH 7^Box 48^Out\" />\n  </Add>\n</IoTree>\n";

        // Output of Read-IoManifest (PowerShell 5.1) for the three files above, 2026-10-20.
        const string PowerShellResult =
            "<?xml version=\"1.0\" encoding=\"utf-8\"?><IoTree><!-- base comment --><Adapter Name=\"A1\" Mac=\"000105B0417E\" Ports=\"8\" DeviceName=\"\\DEVICE\\{AB}\" />" +
            "<Device Name=\"BH 1\" Disabled=\"false\" Via=\"A1\" Port=\"1\"><Box Name=\"Box 1\" RevisionNo=\"#x00000016\" /></Device>" +
            "<Links><Link PlcVar=\"NFLite Instance^GVL.x\" IoPath=\"TIID^BH 1^Box 1^In\" /><Link PlcVar=\"NFLite Instance^GVL.y\" IoPath=\"TIID^BH 7^Box 48^Out\" /></Links>" +
            "<Device Name=\"BH 7\" Disabled=\"false\" Via=\"A1\" Port=\"7\"><Box Name=\"Box 48\" RevisionNo=\"#x00000016\" /></Device></IoTree>";

        static string Family(string machineXml = Machine)
        {
            string root = TestProjects.NewTempDir("bai_overlay");
            Directory.CreateDirectory(Path.Combine(root, "Platform"));
            Directory.CreateDirectory(Path.Combine(root, "M"));
            File.WriteAllText(Path.Combine(root, "Platform", "base.xml"), Base);
            File.WriteAllText(Path.Combine(root, "Platform", "mid.xml"), Mid);
            File.WriteAllText(Path.Combine(root, "M", "io-devices.xml"), machineXml);
            return root;
        }

        [Fact]
        public void TwoLevelOverlay_GivesThePowerShellText()
        {
            string root = Family();
            Assert.Equal(PowerShellResult, IoOverlay.ResolveText(Path.Combine(root, "M", "io-devices.xml")));
        }

        [Fact]
        public void FileWithoutBase_IsReturnedAsItIs_EvenWithAPlaceholder()
        {
            string root = Family();
            Assert.Contains("${PLC_INSTANCE}", IoOverlay.ResolveText(Path.Combine(root, "Platform", "base.xml")));
        }

        [Theory]
        [InlineData("<Set Path=\"/IoTree/Device\" Attribute=\"Port\" Value=\"2\" />", "exactly one node")]           // two devices
        [InlineData("<Set Path=\"/IoTree/Nothing\" Attribute=\"Port\" Value=\"2\" />", "exactly one node")]
        [InlineData("<Set Path=\"/IoTree/Adapter\" Attribute=\"Missing\" Value=\"2\" />", "attribute does not exist")]
        [InlineData("<Replace Path=\"/IoTree/Adapter\"><A /><B /></Replace>", "exactly one child")]
        [InlineData("<Remove Path=\"/IoTree/Adapter\" />", "Unknown I/O override operation")]
        public void BadOperations_AreRefused(string operation, string message)
        {
            string root = Family("<IoTree Base=\"../Platform/mid.xml\" PlcInstance=\"X\">" + operation + "</IoTree>");
            var ex = Assert.Throws<InvalidOperationException>(() => IoOverlay.Resolve(Path.Combine(root, "M", "io-devices.xml")));
            Assert.Contains(message, ex.Message);
        }

        [Fact]
        public void PlaceholderWithoutPlcInstance_IsRefused()
        {
            string root = Family("<IoTree Base=\"../Platform/mid.xml\" />");
            var ex = Assert.Throws<InvalidOperationException>(() => IoOverlay.Resolve(Path.Combine(root, "M", "io-devices.xml")));
            Assert.Contains("PlcInstance", ex.Message);
        }

        [Fact]
        public void BaseOutsideTheFamily_IsRefused()
        {
            string root = Family("<IoTree Base=\"../../outside.xml\" PlcInstance=\"X\" />");
            var ex = Assert.Throws<InvalidOperationException>(() => IoOverlay.Resolve(Path.Combine(root, "M", "io-devices.xml")));
            Assert.Contains("inside its machine family", ex.Message);
        }

        [Fact]
        public void BaseCycle_IsRefused()
        {
            string root = Family("<IoTree Base=\"io-devices.xml\" PlcInstance=\"X\" />");
            var ex = Assert.Throws<InvalidOperationException>(() => IoOverlay.Resolve(Path.Combine(root, "M", "io-devices.xml")));
            Assert.Contains("cycle", ex.Message);
        }

        [Fact]
        public void AbsoluteBase_IsRefused()
        {
            string root = Family("<IoTree Base=\"C:\\x.xml\" PlcInstance=\"X\" />");
            Assert.Throws<InvalidOperationException>(() => IoOverlay.Resolve(Path.Combine(root, "M", "io-devices.xml")));
        }

        /// <summary>The real PLC repository (BAI_PLC_REPO, or the usual path on the development PC):
        /// every NFLite machine resolves to the same text as the repository's own PowerShell
        /// function. Does nothing where the repository is not present (CI).</summary>
        [Fact]
        public void RealMachines_GiveTheSameTextAsReadIoManifest()
        {
            string repo = Environment.GetEnvironmentVariable("BAI_PLC_REPO")
                ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), @"workspace\Active\BeckhoffLib");
            string checks = Path.Combine(repo, @"Tools\build_checks.ps1");
            string family = Path.Combine(repo, @"Machines\NFLite");
            if (!File.Exists(checks) || !Directory.Exists(family))
                return;

            string outDir = TestProjects.NewTempDir("bai_overlay_ps");
            var files = Directory.GetDirectories(family).Select(d => Path.Combine(d, "io-devices.xml")).Where(File.Exists).ToList();
            Assert.NotEmpty(files);
            var script = new StringBuilder(". '" + checks + "';");
            for (int i = 0; i < files.Count; i++)
                script.AppendFormat("[IO.File]::WriteAllText('{0}', (Read-IoManifest '{1}').OuterXml, (New-Object Text.UTF8Encoding($false)));",
                    Path.Combine(outDir, i + ".xml"), files[i]);
            var psi = new ProcessStartInfo("powershell", "-NoProfile -ExecutionPolicy Bypass -Command \"" + script + "\"")
                { UseShellExecute = false, CreateNoWindow = true, RedirectStandardError = true };
            using (Process p = Process.Start(psi))
            {
                string error = p.StandardError.ReadToEnd();
                p.WaitForExit();
                Assert.True(p.ExitCode == 0, error);
            }
            for (int i = 0; i < files.Count; i++)
                Assert.True(File.ReadAllText(Path.Combine(outDir, i + ".xml"), Encoding.UTF8) == IoOverlay.ResolveText(files[i]),
                    "different resolved I/O tree: " + files[i]);
        }
    }
}
