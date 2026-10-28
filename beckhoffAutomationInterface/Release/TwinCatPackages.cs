using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using BeckhoffAutomationInterface.Machines;

namespace BeckhoffAutomationInterface.Release
{
    /// <summary>
    /// 'toolchain check|install': the TwinCAT packages (TcPkg) of bai.machines.json "release" "packages".
    /// The packages decide the library versions of each build, so install adds only what is missing and
    /// never changes an installed version (a person decides that, and then makes a new library lock).
    /// install also switches on the 64-bit XAE shell (bai cannot drive the 32-bit one), shows a TcPkg dry
    /// run and installs only after a typed 'y'. Exit codes: 0 = every package there and the 64-bit shell
    /// on, 1 = not, or a step failed, 2 = wrong command line.
    /// </summary>
    internal static class TwinCatPackages
    {
        /// <summary>Runs tcpkg; fills 'output' with its lines and, with echo, also prints them. Returns the exit code.</summary>
        internal delegate int TcPkg(string[] arguments, bool echo, List<string> output);

        internal sealed class Row
        {
            public TwinCatPackage Package;
            public string Found, Installed, Status;
        }

        /// <summary>'tcpkg list -i' as name -> installed versions.</summary>
        internal static Dictionary<string, List<string>> Installed(IEnumerable<string> lines)
        {
            var installed = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
            foreach (string line in lines)
            {
                var m = Regex.Match(line, @"^(\S+)\s+(\d+(\.\d+)+)\s*$");
                if (!m.Success) continue;
                if (!installed.TryGetValue(m.Groups[1].Value, out var versions)) installed[m.Groups[1].Value] = versions = new List<string>();
                versions.Add(m.Groups[2].Value);
            }
            return installed;
        }

        /// <summary>OK, MISSING or DIFFERENT for each package. The name, its old name (alias) or its library
        /// package counts; a library package counts in any version.</summary>
        internal static List<Row> Compare(IEnumerable<TwinCatPackage> packages, Dictionary<string, List<string>> installed) =>
            packages.Select(p =>
            {
                string found = new[] { p.Name, p.Alias, p.Lib }.FirstOrDefault(n => n != null && installed.ContainsKey(n));
                return new Row
                {
                    Package = p, Found = found, Installed = found == null ? "" : string.Join(", ", installed[found]),
                    Status = found == null ? "MISSING" : installed[found].Contains(p.Version) || found == p.Lib ? "OK" : "DIFFERENT",
                };
            }).ToList();

        internal static int Run(string[] args)
        {
            var o = ArtifactCommands.Options(args, new string[0], new[] { "--manifest" });
            var manifest = MachineManifest.Load(MachineManifest.Find(ArtifactCommands.Get(o, "--manifest"), Directory.GetCurrentDirectory()));
            var packages = manifest.Release?.Packages;
            if (packages == null || packages.Count == 0) throw new ArgumentException(MachineManifest.FileName + " has no \"release\" \"packages\".");
            if (args[1] == "install")
                using (var identity = System.Security.Principal.WindowsIdentity.GetCurrent())
                    if (!new System.Security.Principal.WindowsPrincipal(identity).IsInRole(System.Security.Principal.WindowsBuiltInRole.Administrator))
                        throw new InvalidOperationException("Run bai as administrator: TcPkg installs for all users. Nothing changed.");
            return Run(args[1] == "install", packages, RunTcPkg, Console.In);
        }

        internal static int Run(bool install, IReadOnlyList<TwinCatPackage> packages, TcPkg tcpkg, TextReader input)
        {
            var lines = new List<string>();
            if (tcpkg(new[] { "source", "list" }, false, lines) != 0)
                throw new InvalidOperationException("tcpkg does not run. Install the TwinCAT Package Manager, start it once and sign in with myBeckhoff.");
            bool signedIn = lines.Any(l => l.Contains("Beckhoff Stable Feed") && l.Contains("Authenticated"));
            Console.WriteLine(signedIn ? "OK        Beckhoff Stable Feed signed in"
                : "WARNING   Beckhoff Stable Feed not signed in: start the TwinCAT Package Manager and sign in with myBeckhoff (needed to install)");
            lines.Clear();
            tcpkg(new[] { "config", "list" }, false, lines);
            bool shell64 = lines.Any(l => Regex.IsMatch(l, @"^UseTcXaeShell64:\s*True", RegexOptions.IgnoreCase));
            var rows = Report(packages, tcpkg, shell64);
            int missing = rows.Count(r => r.Status == "MISSING");
            if (!install || (missing == 0 && shell64)) return Result(missing, shell64);

            if (!signedIn) throw new InvalidOperationException("Sign in to the Beckhoff Stable Feed first (TwinCAT Package Manager, myBeckhoff). Nothing installed.");
            if (!shell64)
            {
                Console.WriteLine("Switch on useTcXaeShell64:");
                if (tcpkg(new[] { "config", "set", "-n", "useTcXaeShell64", "-y" }, true, new List<string>()) != 0)
                    throw new InvalidOperationException("tcpkg config set -n useTcXaeShell64 failed.");
            }
            if (missing > 0)
            {
                string[] names = rows.Where(r => r.Status == "MISSING").Select(r => r.Package.Name + "=" + r.Package.Version).ToArray();
                Console.WriteLine();
                Console.WriteLine("Dry run (changes nothing): tcpkg install " + string.Join(" ", names));
                // --dry-run alone still asks for a confirmation; -y only answers it.
                if (tcpkg(new[] { "install" }.Concat(names).Concat(new[] { "--dry-run", "-y" }).ToArray(), true, new List<string>()) != 0)
                    throw new InvalidOperationException("The dry run failed. Nothing installed.");
                Console.WriteLine();
                Console.WriteLine("Read the dry run. It can upgrade a dependency of a missing package, but it must not change");
                Console.WriteLine("TwinCAT.Standard.* or a library (TwinCAT.XAE.PLC.Lib.*). The install can restart the TwinCAT");
                Console.WriteLine("system service (local runtime and UmRT_Default).");
                // A whole line: a caller that reads the output line by line shows it before the answer.
                Console.WriteLine("Install now? Type y and Enter (anything else stops):");
                if ((input.ReadLine() ?? "").Trim() != "y") throw new InvalidOperationException("Stopped. Nothing installed.");
                if (tcpkg(new[] { "install" }.Concat(names).Concat(new[] { "-y" }).ToArray(), true, new List<string>()) != 0)
                    throw new InvalidOperationException("tcpkg install failed. If TcPkg asks for a restart: restart the PC, then run this again.");
            }
            Console.WriteLine();
            Console.WriteLine("After the install:");
            lines.Clear();
            tcpkg(new[] { "config", "list" }, false, lines);
            shell64 = lines.Any(l => Regex.IsMatch(l, @"^UseTcXaeShell64:\s*True", RegexOptions.IgnoreCase));
            rows = Report(packages, tcpkg, shell64);
            return Result(rows.Count(r => r.Status == "MISSING"), shell64);
        }

        static List<Row> Report(IReadOnlyList<TwinCatPackage> packages, TcPkg tcpkg, bool shell64)
        {
            Console.WriteLine(shell64 ? "OK        useTcXaeShell64 on (64-bit XAE shell)" : "MISSING   useTcXaeShell64 off: bai needs the 64-bit XAE shell");
            var lines = new List<string>();
            if (tcpkg(new[] { "list", "-i" }, false, lines) != 0) throw new InvalidOperationException("tcpkg list -i failed.");
            var rows = Compare(packages, Installed(lines));
            foreach (var r in rows)
                Console.WriteLine(r.Status == "DIFFERENT"
                    ? string.Format("{0,-9} {1,-34} installed {2}, list {3}: not changed (on purpose only, then a new library lock)", "WARNING", r.Found, r.Installed, r.Package.Version)
                    : string.Format("{0,-9} {1,-34} {2,-10} {3}", r.Status, r.Found ?? r.Package.Name, r.Status == "OK" ? r.Installed : r.Package.Version, r.Package.Why));
            return rows;
        }

        static int Result(int missing, bool shell64)
        {
            Console.WriteLine();
            if (missing == 0 && shell64) { Console.WriteLine("PASS: every TwinCAT package of " + MachineManifest.FileName + " is installed."); return 0; }
            Console.WriteLine("FAIL: {0} package(s) missing{1}. Run 'bai toolchain install' as administrator.", missing, shell64 ? "" : ", 64-bit XAE shell off");
            return 1;
        }

        static int RunTcPkg(string[] arguments, bool echo, List<string> output)
        {
            var psi = new ProcessStartInfo("tcpkg", string.Join(" ", arguments.Select(BuildInfo.Quote)))
            {
                UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true,
                StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8,
            };
            Process p;
            try { p = Process.Start(psi); }
            catch (System.ComponentModel.Win32Exception) { return -1; }   // tcpkg is not installed
            using (p)
            {
                void Line(object s, DataReceivedEventArgs e)
                {
                    if (e.Data == null) return;
                    lock (output) output.Add(e.Data);
                    if (echo) Console.WriteLine("  " + e.Data);
                }
                p.OutputDataReceived += Line;
                p.ErrorDataReceived += Line;
                p.BeginOutputReadLine();
                p.BeginErrorReadLine();
                p.WaitForExit();
                return p.ExitCode;
            }
        }
    }
}
