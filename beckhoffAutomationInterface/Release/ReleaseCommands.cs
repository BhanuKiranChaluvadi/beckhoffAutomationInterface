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
            "release build --machines <M[,M]> [--tag <tag> | --working-tree] [--bundle <new-folder>] --bai-sha256 <hash>\n" +
            "              --twincat-version <major, e.g. 3.1.4026> --library-lock <json> --library-lock-sha256 <hash> [--toolchain <zip> --toolchain-sha256 <hash>]\n" +
            "              [--platform <name>] [--manifest <bai.machines.json>] [--esi-directory <folder>] [--user-esi-directory <folder>]\n" +
            "release lock --machines <M[,M]> --twincat-version <major> --output <new-json> [--platform <name>] [--work <folder>] [--manifest <file>]\n" +
            PackageUsage;
        internal const string PackageUsage =
            "package preflight --current <zip> --current-sha256 <hash> --service-project <folder> --report <new-folder> [--target local|umrt|<AMS-Net-ID>] [--rehearsal]\n" +
            "package install --package <zip> --sha256 <hash> --current <zip> --current-sha256 <hash> --service-project <folder> --evidence <new-folder>\n" +
            "                [--target local|umrt] [--rehearsal] | --target <AMS-Net-ID> --ssh <user@host>   --yes\n" +
            "package drift --baseline <project.zip> --sha256 <hash> --project <folder> --report <new-json>";

        internal static bool Handles(string[] args) => args.Length >= 2 &&
            (args[0] == "release" || (args[0] == "package" && (args[1] == "preflight" || args[1] == "install" || args[1] == "drift")));

        static string Manifest(Dictionary<string, string> o) => MachineManifest.Find(Get(o, "--manifest"), Directory.GetCurrentDirectory());

        internal static int Run(string[] args, string shell, IUpdateHost host = null)
        {
            if (args.Skip(1).Any(a => a == "--help" || a == "-h")) { Console.WriteLine(Usage); return 0; }
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
                            new[] { "--toolchain", "--toolchain-sha256", "--tag", "--bundle", "--platform", "--manifest", "--esi-directory", "--user-esi-directory" }, new[] { "--working-tree" });
                        var q = new ReleaseRequest
                        {
                            Machines = { o["--machines"] }, Tag = Get(o, "--tag"), WorkingTree = Get(o, "--working-tree") != null, Bundle = Get(o, "--bundle"),
                            Bai = System.Reflection.Assembly.GetEntryAssembly().Location, BaiSha256 = o["--bai-sha256"], TwinCatVersion = o["--twincat-version"],
                            LibraryLock = o["--library-lock"], LibraryLockSha256 = o["--library-lock-sha256"], Toolchain = Get(o, "--toolchain"), ToolchainSha256 = Get(o, "--toolchain-sha256"),
                            Platform = Get(o, "--platform"), Manifest = Get(o, "--manifest"), Shell = shell,
                        };
                        if (Get(o, "--esi-directory") != null) q.EsiDirectory = o["--esi-directory"];
                        if (Get(o, "--user-esi-directory") != null) q.UserEsiDirectory = o["--user-esi-directory"];
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
                        Console.WriteLine("Candidate library baseline: {0}\nSHA256: {1}", result["file"], result["sha256"]);
                        return 0;
                    case "package drift":
                        o = Options(args, new[] { "--baseline", "--sha256", "--project", "--report" }, new string[0]);
                        return ProjectDrift.Run(o["--baseline"], o["--sha256"], o["--project"], o["--report"]);
                    case "package preflight":
                        o = Options(args, new[] { "--current", "--current-sha256", "--service-project", "--report" }, new[] { "--target" }, new[] { "--rehearsal" });
                        PackageInstaller.Preflight(new UpdateRequest { Current = o["--current"], CurrentSha256 = o["--current-sha256"], ServiceProject = o["--service-project"],
                            Report = o["--report"], Target = Get(o, "--target", "local"), Rehearsal = Get(o, "--rehearsal") != null }, host);
                        return 0;
                    case "package install":
                        o = Options(args, new[] { "--package", "--sha256", "--current", "--current-sha256", "--service-project", "--evidence" }, new[] { "--target", "--ssh" }, new[] { "--rehearsal", "--yes" });
                        if (Get(o, "--yes") == null)
                        {
                            Console.WriteLine("Nothing changed. Repeat with --yes to install: the runtime goes to Config, then to Run.");
                            return 1;
                        }
                        PackageInstaller.Install(new UpdateRequest { Package = o["--package"], PackageSha256 = o["--sha256"], Current = o["--current"], CurrentSha256 = o["--current-sha256"],
                            ServiceProject = o["--service-project"], Evidence = o["--evidence"], Target = Get(o, "--target", "local"), SshHost = Get(o, "--ssh"),
                            Rehearsal = Get(o, "--rehearsal") != null }, host);
                        return 0;
                    default:
                        throw new ArgumentException("Unknown command: " + command + ".");
                }
            }
            catch (ArgumentException ex) { Console.Error.WriteLine("ERROR: " + ex.Message + "\nUsage:\n" + Usage); return 2; }
            catch (Exception ex) when (ex is IOException || ex is InvalidDataException || ex is UnauthorizedAccessException || ex is System.Xml.XmlException
                                       || ex is InvalidOperationException || ex is FormatException)
            { Console.Error.WriteLine("FAIL: " + ex.Message); return 1; }
        }
    }
}
