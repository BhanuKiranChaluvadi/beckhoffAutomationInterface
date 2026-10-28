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
                @"bai activate C:\bai\work\sample --target umrt", shell: true,
                notes: "Activation replaces the target boot project. It first prints the boot project that is on the target now."),
            Entry("license", "Check TwinCAT licenses or make a 7-day trial license.", Runtime.LicenseCommands.Usage,
                @"bai license check --target local --licenses ""TC3 PLC,TC3 NC PTP""", "check|trial", shell: true,
                notes: "Reads the license files on this PC (local runtime or UmRT). 'bai license trial --target umrt' renews every trial license of UmRT that ends in less than 2 days. Trial opens XAE on a license-only project with the missing licenses; you click '7 Days Trial License...' and type the code. It never activates."),
            Entry("verify", "Compare live PLC values with an expectation file.", Runtime.RuntimeCommands.VerifyUsage,
                @"bai verify --target umrt --expect ST\sample\expect.json",
                notes: "Reads values over ADS. Use --variant when the expectation file has no default entry."),
            Entry("test", "Run PLC scenarios over ADS and report their results.", Runtime.RuntimeCommands.TestUsage,
                @"bai test --target umrt --scenarios Tests --tags smoke --junit results.xml",
                notes: "Scenarios can send commands and write PLC values. Choose --variants-file or --groups, not both. At least one scenario must execute."),
            Entry("runtime-state", "Read, check, or change the runtime state over ADS.", Runtime.RuntimeStateCommand.Usage,
                "bai runtime-state --target umrt --check run",
                notes: "--check reads the state and changes nothing; --set changes it. No XAE session is needed."),
            Entry("release", "Build release bundles or capture library baselines.", Release.ReleaseCommands.Usage,
                @"bai release lock --machines MachineA --output libraries.lock.json",
                "build|lock", shell: true,
                notes: "bai.machines.json \"release\" gives the TwinCAT major and the approved bai SHA256; the library lock is <family folder>\\libraries.lock.json of the source. The options override them. A tag that is not HEAD is built from a clean checkout; your working tree is not touched. One bundle per family. A toolchain archive is optional. Only the TwinCAT major is compared; the build is recorded. It creates a bundle without activating a runtime."),
            Entry("package", "Create, verify, compare, or install compiled packages.", Release.ArtifactCommands.Usage,
                @"bai package check --package NFPro.zip", "create|verify|check|install|rollback|drift",
                notes: "check changes nothing. install checks first, then installs; it keeps the old image (Boot.before.<id>, and boot-before.zip in the record folder) and a copy of the package (Boot.installed), so the next install and a rollback need no other options. rollback undoes the last install."),
            Entry("libraries", "Capture or verify compiled library baselines.", Release.ArtifactCommands.Usage,
                @"bai libraries lock --project App.tsproj --machine MachineA --platform ""TwinCAT RT (x64)"" --output libraries.lock.json",
                "lock|verify"),
            Entry("toolchain", "Check or install the TwinCAT packages of a build PC, or capture or verify an offline TwinCAT toolchain.", Release.ArtifactCommands.Usage,
                "bai toolchain verify --toolchain toolchain.zip --sha256 <hash> --twincat-version 3.1.4026.24",
                "check|install|capture|verify",
                notes: "check and install use \"release\" \"packages\" of bai.machines.json: the TwinCAT packages decide the library versions of each build. install adds only missing packages and switches on the 64-bit XAE shell; it never changes an installed version (do that on purpose, then make a new library lock). install needs administrator rights and the Beckhoff Stable Feed signed in."),
            Entry("esi", "Install or verify an EtherCAT device catalog.", Release.ArtifactCommands.Usage,
                @"bai esi verify --system-esi C:\TwinCAT\ESI --required-esi .\esi --user-esi C:\bai\esi",
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
            ["--timeout-seconds"] = "Positive timeout in seconds.",
            ["--scenarios"] = "Scenario JSON file or folder (required).",
            ["--variants-file"] = "variants.xml used to resolve feature groups.",
            ["--groups"] = "Comma-separated feature groups; alternative to --variants-file.",
            ["--tags"] = "Run scenarios matching these comma-separated tags.",
            ["--junit"] = "Write test results as JUnit XML.",
            ["--set"] = "Change the runtime state to config or run.",
            ["--check"] = "Check that the runtime state is config or run. Changes nothing.",
            ["--fresh"] = "Create a fresh machine project instead of reusing a kept build.",
            ["--release"] = "Require a clean, tagged repository for a production build.",
            ["--simulation"] = "Build with the SIMULATION compiler define.",
            ["--parallel"] = "Build machines in separate concurrent processes.",
            ["--manifest"] = "Manifest file for this command.",
            ["--work"] = "Build workspace folder outside the source repository.",
            ["--licenses"] = "Comma-separated license names (for example \"TC3 PLC,TC3 NC PTP\") or license ids {...}.",
            ["--wait-minutes"] = "Minutes to wait for the new license file (default 15).",
            ["--configuration"] = "Retained native configuration.xml file.",
            ["--output"] = "Destination path for the generated artifact.",
            ["--machines"] = "A family name (all its machines) or comma-separated machine names. Optional for a build: default all machines.",
            ["--machine"] = "Machine label recorded in the library baseline.",
            ["--tag"] = "Git tag to build (pushed). When it is not HEAD, bai builds it from a clean checkout. Default: HEAD.",
            ["--include-uncommitted"] = "Build the working tree with its uncommitted changes (a development package, not for a machine).",
            ["--bai-sha256"] = "Approved SHA-256 hash of the bai executable. Default: \"release\" \"bai\" of bai.machines.json.",
            ["--twincat-version"] = "Required TwinCAT major, for example 3.1.4026 (only the major is compared). Default: \"release\" \"twincat\" of bai.machines.json.",
            ["--library-lock"] = "Approved library baseline JSON. Default: <family folder>\\libraries.lock.json of the source.",
            ["--library-lock-sha256"] = "Retained SHA-256 hash of --library-lock (needed with it).",
            ["--toolchain"] = "Optional retained TwinCAT toolchain ZIP of one exact build (with --toolchain-sha256).",
            ["--toolchain-sha256"] = "Retained SHA-256 hash of the toolchain ZIP (with --toolchain).",
            ["--system-esi"] = "System EtherCAT device catalog folder (ESI files).",
            ["--user-esi"] = "User EtherCAT device catalog folder (ESI files).",
            ["--required-esi"] = "Folder with the ESI files that the machines need.",
            ["--payload"] = "Folder with the package payload.",
            ["--metadata"] = "Package metadata JSON without file records.",
            ["--package"] = "Compiled package ZIP.",
            ["--sha256"] = "Retained SHA-256 hash of the input artifact.",
            ["--installed"] = "The package installed on the target now. bai finds it itself (Boot.installed); give it only to override.",
            ["--installed-sha256"] = "Retained SHA-256 hash of the installed package (default: <zip>.sha256).",
            ["--saved-project"] = "Saved XAE project of the installed package: adds the drift check.",
            ["--rehearsal"] = "Run the package operation as an UmRT rehearsal.",
            ["--record"] = "Install: new folder for the install record (logs, install.json, boot-before.zip); default C:\\bai\\updates\\<machine>-<time>. Rollback: the record folder of the install to undo; default the last install.",
            ["--first-install"] = "Force a first install (no installed package). bai chooses it itself when it installed nothing on the target.",
            ["--ssh"] = "SSH user@host of the target: a Windows IPC (runs the command there from this PC, over OpenSSH) or a TwinCAT/BSD host.",
            ["--baseline"] = "Retained project ZIP used for drift comparison.",
            ["--components"] = "TwinCAT Components folder to archive.",
            ["--xae-shell"] = "TwinCAT XAE shell folder to archive.",
            ["--check-installed"] = "Also verify the installed toolchain files.",
            ["--library-list"] = "Optional libraries.xml to validate against the compiled cache.",
            ["--json"] = "Emit machine-readable JSON instead of a text report.",
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
            new[] { "test", "machine", "native", "release", "package", "libraries", "toolchain", "esi", "license" }.Contains(name) ? 2 : 1;

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
                arguments.Add(command.Name == "export" ? Pair("<output>", "Destination folder for exported source and manifests.") : Pair("<source>", "Folder containing .st source files and manifests."));
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
            layout.Rows(options.Select(o => Pair(OptionLabel(command.Name, o), (o.StartsWith("--help") ? "" : Need(usage, o) + " ") + Description(command.Name, o))));
            layout.Section("Examples");
            // Keep examples scoped to the requested subcommand.
            layout.Text(Example(command, mode), 2, 6);
            layout.Section("Notes");
            layout.Text("Angle brackets indicate values. [ ] = optional; without brackets = required. a | b = one of them. Repeatable options end in '...'. Quote paths containing spaces.");
            if (command.Notes != null && !(command.Name == "package" && mode != null && mode != "install" && mode != "rollback"))
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

        /// <summary>"Required." when each usage line shown has the option outside [ ], "Required in some
        /// forms (see Usage)." when only some lines do, else "Optional." (also for options not in the usage).</summary>
        internal static string Need(IEnumerable<string> usage, string option)
        {
            string name = option.Split(' ')[0].TrimEnd(',');
            var required = usage.Select(line => Regex.Matches(line, Regex.Escape(name) + @"(?![\w-])").Cast<Match>().Any(m =>
                line.Substring(0, m.Index).Count(c => c == '[') == line.Substring(0, m.Index).Count(c => c == ']'))).ToList();
            return required.Count > 0 && required.All(r => r) ? "Required." : required.Any(r => r) ? "Required in some forms (see Usage)." : "Optional.";
        }

        static string OptionLabel(string command, string option)
        {
            if (option == "--target") return "--target <target>";
            if (option == "--set" || option == "--check") return option + " <state>";
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
                        : prefix + " --tag v1.0.0 --machines FamilyA";
                case "package":
                    switch (mode)
                    {
                        case "create": return prefix + @" --payload .\payload --metadata metadata.json --output release.zip";
                        case "verify": return command.Example;
                        case "check": return prefix + @" --package NFPro.zip";
                        case "install": return prefix + @" --package NFPro.zip";
                        case "rollback": return prefix;
                        case "drift": return prefix + @" --baseline project.zip --sha256 <hash> --saved-project C:\bai\service --output drift.json";
                    }
                    break;
                case "libraries":
                    return prefix + @" --project App.tsproj --machine MachineA --platform ""TwinCAT RT (x64)"""
                        + (mode == "lock" ? " --output libraries.lock.json" : " --library-lock libraries.lock.json --library-lock-sha256 <hash>");
                case "toolchain":
                    return mode == "check" || mode == "install" ? prefix
                        : mode == "verify" ? command.Example
                        : prefix + @" --components C:\TwinCAT\Components --xae-shell C:\TwinCAT\TcXaeShell --twincat-version 3.1.4026.24 --output toolchain.zip";
                case "esi": return prefix + @" --system-esi C:\TwinCAT\ESI --required-esi .\esi --user-esi C:\bai\esi";
                case "license": return mode == "trial" ? prefix + " --target umrt" : prefix + " --target umrt --package NFPro.zip";
            }
            return command.Example;
        }

        static string Description(string command, string option)
        {
            string name = option.Split(' ')[0].TrimEnd(',');
            if (name == "--timeout-seconds")
                return command == "verify" ? "Wait for PLC Run, in seconds (default: 60)."
                    : command == "activate" ? "Wait for PLC Run, in seconds (default: 60)."
                    : command == "test" ? "Wait for PLC Run, in seconds (default: 60). Scenario timeouts come from the scenario files."
                    : Descriptions[name];
            if (name == "--manifest" && (command == "machine" || command == "release" || command == "toolchain")) return "bai.machines.json path; discovered from the current directory when omitted.";
            if (command == "license" && name == "--target") return "Runtime on this PC: local or umrt (default local).";
            if (command == "license" && name == "--work") return @"Folder for the license-only project (default C:\bai\work\license).";
                        return Descriptions.TryGetValue(name, out string description) ? description : "See the command reference in README.md.";
        }

        internal static string ModeSummary(string command, string mode)
        {
            switch (command + " " + mode)
            {
                case "check events": return "Check event classes";
                case "license check": return "Show the license files of a runtime and when they expire; with --package or --licenses, check that each license is there and valid";
                case "license trial": return "Make a 7-day trial license for each license that is missing or ends in less than 2 days (without --package or --licenses: the licenses of the trial files on the target). Opens XAE; you click '7 Days Trial License...' and type the code";
                case "machine build": return "Assemble source and build selected machines";
                case "machine test": return "Build, activate on UmRT, verify, and run scenarios";
                case "native capture": return "Capture configuration from a reviewed project";
                case "native apply": return "Apply configuration to a closed project";
                case "native links": return "Write links.xml from retained mappings";
                case "release build": return "Build a release bundle with evidence and packages";
                case "release lock": return "Capture a candidate library baseline";
                case "package create": return "Create a package from payload and metadata";
                case "package verify": return "Verify package contents and optional retained hash";
                case "package check": return "Run every check of an install; change nothing";
                case "package install": return "Check, then install a compiled package (automatic rollback on a failure)";
                case "package rollback": return "Undo the last install (or the one in --record)";
                case "package drift": return "Compare a project with its retained baseline";
                case "libraries lock": return "Capture compiled library versions and hashes";
                case "libraries verify": return "Compare compiled libraries with a baseline";
                case "toolchain capture": return "Archive the TwinCAT components and XAE shell";
                case "toolchain verify": return "Verify archive hash, version, and optional installed files";
                case "toolchain check": return "Compare the installed TwinCAT packages with \"release\" \"packages\" of bai.machines.json; change nothing";
                case "toolchain install": return "Install the missing TwinCAT packages of bai.machines.json (dry run, then you type y); never change an installed version";
                case "esi install": return "Install missing EtherCAT catalog files";
                case "esi verify": return "Check required EtherCAT catalog files";
                default:
                    bool shared = command == "sync" || command == "export" || command == "check";
                    return shared && ModeDescriptions.TryGetValue(mode, out string summary) ? summary : mode;
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
                Pair("Runtime and diagnostics", "doctor license activate verify test runtime-state"),
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
