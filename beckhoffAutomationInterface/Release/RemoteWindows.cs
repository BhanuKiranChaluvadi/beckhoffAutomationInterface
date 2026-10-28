using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using static BeckhoffAutomationInterface.Release.ArtifactFiles;

namespace BeckhoffAutomationInterface.Release
{
    /// <summary>
    /// 'package check|install|rollback --ssh user@host' for a Windows IPC (OpenSSH, a Windows feature):
    /// bai copies itself and the package to the IPC, runs the same command there (target local), shows
    /// its output here, copies the install record (with boot-before.zip) back to this PC, and removes
    /// its temporary folder on the IPC. The SSH user must be an administrator of the IPC, and its host
    /// key must be known here (connect once with ssh). A TwinCAT/BSD host keeps its own path.
    /// </summary>
    internal static class RemoteWindows
    {
        const string RemoteRoot = @"C:\bai\remote";

        static string[] SshArgs(string ssh, string command) => new[] { "-o", "BatchMode=yes", "-o", "StrictHostKeyChecking=yes", ssh, command };

        static int Ssh(IUpdateHost host, string folder, string name, string ssh, string command) =>
            host.Run(Path.Combine(folder, name + ".log"), "ssh", SshArgs(ssh, command));

        static void Scp(IUpdateHost host, string folder, string name, string source, string destination)
        {
            int code = host.Run(Path.Combine(folder, name + ".log"), "scp", "-B", "-r", "-o", "StrictHostKeyChecking=yes", source, destination);
            if (code != 0) throw new InvalidOperationException(name + " failed (exit " + code + "). See " + Path.Combine(folder, name + ".log"));
        }

        /// <summary>user@host as a folder name.</summary>
        static string Safe(string ssh) => Regex.Replace(ssh.Substring(ssh.IndexOf('@') + 1), @"[^A-Za-z0-9.-]", "_");

        /// <summary>Runs the command on a Windows IPC; null when the SSH host is not Windows (TwinCAT/BSD).</summary>
        internal static Dictionary<string, object> TryRun(string mode, UpdateRequest q, IUpdateHost host, string bai)
        {
            string ssh = q.SshHost;
            if (!Regex.IsMatch(ssh, @"\A[A-Za-z0-9_][A-Za-z0-9_.-]*@[A-Za-z0-9][A-Za-z0-9.-]*\z")) throw new ArgumentException("--ssh must be user@host.");
            string machine = "rollback";
            if (mode != "rollback")
            {
                var package = PackageVerifier.Verify(q.Package, q.PackageSha256);
                machine = Text(package, "machine");
            }
            string time = DateTime.Now.ToString("yyyyMMdd-HHmmss");
            string local = Path.Combine(host.UpdatesFolder, Safe(ssh) + "-" + machine + "-" + time);
            Directory.CreateDirectory(local);

            int ver = Ssh(host, local, "windows", ssh, "cmd /c ver");
            if (ver == 255) // ssh's own error: no connection, unknown host key or refused login
                throw new InvalidOperationException("Cannot log in over SSH to " + ssh + ". Connect once by hand (ssh " + ssh + ") to accept the host key, and use an SSH key for this user. Nothing changed.");
            if (ver != 0 || !File.ReadAllText(Path.Combine(local, "windows.log")).Contains("Windows"))
            {
                Directory.Delete(local, true);
                return null;
            }
            if (q.Current != null || q.ServiceProject != null || q.Evidence != null || q.Rehearsal)
                throw new ArgumentException("With --ssh to a Windows IPC, bai finds the installed package on the IPC: do not give --installed, --saved-project, --record or --rehearsal.");
            if (Ssh(host, local, "administrator", ssh, "cmd /c net session") != 0)
                throw new InvalidOperationException("The SSH user " + ssh + " is not an administrator of the IPC (bai writes the TwinCAT boot folder). Nothing changed.");

            string id = Guid.NewGuid().ToString("N"), dir = RemoteRoot + @"\" + id, scpDir = ssh + ":" + dir.Replace('\\', '/');
            string record = @"C:\bai\updates\" + machine + "-" + time;
            var result = new Dictionary<string, object> { ["status"] = "Checking", ["ssh"] = ssh, ["localRecord"] = local };
            if (Ssh(host, local, "prepare", ssh, "cmd /c mkdir " + dir) != 0) throw new InvalidOperationException("Cannot make " + dir + " on the IPC.");
            try
            {
                Scp(host, local, "copy-bai", Path.GetDirectoryName(bai), scpDir + "/bai");
                string command = "cmd /c " + dir + @"\bai\" + Path.GetFileName(bai) + " package " + mode;
                if (mode != "rollback")
                {
                    Scp(host, local, "copy-package", Path.GetFullPath(q.Package), scpDir + "/package.zip");
                    command += " --package " + dir + @"\package.zip --sha256 " + q.PackageSha256;
                    if (q.Legacy) command += " --first-install";
                    if (mode == "install") command += " --record " + record;
                }
                Console.WriteLine("On the IPC ({0}):", ssh);
                int code = Ssh(host, local, mode, ssh, command);
                if (mode == "install")
                {
                    // The record also holds boot-before.zip: keep a copy off the IPC, also after a failure.
                    try { Scp(host, local, "copy-record", ssh + ":" + record.Replace('\\', '/'), Path.Combine(local, "record")); result["record"] = Path.Combine(local, "record"); }
                    catch (InvalidOperationException ex) { Console.WriteLine("WARNING: the install record was not copied back: " + ex.Message); }
                }
                result["status"] = code == 0 ? (mode == "check" ? "Ready" : mode == "install" ? "Installed" : "RolledBack") : "Failed";
                if (code != 0) throw new InvalidOperationException("package " + mode + " on the IPC failed (exit " + code + "). Logs: " + local);
                Console.WriteLine("PASS: package {0} on the IPC {1}. Logs{2}: {3}", mode, ssh, result.ContainsKey("record") ? " and the install record (with boot-before.zip)" : "", local);
                return result;
            }
            finally
            {
                if (Ssh(host, local, "cleanup", ssh, "cmd /c rmdir /s /q " + dir) != 0)
                    Console.WriteLine("WARNING: could not remove " + dir + " on the IPC.");
            }
        }
    }
}
