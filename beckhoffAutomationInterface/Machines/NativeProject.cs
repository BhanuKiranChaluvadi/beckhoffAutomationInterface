using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using BeckhoffAutomationInterface.Sync;
using static BeckhoffAutomationInterface.Clock;

namespace BeckhoffAutomationInterface.Machines
{
    /// <summary>
    /// The project of a native family (port of the PLC repository's Tools/build_nfpro.ps1; one
    /// build serves the release and, in its -sim copy with simulated NC axes, machine test). A fresh build:
    ///   1. bootstraps an empty project with one PLC (one TwinCAT session, like 'init' of a MAIN),
    ///   2. copies the TwinSAFE folder byte for byte next to the .tsproj,
    ///   3. applies the native configuration to the closed .tsproj (NativeConfiguration.Apply),
    /// then the normal machine session syncs the source into it and applies links.xml after the
    /// PLC compile. After XAE closes, the copied and the source safety files must be byte-equal to
    /// the source files from the start of the build.
    /// </summary>
    static class NativeProject
    {
        public sealed class FileEntry
        {
            public string Path;
            public long Bytes;
            public string Sha256;
            public override string ToString() => Path + " " + Bytes + " " + Sha256;
        }

        public static string SafetySource(MachineLayout l) => MachineSources.RepoPath(l.RepoRoot, l.Family.Native.Safety);

        public static string SafetyTarget(MachineLayout l) =>
            Path.Combine(Path.GetDirectoryName(l.Tsproj), Path.GetFileName(SafetySource(l)));

        public static void Bootstrap(MachineLayout l, string shell, string platform)
        {
            string boot = Path.Combine(l.Dir, "bootstrap");
            if (Directory.Exists(boot))
                BuildLocation.Delete(boot);
            Directory.CreateDirectory(boot);
            File.WriteAllText(Path.Combine(boot, "MAIN.st"), "PROGRAM MAIN\nVAR\nEND_VAR\nEND_PROGRAM\n");
            Console.WriteLine("{0}: Bootstrap: an empty project with PLC {1} ({2}).", Now(), l.Family.Plc, platform);
            var options = new RunOptions(
                sourceFolder: boot, destinationFolder: l.Dir, projectName: l.Machine, stages: SyncStages.All,
                init: true, checkEvents: false, parseOnly: false, ignorePatterns: new string[0], exportObjectName: null,
                exportLinks: false, formatCheck: false, confirmDeleteIo: false, reverseExports: ReverseExports.None,
                overwrite: false, plcProjectName: l.Family.Plc, existingTsprojPath: null, configFileLoaded: false,
                shell: shell, clean: false, sourceFolderExplicit: true, variant: null, allVariants: false,
                defines: new string[0], targetPlatform: platform);
            Environment.ExitCode = 0;
            PipelineOutcome outcome;
            using (var session = new TwinCatSession(options))
            {
                var pipeline = new SyncPipeline(session, options, IgnoreRules.Load(boot, new string[0]));
                outcome = pipeline.Outcome;
                pipeline.Run();
            }
            if (Environment.ExitCode != 0 || !outcome.BuildPassed)
                throw new InvalidOperationException("Bootstrap of the native project failed (see above).");

            CopySafety(l);
            Console.WriteLine("{0}: Safety: {1} file(s) copied byte for byte to {2}.", Now(), Inventory(SafetyTarget(l)).Count, SafetyTarget(l));
            string configuration = Path.Combine(l.Src, MachineSources.NativeConfigurationFile);
            Dictionary<string, object> applied = NativeConfiguration.Apply(configuration, l.Tsproj);
            Console.WriteLine("{0}: Native configuration applied to {1} (PLC {2}, configuration sha256 {3}).",
                Now(), l.Tsproj, applied["plc"], applied["configurationSha256"]);
        }

        /// <summary>Null when the NC axes of the built project are right: a simulation project has
        /// SimulationMode="1" on every axis (XAE saves it as "true"), a normal (release) project has
        /// SimulationMode on none.</summary>
        public static string AxesProblem(string tsproj, bool simulation)
        {
            XDocument project = NativeConfiguration.Load(tsproj);
            List<XElement> axes = NativeConfiguration.Axes(project);
            int simulated = axes.Count(a => (string)a.Attribute("SimulationMode") == "1" ||
                                            string.Equals((string)a.Attribute("SimulationMode"), "true", StringComparison.OrdinalIgnoreCase));
            int marked = NativeConfiguration.SimulatedAxes(project);
            if (!simulation && marked > 0)
                return string.Format("{0} of {1} NC axes have SimulationMode in a project that is not a simulation build: {2}", marked, axes.Count, tsproj);
            if (simulation && simulated != axes.Count)
                return string.Format("only {0} of {1} NC axes have SimulationMode=\"1\" in the simulation project: {2}", simulated, axes.Count, tsproj);
            Console.WriteLine("{0}: NC axes: {1}, {2}.", Now(), axes.Count, simulation ? "all simulated (SimulationMode=\"1\")" : "none simulated");
            return null;
        }

        /// <summary>The license folder of UmRT_Default (machine test activates there).</summary>
        public static string UmRtLicenseFolder() => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            @"Beckhoff\TwinCAT\3.1\Runtimes\UmRT_Default\3.1\Target\License");

        /// <summary>Null when each required license is in a .tclrs file of the folder that has not
        /// expired at 'now'; else a message that names the license, the folder and what to do.</summary>
        public static string LicenseProblem(IReadOnlyList<string> required, string folder, DateTime now)
        {
            var found = new Dictionary<string, (DateTime? Expires, string File)>(StringComparer.OrdinalIgnoreCase);
            foreach (string file in Directory.Exists(folder) ? Directory.GetFiles(folder, "*.tclrs") : new string[0])
            {
                XDocument doc;
                try { doc = XDocument.Load(file); }
                catch (System.Xml.XmlException) { continue; }
                DateTime? expires = Runtime.Doctor.LicenseExpiry(doc.ToString());
                foreach (string name in doc.Descendants().Where(e => e.Name.LocalName == "License")
                             .Select(e => (string)e.Elements().FirstOrDefault(c => c.Name.LocalName == "Name")).Where(n => n != null))
                    if (!found.TryGetValue(name, out var best) || best.Expires != null && (expires == null || expires > best.Expires))
                        found[name] = (expires, Path.GetFileName(file));
            }
            var problems = new List<string>();
            foreach (string name in required)
            {
                if (!found.TryGetValue(name, out var license))
                    problems.Add("'" + name + "' is missing");
                else if (license.Expires != null && license.Expires <= now)
                    problems.Add(string.Format("'{0}' expired {1:yyyy-MM-dd HH:mm} ({2})", name, license.Expires, license.File));
            }
            if (problems.Count == 0)
                return null;
            return string.Format("UmRT_Default has no valid license: {0}. Folder: {1}. Copy a 7-day trial license file (.tclrs) with " +
                "these licenses there unchanged (a file only in the general TwinCAT license folder is not enough).", string.Join("; ", problems), folder);
        }

        /// <summary>Makes the copy next to the .tsproj equal to the source (XAE must be closed).</summary>
        public static void CopySafety(MachineLayout l) => MachineSources.Mirror(SafetySource(l), SafetyTarget(l));

        /// <summary>Null when the copy and the source still equal the source at the start; else why not.</summary>
        public static string SafetyProblem(MachineLayout l, List<FileEntry> before)
        {
            string target = SafetyTarget(l);
            if (!Directory.Exists(target))
                return "the safety folder is missing next to the project: " + target;
            string changed = Difference(before, Inventory(target));
            if (changed != null)
                return "the copied safety files are not byte-equal to the source (" + changed + ").";
            changed = Difference(before, Inventory(SafetySource(l)));
            if (changed != null)
                return "the source safety files changed during the build (" + changed + ").";
            Console.WriteLine("{0}: Safety: {1} file(s) byte-equal to the source after XAE closed.", Now(), before.Count);
            return null;
        }

        /// <summary>Null when equal; else the first different path (pure).</summary>
        internal static string Difference(List<FileEntry> a, List<FileEntry> b)
        {
            for (int i = 0; i < Math.Max(a.Count, b.Count); i++)
            {
                if (i >= a.Count) return "added " + b[i].Path;
                if (i >= b.Count) return "missing " + a[i].Path;
                if (a[i].ToString() != b[i].ToString())
                    return a[i].Path == b[i].Path ? "changed " + a[i].Path : "added or missing " + (string.CompareOrdinal(a[i].Path, b[i].Path) < 0 ? a[i].Path : b[i].Path);
            }
            return null;
        }

        /// <summary>Each file below a folder: relative path ('/'), size, SHA-256; sorted by path.
        /// A junction or symbolic link is refused (as Get-ReleaseInventory).</summary>
        public static List<FileEntry> Inventory(string root)
        {
            root = Path.GetFullPath(root).TrimEnd('\\', '/');
            if (!Directory.Exists(root))
                throw new InvalidOperationException("Folder not found: " + root);
            var files = new List<FileEntry>();
            var pending = new Stack<string>();
            pending.Push(root);
            while (pending.Count > 0)
            {
                string path = pending.Pop();
                if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
                    throw new InvalidOperationException("Cannot check a junction or symbolic link: " + path);
                if (Directory.Exists(path))
                {
                    foreach (string child in Directory.GetFileSystemEntries(path))
                        pending.Push(child);
                    continue;
                }
                files.Add(new FileEntry
                {
                    Path = path.Substring(root.Length).TrimStart('\\').Replace('\\', '/'),
                    Bytes = new FileInfo(path).Length,
                    Sha256 = NativeConfiguration.Sha256(path),
                });
            }
            return files.OrderBy(f => f.Path, StringComparer.Ordinal).ToList();
        }
    }
}
