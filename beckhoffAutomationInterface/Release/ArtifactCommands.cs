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
            "esi install|verify --system-directory <folder> --devices <folder> --user-directory <folder> [--report <new-json>] [--json]\n" +
            "package create --directory <payload> --metadata <json-without-files> --output <new-zip> [--json]\n" +
            "toolchain capture --components <folder> --xae-shell <folder> --version <version> --archive <new-zip> [--json]\n" +
            "toolchain verify --archive <zip> --sha256 <retained-hash> --version <version> [--installed] [--json]\n" +
            "package verify --package <zip> [--sha256 <retained-hash>] [--json]\n" +
            "libraries lock --project <tsproj> --machine <label> --platform <platform> [--variant <name>] [--manifest <libraries.xml>] --output <new-json>\n" +
            "libraries verify --project <tsproj> --machine <label> --platform <platform> [--variant <name>] [--manifest <libraries.xml>] --lock <json> --sha256 <retained-hash> [--json]";

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
            if (args.Skip(1).Any(a => a == "--help" || a == "-h")) { Console.WriteLine(Usage); return 0; }
            try
            {
                if (args.Length < 2) throw new ArgumentException("A subcommand is required.");
                Dictionary<string, object> result;
                Dictionary<string, string> options;
                if (args[0] == "esi" && (args[1] == "install" || args[1] == "verify"))
                {
                    options = Options(args, new[] { "--system-directory", "--devices", "--user-directory" }, new[] { "--report" }, new[] { "--json" });
                    string report = Get(options, "--report");
                    if (report != null && (File.Exists(report) || Inside(report, options["--system-directory"]) || Inside(report, options["--devices"]) || Inside(report, options["--user-directory"])))
                        throw new IOException("Use a new report outside the catalog directories.");
                    result = EsiCatalog.Execute(options["--system-directory"], options["--devices"], options["--user-directory"], args[1] == "install");
                    if (report != null) WriteNew(report, result);
                }
                else if (args[0] == "package" && args[1] == "create")
                {
                    options = Options(args, new[] { "--directory", "--metadata", "--output" }, new string[0], new[] { "--json" });
                    result = ArtifactArchives.CreatePackage(options["--directory"], options["--metadata"], options["--output"]);
                }
                else if (args[0] == "toolchain" && args[1] == "capture")
                {
                    options = Options(args, new[] { "--components", "--xae-shell", "--version", "--archive" }, new string[0], new[] { "--json" });
                    result = ArtifactArchives.CaptureToolchain(options["--components"], options["--xae-shell"], options["--version"], options["--archive"]);
                }
                else if (args[0] == "toolchain" && args[1] == "verify")
                {
                    options = Options(args, new[] { "--archive", "--sha256", "--version" }, new string[0], new[] { "--installed", "--json" });
                    result = ArtifactArchives.VerifyToolchain(options["--archive"], options["--sha256"], options["--version"], Get(options, "--installed") != null);
                }
                else if (args[0] == "package" && args[1] == "verify")
                {
                    options = Options(args, new[] { "--package" }, new[] { "--sha256" }, new[] { "--json" });
                    result = PackageVerifier.Verify(options["--package"], Get(options, "--sha256"));
                }
                else if (args[0] == "libraries" && (args[1] == "lock" || args[1] == "verify"))
                {
                    bool capture = args[1] == "lock";
                    options = Options(args, new[] { "--project", "--machine", "--platform" }.Concat(capture ? new[] { "--output" } : new[] { "--lock", "--sha256" }).ToArray(),
                        new[] { "--variant", "--manifest" }, new[] { "--json" });
                    // Read the approved baseline before inspecting current cache bytes.
                    var baseline = capture ? null : LibraryBaseline.ReadLock(options["--lock"], options["--sha256"]);
                    var actual = LibraryBaseline.Capture(options["--project"], options["--machine"], options["--platform"], Get(options, "--variant", ""), Get(options, "--manifest"));
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
                Console.WriteLine(Get(options, "--json") != null ? Encode(result) : "PASS: " + args[0] + " " + args[1] + ". " + Encode(result));
                return 0;
            }
            catch (ArgumentException ex) { Console.Error.WriteLine("ERROR: " + ex.Message + "\n" + Usage); return 2; }
            catch (Exception ex) when (ex is IOException || ex is InvalidDataException || ex is UnauthorizedAccessException || ex is System.Xml.XmlException || ex is InvalidOperationException)
            { Console.Error.WriteLine("FAIL: " + ex.Message); return 1; }
        }
    }
}
