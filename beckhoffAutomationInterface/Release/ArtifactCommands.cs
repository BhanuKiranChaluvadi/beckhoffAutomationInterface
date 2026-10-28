using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using static BeckhoffAutomationInterface.Release.ArtifactFiles;

namespace BeckhoffAutomationInterface.Release
{
    internal static class ArtifactCommands
    {
        internal const string Usage =
            "esi install|verify --system-esi <folder> --required-esi <folder> --user-esi <folder> [--output <new-json>] [--json]\n" +
            "package create --payload <folder> --metadata <json-without-files> --output <new-zip> [--json]\n" +
            "toolchain capture --components <folder> --xae-shell <folder> --twincat-version <version> --output <new-zip> [--json]\n" +
            "toolchain verify --toolchain <zip> --sha256 <retained-hash> --twincat-version <version> [--check-installed] [--json]\n" +
            "toolchain check [--manifest <bai.machines.json>]\n" +
            "toolchain install [--manifest <bai.machines.json>]\n" +
            "package verify --package <zip> [--sha256 <retained-hash>] [--json]\n" +
            "libraries lock --project <tsproj> --machine <label> --platform <platform> [--variant <name>] [--library-list <libraries.xml>] --output <new-json> [--json]\n" +
            "libraries verify --project <tsproj> --machine <label> --platform <platform> [--variant <name>] [--library-list <libraries.xml>] --library-lock <json> --library-lock-sha256 <retained-hash> [--json]\n" +
            ReleaseCommands.PackageUsage;

        internal static Dictionary<string, string> Options(string[] args, string[] required, string[] optional, string[] switches = null)
        {
            var options = new Dictionary<string, string>(StringComparer.Ordinal);
            switches = switches ?? new string[0];
            var allowed = new HashSet<string>(required.Concat(optional).Concat(switches));
            for (int i = 2; i < args.Length; i++)
            {
                string name = args[i];
                if (!allowed.Contains(name) || options.ContainsKey(name)) throw new ArgumentException("Unknown or duplicate option: " + name);
                string value = "true";
                if (!switches.Contains(name))
                {
                    if (++i == args.Length || args[i].StartsWith("--") || string.IsNullOrWhiteSpace(args[i])) throw new ArgumentException("Missing value: " + name);
                    value = args[i];
                }
                options.Add(name, value);
            }
            foreach (var name in required) if (!options.ContainsKey(name)) throw new ArgumentException("Required option: " + name);
            return options;
        }
        internal static string Get(Dictionary<string, string> options, string name, string fallback = null) => options.TryGetValue(name, out var value) ? value : fallback;

        internal static int Run(string[] args)
        {
            if (args.Skip(1).Any(a => a == "--help" || a == "-h"))
                return Cli.CliHelp.Write(Console.Out, Console.Error, Cli.CliHelp.HelpPath(args), Cli.ConsoleLayout.TerminalWidth);
            if (ReleaseCommands.Handles(args)) return ReleaseCommands.Run(args, null);
            try
            {
                if (args.Length < 2) throw new ArgumentException("A subcommand is required.");
                Dictionary<string, object> result;
                Dictionary<string, string> options;
                if (args[0] == "toolchain" && (args[1] == "check" || args[1] == "install"))
                    return TwinCatPackages.Run(args);
                if (args[0] == "esi" && (args[1] == "install" || args[1] == "verify"))
                {
                    options = Options(args, new[] { "--system-esi", "--required-esi", "--user-esi" }, new[] { "--output" }, new[] { "--json" });
                    string report = Get(options, "--output");
                    if (report != null && (File.Exists(report) || Inside(report, options["--system-esi"]) || Inside(report, options["--required-esi"]) || Inside(report, options["--user-esi"])))
                        throw new IOException("Use a new report outside the catalog directories.");
                    result = EsiCatalog.Execute(options["--system-esi"], options["--required-esi"], options["--user-esi"], args[1] == "install");
                    if (report != null) WriteNew(report, result);
                }
                else if (args[0] == "package" && args[1] == "create")
                {
                    options = Options(args, new[] { "--payload", "--metadata", "--output" }, new string[0], new[] { "--json" });
                    result = ArtifactArchives.CreatePackage(options["--payload"], options["--metadata"], options["--output"]);
                }
                else if (args[0] == "toolchain" && args[1] == "capture")
                {
                    options = Options(args, new[] { "--components", "--xae-shell", "--twincat-version", "--output" }, new string[0], new[] { "--json" });
                    result = ArtifactArchives.CaptureToolchain(options["--components"], options["--xae-shell"], options["--twincat-version"], options["--output"]);
                }
                else if (args[0] == "toolchain" && args[1] == "verify")
                {
                    options = Options(args, new[] { "--toolchain", "--sha256", "--twincat-version" }, new string[0], new[] { "--check-installed", "--json" });
                    result = ArtifactArchives.VerifyToolchain(options["--toolchain"], options["--sha256"], options["--twincat-version"], Get(options, "--check-installed") != null);
                }
                else if (args[0] == "package" && args[1] == "verify")
                {
                    options = Options(args, new[] { "--package" }, new[] { "--sha256" }, new[] { "--json" });
                    result = PackageVerifier.Verify(options["--package"], Get(options, "--sha256"));
                }
                else if (args[0] == "libraries" && (args[1] == "lock" || args[1] == "verify"))
                {
                    bool capture = args[1] == "lock";
                    options = Options(args, new[] { "--project", "--machine", "--platform" }.Concat(capture ? new[] { "--output" } : new[] { "--library-lock", "--library-lock-sha256" }).ToArray(),
                        new[] { "--variant", "--library-list" }, new[] { "--json" });
                    // Read the approved baseline before inspecting current cache bytes.
                    var baseline = capture ? null : LibraryBaseline.ReadLock(options["--library-lock"], options["--library-lock-sha256"]);
                    var actual = LibraryBaseline.Capture(options["--project"], options["--machine"], options["--platform"], Get(options, "--variant", ""), Get(options, "--library-list"));
                    if (capture)
                    {
                        result = new Dictionary<string, object> { ["schemaVersion"] = 1, ["machines"] = new[] { actual } };
                        WriteNew(options["--output"], result);
                        result = new Dictionary<string, object> { ["status"] = "Captured", ["file"] = options["--output"], ["sha256"] = Hash(options["--output"]) };
                    }
                    else
                    {
                        LibraryBaseline.Verify(baseline, actual);
                        result = new Dictionary<string, object> { ["status"] = "Passed", ["machine"] = options["--machine"], ["libraries"] = Array(actual, "libraries").Length };
                    }
                }
                else throw new ArgumentException("Unknown artifact command.");
                if (Get(options, "--json") != null) Console.WriteLine(Encode(result));
                else ConsoleReport.Result(args[0] + " " + args[1], result);
                return 0;
            }
            catch (ArgumentException ex) { Cli.CliHelp.WriteError(Console.Error, args, ex.Message); return 2; }
            catch (Exception ex) when (ex is IOException || ex is InvalidDataException || ex is UnauthorizedAccessException || ex is System.Xml.XmlException || ex is InvalidOperationException
                                       || ex is FormatException)
            { Console.Error.WriteLine("FAIL: " + ex.Message); return 1; }
        }
    }
}
