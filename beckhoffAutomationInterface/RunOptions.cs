using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace BeckhoffAutomationInterface
{
    /// <summary>
    /// Resolved configuration for one run, built once from command-line arguments by
    /// <see cref="Parse"/>. Replaces the previously-hardcoded "Shark" paths and the
    /// _buildOnly/_eventsOnly static fields that used to live directly on Program.
    /// </summary>
    class RunOptions
    {
        /// <summary>Folder containing the .st source files.</summary>
        public string SourceFolder { get; }

        /// <summary>Folder under which "&lt;ProjectName&gt;/&lt;ProjectName&gt;.sln" is created/opened.</summary>
        public string DestinationFolder { get; }

        /// <summary>Project/solution name. Defaults to the SourceFolder's own directory name.</summary>
        public string ProjectName { get; }

        public bool BuildOnly { get; }
        public bool EventsOnly { get; }
        public bool ParseOnly { get; }

        /// <summary>Extra ignore glob patterns from repeated --ignore &lt;pattern&gt; CLI args,
        /// merged with any ".stignore" file found in SourceFolder (see Sync.IgnoreRules).</summary>
        public IReadOnlyList<string> IgnorePatterns { get; }

        // Fixed OS-install locations — not per-run configuration, so not exposed as CLI flags.
        public string TwinCatTemplate { get; } =
            @"C:\Program Files (x86)\Beckhoff\TwinCAT\3.1\Components\Base\PrjTemplate\TwinCAT Project.tsproj";
        public string StandardPlcProjectTemplate { get; } = "Standard PLC Template.plcproj";

        public string SolutionDirectory => Path.Combine(DestinationFolder, ProjectName);
        public string SolutionFilePath => Path.Combine(SolutionDirectory, ProjectName + ".sln");
        public string TsprojFilePath => Path.Combine(SolutionDirectory, ProjectName, ProjectName + ".tsproj");

        public string LibraryManifestPath => Path.Combine(SourceFolder, "libraries.xml");
        public string EventManifestPath => Path.Combine(SourceFolder, "events.xml");
        public string IoManifestPath => Path.Combine(SourceFolder, "io-devices.xml");

        public string PousTreePath => TreePath("POUs");
        public string ReferencesTreePath => TreePath("References");
        public string ProjectRootPath => string.Format("TIPC^{0}^{0} Project", ProjectName);

        string TreePath(string leaf) => string.Format("TIPC^{0}^{0} Project^{1}", ProjectName, leaf);

        RunOptions(string sourceFolder, string destinationFolder, string projectName,
            bool buildOnly, bool eventsOnly, bool parseOnly, IReadOnlyList<string> ignorePatterns)
        {
            SourceFolder = sourceFolder;
            DestinationFolder = destinationFolder;
            ProjectName = projectName;
            BuildOnly = buildOnly;
            EventsOnly = eventsOnly;
            ParseOnly = parseOnly;
            IgnorePatterns = ignorePatterns;
        }

        /// <summary>
        /// Parses command-line arguments into a RunOptions. Prints usage and exits the
        /// process if invoked with no arguments at all, or with --help/-h. Any other
        /// invocation (even just a mode flag like --build-only) resolves --source/--dest
        /// to "." if not given explicitly.
        /// </summary>
        public static RunOptions Parse(string[] args)
        {
            if (args.Length == 0 || args.Contains("--help") || args.Contains("-h"))
            {
                PrintUsage();
                Environment.Exit(args.Length == 0 ? 1 : 0);
            }

            string sourceFolder = Path.GetFullPath(GetOption(args, "--source") ?? ".");
            string destinationFolder = Path.GetFullPath(GetOption(args, "--dest") ?? ".");
            string projectName = GetOption(args, "--name") ?? new DirectoryInfo(sourceFolder).Name;

            return new RunOptions(
                sourceFolder, destinationFolder, projectName,
                buildOnly: args.Contains("--build-only"),
                eventsOnly: args.Contains("--events-only"),
                parseOnly: args.Contains("--parse-only"),
                ignorePatterns: GetOptions(args, "--ignore"));
        }

        static string GetOption(string[] args, string flag)
        {
            int i = Array.IndexOf(args, flag);
            return (i >= 0 && i + 1 < args.Length) ? args[i + 1] : null;
        }

        /// <summary>Collects every value following a repeated flag, e.g. "--ignore a --ignore b" -> [a, b].</summary>
        static List<string> GetOptions(string[] args, string flag)
        {
            var values = new List<string>();
            for (int i = 0; i < args.Length - 1; i++)
                if (args[i] == flag)
                    values.Add(args[i + 1]);
            return values;
        }

        static void PrintUsage()
        {
            Console.WriteLine("Usage: beckhoffAutomationInterface [options]");
            Console.WriteLine();
            Console.WriteLine("  --source <path>   Folder containing the .st source files (default: .)");
            Console.WriteLine("  --dest <path>     Folder under which <name>/<name>.sln is created/opened (default: .)");
            Console.WriteLine("  --name <name>     Project/solution name (default: the --source folder's own name)");
            Console.WriteLine("  --parse-only      Parse all .st files without opening Visual Studio");
            Console.WriteLine("  --build-only      Skip .st/library/IO sync; just open, build, and report");
            Console.WriteLine("  --events-only     Check events.xml against the .tsproj (declared vs actual) and stop");
            Console.WriteLine("  --ignore <glob>   Exclude .st files matching this pattern (repeatable);");
            Console.WriteLine("                    merged with a \".stignore\" file in --source, if present");
            Console.WriteLine("  --help, -h        Show this message");
        }
    }
}
