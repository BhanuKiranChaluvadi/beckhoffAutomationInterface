using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using Microsoft.Win32;

namespace BeckhoffAutomationInterface.Runtime
{
    /// <summary>
    /// `doctor`: a read-only health report of this PC — DTE shells, TwinCAT version, the local
    /// runtime and every User Mode Runtime with their state, boot project and license expiry.
    /// Changes nothing. The license line exists because an expired 7-day trial license leaves
    /// TwinCAT in Config mode after activation with no clear error.
    /// </summary>
    static class Doctor
    {
        public static int Run()
        {
            Console.WriteLine("DTE shells (--shell):");
            foreach ((DteShell shell, string detail, bool usable) in DteShellResolver.Status(new WindowsComRegistry(), File.Exists))
                Console.WriteLine("  {0} {1,-7} {2}: {3}", usable ? "OK  " : "--  ", shell.Name, shell.ProgId, detail);

            Console.WriteLine();
            Console.WriteLine("TwinCAT: {0}", ReadTwinCatVersion() ?? "not found");

            string programData = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);
            string twinCatData = Path.Combine(programData, "Beckhoff", "TwinCAT", "3.1");

            Console.WriteLine();
            Console.WriteLine("Runtime 'local' (activate/verify --target local):");
            string localNetId = null;
            try { localNetId = AdsClient.LocalNetId(); }
            catch (Exception ex) { Console.WriteLine("  ADS router: {0}", ex.Message); }
            if (localNetId != null)
                ReportRuntime(localNetId, Path.Combine(twinCatData, "Boot"),
                    new[] { Path.Combine(twinCatData, "Target", "License"),
                            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Beckhoff", "TwinCAT", "3.1", "Target", "License") });

            string runtimes = Path.Combine(twinCatData, "Runtimes");
            foreach (string dir in Directory.Exists(runtimes) ? Directory.GetDirectories(runtimes) : new string[0])
            {
                string registry = Path.Combine(dir, "3.1", "TcRegistry.xml");
                if (!File.Exists(registry))
                    continue;
                string name = Path.GetFileName(dir);
                Console.WriteLine();
                Console.WriteLine("User Mode Runtime '{0}'{1}:", name, name == "UmRT_Default" ? " (activate/verify --target umrt)" : "");
                string netId;
                try { netId = RuntimeTargets.NetIdFromTcRegistry(File.ReadAllText(registry)); }
                catch (FormatException ex) { Console.WriteLine("  {0}", ex.Message); continue; }
                ReportRuntime(netId, Path.Combine(dir, "3.1", "Boot"), new[] { Path.Combine(dir, "3.1", "Target", "License") });
            }
            return 0;
        }

        static void ReportRuntime(string netId, string bootFolder, IEnumerable<string> licenseFolders)
        {
            Console.WriteLine("  AMS Net ID:   {0}", netId);
            using (var system = new AdsClient(netId, AdsClient.PortSystemService))
            {
                int err = system.TryReadState(out ushort state);
                Console.WriteLine("  TwinCAT:      {0}", err == 0 ? AdsClient.StateName(state) : "not running / not reachable (ADS error " + err + ")");
            }
            using (var plc = new AdsClient(netId, AdsClient.PortPlc1))
            {
                int err = plc.TryReadState(out ushort state);
                Console.WriteLine("  PLC (851):    {0}", err == 0 ? AdsClient.StateName(state) : "not running (ADS error " + err + ")");
            }
            Console.WriteLine("  Boot project: {0}", RuntimeTargets.DescribeBootFolder(bootFolder) ?? "(none)");

            List<string> licenses = licenseFolders.Where(Directory.Exists)
                .SelectMany(d => Directory.GetFiles(d, "*.tclrs")).ToList();
            if (licenses.Count == 0)
                Console.WriteLine("  License:      none found — TwinCAT cannot go to Run (create a 7-day trial license in XAE)");
            foreach (string file in licenses)
            {
                DateTime? expires = LicenseExpiry(File.ReadAllText(file));
                Console.WriteLine("  License:      {0}: {1}", Path.GetFileName(file),
                    expires.HasValue ? DescribeExpiry(expires.Value, DateTime.Now) : "no expiry date (permanent)");
            }
        }

        /// <summary>&lt;ExpireTime&gt;2026-10-22T00:00:00&lt;/ExpireTime&gt; of a .tclrs license file; null when absent.</summary>
        internal static DateTime? LicenseExpiry(string tclrsXml)
        {
            XElement expire = XDocument.Parse(tclrsXml).Descendants().FirstOrDefault(e => e.Name.LocalName == "ExpireTime");
            if (expire == null || !DateTime.TryParse(expire.Value, CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime t))
                return null;
            return t;
        }

        internal static string DescribeExpiry(DateTime expires, DateTime now)
        {
            string when = expires.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);
            if (expires <= now)
                return "EXPIRED " + when + " — TwinCAT will stay in Config mode; renew it in XAE (SYSTEM > License > 7 Days Trial License)";
            TimeSpan left = expires - now;
            if (left < TimeSpan.FromDays(2))
                return string.Format("expires {0} (in {1:0.#} h) — renew soon", when, left.TotalHours);
            return string.Format("valid until {0} ({1:0} days left)", when, Math.Floor(left.TotalDays));
        }

        static string ReadTwinCatVersion()
        {
            using (RegistryKey key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\WOW6432Node\Beckhoff\TwinCAT3\System"))
                return key?.GetValue("TcVersion") as string;
        }
    }
}
