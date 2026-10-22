using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;

namespace BeckhoffAutomationInterface.Cli
{
    /// <summary>Public command catalog and a single help/error presentation for all dispatchers.
    /// Usage syntax is shared with the parsers; help never resolves paths or opens TwinCAT.</summary>
    internal static class CliHelp
    {
        internal sealed class Command
        {
            internal string Name, Summary, Usage, Example, Notes;
            internal string[] Modes = new string[0];
            internal bool Config, Shell;
        }

        static Command Entry(string name, string summary, string usage, string example,
            string modes = null, bool config = false, bool shell = false, string notes = null) =>
            new Command { Name = name, Summary = summary, Usage = usage, Example = example,
                Modes = modes?.Split('|') ?? new string[0], Config = config, Shell = shell, Notes = notes };

        internal static readonly Command[] Commands =
        {
            Entry("init", "Create a TwinCAT solution, synchronize source, and build.", CommandLine.InitUsage,
                @"bai init ST\sample --into C:\bai\work", config: true, shell: true,
                notes: "The default project name is the source folder name. PLC names must contain only letters, digits, and underscores."),
            Entry("sync", "Synchronize source and manifests into a managed project.", CommandLine.SyncUsage,
                @"bai sync all ST\sample --project C:\bai\work\sample",
                "code|libs|io|events|variants|safety|tasks|all", true, true,
                "Requires a project created by bai init. The all mode also builds the project."),
            Entry("build", "Compile an existing TwinCAT project.", CommandLine.BuildUsage,
                @"bai build C:\bai\work\sample --clean", config: true, shell: true,
                notes: "Accepts a .tsproj, a .sln, or a project folder. Adopted projects are opened read-only."),
            Entry("check", "Check source syntax, formatting, or event classes.", CommandLine.CheckUsage,
                @"bai check parse ST\sample", "parse|format|events", true,
                notes: "Read-only checks; no XAE session is started. Format checks report issues without rewriting source."),
            Entry("export", "Export PLC source and configuration from a project.", CommandLine.ExportUsage,
                @"bai export all C:\bai\export --project C:\bai\work\sample",
                "code|libs|io|events|tasks|safety|variants|native|links|all|object", true, true,
                "Use an empty output folder or --overwrite. Export does not save changes to the project."),
            Entry("machine", "Build or test machines defined in bai.machines.json.", Machines.MachineCommands.Usage,
                "bai machine build MachineA --fresh", "build|test", shell: true,
                notes: "Omit machine names to select all machines. Test builds use simulation and activate UmRT; machine test runs are serialized."),
            Entry("native", "Capture or apply retained native configuration.", Machines.NativeCommands.Usage,
                @"bai native capture --project C:\bai\work\sample\sample.tsproj --output C:\bai\native",
                "capture|apply|links", notes: "Apply requires a closed project. These commands emit JSON results."),
            Entry("doctor", "Inspect installed shells, runtimes, and licenses.", "doctor", "bai doctor",
                notes: "Read-only diagnostics for this PC. Reports TwinCAT versions, boot projects, and license expiry."),
            Entry("activate", "Build and activate a project on a TwinCAT runtime.", Runtime.RuntimeCommands.ActivateUsage,
                @"bai activate C:\bai\work\sample --target umrt --yes", shell: true,
                notes: "Without --yes, reports the target and makes no changes. Activation replaces the target boot project."),
            Entry("verify", "Compare live PLC values with an expectation file.", Runtime.RuntimeCommands.VerifyUsage,
                @"bai verify --target umrt --expect ST\sample\expect.json",
                notes: "Reads values over ADS. Use --variant when the expectation file has no default entry."),
            Entry("test", "Run PLC scenarios over ADS and report their results.", Runtime.RuntimeCommands.TestUsage,
                @"bai test --target umrt --scenarios Tests --tags smoke --junit results.xml",
                notes: "Scenarios can send commands and write PLC values. Choose --variants or --groups, not both. At least one scenario must execute."),
            Entry("runtime-state", "Read, check, or change the runtime state over ADS.", Runtime.RuntimeStateCommand.Usage,
                "bai runtime-state --target umrt --expect run",
                notes: "Choose --expect to check a state or --state with --yes to change it. No XAE session is needed."),
            Entry("release", "Build release bundles or capture library baselines.", Release.ReleaseCommands.Usage,
                @"bai release lock --machines MachineA --twincat-version 3.1.4026.24 --output libraries.lock.json",
                "build|lock", shell: true,
                notes: "Build requires retained hashes for the tool, library lock, and toolchain. It creates a bundle without activating a runtime."),
            Entry("package", "Create, verify, compare, or install compiled packages.", Release.ArtifactCommands.Usage,
                @"bai package verify --package release.zip", "create|verify|preflight|install|drift",
                notes: "Install requires --yes and retains rollback evidence. Without --yes, it makes no changes."),
            Entry("libraries", "Capture or verify compiled library baselines.", Release.ArtifactCommands.Usage,
                @"bai libraries lock --project App.tsproj --machine MachineA --platform ""TwinCAT RT (x64)"" --output libraries.lock.json",
                "lock|verify"),
            Entry("toolchain", "Capture or verify an offline TwinCAT toolchain.", Release.ArtifactCommands.Usage,
                "bai toolchain verify --archive toolchain.zip --sha256 <hash> --version 3.1.4026.24",
                "capture|verify"),
            Entry("esi", "Install or verify an EtherCAT device catalog.", Release.ArtifactCommands.Usage,
                @"bai esi verify --system-directory C:\TwinCAT\ESI --devices .\esi --user-directory C:\bai\esi",
                "install|verify", notes: "Existing catalog files are never replaced."),
        };

        static readonly Dictionary<string, string> Descriptions = new Dictionary<string, string>
        {
            ["--help"] = "Show help and exit.",
            ["--shell"] = "DTE shell: xae or vs2022. Defaults to installed XAE, then VS 2022.",
            ["--config"] = "Read .stconfig from this folder. Command-line values take precedence.",
            ["--no-config"] = "Ignore .stconfig defaults.",
            ["--project"] = "TwinCAT project path.",
            ["--into"] = "Parent folder for the new solution (required).",
            ["--name"] = "New project name; defaults to the source folder name.",
            ["--plc"] = "PLC project name; use to disambiguate projects with multiple PLCs.",
            ["--platform"] = "Target: TwinCAT RT (x86), TwinCAT RT (x64), or TwinCAT OS (x64). Quote names containing spaces.",
            ["--ignore"] = "Exclude a source glob. May be repeated.",
            ["--clean"] = "Perform a clean build.",
            ["--variant"] = "Select one project variant.",
            ["--all-variants"] = "Build every variant; cannot be combined with --variant.",
            ["--define"] = "Add a PLC compiler define. May be repeated.",
            ["--confirm-delete-io"] = "Allow I/O device recreation when its layout changes.",
            ["--overwrite"] = "Allow replacing files in the export folder.",
            ["--target"] = "Runtime: umrt, local, or an AMS Net ID.",
            ["--expect"] = "Expectation JSON file to compare with live PLC values.",
            ["--timeout"] = "Positive timeout in seconds.",
            ["--yes"] = "Confirm the requested runtime change.",
            ["--scenarios"] = "Scenario JSON file or folder (required).",
            ["--variants"] = "variants.xml used to resolve feature groups.",
            ["--groups"] = "Comma-separated feature groups; alternative to --variants.",
            ["--tags"] = "Run scenarios matching these comma-separated tags.",
            ["--junit"] = "Write test results as JUnit XML.",
            ["--state"] = "Requested runtime state: config or run. Requires --yes.",
            ["--fresh"] = "Create a fresh machine project instead of reusing a kept build.",
            ["--release"] = "Require a clean, tagged repository for a production build.",
            ["--simulation"] = "Build with the SIMULATION compiler define.",
            ["--parallel"] = "Build machines in separate concurrent processes.",
            ["--manifest"] = "Manifest file for this command.",
            ["--work"] = "Build workspace folder outside the source repository.",
            ["--configuration"] = "Retained native configuration.xml file.",
            ["--output"] = "Destination path for the generated artifact.",
            ["--machines"] = "Comma-separated machine names (required).",
            ["--machine"] = "Machine label recorded in the library baseline.",
            ["--tag"] = "Git tag to build; alternative to --working-tree.",
            ["--working-tree"] = "Build from the working tree instead of a release tag.",
            ["--bundle"] = "New release bundle folder.",
            ["--bai-sha256"] = "Retained SHA-256 hash of the bai executable (required).",
            ["--twincat-version"] = "Required TwinCAT build version.",
            ["--library-lock"] = "Approved library baseline JSON.",
            ["--library-lock-sha256"] = "Retained SHA-256 hash of the library baseline.",
            ["--toolchain"] = "Retained TwinCAT toolchain ZIP.",
            ["--toolchain-sha256"] = "Retained SHA-256 hash of the toolchain ZIP.",
            ["--esi-directory"] = "System EtherCAT device catalog folder.",
            ["--user-esi-directory"] = "User EtherCAT device catalog folder.",
            ["--directory"] = "Folder containing the package payload.",
            ["--metadata"] = "Package metadata JSON without file records.",
            ["--package"] = "Compiled package ZIP.",
            ["--sha256"] = "Retained SHA-256 hash of the input artifact.",
            ["--current"] = "Current package ZIP for preflight or rollback.",
            ["--current-sha256"] = "Retained SHA-256 hash of the current package.",
            ["--service-project"] = "Service project folder for the target machine.",
            ["--report"] = "New report destination (see usage for file or folder).",
            ["--rehearsal"] = "Run the package operation as an UmRT rehearsal.",
            ["--evidence"] = "New folder for installation evidence.",
            ["--ssh"] = "SSH user@host for a remote TwinCAT/BSD installation.",
            ["--baseline"] = "Retained project ZIP used for drift comparison.",
            ["--components"] = "TwinCAT Components folder to archive.",
            ["--xae-shell"] = "TwinCAT XAE shell folder to archive.",
            ["--version"] = "TwinCAT version recorded or checked in the archive.",
            ["--archive"] = "Toolchain ZIP path.",
            ["--installed"] = "Also verify the installed toolchain.",
            ["--lock"] = "Approved library baseline JSON to verify against.",
            ["--json"] = "Emit machine-readable JSON instead of a text report.",
            ["--system-directory"] = "System EtherCAT catalog folder.",
            ["--devices"] = "Folder containing required device ESI files.",
            ["--user-directory"] = "User EtherCAT catalog folder.",
        };

        static readonly Dictionary<string, string> ModeDescriptions = new Dictionary<string, string>
        {
            ["code"] = "PLC source (.st files)", ["libs"] = "Library references", ["io"] = "I/O devices and mappings",
            ["events"] = "Event classes", ["variants"] = "Project variants and feature groups",
            ["safety"] = "TwinSAFE project files", ["tasks"] = "PLC task configuration", ["all"] = "All supported stages",
            ["object"] = "One named PLC object", ["native"] = "Native project archive", ["links"] = "Variable mappings",
            ["parse"] = "Parse source and report lint and link findings", ["format"] = "Check source formatting",
        };

        internal static string Version => typeof(CliHelp).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
            ?? typeof(CliHelp).Assembly.GetName().Version.ToString();

        internal static string[] HelpPath(string[] args)
        {
            if (args.Length == 0 || args[0] == "--help" || args[0] == "-h") return new string[0];
            Command command = Commands.FirstOrDefault(c => c.Name == args[0]);
            return command != null && command.Modes.Length > 0 && args.Length > 1 && !args[1].StartsWith("-")
                ? args.Take(2).ToArray() : args.Take(1).ToArray();
        }
        internal static int UsageExitCode(string name) =>
            new[] { "test", "machine", "native", "release", "package", "libraries", "toolchain", "esi" }.Contains(name) ? 2 : 1;

        // One logical invocation per line, assembled from the existing wrapped usage constants.
        internal static IEnumerable<string> Invocations(Command command, string mode = null)
        {
            var lines = new List<string>();
            foreach (string raw in command.Usage.Replace("Usage: beckhoffAutomationInterface ", "").Split('\n'))
            {
                string line = raw.Trim();
                if (Commands.Any(c => line == c.Name || line.StartsWith(c.Name + " ", StringComparison.Ordinal)))
                    lines.Add(line);
                else if (lines.Count > 0) lines[lines.Count - 1] += " " + line;
            }
            foreach (string line in lines.Where(l => l == command.Name || l.StartsWith(command.Name + " ", StringComparison.Ordinal)))
            {
                string[] tokens = line.Split(' ');
                if (mode == null) { yield return line; continue; }
                if (tokens.Length > 1 && tokens[1].Split('|').Contains(mode))
                {
                    tokens[1] = mode;
                    yield return string.Join(" ", tokens);
                }
            }
        }

        static bool Resolve(string[] path, out Command command, out string mode)
        {
            command = Commands.FirstOrDefault(c => c.Name == path.FirstOrDefault());
            mode = path.Length > 1 ? path[1] : null;
            return command != null && path.Length <= 2 && (mode == null || command.Modes.Contains(mode));
        }

        internal static int Write(TextWriter writer, TextWriter error, string[] path, int width = 80)
        {
            if (path.Length == 0) { WriteOverview(new ConsoleLayout(writer, width)); return 0; }
            if (!Resolve(path, out Command command, out string mode))
            {
                WriteError(error, path, "Unknown help topic '" + string.Join(" ", path) + "'.", width);
                return UsageExitCode(path[0]);
            }
            var layout = new ConsoleLayout(writer, width);
            layout.Heading("bai " + command.Name + (mode == null ? "" : " " + mode));
            layout.Text(mode == null ? command.Summary : ModeSummary(command.Name, mode) + ".", 0);
            layout.Section("Usage");
            string[] usage = Invocations(command, mode).ToArray();
            foreach (string line in usage) layout.Text("bai " + CompactModes(line, command, mode), 2, 6);
            if (mode == null && command.Modes.Length > 0)
            {
                layout.Section("Subcommands");
                layout.Rows(command.Modes.Select(m => Pair(m, ModeSummary(command.Name, m))));
            }
            var arguments = new List<KeyValuePair<string, string>>();
            if (new[] { "init", "sync", "check", "export" }.Contains(command.Name))
                arguments.Add(Pair("<source>", command.Name == "export" ? "Destination folder for exported source and manifests." : "Folder containing .st source files and manifests."));
            if (command.Name == "build" || command.Name == "activate")
                arguments.Add(Pair("<project>", "A .tsproj, a .sln, or a folder containing one TwinCAT project."));
            if (command.Name == "machine") arguments.Add(Pair("<Machine>...", "Machine names, separated by spaces or commas. Defaults to all machines."));
            if (command.Name == "export" && (mode == null || mode == "object"))
                arguments.Add(Pair("<ObjectName>", "PLC object to export (object mode only)."));
            if (arguments.Count > 0)
            {
                layout.Section("Arguments");
                layout.Rows(arguments);
            }
            var options = Regex.Matches(string.Join(" ", usage), @"--[\w-]+(?:\s+<[^>]+>)?")
                .Cast<Match>().Select(m => m.Value).GroupBy(o => o.Split(' ')[0]).Select(g => g.First()).ToList();
            if (command.Shell) options.Add("--shell <name>");
            if (command.Config) { options.Add("--config <folder>"); options.Add("--no-config"); }
            options.Add("--help, -h");
            layout.Section("Options");
            layout.Rows(options.Select(o => Pair(OptionLabel(command.Name, o), Description(command.Name, o))));
            layout.Section("Examples");
            // Keep examples scoped to the requested subcommand.
            layout.Text(Example(command, mode), 2, 6);
            layout.Section("Notes");
            layout.Text("Angle brackets indicate values; square brackets indicate optional arguments. Repeatable options end in '...'. Quote paths containing spaces.");
            if (command.Notes != null && !(command.Name == "package" && mode != null && mode != "install"))
                layout.Text(command.Notes);
            layout.Section("Exit codes");
            layout.Rows(ExitCodes(command.Name));
            layout.Blank();
            layout.Text("Run 'bai --help' for all commands.", 0);
            return 0;
        }

        static string CompactModes(string usage, Command command, string mode)
        {
            if (mode != null || command.Modes.Length == 0) return usage;
            string[] parts = usage.Split(' ');
            if (parts.Length > 1 && parts[1].Contains("|")) parts[1] = "<mode>";
            return string.Join(" ", parts);
        }

        static string OptionLabel(string command, string option)
        {
            if (option == "--target") return "--target <target>";
            if (option == "--state" || (command == "runtime-state" && option == "--expect")) return option + " <state>";
            return option;
        }

        static string Example(Command command, string mode)
        {
            if (mode == null) return command.Example;
            string prefix = "bai " + command.Name + " " + mode;
            switch (command.Name)
            {
                case "sync": return prefix + @" ST\sample --project C:\bai\work\sample";
                case "check": return prefix + @" ST\sample" + (mode == "events" ? @" --project C:\bai\work\sample" : "");
                case "export": return prefix + (mode == "object" ? " MAIN" : "") + @" C:\bai\export --project C:\bai\work\sample";
                case "machine": return prefix + " MachineA" + (mode == "test" ? " --tags smoke --junit results.xml" : " --fresh");
                case "native":
                    return prefix + (mode == "capture" ? @" --project App.tsproj --output C:\bai\native"
                        : mode == "apply" ? @" --configuration C:\bai\native\configuration.xml --project App.tsproj"
                        : @" --configuration C:\bai\native\configuration.xml --plc App --output links.xml");
                case "release":
                    return mode == "lock" ? command.Example
                        : prefix + " --machines MachineA --tag v1.0.0 --bai-sha256 <hash> --twincat-version 3.1.4026.24"
                            + " --library-lock libraries.lock.json --library-lock-sha256 <hash> --toolchain toolchain.zip --toolchain-sha256 <hash>";
                case "package":
                    switch (mode)
                    {
                        case "create": return prefix + @" --directory .\payload --metadata metadata.json --output release.zip";
                        case "verify": return command.Example;
                        case "preflight": return prefix + @" --current current.zip --current-sha256 <hash> --service-project C:\bai\service --report C:\bai\preflight";
                        case "install": return prefix + @" --package release.zip --sha256 <hash> --current current.zip --current-sha256 <hash> --service-project C:\bai\service --evidence C:\bai\evidence --target umrt --rehearsal --yes";
                        case "drift": return prefix + @" --baseline project.zip --sha256 <hash> --project C:\bai\service --report drift.json";
                    }
                    break;
                case "libraries":
                    return prefix + @" --project App.tsproj --machine MachineA --platform ""TwinCAT RT (x64)"""
                        + (mode == "lock" ? " --output libraries.lock.json" : " --lock libraries.lock.json --sha256 <hash>");
                case "toolchain":
                    return mode == "verify" ? command.Example
                        : prefix + @" --components C:\TwinCAT\Components --xae-shell C:\TwinCAT\TcXaeShell --version 3.1.4026.24 --archive toolchain.zip";
                case "esi": return prefix + @" --system-directory C:\TwinCAT\ESI --devices .\esi --user-directory C:\bai\esi";
            }
            return command.Example;
        }

        static string Description(string command, string option)
        {
            string name = option.Split(' ')[0].TrimEnd(',');
            if (command == "runtime-state" && name == "--expect") return "Expected runtime state: config or run. Does not change the state.";
            if (name == "--timeout")
                return command == "verify" ? "Wait for PLC Run, in seconds (default: 60)."
                    : command == "activate" ? "Wait for PLC Run, in seconds (default: 60)."
                    : command == "test" ? "Wait for PLC Run, in seconds (default: 60). Scenario timeouts come from the scenario files."
                    : Descriptions[name];
            if (name == "--manifest" && (command == "machine" || command == "release")) return "bai.machines.json path; discovered from the current directory when omitted.";
            if (name == "--manifest" && command == "libraries") return "Optional libraries.xml to validate against the compiled cache.";
            return Descriptions.TryGetValue(name, out string description) ? description : "See the command reference in README.md.";
        }

        static string ModeSummary(string command, string mode)
        {
            if (command == "sync" || command == "export" || command == "check")
                return ModeDescriptions.TryGetValue(mode, out string summary) ? summary : mode;
            switch (command + " " + mode)
            {
                case "machine build": return "Assemble source and build selected machines";
                case "machine test": return "Build, activate on UmRT, verify, and run scenarios";
                case "native capture": return "Capture configuration from a reviewed project";
                case "native apply": return "Apply configuration to a closed project";
                case "native links": return "Write links.xml from retained mappings";
                case "release build": return "Build a release bundle with evidence and packages";
                case "release lock": return "Capture a candidate library baseline";
                case "package create": return "Create a package from payload and metadata";
                case "package verify": return "Verify package contents and optional retained hash";
                case "package preflight": return "Check readiness before installation";
                case "package install": return "Install a compiled package with rollback";
                case "package drift": return "Compare a project with its retained baseline";
                case "libraries lock": return "Capture compiled library versions and hashes";
                case "libraries verify": return "Compare compiled libraries with a baseline";
                case "toolchain capture": return "Archive the TwinCAT components and XAE shell";
                case "toolchain verify": return "Verify archive hash, version, and optional installed files";
                case "esi install": return "Install missing EtherCAT catalog files";
                case "esi verify": return "Check required EtherCAT catalog files";
                default: return mode;
            }
        }

        static IEnumerable<KeyValuePair<string, string>> ExitCodes(string command)
        {
            yield return Pair("0", "Success.");
            yield return Pair("1", "Operation failed, a check failed, or a requested change was not confirmed."
                + (UsageExitCode(command) == 1 ? " Also used for invalid arguments." : ""));
            if (UsageExitCode(command) == 2) yield return Pair("2", "Invalid command line or input.");
            if (command == "machine") yield return Pair("3", "Another run holds the UmRT lock (machine test).");
        }

        static void WriteOverview(ConsoleLayout layout)
        {
            layout.Heading("bai - Beckhoff Automation Interface");
            layout.Text("Build, synchronize, test, and release TwinCAT 3 PLC projects.", 0);
            layout.Section("Usage");
            layout.Text("bai <command> [<subcommand>] [arguments] [options]");
            layout.Text("bai help [<command> [<subcommand>]]");
            layout.Section("Commands");
            foreach (var group in new[]
            {
                Pair("Project and source", "init sync build check export machine native"),
                Pair("Runtime and diagnostics", "doctor activate verify test runtime-state"),
                Pair("Release and artifacts", "release package libraries toolchain esi")
            })
            {
                layout.Text(group.Key, 0);
                layout.Rows(group.Value.Split(' ').Select(name => Commands.First(c => c.Name == name)).Select(c => Pair(c.Name, c.Summary)));
                layout.Blank();
            }
            layout.Text("Common options:", 0);
            layout.Rows(new[]
            {
                Pair("--help, -h", "Show help for a command or subcommand."),
                Pair("--version", "Show the bai version and exit."),
                Pair("--shell <name>", "Select xae or vs2022 for commands that open TwinCAT."),
                Pair("--config <folder>", "Read .stconfig for init, sync, build, check, or export."),
                Pair("--no-config", "Ignore .stconfig for those project commands."),
            });
            layout.Section("Examples");
            layout.Text("bai check parse ST\\sample");
            layout.Text("bai init ST\\sample --into C:\\bai\\work");
            layout.Text("bai sync all ST\\sample --project C:\\bai\\work\\sample");
            layout.Text("bai package verify --help");
            layout.Blank();
            layout.Text("Run 'bai <command> --help' or 'bai help <command>' for details.", 0);
            layout.Text("The executable is beckhoffAutomationInterface.exe; bai is its short alias.", 0);
        }

        internal static void WriteError(TextWriter writer, string[] args, string message, int width = 80)
        {
            var layout = new ConsoleLayout(writer, width);
            layout.Text("ERROR: " + message, 0, 2);
            var command = Commands.FirstOrDefault(c => c.Name == args.FirstOrDefault());
            if (command != null)
            {
                string mode = args.Length > 1 && command.Modes.Contains(args[1]) ? args[1] : null;
                layout.Section("Usage");
                foreach (string line in Invocations(command, mode)) layout.Text("bai " + CompactModes(line, command, mode), 2, 6);
                layout.Blank();
                layout.Text("Run 'bai " + command.Name + (mode == null ? "" : " " + mode) + " --help' for details.", 0);
            }
            else
            {
                string suggestion = Suggest(args.FirstOrDefault());
                if (suggestion != null) layout.Text("Did you mean 'bai " + suggestion + "'?", 0);
                layout.Blank();
                layout.Text("Run 'bai --help' for available commands.", 0);
            }
        }

        static string Suggest(string value)
        {
            if (string.IsNullOrEmpty(value) || value.Length > 30) return null;
            return Commands.Select(c => new { c.Name, Distance = Distance(value, c.Name) })
                .Where(c => c.Distance <= 2).OrderBy(c => c.Distance).ThenBy(c => c.Name, StringComparer.Ordinal)
                .FirstOrDefault()?.Name;
        }

        static int Distance(string a, string b)
        {
            int[] row = Enumerable.Range(0, b.Length + 1).ToArray();
            for (int i = 0; i < a.Length; i++)
            {
                int previous = row[0];
                row[0] = i + 1;
                for (int j = 0; j < b.Length; j++)
                {
                    int old = row[j + 1];
                    row[j + 1] = Math.Min(Math.Min(row[j + 1] + 1, row[j] + 1), previous + (a[i] == b[j] ? 0 : 1));
                    previous = old;
                }
            }
            return row[b.Length];
        }

        static KeyValuePair<string, string> Pair(string key, string value) => new KeyValuePair<string, string>(key, value);
    }
}
