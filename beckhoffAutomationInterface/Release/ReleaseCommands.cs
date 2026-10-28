using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BeckhoffAutomationInterface.Machines;
using static BeckhoffAutomationInterface.Release.ArtifactCommands;
using static BeckhoffAutomationInterface.Release.ArtifactFiles;

namespace BeckhoffAutomationInterface.Release
{
    /// <summary>'release build|lock' and 'package preflight|install|drift'. Exit codes: 0 = passed,
    /// 1 = refused or failed (the evidence says why; 'drift': the project differs), 2 = wrong
    /// command line ('drift': the comparison could not be made).</summary>
    internal static class ReleaseCommands
    {
        internal const string Usage =
            "release build --machines <M[,M]> [--tag <tag> | --include-uncommitted] [--output <new-folder>] --bai-sha256 <hash>\n" +
            "              --twincat-version <major, e.g. 3.1.4026> --library-lock <json> --library-lock-sha256 <hash> [--toolchain <zip> --toolchain-sha256 <hash>]\n" +
            "              [--platform <name>] [--manifest <bai.machines.json>] [--system-esi <folder>] [--user-esi <folder>]\n" +
            "release lock --machines <M[,M]> --twincat-version <major> --output <new-json> [--platform <name>] [--work <folder>] [--manifest <file>]\n" +
            PackageUsage;
        internal const string PackageUsage =
            "package check --package <zip> [--sha256 <hash>] [--installed <zip> [--installed-sha256 <hash>] [--saved-project <folder>] | --first-install]\n" +
            "              [--target local|umrt] [--rehearsal]\n" +
            "package install --package <zip> [--sha256 <hash>] [--installed <zip> [--installed-sha256 <hash>] [--saved-project <folder>] | --first-install]\n" +
            "                [--record <new-folder>] [--target local|umrt] [--rehearsal]\n" +
            "package install --package <zip> [--sha256 <hash>] --installed <zip> --installed-sha256 <hash> --saved-project <folder> --record <new-folder>\n" +
            "                --target <AMS-Net-ID> --ssh <user@host>\n" +
            "package rollback [--record <install-folder>] [--target local|umrt]\n" +
            "package drift --baseline <project.zip> --sha256 <hash> --saved-project <folder> --output <new-json>";

        internal static bool Handles(string[] args) => args.Length >= 2 &&
            (args[0] == "release" || (args[0] == "package" && (args[1] == "check" || args[1] == "install" || args[1] == "rollback" || args[1] == "drift")));

        /// <summary>The hash in &lt;package&gt;.sha256 (written by release build) when --sha256 is not given.</summary>
        static string HashFile(string package)
        {
            string file = package + ".sha256";
            if (!File.Exists(file)) throw new ArgumentException("--sha256 is required: there is no " + Path.GetFileName(file) + " next to the package.");
            string hash = File.ReadAllText(file).Split(new[] { ' ', '\t', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
            Console.WriteLine("SHA256 from {0}: {1}", file, hash);
            return hash;
        }

        static string Manifest(Dictionary<string, string> o) => MachineManifest.Find(Get(o, "--manifest"), Directory.GetCurrentDirectory());

        internal static int Run(string[] args, string shell, IUpdateHost host = null)
        {
            if (args.Skip(1).Any(a => a == "--help" || a == "-h"))
                return Cli.CliHelp.Write(Console.Out, Console.Error, Cli.CliHelp.HelpPath(args), Cli.ConsoleLayout.TerminalWidth);
            host = host ?? new ProcessHost();
            try
            {
                if (args.Length < 2) throw new ArgumentException("A subcommand is required.");
                string command = args[0] + " " + args[1];
                Dictionary<string, string> o;
                switch (command)
                {
                    case "release build":
                        o = Options(args, new[] { "--machines", "--bai-sha256", "--twincat-version", "--library-lock", "--library-lock-sha256" },
                            new[] { "--toolchain", "--toolchain-sha256", "--tag", "--output", "--platform", "--manifest", "--system-esi", "--user-esi" }, new[] { "--include-uncommitted" });
                        var q = new ReleaseRequest
                        {
                            Machines = { o["--machines"] }, Tag = Get(o, "--tag"), WorkingTree = Get(o, "--include-uncommitted") != null, Bundle = Get(o, "--output"),
                            Bai = System.Reflection.Assembly.GetEntryAssembly().Location, BaiSha256 = o["--bai-sha256"], TwinCatVersion = o["--twincat-version"],
                            LibraryLock = o["--library-lock"], LibraryLockSha256 = o["--library-lock-sha256"], Toolchain = Get(o, "--toolchain"), ToolchainSha256 = Get(o, "--toolchain-sha256"),
                            Platform = Get(o, "--platform"), Manifest = Get(o, "--manifest"), Shell = shell,
                        };
                        if (Get(o, "--system-esi") != null) q.EsiDirectory = o["--system-esi"];
                        if (Get(o, "--user-esi") != null) q.UserEsiDirectory = o["--user-esi"];
                        ReleaseBuilder.Build(q, ReleaseBuilder.Spawn(shell));
                        return 0;
                    case "release compile": // internal: run by the private BAI copy of 'release build'
                        o = Options(args, new[] { "--manifest", "--work", "--platform", "--version", "--machines" }, new string[0], new[] { "--dirty" });
                        return ReleaseBuilder.Compile(o["--manifest"], o["--work"], o["--machines"].Split(','), o["--platform"], o["--version"], Get(o, "--dirty") != null, shell);
                    case "release lock":
                        o = Options(args, new[] { "--machines", "--twincat-version", "--output" }, new[] { "--platform", "--work", "--manifest" });
                        string manifest = Manifest(o);
                        var result = ReleaseBuilder.Lock(manifest, Get(o, "--work") ?? MachineManifest.Load(manifest).Work, new[] { o["--machines"] },
                            o["--twincat-version"], Get(o, "--platform"), o["--output"]);
                        ConsoleReport.Result("release lock", result);
                        return 0;
                    case "package drift":
                        o = Options(args, new[] { "--baseline", "--sha256", "--saved-project", "--output" }, new string[0]);
                        return ProjectDrift.Run(o["--baseline"], o["--sha256"], o["--saved-project"], o["--output"]);
                    case "package check":
                    case "package install":
                        bool check = args[1] == "check";
                        o = Options(args, new[] { "--package" }, new[] { "--sha256", "--installed", "--installed-sha256", "--saved-project", "--target", "--ssh" }
                            .Concat(check ? new string[0] : new[] { "--record" }).ToArray(), new[] { "--rehearsal", "--first-install" });
                        var install = new UpdateRequest { Package = o["--package"], PackageSha256 = Get(o, "--sha256") ?? HashFile(o["--package"]), Current = Get(o, "--installed"),
                            CurrentSha256 = Get(o, "--installed-sha256"), ServiceProject = Get(o, "--saved-project"), Evidence = Get(o, "--record"), Target = Get(o, "--target", "local"),
                            SshHost = Get(o, "--ssh"), Rehearsal = Get(o, "--rehearsal") != null, Legacy = Get(o, "--first-install") != null, CheckOnly = check };
                        if (!check && install.SshHost == null && Runtime.RuntimeTargets.Normalize(install.Target) == Runtime.RuntimeTargets.Local) PackageInstaller.RequireAdministrator();
                        PackageInstaller.Install(install, host);
                        return 0;
                    case "package rollback":
                        o = Options(args, new string[0], new[] { "--record", "--target", "--ssh" });
                        var rollback = new UpdateRequest { Evidence = Get(o, "--record"), Target = Get(o, "--target", "local"), SshHost = Get(o, "--ssh") };
                        if (rollback.SshHost != null)
                        {
                            if (RemoteWindows.TryRun("rollback", rollback, host, host.BaiPath) == null) throw new InvalidOperationException("package rollback over SSH is for a Windows IPC only.");
                            return 0;
                        }
                        if (Runtime.RuntimeTargets.Normalize(rollback.Target) == Runtime.RuntimeTargets.Local) PackageInstaller.RequireAdministrator();
                        PackageInstaller.Rollback(rollback, host);
                        return 0;
                    default:
                        throw new ArgumentException("Unknown command: " + command + ".");
                }
            }
            catch (ArgumentException ex) { Cli.CliHelp.WriteError(Console.Error, args, ex.Message); return 2; }
            catch (Exception ex) when (ex is IOException || ex is InvalidDataException || ex is UnauthorizedAccessException || ex is System.Xml.XmlException
                                       || ex is InvalidOperationException || ex is FormatException)
            { Console.Error.WriteLine("FAIL: " + ex.Message); return 1; }
        }
    }
}
