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
        /// <summary>The licenses the package needs that are missing or expired on the target.</summary>
        List<string> MissingLicenses(string target, string package);
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
        public List<string> MissingLicenses(string target, string package) => LicenseCommands.Missing(target, package);
    }

    internal sealed class UpdateRequest
    {
        public string Package, PackageSha256, Current, CurrentSha256, ServiceProject, Evidence, Report, SshHost;
        public string Target = RuntimeTargets.Local;
        /// <summary>Legacy: the machine has no bai package yet (its first update), so there is no
        /// current package or service project to compare.</summary>
        public bool Rehearsal, Legacy;
        /// <summary>Only the read-only checks (no --yes): nothing changes, no evidence folder.</summary>
        public bool CheckOnly;
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
        const string OnlineChanges = "TwinCAT_SystemInfoVarList._AppInfo.OnlineChangeCnt";
        /// <summary>The expect-file variant of a legacy machine: only the online-change counter.</summary>
        const string LegacyVariant = "legacy";
        /// <summary>The copy of the old boot image in the evidence folder.</summary>
        internal const string BackupArchive = "boot-before.zip";
        /// <summary>The default parent of the evidence folders of a Windows install.</summary>
        internal const string DefaultUpdates = @"C:\bai\updates";

        /// <summary>Beside the boot folder: the packages that bai installed and current.json (the installed one).</summary>
        internal const string InstalledStore = "Boot.installed";

        /// <summary>Either the installed package (--current, --current-sha256; --service-project optional) or --legacy.</summary>
        static void CheckCurrent(UpdateRequest q)
        {
            if (q.Legacy && (q.Current ?? q.CurrentSha256 ?? q.ServiceProject) != null)
                throw new ArgumentException("--legacy means no installed package: do not give --current, --current-sha256 or --service-project.");
            if (!q.Legacy && (q.Current == null || q.CurrentSha256 == null))
                throw new ArgumentException("Give --current and --current-sha256, or --legacy for a machine without a bai package.");
            if (q.ServiceProject != null && q.Current == null) throw new ArgumentException("--service-project needs --current.");
            if (q.Legacy && q.Rehearsal) throw new ArgumentException("--legacy is for a machine, not for a rehearsal.");
        }

        static string Store(string boot) => Path.Combine(Path.GetDirectoryName(boot), InstalledStore);

        /// <summary>current.json of the target: the package that bai installed last; null when bai installed none.</summary>
        internal static Dictionary<string, object> Installed(string boot)
        {
            string file = Path.Combine(Store(boot), "current.json");
            return File.Exists(file) ? Read(file) : null;
        }

        /// <summary>The current package when the command line names none: the one bai installed on this
        /// target, else a first install (legacy). Returns where the answer came from (evidence).</summary>
        static string ResolveCurrent(UpdateRequest q, string boot)
        {
            if (q.Legacy) return "legacy (--legacy)";
            if (q.Current != null)
            {
                if (q.CurrentSha256 == null && File.Exists(q.Current + ".sha256"))
                    q.CurrentSha256 = File.ReadAllText(q.Current + ".sha256").Split(new[] { ' ', '\t', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
                return "--current";
            }
            if (q.ServiceProject != null) return "--current missing";
            var installed = Installed(boot);
            if (installed != null)
            {
                q.Current = Path.Combine(Store(boot), Text(installed, "file"));
                q.CurrentSha256 = Text(installed, "sha256");
                Console.WriteLine("Current package: {0} {1}, installed by bai {2}.", Text(installed, "machine"), Field(installed, "sourceTag"), Field(installed, "installedUtc"));
                return "installed by bai";
            }
            q.Legacy = true;
            Console.WriteLine("Current package: none installed by bai on this target. First install: the PLC must run with no online change; the whole boot folder is kept.");
            return "first install (no package installed by bai)";
        }

        /// <summary>Keeps a copy of the installed package and makes it current.json. Returns the previous current.json (null: none).</summary>
        static Dictionary<string, object> RecordInstalled(string boot, string package, string sha256, Dictionary<string, object> candidate)
        {
            string store = Store(boot), file = sha256.ToUpperInvariant() + ".zip", copy = Path.Combine(store, file);
            Directory.CreateDirectory(store);
            if (!File.Exists(copy)) File.Copy(package, copy);
            CheckHash(Hash(copy), sha256);
            var previous = Installed(boot);
            Write(Path.Combine(store, "current.json"), new Dictionary<string, object> { ["machine"] = Text(candidate, "machine"), ["file"] = file,
                ["sha256"] = sha256.ToUpperInvariant(), ["sourceTag"] = candidate.TryGetValue("sourceTag", out object tag) ? tag : null, ["installedUtc"] = DateTime.UtcNow.ToString("o") });
            return previous;
        }

        /// <summary>The expect file and variant that prove the old program runs again after a rollback.</summary>
        static (string Expect, string Variant) OldIdentity(string evidence, bool legacy, string machine) => legacy
            ? (Path.Combine(evidence, "preflight", "current.expect.json"), LegacyVariant)
            : (Path.Combine(evidence, "preflight", "identity.expect.json"), machine);

        static string LockPath(UpdateRequest q, string target, string boot) =>
            q.LockFile ?? (target == RuntimeTargets.UmRt ? UmRtLock : Path.Combine(Path.GetDirectoryName(boot), "boot-update.lock"));

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
            CheckCurrent(q);
            if (q.Legacy) return LegacyPreflight(q, host);
            string target = RuntimeTargets.Normalize(q.Target);
            if (q.Rehearsal && target != RuntimeTargets.UmRt) throw new ArgumentException("Development rehearsal is restricted to UmRT.");
            // The saved service project is optional: without it there is no drift check (the live identity check stays).
            string project = q.ServiceProject == null ? null : Path.GetFullPath(q.ServiceProject), report = Path.GetFullPath(q.Report);
            if (project != null && !Directory.Exists(project)) throw new DirectoryNotFoundException("Service project not found: " + project);
            if (project != null) BuildLocation.Assert(project, report);
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
                if (project == null)
                    Console.WriteLine("SKIP: no saved service project (--service-project): no drift check.");
                else if (ProjectDrift.Run(baseline, baselineHash, project, Path.Combine(report, "drift.json")) != 0)
                    throw new InvalidDataException("Saved project differs from the installed release. Export and reconcile it before updating.");
                string expect = Path.Combine(report, "current.expect.json");
                WriteChecks(identity, machine, Check(OnlineChanges, "UDINT", 0), expect);
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

        /// <summary>The preflight of a machine without a bai package: no saved project or identity to
        /// compare. The PLC must run with no online change, so that the boot image is the running
        /// program and a rollback brings that program back. No runtime writes.</summary>
        static Dictionary<string, object> LegacyPreflight(UpdateRequest q, IUpdateHost host)
        {
            string target = RuntimeTargets.Normalize(q.Target), report = Path.GetFullPath(q.Report);
            if (Directory.Exists(report) || File.Exists(report)) throw new IOException("Use a new report directory.");
            Directory.CreateDirectory(report);
            var record = new Dictionary<string, object> { ["schemaVersion"] = 1, ["status"] = "Checking", ["target"] = target, ["legacy"] = true };
            try
            {
                string expect = Path.Combine(report, "current.expect.json");
                Write(expect, new Dictionary<string, object> { ["variants"] = new Dictionary<string, object> { [LegacyVariant] = new[] { Check(OnlineChanges, "UDINT", 0) } } });
                if (host.Bai(Path.Combine(report, "online.log"), "verify", "--target", target, "--expect", expect, "--variant", LegacyVariant) != 0)
                    throw new InvalidOperationException("The PLC is not in Run or has online changes. No update is permitted.");
                record["status"] = "Passed";
                record["baiSha256"] = host.BaiSha256;
                Console.WriteLine("PASS: legacy machine, PLC in Run, online-change counter 0. There is no installed package to compare.");
                return record;
            }
            catch (Exception ex) { record["status"] = "Failed"; record["error"] = ex.Message; throw; }
            finally
            {
                record["completedUtc"] = DateTime.UtcNow.ToString("o");
                Write(Path.Combine(report, "preflight.json"), record);
            }
        }

        /// <summary>Both packages, verified; current is null for a legacy machine.</summary>
        static (Dictionary<string, object> Candidate, Dictionary<string, object> Current) Packages(UpdateRequest q)
        {
            var candidate = PackageVerifier.Verify(q.Package, q.PackageSha256);
            var current = q.Legacy ? null : PackageVerifier.Verify(q.Current, q.CurrentSha256);
            if (Number(candidate, "schemaVersion") != 2 || (current != null && Text(candidate, "machine") != Text(current, "machine")))
                throw new InvalidDataException("Candidate identity/machine does not match the installed release.");
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
            string currentSource = ResolveCurrent(q, boot);
            CheckCurrent(q);
            var (candidate, current) = Packages(q);
            string machine = Text(candidate, "machine");
            if (q.Evidence == null) q.Evidence = Path.Combine(DefaultUpdates, machine + "-" + DateTime.Now.ToString("yyyyMMdd-HHmmss"));
            string evidence = NewEvidence(q, boot);
            Console.WriteLine("PASS: package {0} ({1}, {2}, {3}).", machine, Text(candidate, "kind"), Text(candidate, "targetPlatform"), candidate.TryGetValue("sourceTag", out object tag) ? tag : "no tag");
            if (!q.Rehearsal && Text(candidate, "kind") != "ReleaseCandidate") throw new InvalidDataException("Development packages cannot be installed on a machine.");
            if (!File.Exists(Path.Combine(boot, "CurrentConfig.xml")))
                throw new InvalidDataException("The target has no boot project (" + Path.Combine(boot, "CurrentConfig.xml") + " is missing): there is no image to keep or to roll back to.");
            string bootPlatform = (string)Xml(Path.Combine(boot, "CurrentConfig.xml")).Root.Attribute("Platform");
            if (bootPlatform != Text(candidate, "targetPlatform"))
                throw new InvalidDataException("Candidate platform " + Text(candidate, "targetPlatform") + " differs from the current runtime boot platform " + bootPlatform + ".");
            Console.WriteLine("PASS: platform {0} = the boot platform of the target.", bootPlatform);
            var missing = host.MissingLicenses(target, q.Package);
            if (missing.Count > 0)
                throw new InvalidOperationException("Missing or expired licenses: " + string.Join(", ", missing) + ". The new program would not start. Make a 7-day trial: bai license trial --package \""
                    + q.Package + "\", or load the ordered licenses. Nothing changed.");
            Console.WriteLine("PASS: every license the package needs is valid on the target.");
            if (q.CheckOnly)
            {
                string report = Path.Combine(Path.GetTempPath(), "bai-install-check-" + Guid.NewGuid().ToString("N"));
                Preflight(new UpdateRequest { Current = q.Current, CurrentSha256 = q.CurrentSha256, ServiceProject = q.ServiceProject,
                    Report = report, Target = target, Rehearsal = q.Rehearsal, Legacy = q.Legacy }, host);
                Console.WriteLine("READY: nothing changed. Repeat the command with --yes to install (the runtime goes to Config, then to Run).");
                return new Dictionary<string, object> { ["status"] = "Ready", ["report"] = report };
            }
            string lockPath = LockPath(q, target, boot);
            Directory.CreateDirectory(Path.GetDirectoryName(lockPath));
            FileStream held = MachineCommands.TryLock(lockPath) ?? throw new IOException("Another update or test holds the lock: " + lockPath);
            Directory.CreateDirectory(evidence);
            var record = new Dictionary<string, object> { ["schemaVersion"] = 1, ["status"] = "Checking", ["machine"] = machine, ["packageSha256"] = q.PackageSha256,
                ["target"] = target, ["rehearsal"] = q.Rehearsal, ["legacy"] = q.Legacy, ["currentSource"] = currentSource, ["physicalAcceptance"] = false };
            var (oldExpect, oldVariant) = OldIdentity(evidence, q.Legacy, current == null ? null : Text(current, "machine"));
            try
            {
                Preflight(new UpdateRequest { Current = q.Current, CurrentSha256 = q.CurrentSha256, ServiceProject = q.ServiceProject,
                    Report = Path.Combine(evidence, "preflight"), Target = target, Rehearsal = q.Rehearsal, Legacy = q.Legacy }, host);
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
                    var transaction = BootTransaction.Install(Path.Combine(evidence, "boot"), boot, transactionFile, Path.Combine(evidence, BackupArchive));
                    record["backupArchive"] = transaction["archive"];
                    record["backupArchiveSha256"] = transaction["archiveSha256"];
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
                    Step(host, evidence, "rollback-identity", "verify", "--target", target, "--expect", oldExpect, "--variant", oldVariant);
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
            // The program runs and is verified: a failure to keep the package copy only costs --current next time.
            try
            {
                record["previousInstalled"] = RecordInstalled(boot, q.Package, q.PackageSha256, candidate);
                record["installedPackage"] = Path.Combine(Store(boot), "current.json");
                Write(Path.Combine(evidence, "install.json"), record);
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is InvalidDataException)
            {
                Console.WriteLine("WARNING: could not keep a copy of the installed package (" + ex.Message + "). The next update needs --current.");
            }
            Console.WriteLine("PASS: compiled files installed and live identity verified. Evidence: " + evidence);
            Console.WriteLine("Copy " + Path.Combine(evidence, BackupArchive) + " and its .sha256 (the old boot image) off the IPC.");
            return record;
        }

        /// <summary>'package rollback': puts back the image that an install replaced, for example after
        /// a failed acceptance check. Config, restore, Run, then the old identity. The evidence folder of
        /// the install gets a new 'rollback' folder; the replaced files go to Boot.rolled-back.&lt;id&gt;.</summary>
        internal static Dictionary<string, object> Rollback(UpdateRequest q, IUpdateHost host)
        {
            string target = RuntimeTargets.Normalize(q.Target);
            if (target != RuntimeTargets.Local && target != RuntimeTargets.UmRt) throw new ArgumentException("A Windows rollback runs on the IPC itself: --target local or umrt.");
            string evidence = Path.GetFullPath(q.Evidence), transactionFile = Path.Combine(evidence, "transaction.json");
            var installed = Read(Path.Combine(evidence, "install.json"));
            if (Text(installed, "status") != "Installed") throw new InvalidOperationException("Only an installed update can be rolled back; install.json has status " + Text(installed, "status") + ".");
            string boot = Path.GetFullPath(q.Boot ?? RuntimeTargets.BootFolder(target)).TrimEnd('\\');
            if (!Names.Equals(Path.GetFullPath(Text(Read(transactionFile), "boot")).TrimEnd('\\'), boot))
                throw new InvalidDataException("The update was installed in another boot folder: " + Text(Read(transactionFile), "boot"));
            string folder = Path.Combine(evidence, "rollback");
            if (Directory.Exists(folder)) throw new IOException("This update was already rolled back (or a rollback started): " + folder);
            bool legacy = installed.TryGetValue("legacy", out object flag) && flag is bool b && b;
            var (oldExpect, oldVariant) = OldIdentity(evidence, legacy, Text(installed, "machine"));
            string lockPath = LockPath(q, target, boot);
            FileStream held = MachineCommands.TryLock(lockPath) ?? throw new IOException("Another update or test holds the lock: " + lockPath);
            Directory.CreateDirectory(folder);
            var record = new Dictionary<string, object> { ["schemaVersion"] = 1, ["status"] = "Checking", ["target"] = target, ["install"] = evidence };
            try
            {
                Step(host, folder, "config", "runtime-state", "--target", target, "--state", "config", "--yes");
                Step(host, folder, "confirm-config", "runtime-state", "--target", target, "--expect", "config");
                record["replacedImage"] = BootTransaction.Restore(transactionFile)["replacedImage"];
                Step(host, folder, "run", "runtime-state", "--target", target, "--state", "run", "--yes");
                Step(host, folder, "identity", "verify", "--target", target, "--expect", oldExpect, "--variant", oldVariant);
                record["status"] = "RolledBack";
                // The old program runs again: so is its package the installed one again (none after a first install).
                if (installed.ContainsKey("installedPackage"))
                {
                    string current = Path.Combine(Store(boot), "current.json");
                    if (installed.TryGetValue("previousInstalled", out object previous) && previous is Dictionary<string, object> before) Write(current, before);
                    else if (File.Exists(current)) File.Delete(current);
                }
            }
            catch (Exception ex) { record["status"] = "Failed"; record["error"] = ex.Message; throw; }
            finally
            {
                record["completedUtc"] = DateTime.UtcNow.ToString("o");
                Write(Path.Combine(folder, "rollback.json"), record);
                held.Dispose();
            }
            Console.WriteLine("PASS: the previous boot image is back and runs. Evidence: " + folder);
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
            if (q.Legacy) throw new ArgumentException("--legacy is for a Windows IPC only.");
            if (q.Evidence == null || q.ServiceProject == null) throw new ArgumentException("--ssh needs --evidence <new-folder>, --current, --current-sha256 and --service-project.");
            CheckCurrent(q);
            string evidence = NewEvidence(q, q.ServiceProject);
            var (candidate, current) = Packages(q);
            string machine = Text(candidate, "machine");
            if (Text(candidate, "kind") != "ReleaseCandidate" || Text(candidate, "targetPlatform") != OsX64)
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
