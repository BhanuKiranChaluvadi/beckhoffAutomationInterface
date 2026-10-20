using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Web.Script.Serialization;
using System.Xml;

namespace BeckhoffAutomationInterface.Machines
{
    /// <summary>Where one machine build lives. A simulation build has its own folder
    /// (&lt;Machine&gt;-sim), so a test run and a normal build do not replace each other's project.</summary>
    sealed class MachineLayout
    {
        public string Machine { get; }
        public MachineFamily Family { get; }
        public string RepoRoot { get; }
        public string MachineFolder { get; }
        public string FamilyWork { get; }
        public string Dir { get; }
        public string Src => Path.Combine(Dir, "src");
        public string Sln => Path.Combine(Dir, Machine, Machine + ".sln");
        public string Tsproj => Path.Combine(Dir, Machine, Machine, Machine + ".tsproj");
        /// <summary>Outside Dir: a fresh build deletes Dir while the log is open. The log of the
        /// run before is kept as &lt;name&gt;.previous.log.</summary>
        public string Log => Dir + ".log";
        public string StateFile => Path.Combine(Dir, "bai-machine.json");
        public string StartupExpect => Path.Combine(Dir, "startup.expect.json");
        public string DefaultJUnit => Path.Combine(Dir, "test-results.xml");

        public MachineLayout(MachineManifest manifest, string machine, bool simulation, string work = null)
        {
            Family = manifest.FamilyOf(machine) ?? throw new ArgumentException("unknown machine '" + machine + "'.");
            Machine = manifest.Spelling(machine);
            RepoRoot = manifest.Root;
            MachineFolder = Family.MachineFolder(RepoRoot, Machine);
            FamilyWork = Path.Combine(Path.GetFullPath(work ?? manifest.Work), Family.Name);
            Dir = Path.Combine(FamilyWork, Machine + (simulation ? "-sim" : ""));
        }
    }

    /// <summary>
    /// Puts together the source folder of one machine build (pure file work, unit-tested):
    /// the family's source folders, the shared manifests of the family folder (libraries.xml,
    /// events.xml, event-classes/, plc-data-types/), and the machine's own manifests
    /// (variants.xml, the resolved io-devices.xml, tasks.xml, safety.xml with its TwinSAFE
    /// files, simulation-gas.json). Files are copied only when their bytes differ, and files
    /// that the source no longer has are deleted (a mirror).
    /// </summary>
    static class MachineSources
    {
        public static readonly string[] SharedFiles = { "libraries.xml", "events.xml" };
        public static readonly string[] SharedFolders = { "event-classes", "plc-data-types" };
        static readonly string[] StampFiles = { "libraries.xml", "events.xml", "variants.xml", "io-devices.xml", "tasks.xml", "simulation-gas.json", "safety.xml" };

        /// <summary>Runs an external command in a folder: exit code (-1 when it cannot start).</summary>
        internal delegate int CommandRunner(IReadOnlyList<string> command, string workingFolder);

        public static void Assemble(MachineLayout l, CommandRunner run)
        {
            if (!File.Exists(Path.Combine(l.MachineFolder, "variants.xml")))
                throw new InvalidOperationException("No variants.xml in " + l.MachineFolder);
            string src = l.Src;
            Directory.CreateDirectory(src);
            foreach (string source in l.Family.Sources)
                Mirror(RepoPath(l.RepoRoot, source), RepoPath(src, source));

            string shared = RepoPath(l.RepoRoot, l.Family.Shared);
            foreach (string folder in SharedFolders)
                Mirror(Path.Combine(shared, folder), Path.Combine(src, folder));
            foreach (string file in SharedFiles)
                CopyOrDelete(Path.Combine(shared, file), Path.Combine(src, file));
            CopyIfDifferent(Path.Combine(l.MachineFolder, "variants.xml"), Path.Combine(src, "variants.xml"));

            string io = Path.Combine(l.MachineFolder, "io-devices.xml");
            if (File.Exists(io))
                WriteIfDifferent(Path.Combine(src, "io-devices.xml"), Encoding.UTF8.GetBytes(IoOverlay.ResolveText(io)));
            else
                DeleteFile(Path.Combine(src, "io-devices.xml"));
            CopyOrDelete(Path.Combine(l.MachineFolder, "tasks.xml"), Path.Combine(src, "tasks.xml"));

            WriteSimulationGasModel(l, run);

            SafetySpec safety = SafetySpec.Read(l.MachineFolder);
            if (safety != null)
            {
                Mirror(RepoPath(l.RepoRoot, safety.Source), RepoPath(src, safety.Folder));
                if (safety.TargetSystemConfig != null)
                {
                    string tsc = RepoPath(src, safety.TargetSystemConfig);
                    Directory.CreateDirectory(Path.GetDirectoryName(tsc));
                    CopyIfDifferent(RepoPath(l.RepoRoot, safety.TargetSystemConfigSource), tsc);
                }
                CopyIfDifferent(safety.File, Path.Combine(src, "safety.xml"));
            }
            else
                DeleteFile(Path.Combine(src, "safety.xml"));
        }

        /// <summary>.st files of the kept source folder that the repository no longer has
        /// (deleted or renamed): their objects would stay in a kept project.</summary>
        public static List<string> GoneStFiles(string repoRoot, string src, IEnumerable<string> sources)
        {
            var gone = new List<string>();
            foreach (string source in sources)
            {
                string kept = RepoPath(src, source);
                if (!Directory.Exists(kept))
                    continue;
                string now = RepoPath(repoRoot, source);
                foreach (string file in Directory.EnumerateFiles(kept, "*.st", SearchOption.AllDirectories))
                {
                    string rel = file.Substring(kept.Length).TrimStart('\\', '/');
                    if (!File.Exists(Path.Combine(now, rel)))
                        gone.Add(source + "/" + rel.Replace('\\', '/'));
                }
            }
            gone.Sort(StringComparer.OrdinalIgnoreCase);
            return gone;
        }

        /// <summary>One hash of every manifest of the assembled source folder (I/O tree, links,
        /// variants, libraries, events, tasks, gas model, safety project). The sync stages only add,
        /// never remove, links, I/O items and tasks: when this changes, a kept project is not
        /// reliable and the build goes fresh.</summary>
        public static string Stamp(string src)
        {
            var parts = new List<string>();
            foreach (string name in StampFiles)
                parts.Add(name + "=" + Hash(Path.Combine(src, name)));
            foreach (string folder in SharedFolders)
                parts.AddRange(HashFolder(src, Path.Combine(src, folder)));
            SafetySpec safety = SafetySpec.Read(src, requireSource: false);
            if (safety != null)
            {
                parts.AddRange(HashFolder(src, RepoPath(src, safety.Folder)));
                if (safety.TargetSystemConfig != null)
                    parts.Add("TargetSystemConfig=" + Hash(RepoPath(src, safety.TargetSystemConfig)));
            }
            parts.Sort(StringComparer.Ordinal);
            using (var sha = SHA256.Create())
                return Hex(sha.ComputeHash(Encoding.UTF8.GetBytes(string.Join(";", parts))));
        }

        // ------------------------------------------------------------------ gas model

        /// <summary>Generates the simulation gas model only where a model and its own task are
        /// both configured (same rules as Write-SimulationGasModel of Tools/build_checks.ps1).</summary>
        static void WriteSimulationGasModel(MachineLayout l, CommandRunner run)
        {
            string model = Path.Combine(l.MachineFolder, "simulation-gas.json");
            string target = Path.Combine(l.Src, "simulation-gas.json");
            SimulationGasSettings gas = l.Family.SimulationGas;
            List<XmlElement> gasTasks = GasTasks(Path.Combine(l.MachineFolder, "tasks.xml"), gas?.Program);
            bool hasModel = File.Exists(model);
            if ((!hasModel && gasTasks.Count > 0) || (hasModel && gasTasks.Count != 1))
                throw new InvalidOperationException("Gas simulation requires one resolved model and one gas task; neither can be omitted.");
            if (!hasModel)
            {
                DeleteFile(target);
                return;
            }
            if (gas == null)
                throw new InvalidOperationException(model + " exists, but the family has no \"simulationGas\" in " + MachineManifest.FileName + ".");
            if (!int.TryParse(gasTasks[0].GetAttribute("CycleTimeUs").Trim(), out int cycle) || cycle != gas.CycleTimeUs)
                throw new InvalidOperationException(string.Format("The gas simulation task must use the validated {0} µs period.", gas.CycleTimeUs));
            if (gasTasks[0].SelectNodes("Call").Count != 1)
                throw new InvalidOperationException("Gas simulation requires a dedicated task.");

            string provenancePath = RepoPath(l.RepoRoot, gas.Provenance);
            if (!File.Exists(provenancePath))
                throw new InvalidOperationException("Gas model provenance is missing: " + provenancePath);
            string relative = model.Substring(l.RepoRoot.TrimEnd('\\', '/').Length + 1).Replace('\\', '/');
            if (!ProvenanceMatches(File.ReadAllText(provenancePath), relative, File.ReadAllBytes(model)))
                throw new InvalidOperationException("Gas model does not match the retained producer provenance: " + relative);
            CopyIfDifferent(model, target);

            List<string> command = gas.Generator.Select(a => a.Replace("{model}", model).Replace("{src}", l.Src)).ToList();
            int code = run(command, l.RepoRoot);
            if (code != 0)
                throw new InvalidOperationException(string.Format("Gas model application binding failed (exit {0}): {1}", code, string.Join(" ", command)));
        }

        internal static List<XmlElement> GasTasks(string tasksFile, string program)
        {
            if (program == null || !File.Exists(tasksFile))
                return new List<XmlElement>();
            var doc = new XmlDocument { XmlResolver = null };
            doc.Load(tasksFile);
            return doc.SelectNodes("/Tasks/Task").Cast<XmlElement>()
                .Where(t => t.SelectNodes("Call").Cast<XmlNode>().Any(c => c.InnerText == program))
                .ToList();
        }

        /// <summary>schemaVersion 1 and resolvedModels[relative] = the model's SHA-256 (any case).</summary>
        internal static bool ProvenanceMatches(string provenanceJson, string relative, byte[] model)
        {
            if (!(new JavaScriptSerializer().DeserializeObject(provenanceJson) is Dictionary<string, object> p))
                return false;
            if (!(p.TryGetValue("schemaVersion", out object v) && v is int version && version == 1))
                return false;
            if (!(p.TryGetValue("resolvedModels", out object r) && r is Dictionary<string, object> models
                  && models.TryGetValue(relative, out object expected) && expected is string hash))
                return false;
            using (var sha = SHA256.Create())
                return string.Equals(Hex(sha.ComputeHash(model)), hash, StringComparison.OrdinalIgnoreCase);
        }

        // ------------------------------------------------------------------ files

        /// <summary>A repository-relative path ('/' separators) below a root.</summary>
        internal static string RepoPath(string root, string relative) =>
            Path.GetFullPath(Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar)));

        /// <summary>Makes 'to' equal to 'from': copies files whose bytes differ, deletes what
        /// 'from' does not have. No 'from' = no 'to'.</summary>
        internal static void Mirror(string from, string to)
        {
            if (!Directory.Exists(from))
            {
                if (Directory.Exists(to))
                    Directory.Delete(to, recursive: true);
                return;
            }
            Directory.CreateDirectory(to);
            foreach (string dir in Directory.GetDirectories(to))
                if (!Directory.Exists(Path.Combine(from, Path.GetFileName(dir))))
                    Directory.Delete(dir, recursive: true);
            foreach (string file in Directory.GetFiles(to))
                if (!File.Exists(Path.Combine(from, Path.GetFileName(file))))
                    DeleteFile(file);
            foreach (string file in Directory.GetFiles(from))
                CopyIfDifferent(file, Path.Combine(to, Path.GetFileName(file)));
            foreach (string dir in Directory.GetDirectories(from))
                Mirror(dir, Path.Combine(to, Path.GetFileName(dir)));
        }

        static void CopyOrDelete(string from, string to)
        {
            if (File.Exists(from))
                CopyIfDifferent(from, to);
            else
                DeleteFile(to);
        }

        internal static void CopyIfDifferent(string from, string to)
        {
            if (!File.Exists(from))
                throw new FileNotFoundException("File not found: " + from, from);
            WriteIfDifferent(to, File.ReadAllBytes(from));
        }

        static void WriteIfDifferent(string path, byte[] bytes)
        {
            if (File.Exists(path) && File.ReadAllBytes(path).SequenceEqual(bytes))
                return;
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            if (File.Exists(path))
                File.SetAttributes(path, FileAttributes.Normal);
            File.WriteAllBytes(path, bytes);
        }

        static void DeleteFile(string path)
        {
            if (!File.Exists(path))
                return;
            File.SetAttributes(path, FileAttributes.Normal);
            File.Delete(path);
        }

        static string Hash(string file)
        {
            if (!File.Exists(file))
                return "missing";
            using (var sha = SHA256.Create())
            using (FileStream s = File.OpenRead(file))
                return Hex(sha.ComputeHash(s));
        }

        static IEnumerable<string> HashFolder(string src, string folder)
        {
            if (!Directory.Exists(folder))
                return new string[0];
            return Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories)
                .Select(f => f.Substring(src.Length).TrimStart('\\', '/').Replace('\\', '/') + "=" + Hash(f));
        }

        static string Hex(byte[] bytes) => string.Concat(bytes.Select(b => b.ToString("x2")));
    }

    /// <summary>safety.xml (optional): the machine's TwinSAFE project. bai's safety stage reads
    /// Name, Folder and TargetSystemConfig (paths in the source folder). Source and
    /// TargetSystemConfigSource say where these are in the repository.</summary>
    sealed class SafetySpec
    {
        public string File;
        public string Folder;
        public string Source;
        public string TargetSystemConfig;
        public string TargetSystemConfigSource;

        public static SafetySpec Read(string folder, bool requireSource = true)
        {
            string file = Path.Combine(folder, "safety.xml");
            if (!System.IO.File.Exists(file))
                return null;
            var doc = new XmlDocument { XmlResolver = null };
            doc.Load(file);
            XmlElement s = doc.DocumentElement;
            string Attr(string name) => s.HasAttribute(name) && s.GetAttribute(name).Length > 0 ? s.GetAttribute(name) : null;
            var spec = new SafetySpec
            {
                File = file,
                Folder = Attr("Folder"),
                Source = Attr("Source"),
                TargetSystemConfig = Attr("TargetSystemConfig"),
                TargetSystemConfigSource = Attr("TargetSystemConfigSource"),
            };
            if (s.Name != "Safety" || spec.Folder == null || (requireSource && spec.Source == null))
                throw new InvalidOperationException(file + " needs <Safety Name=... Folder=... Source=...>");
            if (requireSource && spec.TargetSystemConfig != null && spec.TargetSystemConfigSource == null)
                throw new InvalidOperationException(file + " has TargetSystemConfig but no TargetSystemConfigSource");
            return spec;
        }
    }

    /// <summary>Guards for the work folder (same rules as Assert-BuildLocation of Tools/build_checks.ps1).</summary>
    static class BuildLocation
    {
        /// <summary>Null when 'work' may hold builds of 'source', else the reason (pure).</summary>
        internal static string Problem(string source, string work)
        {
            string s = Path.GetFullPath(source).TrimEnd('\\', '/');
            string w = Path.GetFullPath(work).TrimEnd('\\', '/');
            string driveRoot = Path.GetPathRoot(w).TrimEnd('\\', '/');
            if (w.Length == 0 || string.Equals(w, driveRoot, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(w, s, StringComparison.OrdinalIgnoreCase) ||
                w.StartsWith(s + "\\", StringComparison.OrdinalIgnoreCase) ||
                s.StartsWith(w + "\\", StringComparison.OrdinalIgnoreCase))
                return "the work folder must be a separate build folder outside the source tree, not a drive root or a source ancestor.";
            return null;
        }

        /// <summary>Throws when the work folder is not allowed or a path goes through a junction or symbolic link.</summary>
        public static void Assert(string source, string work)
        {
            string problem = Problem(source, work);
            if (problem != null)
                throw new InvalidOperationException(problem);
            foreach (string start in new[] { Path.GetFullPath(work), Path.GetFullPath(source) })
                for (string path = start.TrimEnd('\\', '/'); !string.IsNullOrEmpty(path); path = Path.GetDirectoryName(path))
                {
                    if ((File.Exists(path) || Directory.Exists(path)) && (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
                        throw new InvalidOperationException("Build paths must not go through a junction or symbolic link: " + path);
                }
        }

        /// <summary>Copies a failed build folder to &lt;family work&gt;\_failed\&lt;M&gt;-&lt;time&gt;-&lt;id&gt; before
        /// a fresh build deletes it. Returns the copy.</summary>
        public static string SaveFailedEvidence(string familyWork, string dir, string label)
        {
            if (Directory.EnumerateFileSystemEntries(dir, "*", SearchOption.AllDirectories)
                .Any(p => (File.GetAttributes(p) & FileAttributes.ReparsePoint) != 0))
                throw new InvalidOperationException("Cannot keep a failed build that contains junctions or symbolic links: " + dir);
            string archive = Path.Combine(familyWork, "_failed",
                label + "-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + "-" + Guid.NewGuid().ToString("N"));
            CopyTree(dir, archive);
            return archive;
        }

        static void CopyTree(string from, string to)
        {
            Directory.CreateDirectory(to);
            foreach (string file in Directory.GetFiles(from))
                File.Copy(file, Path.Combine(to, Path.GetFileName(file)));
            foreach (string dir in Directory.GetDirectories(from))
                CopyTree(dir, Path.Combine(to, Path.GetFileName(dir)));
        }

        /// <summary>Deletes a build folder (read-only files too).</summary>
        public static void Delete(string dir)
        {
            if (!Directory.Exists(dir))
                return;
            foreach (string file in Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories))
                File.SetAttributes(file, FileAttributes.Normal);
            Directory.Delete(dir, recursive: true);
        }
    }
}
