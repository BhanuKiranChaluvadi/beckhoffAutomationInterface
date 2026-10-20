using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using BeckhoffAutomationInterface.Machines;
using BeckhoffAutomationInterface.Runtime;
using static BeckhoffAutomationInterface.Release.ArtifactFiles;

namespace BeckhoffAutomationInterface.Release
{
    /// <summary>The runtime and remote steps of an update. Each step writes its output to a log.</summary>
    internal interface IUpdateHost
    {
        /// <summary>A bai runtime command ('runtime-state' or 'verify'): its exit code.</summary>
        int Bai(string log, params string[] args);
        /// <summary>An external program (ssh, scp): its exit code.</summary>
        int Run(string log, string program, params string[] args);
        /// <summary>SHA256 of the running bai (evidence).</summary>
        string BaiSha256 { get; }
    }

    /// <summary>The real host: bai commands in this process, programs as child processes.</summary>
    internal sealed class ProcessHost : IUpdateHost
    {
        public int Bai(string log, params string[] args)
        {
            using (TeeWriter.Capture(log))
                return args[0] == "verify" ? RuntimeCommands.Verify(args)
                     : args[0] == "runtime-state" ? RuntimeStateCommand.Run(args)
                     : throw new ArgumentException("Unknown runtime step: " + args[0]);
        }
        public int Run(string log, string program, params string[] args)
        {
            using (TeeWriter.Capture(log))
                return MachineBuilder.RunCommand(new[] { program }.Concat(args).ToList(), Path.GetDirectoryName(log));
        }
        public string BaiSha256 => Hash(System.Reflection.Assembly.GetEntryAssembly().Location).ToUpperInvariant();
    }

    internal sealed class UpdateRequest
    {
        public string Package, PackageSha256, Current, CurrentSha256, ServiceProject, Evidence, Report, SshHost;
        public string Target = RuntimeTargets.Local;
        public bool Rehearsal;
        /// <summary>Default: the boot folder and update lock of the target.</summary>
        public string Boot, LockFile;
    }

    /// <summary>
    /// 'package preflight' and 'package install': replace a compiled boot image on a machine (or
    /// on UmRT for a rehearsal) without a compiler or XAE. The ADS route must already exist.
    /// Before Config mode: the saved service project has no drift from the installed package,
    /// and the live identity and online-change counter match it. Every old boot file is kept;
    /// a failed activation or identity check restores the old image and its identity.
    /// </summary>
    internal static class PackageInstaller
    {
        internal const string UmRtLock = @"C:\bai\lock\umrt.lock";
        const string OsX64 = "TwinCAT OS (x64)";

        static void Write(string path, object value) => File.WriteAllText(path, Encode(value), new UTF8Encoding(false));

        static void Extract(string package, string entryName, string destination)
        {
            using (var zip = ZipFile.OpenRead(package))
            {
                var entry = PackageVerifier.Entries(zip).TryGetValue(entryName, out var found) ? found : throw new InvalidDataException("Package has no " + entryName);
                Directory.CreateDirectory(Path.GetDirectoryName(destination));
                entry.ExtractToFile(destination);
            }
        }

        /// <summary>The compiled identity checks of the package plus one more check, as a new expect file.</summary>
        static void WriteChecks(string identityFile, string machine, Dictionary<string, object> extra, string output)
        {
            var identity = Read(identityFile);
            var variants = Object(Field(identity, "variants"));
            var checks = variants.TryGetValue(machine, out object list) && list is object[] items ? items.ToList() : new List<object>();
            if (checks.Count < 5) throw new InvalidDataException("Missing compiled identity checks.");
            checks.Add(extra);
            variants[machine] = checks;
            Write(output, identity);
        }

        static Dictionary<string, object> Check(string symbol, string type, object equals) =>
            new Dictionary<string, object> { ["symbol"] = symbol, ["type"] = type, ["equals"] = equals };

        static void Step(IUpdateHost host, string folder, string name, params string[] args)
        {
            int code = host.Bai(Path.Combine(folder, name + ".log"), args);
            if (code != 0) throw new InvalidOperationException(name + " failed (exit " + code + ").");
        }

        static void Remote(IUpdateHost host, string folder, string name, string ssh, string command)
        {
            int code = host.Run(Path.Combine(folder, name + ".log"), "ssh", "-o", "BatchMode=yes", "-o", "StrictHostKeyChecking=yes", ssh, command);
            if (code != 0) throw new InvalidOperationException(name + " failed (exit " + code + "). Keep local and remote recovery evidence.");
        }

        static void Copy(IUpdateHost host, string folder, string name, string source, string destination)
        {
            int code = host.Run(Path.Combine(folder, name + ".log"), "scp", "-B", "-o", "StrictHostKeyChecking=yes", source, destination);
            if (code != 0) throw new InvalidOperationException(name + " failed (exit " + code + "). Keep local and remote recovery evidence.");
        }

        /// <summary>Checks the CURRENT installed release before it is replaced. No runtime writes.</summary>
        internal static Dictionary<string, object> Preflight(UpdateRequest q, IUpdateHost host)
        {
            string target = RuntimeTargets.Normalize(q.Target);
            if (q.Rehearsal && target != RuntimeTargets.UmRt) throw new ArgumentException("Development rehearsal is restricted to UmRT.");
            string project = Path.GetFullPath(q.ServiceProject), report = Path.GetFullPath(q.Report);
            if (!Directory.Exists(project)) throw new DirectoryNotFoundException("Service project not found: " + project);
            BuildLocation.Assert(project, report);
            if (Directory.Exists(report) || File.Exists(report)) throw new IOException("Use a new report directory.");
            var package = PackageVerifier.Verify(q.Current, q.CurrentSha256);
            if (Number(package, "schemaVersion") != 2) throw new InvalidDataException("Update gate requires a schema 2 package with compiled identity.");
            if (!q.Rehearsal && Text(package, "kind") != "ReleaseCandidate") throw new InvalidDataException("Reconcile the installed development build before updating a machine.");
            string machine = Text(package, "machine");
            Directory.CreateDirectory(report);
            var record = new Dictionary<string, object> { ["schemaVersion"] = 1, ["status"] = "Checking", ["target"] = target, ["rehearsal"] = q.Rehearsal,
                ["currentPackageSha256"] = q.CurrentSha256, ["machine"] = machine, ["serviceProject"] = project };
            try
            {
                string baseline = Path.Combine(report, "project.zip"), identity = Path.Combine(report, "identity.expect.json");
                Extract(q.Current, "project.zip", baseline);
                Extract(q.Current, "identity.expect.json", identity);
                string baselineHash = Hash(baseline).ToUpperInvariant();
                if (ProjectDrift.Run(baseline, baselineHash, project, Path.Combine(report, "drift.json")) != 0)
                    throw new InvalidDataException("Saved project differs from the installed release. Export and reconcile it before updating.");
                string expect = Path.Combine(report, "current.expect.json");
                WriteChecks(identity, machine, Check("TwinCAT_SystemInfoVarList._AppInfo.OnlineChangeCnt", "UDINT", 0), expect);
                if (host.Bai(Path.Combine(report, "online.log"), "verify", "--target", target, "--expect", expect, "--variant", machine) != 0)
                    throw new InvalidOperationException("Live identity or online-change counter differs. No update is permitted.");
                record["status"] = "Passed";
                record["baselineSha256"] = baselineHash;
                record["baiSha256"] = host.BaiSha256;
                Console.WriteLine("PASS: saved project, live release identity and online-change counter.");
                return record;
            }
            catch (Exception ex) { record["status"] = "Failed"; record["error"] = ex.Message; throw; }
            finally
            {
                record["completedUtc"] = DateTime.UtcNow.ToString("o");
                Write(Path.Combine(report, "preflight.json"), record);
            }
        }

        static (Dictionary<string, object> Candidate, Dictionary<string, object> Current) Packages(UpdateRequest q)
        {
            var candidate = PackageVerifier.Verify(q.Package, q.PackageSha256);
            var current = PackageVerifier.Verify(q.Current, q.CurrentSha256);
            return (candidate, current);
        }

        static string NewEvidence(UpdateRequest q, string protectedRoot)
        {
            string evidence = Path.GetFullPath(q.Evidence);
            BuildLocation.Assert(protectedRoot, evidence);
            if (Directory.Exists(evidence) || File.Exists(evidence)) throw new IOException("Use a new evidence directory.");
            return evidence;
        }

        /// <summary>Installs the candidate. On a failure after Config, rolls back and throws.</summary>
        internal static Dictionary<string, object> Install(UpdateRequest q, IUpdateHost host) =>
            q.SshHost != null ? InstallBsd(q, host) : InstallWindows(q, host);

        static Dictionary<string, object> InstallWindows(UpdateRequest q, IUpdateHost host)
        {
            string target = RuntimeTargets.Normalize(q.Target);
            if (target != RuntimeTargets.Local && target != RuntimeTargets.UmRt) throw new ArgumentException("A Windows update runs on the IPC itself: --target local or umrt. For TwinCAT/BSD give --ssh.");
            if (q.Rehearsal && target != RuntimeTargets.UmRt) throw new ArgumentException("Development rehearsal is restricted to UmRT.");
            string boot = Path.GetFullPath(q.Boot ?? RuntimeTargets.BootFolder(target)).TrimEnd('\\');
            string evidence = NewEvidence(q, boot);
            var (candidate, current) = Packages(q);
            string machine = Text(candidate, "machine");
            if (Number(candidate, "schemaVersion") != 2 || machine != Text(current, "machine")) throw new InvalidDataException("Candidate identity/machine does not match the installed release.");
            if (!q.Rehearsal && Text(candidate, "kind") != "ReleaseCandidate") throw new InvalidDataException("Development packages cannot be installed on a machine.");
            if ((string)Xml(Path.Combine(boot, "CurrentConfig.xml")).Root.Attribute("Platform") != Text(candidate, "targetPlatform"))
                throw new InvalidDataException("Candidate platform differs from the current runtime boot platform.");
            string lockPath = q.LockFile ?? (target == RuntimeTargets.UmRt ? UmRtLock : Path.Combine(Path.GetDirectoryName(boot), "boot-update.lock"));
            Directory.CreateDirectory(Path.GetDirectoryName(lockPath));
            FileStream held = MachineCommands.TryLock(lockPath) ?? throw new IOException("Another update or test holds the lock: " + lockPath);
            Directory.CreateDirectory(evidence);
            var record = new Dictionary<string, object> { ["schemaVersion"] = 1, ["status"] = "Checking", ["machine"] = machine, ["packageSha256"] = q.PackageSha256,
                ["target"] = target, ["rehearsal"] = q.Rehearsal, ["physicalAcceptance"] = false };
            try
            {
                Preflight(new UpdateRequest { Current = q.Current, CurrentSha256 = q.CurrentSha256, ServiceProject = q.ServiceProject,
                    Report = Path.Combine(evidence, "preflight"), Target = target, Rehearsal = q.Rehearsal }, host);
                using (var zip = ZipFile.OpenRead(q.Package))
                    foreach (var entry in PackageVerifier.Entries(zip).Where(e => e.Key.StartsWith("boot/", StringComparison.Ordinal) || e.Key == "identity.expect.json"))
                    {
                        string path = Path.Combine(evidence, entry.Key);
                        Directory.CreateDirectory(Path.GetDirectoryName(path));
                        entry.Value.ExtractToFile(path);
                    }
                string installed = Path.Combine(evidence, "installed.expect.json"), transactionFile = Path.Combine(evidence, "transaction.json");
                WriteChecks(Path.Combine(evidence, "identity.expect.json"), machine, Check("GVL_Simulation.bActive", "BOOL", false), installed);
                try
                {
                    Step(host, evidence, "config", "runtime-state", "--target", target, "--state", "config", "--yes");
                    Step(host, evidence, "confirm-config", "runtime-state", "--target", target, "--expect", "config");
                    BootTransaction.Install(Path.Combine(evidence, "boot"), boot, transactionFile);
                    Step(host, evidence, "run", "runtime-state", "--target", target, "--state", "run", "--yes");
                    Step(host, evidence, "identity", "verify", "--target", target, "--expect", installed, "--variant", machine);
                    record["status"] = "Installed";
                }
                catch (Exception ex)
                {
                    record["installError"] = ex.Message;
                    Step(host, evidence, "rollback-config", "runtime-state", "--target", target, "--state", "config", "--yes");
                    if (File.Exists(transactionFile))
                    {
                        var transaction = Read(transactionFile);
                        if ((string)transaction["state"] == "RestoredAfterFailure")
                            SameFiles(Array(transaction, "before"), BootTransaction.BootInventory(boot), "Recovered boot after copy failure");
                        else
                            BootTransaction.Restore(transactionFile);
                    }
                    Step(host, evidence, "rollback-run", "runtime-state", "--target", target, "--state", "run", "--yes");
                    Step(host, evidence, "rollback-identity", "verify", "--target", target, "--expect", Path.Combine(evidence, "preflight", "identity.expect.json"), "--variant", Text(current, "machine"));
                    record["status"] = "RolledBack";
                    throw new InvalidOperationException("Installation failed; the previous boot image and identity were restored. " + ex.Message);
                }
            }
            catch (Exception ex) { if ((string)record["status"] != "RolledBack") record["status"] = "Failed"; record["error"] = ex.Message; throw; }
            finally
            {
                record["completedUtc"] = DateTime.UtcNow.ToString("o");
                Write(Path.Combine(evidence, "install.json"), record);
                held.Dispose();
            }
            Console.WriteLine("PASS: compiled files installed and live identity verified. Evidence: " + evidence);
            return record;
        }

        /// <summary>TwinCAT/BSD from the build PC over SSH. The target needs python3, an ADS route, a
        /// trusted SSH host key and noninteractive doas for the helper. The helper reserves the
        /// host-side lock before the first ADS state write.</summary>
        static Dictionary<string, object> InstallBsd(UpdateRequest q, IUpdateHost host)
        {
            if (!RuntimeTargets.IsNetId(q.Target)) throw new ArgumentException("--ssh needs --target <AMS-Net-ID> of the TwinCAT/BSD host.");
            string target = RuntimeTargets.Normalize(q.Target), ssh = q.SshHost;
            if (!Regex.IsMatch(ssh, @"\A[A-Za-z0-9_][A-Za-z0-9_.-]*@[A-Za-z0-9][A-Za-z0-9.-]*\z")) throw new ArgumentException("--ssh must be user@host.");
            if (q.Rehearsal) throw new ArgumentException("Development rehearsal is restricted to UmRT.");
            string evidence = NewEvidence(q, q.ServiceProject);
            var (candidate, current) = Packages(q);
            string machine = Text(candidate, "machine");
            if (Number(candidate, "schemaVersion") != 2 || Text(candidate, "kind") != "ReleaseCandidate" || Text(candidate, "targetPlatform") != OsX64 || machine != Text(current, "machine"))
                throw new InvalidDataException("Candidate must be an OS x64 release for the current machine.");
            Directory.CreateDirectory(evidence);
            string remote = "/var/tmp/plc-release-" + Guid.NewGuid().ToString("N");
            string helper = "doas -n python3 " + remote + "/helper.py ";
            string transaction = " --net-id " + target + " --transaction " + remote + "/transaction";
            var record = new Dictionary<string, object> { ["status"] = "Checking", ["target"] = target, ["sshHost"] = ssh, ["remoteEvidence"] = remote,
                ["packageSha256"] = q.PackageSha256, ["physicalAcceptance"] = false };
            try
            {
                Preflight(new UpdateRequest { Current = q.Current, CurrentSha256 = q.CurrentSha256, ServiceProject = q.ServiceProject,
                    Report = Path.Combine(evidence, "preflight"), Target = target }, host);
                Remote(host, evidence, "host", ssh, "TcSysExe.exe --netid");
                if (!Regex.IsMatch(File.ReadAllText(Path.Combine(evidence, "host.log")), @"(?im)AMS Net Id:\s*" + Regex.Escape(target) + @"\s*$"))
                    throw new InvalidOperationException("SSH host does not match the ADS target.");
                Remote(host, evidence, "prepare", ssh, "mkdir -m 700 " + remote);
                string helperFile = Path.Combine(evidence, "helper.py");
                using (var resource = typeof(PackageInstaller).Assembly.GetManifestResourceStream("bsd_boot_transaction.py"))
                using (var output = new FileStream(helperFile, FileMode.CreateNew)) resource.CopyTo(output);
                Copy(host, evidence, "upload-helper", helperFile, ssh + ":" + remote + "/helper.py");
                Copy(host, evidence, "upload-package", Path.GetFullPath(q.Package), ssh + ":" + remote + "/package.zip");
                Extract(q.Package, "identity.expect.json", Path.Combine(evidence, "identity.expect.json"));
                string installed = Path.Combine(evidence, "installed.expect.json");
                WriteChecks(Path.Combine(evidence, "identity.expect.json"), machine, Check("GVL_Simulation.bActive", "BOOL", false), installed);
                // A second updater must fail here, without interrupting the one that owns the lock.
                Remote(host, evidence, "reserve", ssh, helper + "reserve" + transaction);
                try
                {
                    Step(host, evidence, "config", "runtime-state", "--target", target, "--state", "config", "--yes");
                    Remote(host, evidence, "install", ssh, helper + "install" + transaction + " --package " + remote + "/package.zip --sha256 " + q.PackageSha256);
                    Step(host, evidence, "run", "runtime-state", "--target", target, "--state", "run", "--yes");
                    Step(host, evidence, "identity", "verify", "--target", target, "--expect", installed, "--variant", machine);
                    Remote(host, evidence, "finish", ssh, helper + "finish" + transaction);
                    record["status"] = "Installed";
                }
                catch (Exception ex)
                {
                    record["installError"] = ex.Message;
                    Step(host, evidence, "rollback-config", "runtime-state", "--target", target, "--state", "config", "--yes");
                    Remote(host, evidence, "rollback", ssh, helper + "rollback" + transaction);
                    Step(host, evidence, "rollback-run", "runtime-state", "--target", target, "--state", "run", "--yes");
                    Step(host, evidence, "rollback-identity", "verify", "--target", target, "--expect", Path.Combine(evidence, "preflight", "identity.expect.json"), "--variant", Text(current, "machine"));
                    Remote(host, evidence, "rollback-finish", ssh, helper + "finish" + transaction);
                    record["status"] = "RolledBack";
                    throw new InvalidOperationException("Installation failed; the previous image and identity were restored. " + ex.Message);
                }
            }
            catch (Exception ex) { if ((string)record["status"] != "RolledBack") record["status"] = "Failed"; record["error"] = ex.Message; throw; }
            finally
            {
                record["completedUtc"] = DateTime.UtcNow.ToString("o");
                Write(Path.Combine(evidence, "install.json"), record);
            }
            Console.WriteLine("PASS: compiled release copied to BSD and live identity verified. Evidence: " + evidence);
            return record;
        }
    }
}
