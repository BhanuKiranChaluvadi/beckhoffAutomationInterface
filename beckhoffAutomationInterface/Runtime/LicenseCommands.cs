using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Xml.Linq;
using Interop.TCatSysManager;
using static BeckhoffAutomationInterface.Clock;

namespace BeckhoffAutomationInterface.Runtime
{
    /// <summary>One license of a .tclrs file.</summary>
    internal sealed class LicenseEntry
    {
        public string Id, Name, OrderNo;
        public int Instances;
    }

    /// <summary>One LicenseInfo block of a .tclrs file: the licenses of one system, with one expiry.</summary>
    internal sealed class LicenseFile
    {
        public string Path, SystemId;
        /// <summary>Null: no expiry (a permanent license).</summary>
        public DateTime? Expires;
        public List<LicenseEntry> Licenses = new List<LicenseEntry>();
        public bool ValidAt(DateTime now) => Expires == null || Expires > now;
    }

    /// <summary>
    /// 'license check' lists the license files of a runtime and checks the required licenses.
    /// 'license trial' makes a license-only project that selects the missing licenses, opens XAE
    /// on it with the target set, and waits while a person clicks "7 Days Trial License..." and
    /// types the security code (Beckhoff makes that step manual). Then it checks the new file and
    /// closes XAE. It never activates: activation would replace the boot project of the target.
    /// Exit codes: 0 = all required licenses valid, 1 = a license is missing or expired, or a step
    /// failed, 2 = wrong command line.
    /// </summary>
    static class LicenseCommands
    {
        internal const string Usage =
            "license check [--target local|umrt] [--package <zip> | --licenses <name[,name]>]\n" +
            "license trial [--target local|umrt] --package <zip> | --licenses <name[,name]>   [--work <folder>] [--wait <minutes>]";

        /// <summary>The TwinCAT 3 licenses bai can select: name -> license id. XAE selects a license by id.</summary>
        internal static readonly IReadOnlyDictionary<string, string> Known = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["TC3 ADS"] = "{4C256767-E6E6-4AF5-BD68-9F7ABAD0C200}",
            ["TC3 IO"] = "{3FF18E97-7754-401B-93FB-70544DE28A13}",
            ["TC3 PLC"] = "{66689887-CCBD-452C-AC9A-039D997C6E66}",
            ["TC3 Controller Toolbox"] = "{777F1598-583B-4503-99BB-7C02E0ABD97E}",
            ["TC3 Temperature-Controller"] = "{1EEBBAAA-51E1-4750-9D6A-7B04116EA32B}",
            ["TC3 Modbus-RTU"] = "{A1AF8A04-C08D-4853-A5A0-0E6C3A278A8C}",
            ["TC3 NC PTP"] = "{520DE751-9DB6-47CB-8240-BD5C466E7E64}",
            ["TC3 NC PTP Axis"] = "{A19036CF-A53B-4E3A-99FF-023EF5C4798B}",
        };

        /// <summary>A trial is also made for a license that ends sooner than this.</summary>
        static readonly TimeSpan RenewBefore = TimeSpan.FromDays(2);

        /// <summary>"A, B" -> the license ids, in order. A name from Known, or a license id in braces.</summary>
        internal static List<KeyValuePair<string, string>> Select(string list)
        {
            var result = new List<KeyValuePair<string, string>>();
            foreach (string item in (list ?? "").Split(',').Select(s => s.Trim()).Where(s => s.Length > 0))
            {
                if (Regex.IsMatch(item, @"\A\{[0-9A-Fa-f-]{36}\}\z")) result.Add(new KeyValuePair<string, string>(item.ToUpperInvariant(), item.ToUpperInvariant()));
                else if (Known.TryGetValue(item, out string id)) result.Add(new KeyValuePair<string, string>(Known.Keys.First(k => k.Equals(item, StringComparison.OrdinalIgnoreCase)), id));
                else throw new ArgumentException(string.Format("unknown license '{0}'. Use a license id {{...}} or one of: {1}.", item, string.Join(", ", Known.Keys)));
            }
            if (result.Count == 0) throw new ArgumentException("--licenses needs at least one license name.");
            return result;
        }

        /// <summary>The licenses a compiled package needs, from its boot files: TC3 ADS always; TC3 IO,
        /// TC3 PLC and TC3 NC PTP (+ Axis) for the TcIo, TcPlc30 and TcNc drivers of CurrentConfig.xml;
        /// and each LicenseId that the PLC .tmc lists (its libraries, for example TF4110).</summary>
        internal static List<KeyValuePair<string, string>> FromPackage(string package)
        {
            var names = new List<string> { "TC3 ADS" };
            var ids = new List<KeyValuePair<string, string>>();
            using (var zip = System.IO.Compression.ZipFile.OpenRead(package))
            {
                var config = zip.GetEntry("boot/CurrentConfig.xml") ?? throw new InvalidDataException("Package has no boot/CurrentConfig.xml: " + package);
                XDocument boot;
                using (var stream = config.Open()) boot = XDocument.Load(stream);
                var drivers = new HashSet<string>(boot.Descendants().Where(e => e.Name.LocalName == "ServiceName").Select(e => e.Value.Trim()), StringComparer.OrdinalIgnoreCase);
                if (drivers.Contains("TcIo")) names.Add("TC3 IO");
                if (drivers.Contains("TcPlc30")) names.Add("TC3 PLC");
                if (drivers.Contains("TcNc")) names.AddRange(new[] { "TC3 NC PTP", "TC3 NC PTP Axis" });
                foreach (var tmc in zip.Entries.Where(e => e.FullName.StartsWith("boot/Plc/", StringComparison.Ordinal) && e.FullName.EndsWith(".tmc", StringComparison.OrdinalIgnoreCase)))
                {
                    XDocument doc;
                    using (var stream = tmc.Open()) doc = XDocument.Load(stream);
                    foreach (XElement license in doc.Descendants().Where(e => e.Name.LocalName == "License"))
                    {
                        string id = license.Elements().FirstOrDefault(e => e.Name.LocalName == "LicenseId")?.Value.Trim().ToUpperInvariant();
                        if (string.IsNullOrEmpty(id)) continue;
                        string name = Known.FirstOrDefault(k => k.Value == id).Key ?? license.Elements().FirstOrDefault(e => e.Name.LocalName == "Comment")?.Value.Trim() ?? id;
                        ids.Add(new KeyValuePair<string, string>(name, id));
                    }
                }
            }
            return names.Select(n => new KeyValuePair<string, string>(n, Known[n])).Concat(ids)
                .GroupBy(p => p.Value).Select(g => g.First()).ToList();
        }

        /// <summary>The required licenses of a package that are missing or expired on the target (local or UmRT).</summary>
        internal static List<string> Missing(string target, string package) =>
            Check(Read(RuntimeTargets.LicenseFolders(target), new List<string>()), FromPackage(package), DateTime.Now)
                .Where(c => c.File == null).Select(c => c.Name + " (" + c.Problem + ")").ToList();

        /// <summary>The LicenseInfo blocks of one .tclrs file.</summary>
        internal static IEnumerable<LicenseFile> Parse(string path, string xml)
        {
            string Value(XElement e, string name) => e.Elements().FirstOrDefault(x => x.Name.LocalName == name)?.Value.Trim();
            foreach (XElement info in XDocument.Parse(xml).Descendants().Where(e => e.Name.LocalName == "LicenseInfo"))
            {
                var file = new LicenseFile { Path = path, SystemId = Value(info, "SystemId") };
                if (DateTime.TryParse(Value(info, "ExpireTime"), CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime expires)) file.Expires = expires;
                foreach (XElement license in info.Elements().Where(e => e.Name.LocalName == "License"))
                    file.Licenses.Add(new LicenseEntry { Id = (Value(license, "LicenseId") ?? "").ToUpperInvariant(), Name = Value(license, "Name"), OrderNo = Value(license, "OrderNo"),
                        Instances = int.TryParse(Value(license, "Instances"), out int n) ? n : 0 });
                yield return file;
            }
        }

        /// <summary>All license files of the target's license folders. A file that cannot be read is reported, not skipped silently.</summary>
        internal static List<LicenseFile> Read(IEnumerable<string> folders, List<string> problems)
        {
            var files = new List<LicenseFile>();
            foreach (string file in folders.Where(Directory.Exists).SelectMany(d => Directory.GetFiles(d, "*.tclrs")))
            {
                try { files.AddRange(Parse(file, File.ReadAllText(file))); }
                catch (Exception ex) when (ex is IOException || ex is System.Xml.XmlException || ex is UnauthorizedAccessException) { problems.Add(Path.GetFileName(file) + ": " + ex.Message); }
            }
            return files;
        }

        /// <summary>For each required license: (name, the valid file that has it or null, why not).</summary>
        internal static List<(string Name, LicenseFile File, string Problem)> Check(List<LicenseFile> files, List<KeyValuePair<string, string>> required, DateTime now)
        {
            var result = new List<(string, LicenseFile, string)>();
            foreach (var r in required)
            {
                bool Has(LicenseFile f) => f.Licenses.Any(l => l.Id == r.Value || string.Equals(l.Name, r.Key, StringComparison.OrdinalIgnoreCase));
                LicenseFile valid = files.Where(f => f.ValidAt(now) && Has(f)).OrderByDescending(f => f.Expires ?? DateTime.MaxValue).FirstOrDefault();
                LicenseFile expired = files.Where(Has).OrderByDescending(f => f.Expires ?? DateTime.MaxValue).FirstOrDefault();
                result.Add((r.Key, valid, valid != null ? null
                    : expired != null ? "expired " + expired.Expires.Value.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture) : "missing"));
            }
            return result;
        }

        static string Until(LicenseFile f, DateTime now) => f.Expires == null ? "permanent"
            : f.ValidAt(now) ? Doctor.DescribeExpiry(f.Expires.Value, now) : "EXPIRED " + f.Expires.Value.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);

        /// <summary>Prints the files and the check; true when every required license is valid
        /// (without --licenses: when at least one license file is valid).</summary>
        static bool Report(string target, IReadOnlyList<string> folders, List<KeyValuePair<string, string>> required)
        {
            var problems = new List<string>();
            var files = Read(folders, problems);
            DateTime now = DateTime.Now;
            Console.WriteLine("License folders of {0}: {1}", target, string.Join("; ", folders.Select(f => f + (Directory.Exists(f) ? "" : " (none)"))));
            foreach (LicenseFile f in files)
            {
                Console.WriteLine("  {0}  system {1}  {2}", Path.GetFileName(f.Path), f.SystemId ?? "?", Until(f, now));
                foreach (LicenseEntry l in f.Licenses)
                    Console.WriteLine("      {0}{1}{2}", l.Name, l.OrderNo != null ? " (" + l.OrderNo + ")" : "", l.Instances > 0 ? ", " + l.Instances + " instances" : "");
            }
            foreach (string p in problems) Console.WriteLine("  UNREADABLE {0}", p);
            if (files.Select(f => f.SystemId).Distinct(StringComparer.OrdinalIgnoreCase).Count() > 1)
                Console.WriteLine("  NOTE: the files are for more than one system id. A file for another system id does not count on this system.");
            if (files.Count == 0)
                Console.WriteLine("  No license file. If TwinCAT runs, its licenses can be on a license dongle: XAE > SYSTEM > License > Online Licenses.");
            if (required == null)
                return files.Any(f => f.ValidAt(now));
            Console.WriteLine();
            Console.WriteLine("Required licenses:");
            var check = Check(files, required, now);
            foreach (var c in check)
                Console.WriteLine("  {0,-8} {1,-28} {2}", c.File != null ? "OK" : "MISSING", c.Name, c.File != null ? Path.GetFileName(c.File.Path) + ", " + Until(c.File, now) : c.Problem);
            return check.All(c => c.File != null);
        }

        internal static int Run(string[] args, string shellName)
        {
            if (args.Skip(1).Any(a => a == "--help" || a == "-h"))
                return Cli.CliHelp.Write(Console.Out, Console.Error, Cli.CliHelp.HelpPath(args), Cli.ConsoleLayout.TerminalWidth);
            Dictionary<string, string> o;
            string target;
            List<KeyValuePair<string, string>> required;
            int waitMinutes = 15;
            try
            {
                if (args.Length < 2 || (args[1] != "check" && args[1] != "trial")) throw new ArgumentException("license needs a subcommand: check or trial.");
                o = Release.ArtifactCommands.Options(args, new string[0], new[] { "--target", "--licenses", "--package", "--work", "--wait" });
                target = RuntimeTargets.Normalize(Release.ArtifactCommands.Get(o, "--target", RuntimeTargets.Local));
                if (target != RuntimeTargets.Local && target != RuntimeTargets.UmRt)
                    throw new ArgumentException("license reads the license files on this PC: --target local or umrt. For an IPC, run bai on the IPC.");
                string list = Release.ArtifactCommands.Get(o, "--licenses"), package = Release.ArtifactCommands.Get(o, "--package");
                if (list != null && package != null) throw new ArgumentException("use --package or --licenses, not both.");
                if (package != null && !File.Exists(package)) throw new ArgumentException("package not found: " + package);
                required = list != null ? Select(list) : package != null ? FromPackage(package) : null;
                if (args[1] == "trial" && required == null) throw new ArgumentException("license trial needs --package or --licenses.");
                if (args[1] == "check" && (o.ContainsKey("--work") || o.ContainsKey("--wait"))) throw new ArgumentException("--work and --wait are for license trial.");
                if (o.ContainsKey("--wait") && (!int.TryParse(o["--wait"], out waitMinutes) || waitMinutes < 1)) throw new ArgumentException("--wait needs a number of minutes (1 or more).");
            }
            catch (ArgumentException ex) { Cli.CliHelp.WriteError(Console.Error, args, ex.Message); return 2; }
            catch (Exception ex) when (ex is IOException || ex is InvalidDataException || ex is System.Xml.XmlException)
            { Console.Error.WriteLine("FAIL: cannot read the package: " + ex.Message); return 1; }

            var folders = RuntimeTargets.LicenseFolders(target);
            if (args[1] == "check")
                return Report(target, folders, required) ? 0 : 1;
            try
            {
                return Trial(target, folders, required, Release.ArtifactCommands.Get(o, "--work", @"C:\bai\work\license"), TimeSpan.FromMinutes(waitMinutes), shellName);
            }
            catch (Exception ex) when (ex is IOException || ex is InvalidOperationException || ex is System.Runtime.InteropServices.COMException
                                       || ex is UnauthorizedAccessException || ex is TimeoutException || ex is System.Xml.XmlException)
            {
                Console.Error.WriteLine("FAIL: " + ex.Message);
                return 1;
            }
        }

        /// <summary>The license-only project: XAE reads ManualSelect as the licenses to order.</summary>
        internal static string ProjectXml(IEnumerable<string> ids) =>
            "<?xml version=\"1.0\"?>\r\n<TcSmProject TcSmVersion=\"1.0\">\r\n\t<Project ProjectGUID=\"{" + Guid.NewGuid().ToString().ToUpperInvariant() + "}\" Target64Bit=\"true\">\r\n" +
            "\t\t<System>\r\n\t\t\t<Licenses>\r\n\t\t\t\t<Target>\r\n" + string.Concat(ids.Select(id => "\t\t\t\t\t<ManualSelect>" + id + "</ManualSelect>\r\n")) +
            "\t\t\t\t</Target>\r\n\t\t\t</Licenses>\r\n\t\t</System>\r\n\t</Project>\r\n</TcSmProject>\r\n";

        static int Trial(string target, IReadOnlyList<string> folders, List<KeyValuePair<string, string>> required, string work, TimeSpan wait, string shellName)
        {
            DateTime now = DateTime.Now;
            var current = Read(folders, new List<string>());
            // Only what is missing, expired or ends soon: a trial never replaces a permanent license.
            var needed = Check(current, required, now)
                .Where(c => c.File == null || (c.File.Expires != null && c.File.Expires - now < RenewBefore))
                .Select(c => required.First(r => r.Key == c.Name)).ToList();
            if (needed.Count == 0)
            {
                Report(target, folders, required);
                Console.WriteLine();
                Console.WriteLine("Nothing to do: every required license is valid for more than {0} days.", RenewBefore.TotalDays);
                return 0;
            }
            string netId = RuntimeTargets.NetId(target);
            string folder = Path.Combine(Path.GetFullPath(work), target + "-" + now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture));
            Directory.CreateDirectory(folder);
            string tsproj = Path.Combine(folder, "License.tsproj");
            File.WriteAllText(tsproj, ProjectXml(needed.Select(n => n.Value)));
            Console.WriteLine("{0}: License project {1}: {2}", Now(), tsproj, string.Join(", ", needed.Select(n => n.Key)));

            var before = folders.Where(Directory.Exists).SelectMany(d => Directory.GetFiles(d, "*.tclrs")).ToDictionary(f => f, File.GetLastWriteTimeUtc, StringComparer.OrdinalIgnoreCase);
            DteShell shell = DteShellResolver.Resolve(shellName, new WindowsComRegistry(), File.Exists);
            MessageFilter.Register();
            var dte = (EnvDTE80.DTE2)Activator.CreateInstance(Type.GetTypeFromProgID(shell.ProgId));
            bool close = false;
            try
            {
                VisualStudioSession.WaitForVsToLoad(dte);
                dte.UserControl = true; // a person works in this XAE; it stays open if bai stops
                dte.Solution.Create(folder, "License");
                EnvDTE.Project project = dte.Solution.AddFromFile(tsproj);
                ((ITcSysManager14)project.Object).SetTargetNetId(netId);
                dte.Solution.SaveAs(Path.Combine(folder, "License.sln"));
                dte.ExecuteCommand("File.SaveAll");
                Thread.Sleep(3000);
                // XAE writes back what it read: the target and its own list of the selected licenses.
                XElement saved = XDocument.Load(tsproj).Root.Elements().First(e => e.Name.LocalName == "Project");
                // Local is the XAE default target: XAE writes no TargetNetId for it.
                string savedTarget = (string)saved.Attribute("TargetNetId") ?? (target == RuntimeTargets.Local ? netId : "");
                int kept = saved.Descendants().Count(e => e.Name.LocalName == "ManualSelect");
                if (savedTarget != netId) { close = true; throw new InvalidOperationException("XAE target is '" + savedTarget + "', not " + netId + ". No license was made."); }
                if (kept != needed.Count) { close = true; throw new InvalidOperationException("XAE kept " + kept + " of " + needed.Count + " licenses. No license was made."); }

                Console.WriteLine("{0}: XAE is open. Target {1} ({2}).", Now(), target, netId);
                Console.WriteLine();
                Console.WriteLine("Now in XAE:");
                Console.WriteLine("  1. Solution Explorer: double-click License > SYSTEM > License.");
                Console.WriteLine("  2. Tab \"Order Information (Runtime)\": check the licenses and the target {0}.", netId);
                Console.WriteLine("  3. Click \"7 Days Trial License...\", type the security code, click OK.");
                Console.WriteLine("  4. Do NOT activate the configuration. bai closes XAE when the license file is there.");
                Console.WriteLine();
                Console.WriteLine("{0}: Waiting for the new license file (max {1} min)...", Now(), (int)wait.TotalMinutes);
                DateTime deadline = DateTime.Now + wait;
                List<string> fresh;
                do
                {
                    Thread.Sleep(3000);
                    fresh = folders.Where(Directory.Exists).SelectMany(d => Directory.GetFiles(d, "*.tclrs"))
                        .Where(f => !before.TryGetValue(f, out DateTime t) || File.GetLastWriteTimeUtc(f) > t).ToList();
                }
                while (fresh.Count == 0 && DateTime.Now < deadline);
                if (fresh.Count == 0)
                {
                    close = true;
                    throw new TimeoutException("No new license file in " + (int)wait.TotalMinutes + " min. Run this again.");
                }
                Thread.Sleep(2000); // the file can still be in writing
                Console.WriteLine("{0}: New license file: {1}", Now(), string.Join(", ", fresh.Select(Path.GetFileName)));
                close = true;
                Console.WriteLine();
                return Report(target, folders, required) ? 0 : 1;
            }
            finally
            {
                if (close) Close(dte);
            }
        }

        /// <summary>Closes the license project without saving and quits XAE. A failure only means
        /// that the person closes XAE.</summary>
        static void Close(EnvDTE80.DTE2 dte)
        {
            var quit = new Thread(() =>
            {
                try { dte.Solution.Close(false); dte.Quit(); }
                catch (Exception) { /* XAE busy or already closed */ }
            }) { IsBackground = true };
            quit.Start();
            Console.WriteLine(quit.Join(30000) ? Now() + ": XAE closed (not saved, not activated)." : Now() + ": Close XAE without saving.");
            MessageFilter.Revoke();
        }
    }
}
