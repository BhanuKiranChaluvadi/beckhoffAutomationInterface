using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using BeckhoffAutomationInterface.Release;
using Xunit;
using static BeckhoffAutomationInterface.Release.ArtifactFiles;

namespace BeckhoffAutomationInterface.Tests
{
    /// <summary>python on PATH; the TwinCAT/BSD helper is Python and runs there.</summary>
    static class Python
    {
        public static readonly string Exe = Find();
        static string Find()
        {
            try
            {
                using (var p = Process.Start(new ProcessStartInfo("python", "--version") { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true }))
                {
                    p.StandardOutput.ReadToEnd(); p.StandardError.ReadToEnd(); p.WaitForExit();
                    return p.ExitCode == 0 ? "python" : null;
                }
            }
            catch (System.ComponentModel.Win32Exception) { return null; }
        }

        /// <summary>Runs a script: (exit code, output).</summary>
        public static (int Code, string Output) Run(string script, IDictionary<string, string> environment, params string[] args)
        {
            var psi = new ProcessStartInfo(Exe, "-I " + string.Join(" ", new[] { script }.Concat(args).Select(Machines.BuildInfo.Quote)))
            { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true };
            foreach (var pair in environment ?? new Dictionary<string, string>()) psi.EnvironmentVariables[pair.Key] = pair.Value;
            using (var p = Process.Start(psi))
            {
                var error = p.StandardError.ReadToEndAsync();
                string output = p.StandardOutput.ReadToEnd();
                p.WaitForExit();
                return (p.ExitCode, output + error.Result);
            }
        }

        public static string Helper(string folder)
        {
            string path = Path.Combine(folder, "helper.py");
            using (var resource = typeof(PackageInstaller).Assembly.GetManifestResourceStream("bsd_boot_transaction.py"))
            using (var output = File.Create(path)) resource.CopyTo(output);
            return path;
        }
    }

    public sealed class PythonFactAttribute : FactAttribute
    {
        public PythonFactAttribute() { if (Python.Exe == null) Skip = "python is not installed"; }
    }

    /// <summary>Runtime and SSH stand-ins: the runtime mode is a file, the live identity is the JSON
    /// of the installed Plc/Port_851.app, and the BSD host runs the real helper on local folders.</summary>
    sealed class FakeHost : IUpdateHost
    {
        public readonly string Root;
        public string Mode { get => File.ReadAllText(Path.Combine(Root, "mode")).Trim(); set => File.WriteAllText(Path.Combine(Root, "mode"), value); }
        public string Boot => Path.Combine(Root, "Boot");
        public readonly List<string> Steps = new List<string>();
        public FakeHost(string root) { Root = root; Directory.CreateDirectory(root); Mode = "Run"; }
        public string BaiSha256 => new string('A', 64);
        /// <summary>The answer of the license check: empty = all licenses valid.</summary>
        public List<string> Missing = new List<string>();
        public List<string> MissingLicenses(string target, string package) => Missing;
        public string BaiPath => Path.Combine(Root, "bai", "bai.exe");
        public string UpdatesFolder => Path.Combine(Root, "updates");

        static string After(string[] args, string name) => args[System.Array.IndexOf(args, name) + 1];

        public int Bai(string log, params string[] args)
        {
            Steps.Add(Path.GetFileNameWithoutExtension(log));
            Directory.CreateDirectory(Path.GetDirectoryName(log));
            string result;
            int code = 0;
            if (args[0] == "runtime-state" && args.Contains("--set")) { Mode = After(args, "--set"); result = "Reached " + Mode; }
            else if (args[0] == "runtime-state") { code = Mode == After(args, "--check") ? 0 : 1; result = "Target: " + Mode; }
            else
            {
                var actual = Read(Path.Combine(Boot, "Plc", "Port_851.app"));
                var checks = Array(Object(Read(After(args, "--expect"))["variants"]), After(args, "--variant")).Select(Object);
                var wrong = checks.FirstOrDefault(c => !actual.TryGetValue((string)c["symbol"], out object value) || !Equals(value, c["equals"]));
                code = wrong == null ? 0 : 1;
                result = wrong == null ? "Fixture identity matches" : "Fixture identity mismatch: " + wrong["symbol"];
            }
            File.WriteAllText(log, result);
            return code;
        }

        public const string Remote =
            "import importlib.util, json, os, sys\n" +
            "from pathlib import Path\n" +
            "root = Path(os.environ['BSD_FIXTURE_ROOT'])\n" +
            "remote = root / 'remote'\n" +
            "spec = importlib.util.spec_from_file_location('helper', remote / 'helper.py')\n" +
            "helper = importlib.util.module_from_spec(spec)\n" +
            "spec.loader.exec_module(helper)\n" +
            "action = sys.argv[1]\n" +
            "mode = (root / 'mode').read_text().strip()\n" +
            "helper.assert_host('1.2.3.4.1.1', 'Run' if action in ('reserve', 'finish') else 'Config',\n" +
            "    lambda args, **kw: 'AMS Net Id: 1.2.3.4.1.1' if args[-1] == '--netid' else mode)\n" +
            "if action == 'reserve': result = helper.reserve(root / 'Boot', remote / 'transaction')\n" +
            "elif action == 'install': result = helper.install(remote / 'package.zip', helper.sha(remote / 'package.zip'), root / 'Boot', remote / 'transaction')\n" +
            "else: result = helper.complete(remote / 'transaction', rollback=action == 'rollback')\n" +
            "print(json.dumps({'state': result['state']}))\n";

        public int Run(string log, string program, params string[] args)
        {
            Steps.Add(Path.GetFileNameWithoutExtension(log));
            Directory.CreateDirectory(Path.GetDirectoryName(log));
            string remote = Path.Combine(Root, "remote");
            if (program == "scp")
            {
                string name = args[args.Length - 1].Split('/').Last();
                if (name != "package.zip" && name != "helper.py") throw new InvalidOperationException("Unexpected remote file name.");
                File.Copy(args[args.Length - 2], Path.Combine(remote, name));
                File.WriteAllText(log, "copied " + name);
                return 0;
            }
            string command = args[args.Length - 1];
            if (command == "cmd /c ver") { File.WriteAllText(log, "cmd: not found"); return 127; } // a BSD host is not Windows
            if (command == "TcSysExe.exe --netid") { File.WriteAllText(log, "    AMS Net Id: 1.2.3.4.1.1\r\n"); return 0; }
            if (command.StartsWith("mkdir ")) { Directory.CreateDirectory(remote); File.WriteAllText(log, ""); return 0; }
            var action = System.Text.RegularExpressions.Regex.Match(command, @"helper\.py (reserve|install|rollback|finish) ");
            if (!action.Success) throw new InvalidOperationException("Unexpected SSH command: " + command);
            string harness = Path.Combine(Root, "remote.py");
            if (!File.Exists(harness)) File.WriteAllText(harness, Remote);
            var (code, output) = Python.Run(harness, new Dictionary<string, string> { ["BSD_FIXTURE_ROOT"] = Root }, action.Groups[1].Value);
            File.WriteAllText(log, output);
            return code;
        }
    }

    /// <summary>A Windows IPC over SSH: records each ssh/scp call; the remote bai run returns RemoteExit.</summary>
    sealed class FakeWindowsIpc : IUpdateHost
    {
        public readonly string Root;
        public readonly List<string> Calls = new List<string>();
        public int AdminExit, RemoteExit;
        public FakeWindowsIpc(string root) { Root = root; Directory.CreateDirectory(Path.Combine(root, "bai")); File.WriteAllText(BaiPath, "bai"); }
        public string BaiSha256 => new string('A', 64);
        public string BaiPath => Path.Combine(Root, "bai", "beckhoffAutomationInterface.exe");
        public string UpdatesFolder => Path.Combine(Root, "updates");
        public List<string> MissingLicenses(string target, string package) => new List<string>();
        public int Bai(string log, params string[] args) => throw new InvalidOperationException("No local runtime step in a remote run.");
        public int Run(string log, string program, params string[] args)
        {
            string last = args[args.Length - 1];
            Calls.Add(program + " " + (program == "scp" ? args[args.Length - 2] + " -> " + last : last));
            string output = last == "cmd /c ver" ? "Microsoft Windows [Version 10.0.19045]" : "";
            if (program == "scp" && last.StartsWith(Root)) Directory.CreateDirectory(last); // the record copied back
            File.WriteAllText(log, output);
            return last == "cmd /c net session" ? AdminExit : last.Contains("beckhoffAutomationInterface.exe package ") ? RemoteExit : 0;
        }
    }

    /// <summary>Preflight and install with the fake host (was test_bsd_installer.ps1 and the
    /// install part of the runtime tests). No ADS, no runtime, no SSH.</summary>
    public sealed class PackageInstallerTests : IDisposable
    {
        readonly string work = TestProjects.NewTempDir("bai_install");
        public void Dispose() { try { Directory.Delete(work, true); } catch (IOException) { } }
        string Service => Path.Combine(work, "service");

        public PackageInstallerTests()
        {
            Directory.CreateDirectory(Service);
            File.WriteAllText(Path.Combine(Service, "NFPro.tsproj"), "<TcSmProject><Project><Plc><Project Name=\"NFPro\" PrjFilePath=\"NFPro.plcproj\"/></Plc></Project></TcSmProject>");
            File.WriteAllText(Path.Combine(Service, "NFPro.plcproj"), "<Project><ItemGroup><Compile Include=\"MAIN.TcPOU\"/></ItemGroup></Project>");
            File.WriteAllText(Path.Combine(Service, "MAIN.TcPOU"), "<TcPlcObject><POU Name=\"MAIN\"><Declaration>PROGRAM MAIN</Declaration><Implementation><ST>value := 1;</ST></Implementation></POU></TcPlcObject>");
        }

        /// <summary>A package whose Port_851.app holds the live values that the fake host reads.</summary>
        string Package(string name, string platform = "TwinCAT OS (x64)", string kind = "ReleaseCandidate")
        {
            string stage = Path.Combine(work, "stage-" + name);
            Directory.CreateDirectory(Path.Combine(stage, "boot", "Plc"));
            File.WriteAllText(Path.Combine(stage, "boot", "CurrentConfig.xml"), "<TcBootProject Platform=\"" + platform + "\"/>");
            File.WriteAllText(Path.Combine(stage, "boot", "CurrentProjectInfo.json"), "{}");
            var identity = new Dictionary<string, object> { ["GVL_BuildInfo.sGitTag"] = "fixture", ["GVL_BuildInfo.sGitCommit"] = name, ["GVL_BuildInfo.bDirty"] = false,
                ["GVL_BuildInfo.sMachine"] = "NFPro", ["GVL_BuildInfo.sBuildTime"] = name, ["GVL_Simulation.bActive"] = false, ["TwinCAT_SystemInfoVarList._AppInfo.OnlineChangeCnt"] = 0 };
            File.WriteAllText(Path.Combine(stage, "boot", "Plc", "Port_851.app"), Encode(identity));
            foreach (string file in new[] { "Port_851.crc", "Port_851.autostart", "Port_851_boot.tizip", "Port_851.tmc" })
                File.WriteAllText(Path.Combine(stage, "boot", "Plc", file), "fixture");
            var checks = identity.Take(5).Select(p => new Dictionary<string, object> { ["symbol"] = p.Key, ["type"] = p.Value is bool ? "BOOL" : "STRING",
                ["equals"] = name == "wrong" && p.Key == "GVL_BuildInfo.sGitCommit" ? "mismatch" : p.Value }).ToList();
            File.WriteAllText(Path.Combine(stage, "identity.expect.json"), Encode(new Dictionary<string, object> { ["variants"] = new Dictionary<string, object> { ["NFPro"] = checks } }));
            ZipFile.CreateFromDirectory(Service, Path.Combine(stage, "project.zip"));
            File.WriteAllText(Path.Combine(stage, "evidence.json"), "{\"scope\":\"synthetic installer fixture\"}");
            string metadata = Path.Combine(work, name + ".metadata.json");
            File.WriteAllText(metadata, Encode(new Dictionary<string, object> { ["schemaVersion"] = 2, ["status"] = "Packaged", ["kind"] = kind,
                ["deploymentApproved"] = false, ["machine"] = "NFPro", ["targetPlatform"] = platform }));
            string package = Path.Combine(work, name + ".zip");
            ArtifactArchives.CreatePackage(stage, metadata, package);
            return package;
        }

        FakeHost Host(string name, string current)
        {
            var host = new FakeHost(Path.Combine(work, "host-" + name));
            using (var zip = ZipFile.OpenRead(current))
                foreach (var entry in zip.Entries.Where(e => e.FullName.StartsWith("boot/")))
                {
                    string path = Path.Combine(host.Boot, entry.FullName.Substring(5));
                    Directory.CreateDirectory(Path.GetDirectoryName(path));
                    entry.ExtractToFile(path);
                }
            return host;
        }

        UpdateRequest Request(FakeHost host, string package, string current, string target = "local") => new UpdateRequest
        {
            Package = package, PackageSha256 = package == null ? null : Hash(package), Current = current, CurrentSha256 = Hash(current),
            ServiceProject = Service, Evidence = Path.Combine(host.Root, "evidence"), Target = target, Boot = host.Boot, LockFile = Path.Combine(host.Root, "update.lock"),
        };

        [Fact]
        public void WindowsInstallVerifiesIdentityAndKeepsEvidence()
        {
            string a = Package("a", "TwinCAT RT (x64)"), b = Package("b", "TwinCAT RT (x64)");
            var host = Host("b", a);
            var record = PackageInstaller.Install(Request(host, b, a), host);
            Assert.Equal("Installed", record["status"]);
            Assert.Equal("b", Read(Path.Combine(host.Boot, "Plc", "Port_851.app"))["GVL_BuildInfo.sGitCommit"]);
            Assert.Equal("Installed", Read(Path.Combine(host.Root, "evidence", "install.json"))["status"]);
            Assert.Equal("Passed", Read(Path.Combine(host.Root, "evidence", "preflight", "preflight.json"))["status"]);
            Assert.Equal("Installed", Read(Path.Combine(host.Root, "evidence", "transaction.json"))["state"]);
            Assert.Equal(new[] { "online", "config", "confirm-config", "run", "identity" }, host.Steps);
            Assert.Equal("run", host.Mode);
            Assert.True(File.Exists(Path.Combine(host.Root, "evidence", PackageInstaller.BackupArchive + ".sha256")));
        }

        /// <summary>A legacy request: no current package or service project.</summary>
        UpdateRequest Legacy(FakeHost host, string package) => new UpdateRequest
        {
            Package = package, PackageSha256 = Hash(package), Evidence = Path.Combine(host.Root, "evidence"), Target = "local", Boot = host.Boot,
            LockFile = Path.Combine(host.Root, "update.lock"), Legacy = true,
        };

        /// <summary>The files of a ZIP as evidence records.</summary>
        static List<Dictionary<string, object>> ZipRecords(string zipPath)
        {
            using (var zip = ZipFile.OpenRead(zipPath))
                return zip.Entries.Select(e => { using (var s = e.Open()) return new Dictionary<string, object> { ["path"] = e.FullName, ["bytes"] = e.Length, ["sha256"] = Hash(s) }; }).ToList();
        }

        [Fact]
        public void LegacyInstallKeepsTheOldImageAsAZip()
        {
            string a = Package("a", "TwinCAT RT (x64)"), b = Package("b", "TwinCAT RT (x64)");
            var host = Host("legacy", a);
            File.WriteAllText(Path.Combine(host.Boot, "Plc", "Port_851.bootdata"), "persistent");
            var before = BootTransaction.BootInventory(host.Boot);
            var record = PackageInstaller.Install(Legacy(host, b), host);
            string evidence = Path.Combine(host.Root, "evidence"), archive = Path.Combine(evidence, PackageInstaller.BackupArchive);
            Assert.Equal("Installed", record["status"]);
            Assert.Equal(true, record["legacy"]);
            Assert.Equal("b", Read(Path.Combine(host.Boot, "Plc", "Port_851.app"))["GVL_BuildInfo.sGitCommit"]);
            Assert.Equal("persistent", File.ReadAllText(Path.Combine(host.Boot, "Plc", "Port_851.bootdata")));
            SameFiles(before, ZipRecords(archive), "Backup archive");
            Assert.StartsWith(Hash(archive).ToUpperInvariant(), File.ReadAllText(archive + ".sha256"));
            Assert.Equal(Hash(archive).ToUpperInvariant(), record["backupArchiveSha256"]);
            Assert.Equal(true, Read(Path.Combine(evidence, "preflight", "preflight.json"))["legacy"]);
            Assert.Equal(new[] { "online", "config", "confirm-config", "run", "identity" }, host.Steps);
        }

        [Fact]
        public void LegacyWrongIdentityRollsBackAndChecksTheOldPlcRuns()
        {
            string a = Package("a", "TwinCAT RT (x64)"), wrong = Package("wrong", "TwinCAT RT (x64)");
            var host = Host("legacy-wrong", a);
            var before = BootTransaction.BootInventory(host.Boot);
            Assert.Contains("restored", Assert.Throws<InvalidOperationException>(() => PackageInstaller.Install(Legacy(host, wrong), host)).Message);
            SameFiles(before, BootTransaction.BootInventory(host.Boot), "Rolled back boot");
            Assert.Equal("RolledBack", Read(Path.Combine(host.Root, "evidence", "install.json"))["status"]);
            Assert.Contains("rollback-identity", host.Steps);
        }

        [Fact]
        public void RollbackPutsTheOldImageBackOnce()
        {
            string a = Package("a", "TwinCAT RT (x64)"), b = Package("b", "TwinCAT RT (x64)");
            var host = Host("rollback", a);
            var before = BootTransaction.BootInventory(host.Boot);
            PackageInstaller.Install(Request(host, b, a), host);
            var rollback = new UpdateRequest { Evidence = Path.Combine(host.Root, "evidence"), Target = "local", Boot = host.Boot, LockFile = Path.Combine(host.Root, "update.lock") };
            host.Steps.Clear();
            Assert.Equal("RolledBack", PackageInstaller.Rollback(rollback, host)["status"]);
            SameFiles(before, BootTransaction.BootInventory(host.Boot), "Rolled back boot");
            Assert.Equal(new[] { "config", "confirm-config", "run", "identity" }, host.Steps);
            Assert.Equal("RolledBack", Read(Path.Combine(host.Root, "evidence", "rollback", "rollback.json"))["status"]);
            Assert.Throws<IOException>(() => PackageInstaller.Rollback(rollback, host));
        }

        /// <summary>No --current, no --legacy: bai finds the installed package beside the boot folder.</summary>
        UpdateRequest Auto(FakeHost host, string package, string evidence) => new UpdateRequest
        {
            Package = package, PackageSha256 = Hash(package), Evidence = Path.Combine(host.Root, evidence), Target = "local", Boot = host.Boot,
            LockFile = Path.Combine(host.Root, "update.lock"),
        };

        [Fact]
        public void BaiRemembersTheInstalledPackageForTheNextUpdateAndRollback()
        {
            string a = Package("a", "TwinCAT RT (x64)"), b = Package("b", "TwinCAT RT (x64)"), c = Package("c", "TwinCAT RT (x64)");
            var host = Host("auto", a);
            string current = Path.Combine(host.Root, PackageInstaller.InstalledStore, "current.json");

            var first = PackageInstaller.Install(Auto(host, b, "e1"), host);
            Assert.Equal(true, first["legacy"]);
            Assert.StartsWith("first install", (string)first["currentSource"]);
            Assert.Equal(Hash(b).ToUpperInvariant(), Read(current)["sha256"]);

            var second = PackageInstaller.Install(Auto(host, c, "e2"), host);
            Assert.Equal(false, second["legacy"]);
            Assert.Equal("installed by bai", second["currentSource"]);
            Assert.Equal(Hash(c).ToUpperInvariant(), Read(current)["sha256"]);
            Assert.Equal("c", Read(Path.Combine(host.Boot, "Plc", "Port_851.app"))["GVL_BuildInfo.sGitCommit"]);

            // No record folder: each rollback undoes the last install on the target.
            UpdateRequest Last() => new UpdateRequest { Target = "local", Boot = host.Boot, LockFile = Path.Combine(host.Root, "update.lock") };
            PackageInstaller.Rollback(Last(), host);
            Assert.Equal("b", Read(Path.Combine(host.Boot, "Plc", "Port_851.app"))["GVL_BuildInfo.sGitCommit"]);
            Assert.Equal(Hash(b).ToUpperInvariant(), Read(current)["sha256"]);

            PackageInstaller.Rollback(Last(), host);
            Assert.Equal("a", Read(Path.Combine(host.Boot, "Plc", "Port_851.app"))["GVL_BuildInfo.sGitCommit"]);
            Assert.False(File.Exists(current));
            Assert.Contains("no record of an install", Assert.Throws<InvalidOperationException>(() => PackageInstaller.Rollback(Last(), host)).Message);
        }

        [Fact]
        public void WindowsIpcOverSshRunsTheSameInstallThereAndCopiesTheRecordBack()
        {
            string b = Package("b", "TwinCAT RT (x64)");
            var ipc = new FakeWindowsIpc(Path.Combine(work, "ipc"));
            var q = new UpdateRequest { Package = b, PackageSha256 = Hash(b), SshHost = "admin@10.10.30.215" };
            var result = PackageInstaller.Install(q, ipc);
            Assert.Equal("Installed", result["status"]);
            string run = ipc.Calls.Single(c => c.Contains("package install"));
            Assert.Matches(@"ssh cmd /c C:\\bai\\remote\\[0-9a-f]{32}\\bai\\beckhoffAutomationInterface\.exe package install --package C:\\bai\\remote\\[0-9a-f]{32}\\package\.zip --sha256 " + Hash(b) + @" --record C:\\bai\\updates\\NFPro-\d{8}-\d{6}\z", run);
            Assert.Equal(new[] { "cmd /c ver", "cmd /c net session" }, ipc.Calls.Take(2).Select(c => c.Substring(4)));
            Assert.Contains(ipc.Calls, c => c.StartsWith("scp admin@10.10.30.215:C:/bai/updates/NFPro-"));
            Assert.StartsWith("ssh cmd /c rmdir /s /q C:\\bai\\remote\\", ipc.Calls.Last());
            Assert.True(Directory.Exists((string)result["record"]));

            ipc.Calls.Clear(); ipc.RemoteExit = 1;
            Assert.Contains("failed", Assert.Throws<InvalidOperationException>(() => PackageInstaller.Install(new UpdateRequest { Package = b, PackageSha256 = Hash(b), SshHost = "admin@10.10.30.215" }, ipc)).Message);
            Assert.StartsWith("ssh cmd /c rmdir", ipc.Calls.Last());

            ipc.Calls.Clear(); ipc.AdminExit = 2;
            Assert.Contains("not an administrator", Assert.Throws<InvalidOperationException>(() => PackageInstaller.Install(new UpdateRequest { Package = b, PackageSha256 = Hash(b), SshHost = "admin@10.10.30.215" }, ipc)).Message);
            Assert.DoesNotContain(ipc.Calls, c => c.StartsWith("scp"));
            Assert.Throws<ArgumentException>(() => PackageInstaller.Install(new UpdateRequest { Package = b, PackageSha256 = Hash(b), SshHost = "admin@10.10.30.215", Current = b }, ipc));
        }

        [Fact]
        public void CheckOnlyRunsTheReadOnlyChecksAndChangesNothing()
        {
            string a = Package("a", "TwinCAT RT (x64)"), b = Package("b", "TwinCAT RT (x64)");
            var host = Host("check", a);
            var before = BootTransaction.BootInventory(host.Boot);
            var q = Legacy(host, b); q.CheckOnly = true;
            Assert.Equal("Ready", PackageInstaller.Install(q, host)["status"]);
            Assert.Equal(new[] { "online" }, host.Steps);
            SameFiles(before, BootTransaction.BootInventory(host.Boot), "Boot after a check");
            Assert.False(Directory.Exists(Path.Combine(host.Root, "evidence")));
            Assert.Equal("Run", host.Mode);
        }

        [Fact]
        public void MissingLicensesStopBeforeAnyChange()
        {
            string a = Package("a", "TwinCAT RT (x64)"), b = Package("b", "TwinCAT RT (x64)");
            var host = Host("license", a);
            host.Missing.Add("TC3 Temperature-Controller (missing)");
            var before = BootTransaction.BootInventory(host.Boot);
            Assert.Contains("license trial", Assert.Throws<InvalidOperationException>(() => PackageInstaller.Install(Legacy(host, b), host)).Message);
            Assert.Empty(host.Steps);
            SameFiles(before, BootTransaction.BootInventory(host.Boot), "Boot after a refusal");
            Assert.False(Directory.Exists(Path.Combine(host.Root, "evidence")));
        }

        [Fact]
        public void CurrentPackageOrLegacyNotBoth()
        {
            string a = Package("a", "TwinCAT RT (x64)"), b = Package("b", "TwinCAT RT (x64)");
            var host = Host("options", a);
            var both = Request(host, b, a); both.Legacy = true;
            Assert.Throws<ArgumentException>(() => PackageInstaller.Install(both, host));
            var project = Legacy(host, b); project.Legacy = false; project.ServiceProject = Service;
            Assert.Throws<ArgumentException>(() => PackageInstaller.Install(project, host));
            var bsd = Legacy(host, b); bsd.Target = "1.2.3.4.1.1"; bsd.SshHost = "fixture@fixture";
            Assert.Throws<ArgumentException>(() => PackageInstaller.Install(bsd, host));
            Assert.All(host.Steps, s => Assert.Equal("windows", s)); // only the read-only "is it Windows" question over SSH
            Assert.False(Directory.Exists(Path.Combine(host.Root, "evidence")));
        }

        [Fact]
        public void WindowsWrongIdentityRollsBackTheOldImage()
        {
            string a = Package("a", "TwinCAT RT (x64)"), wrong = Package("wrong", "TwinCAT RT (x64)");
            var host = Host("wrong", a);
            var before = BootTransaction.BootInventory(host.Boot);
            Assert.Contains("restored", Assert.Throws<InvalidOperationException>(() => PackageInstaller.Install(Request(host, wrong, a), host)).Message);
            SameFiles(before, BootTransaction.BootInventory(host.Boot), "Rolled back boot");
            Assert.Equal("RolledBack", Read(Path.Combine(host.Root, "evidence", "install.json"))["status"]);
            Assert.Equal("run", host.Mode);
            Assert.Contains("rollback-identity", host.Steps);
        }

        [Fact]
        public void DevelopmentPackagesOnlyOnUmRtRehearsal()
        {
            string a = Package("a", "TwinCAT RT (x64)"), dev = Package("dev", "TwinCAT RT (x64)", "DevelopmentValidation");
            var host = Host("dev", a);
            Assert.Contains("Development packages", Assert.Throws<InvalidDataException>(() => PackageInstaller.Install(Request(host, dev, a), host)).Message);
            var rehearsal = Request(host, dev, a); rehearsal.Rehearsal = true;
            Assert.Throws<ArgumentException>(() => PackageInstaller.Install(rehearsal, host));
            Assert.False(Directory.Exists(Path.Combine(host.Root, "evidence")));
            var current = Request(Host("dev2", dev), null, dev); current.Report = Path.Combine(work, "report-dev");
            Assert.Contains("development build", Assert.Throws<InvalidDataException>(() => PackageInstaller.Preflight(current, host)).Message);
        }

        [Fact]
        public void PreflightRefusesProjectDriftAndHeldLock()
        {
            string a = Package("a", "TwinCAT RT (x64)"), b = Package("b", "TwinCAT RT (x64)");
            var host = Host("drift", a);
            File.WriteAllText(Path.Combine(Service, "MAIN.TcPOU"), "<TcPlcObject><POU Name=\"MAIN\"><Implementation><ST>value := 2;</ST></Implementation></POU></TcPlcObject>");
            var q = Request(host, null, a); q.Report = Path.Combine(work, "report");
            Assert.Contains("Saved project differs", Assert.Throws<InvalidDataException>(() => PackageInstaller.Preflight(q, host)).Message);
            Assert.Equal("Drift", Read(Path.Combine(work, "report", "drift.json"))["status"]);
            Assert.Equal("Failed", Read(Path.Combine(work, "report", "preflight.json"))["status"]);
            using (new FileStream(Path.Combine(host.Root, "update.lock"), FileMode.Create, FileAccess.ReadWrite, FileShare.None))
                Assert.Throws<IOException>(() => PackageInstaller.Install(Request(host, b, a), host));
            Assert.Empty(host.Steps);
        }

        [Fact]
        public void PlatformMustMatchTheRuntimeBoot()
        {
            string a = Package("a", "TwinCAT RT (x64)"), os = Package("os");
            var host = Host("os", a);
            Assert.Contains("platform", Assert.Throws<InvalidDataException>(() => PackageInstaller.Install(Request(host, os, a), host)).Message);
        }

        [PythonFact]
        public void BsdInstallAndWrongIdentityRollback()
        {
            string a = Package("a"), b = Package("b"), wrong = Package("wrong");
            foreach (string name in new[] { "b", "wrong" })
            {
                var host = Host("bsd-" + name, a);
                var q = Request(host, name == "b" ? b : wrong, a, "1.2.3.4.1.1"); q.SshHost = "fixture@fixture"; q.Boot = null;
                Exception failure = Record.Exception(() => PackageInstaller.Install(q, host));
                var record = Read(Path.Combine(host.Root, "evidence", "install.json"));
                Assert.True((failure != null) == (name == "wrong"), failure?.Message + " " + Encode(record));
                Assert.Equal(name == "wrong" ? "RolledBack" : "Installed", record["status"]);
                Assert.Equal("run", host.Mode);
                Assert.False(File.Exists(Path.Combine(host.Root, "plc-release.lock")), "Verified transaction left its lock held.");
                Assert.Equal(name == "b" ? "b" : "a", Read(Path.Combine(host.Boot, "Plc", "Port_851.app"))["GVL_BuildInfo.sGitCommit"]);
            }
        }

        [Fact]
        public void CommandLineRoutesPackageCommands()
        {
            string a = Package("a", "TwinCAT RT (x64)"), b = Package("b", "TwinCAT RT (x64)");
            var host = Host("cli", a);
            string evidence = Path.Combine(host.Root, "evidence");
            var install = new[] { "package", "check", "--package", b, "--sha256", Hash(b), "--installed", a, "--installed-sha256", Hash(a),
                "--saved-project", Service, "--target", "umrt" };
            // 'package check' runs only the read-only checks (their result depends on this PC's UmRT boot folder).
            ReleaseCommands.Run(install, null, host);
            Assert.False(Directory.Exists(evidence));
            Assert.DoesNotContain("config", host.Steps);
            Assert.Equal(2, ReleaseCommands.Run(new[] { "release", "build", "--machines", "M1" }, null, host));
            Assert.Equal(2, ArtifactCommands.Run(new[] { "package", "install", "--package", b }));
            string project = Path.Combine(work, "project.zip");
            ZipFile.CreateFromDirectory(Service, project);
            Assert.Equal(0, ArtifactCommands.Run(new[] { "package", "drift", "--baseline", project, "--sha256", Hash(project), "--saved-project", Service, "--output", Path.Combine(work, "d.json") }));
            Assert.Equal(0, ReleaseCommands.Run(new[] { "release", "--help" }, null, host));
        }

        [Fact]
        public void BsdNeedsAnOsReleaseCandidate()
        {
            string a = Package("a", "TwinCAT RT (x64)"), b = Package("b", "TwinCAT RT (x64)");
            var host = Host("bsd-rt", a);
            var q = Request(host, b, a, "1.2.3.4.1.1"); q.SshHost = "fixture@fixture";
            Assert.Contains("OS x64", Assert.Throws<InvalidDataException>(() => PackageInstaller.Install(q, host)).Message);
            q.SshHost = "not a host";
            Assert.Throws<ArgumentException>(() => PackageInstaller.Install(q, host));
        }
    }
}
