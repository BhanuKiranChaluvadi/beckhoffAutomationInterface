using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Web.Script.Serialization;

namespace BeckhoffAutomationInterface.Machines
{
    /// <summary>
    /// bai.machines.json in the root of a PLC repository: the machine families and where their
    /// parts are (pure, unit-tested). Paths are relative to the folder of the file, except
    /// "work" and "lock" (absolute, or relative to that folder).
    /// <code>
    /// { "work": "C:/bai/work", "lock": "C:/bai/lock/umrt.lock",
    ///   "preCheck": ["python", "Tools/check_interlock_sources.py"],
    ///   "buildInputs": ["Lib", "App", "Machines", "Safety", "Tools"],
    ///   "families": { "NFLite": {
    ///       "plc": "NFLite", "platform": "TwinCAT RT (x64)", "sources": ["Lib", "App/NFLite"],
    ///       "shared": "Machines/NFLite", "buildInfo": "App/NFLite/Version/GVL_BuildInfo.st",
    ///       "tests": "Tests/Integration/NFLite", "machines": ["Shark", "UVM", "SAS", "Google"],
    ///       "simulationGas": { "provenance": "Machines/simulation-models.provenance.json",
    ///                          "program": "PRG_GAS_SIMULATION", "cycleTimeUs": 100000,
    ///                          "generator": ["python", "Tools/generate_app_gas_model.py", "{model}", "App/NFLite",
    ///                                        "{src}/App/NFLite/Simulation/GVL_SimulationGasModel.st"] } } } }
    /// </code>
    /// A family has "kind": "st" (the default: .st source + manifests per machine) or "native":
    /// one machine whose I/O, NC/motion, data types and mappings are a retained native TwinCAT
    /// configuration, with its TwinSAFE folder copied byte for byte (see NativeSettings). Its one
    /// build is the full native project; machine test activates the -sim copy with simulated axes.
    /// </summary>
    sealed class MachineManifest
    {
        public const string FileName = "bai.machines.json";
        public static readonly string[] DefaultBuildInputs = { "Lib", "App", "Machines", "Safety", "Tools" };
        static readonly Regex Identifier = new Regex(@"^[A-Za-z][A-Za-z0-9_]*$");

        public string File { get; private set; }
        public string Root { get; private set; }
        public string Work { get; private set; }
        public string Lock { get; private set; }
        public IReadOnlyList<string> PreCheck { get; private set; } = new string[0];
        public IReadOnlyList<string> BuildInputs { get; private set; } = DefaultBuildInputs;
        public IReadOnlyList<MachineFamily> Families { get; private set; } = new MachineFamily[0];
        /// <summary>"release": the defaults of 'release build|lock'. Null when the file has none.</summary>
        public ReleaseSettings Release { get; private set; }

        /// <summary>The family of a machine (name compared without case); null when unknown.</summary>
        public MachineFamily FamilyOf(string machine) =>
            Families.FirstOrDefault(f => f.Machines.Contains(machine, StringComparer.OrdinalIgnoreCase));

        /// <summary>The machine name in the manifest's spelling.</summary>
        public string Spelling(string machine) =>
            FamilyOf(machine)?.Machines.First(m => m.Equals(machine, StringComparison.OrdinalIgnoreCase));

        /// <summary>Every machine of every family, in file order.</summary>
        public IEnumerable<string> AllMachines => Families.SelectMany(f => f.Machines);

        /// <summary>--manifest, else bai.machines.json in the current folder or a parent folder.</summary>
        public static string Find(string explicitPath, string cwd)
        {
            if (explicitPath != null)
            {
                string full = Path.GetFullPath(explicitPath);
                if (Directory.Exists(full))
                    full = Path.Combine(full, FileName);
                if (!System.IO.File.Exists(full))
                    throw new ArgumentException("machine manifest not found: " + full);
                return full;
            }
            for (string dir = Path.GetFullPath(cwd); dir != null; dir = Path.GetDirectoryName(dir))
            {
                string candidate = Path.Combine(dir, FileName);
                if (System.IO.File.Exists(candidate))
                    return candidate;
            }
            throw new ArgumentException(FileName + " not found in " + cwd + " or a parent folder (use --manifest <file>).");
        }

        public static MachineManifest Load(string path) => Parse(System.IO.File.ReadAllText(path), path);

        internal static MachineManifest Parse(string json, string path)
        {
            object parsed;
            try { parsed = new JavaScriptSerializer().DeserializeObject(json); }
            catch (ArgumentException ex) { throw new FormatException(path + ": not valid JSON: " + ex.Message); }
            if (!(parsed is Dictionary<string, object> root))
                throw new FormatException(path + ": the root must be an object.");
            Known(root, path, "work", "lock", "preCheck", "buildInputs", "families", "release");

            string folder = Path.GetDirectoryName(Path.GetFullPath(path));
            var m = new MachineManifest
            {
                File = Path.GetFullPath(path),
                Root = folder,
                Work = Full(folder, Text(root, "work", path, required: true)),
                Lock = Full(folder, Text(root, "lock", path, required: true)),
            };
            if (root.ContainsKey("preCheck"))
                m.PreCheck = List(root, "preCheck", path);
            if (root.ContainsKey("buildInputs"))
                m.BuildInputs = List(root, "buildInputs", path);
            if (root.ContainsKey("release"))
                m.Release = ReleaseSettings.Parse(root["release"], path);

            if (!(root.TryGetValue("families", out object fams) && fams is Dictionary<string, object> families) || families.Count == 0)
                throw new FormatException(path + ": \"families\" must be an object with at least one family.");
            var list = new List<MachineFamily>();
            foreach (KeyValuePair<string, object> f in families)
                list.Add(MachineFamily.Parse(f.Key, f.Value, folder, path));
            m.Families = list;

            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (string machine in m.AllMachines)
                if (!seen.Add(machine))
                    throw new FormatException(string.Format("{0}: machine '{1}' is listed more than once.", path, machine));
            return m;
        }

        internal static void Known(Dictionary<string, object> obj, string path, params string[] keys)
        {
            string unknown = obj.Keys.FirstOrDefault(k => !keys.Contains(k));
            if (unknown != null)
                throw new FormatException(string.Format("{0}: unknown key '{1}'. Known keys: {2}.", path, unknown, string.Join(", ", keys)));
        }

        internal static string Text(Dictionary<string, object> obj, string key, string path, bool required)
        {
            if (!obj.TryGetValue(key, out object v) || v == null)
            {
                if (required)
                    throw new FormatException(string.Format("{0}: \"{1}\" is required.", path, key));
                return null;
            }
            if (!(v is string s) || s.Trim().Length == 0)
                throw new FormatException(string.Format("{0}: \"{1}\" must be a text.", path, key));
            return s;
        }

        internal static List<string> List(Dictionary<string, object> obj, string key, string path)
        {
            if (!obj.TryGetValue(key, out object v) || !(v is IEnumerable items) || v is string)
                throw new FormatException(string.Format("{0}: \"{1}\" must be a list of texts.", path, key));
            var list = new List<string>();
            foreach (object item in items)
            {
                if (!(item is string s) || s.Length == 0)
                    throw new FormatException(string.Format("{0}: \"{1}\" must be a list of texts.", path, key));
                list.Add(s);
            }
            return list;
        }

        internal static string Full(string folder, string relative) =>
            relative == null ? null : Path.GetFullPath(Path.Combine(folder, relative));

        internal static bool IsIdentifier(string name) => name != null && Identifier.IsMatch(name);
    }

    /// <summary>
    /// "release" in bai.machines.json: what 'release build' uses when its options do not give it.
    /// The tag of a release then records the TwinCAT major and the approved bai that built it.
    /// <code>
    /// "release": { "twincat": "3.1.4026", "bai": { "version": "1.9.0", "sha256": "&lt;64 hex digits&gt;" } }
    /// </code>
    /// </summary>
    sealed class ReleaseSettings
    {
        static readonly Regex Sha = new Regex(@"\A[0-9a-fA-F]{64}\z");
        static readonly Regex Major = new Regex(@"\A\d+\.\d+\.\d+(\.\d+)?\z");

        /// <summary>The TwinCAT major (3.1.4026).</summary>
        public string TwinCat { get; private set; }
        public string BaiVersion { get; private set; }
        public string BaiSha256 { get; private set; }
        /// <summary>The TwinCAT packages a build PC needs (bai toolchain check|install); null when not given.</summary>
        public IReadOnlyList<TwinCatPackage> Packages { get; private set; }

        internal static ReleaseSettings Parse(object value, string path)
        {
            string where = path + ": \"release\"";
            if (!(value is Dictionary<string, object> r))
                throw new FormatException(where + " must be an object.");
            MachineManifest.Known(r, where, "twincat", "bai", "packages");
            var settings = new ReleaseSettings { TwinCat = MachineManifest.Text(r, "twincat", where, required: false) };
            if (settings.TwinCat != null && !Major.IsMatch(settings.TwinCat))
                throw new FormatException(where + ": \"twincat\" must look like 3.1.4026.");
            if (r.TryGetValue("bai", out object b))
            {
                if (!(b is Dictionary<string, object> bai))
                    throw new FormatException(where + ": \"bai\" must be an object.");
                MachineManifest.Known(bai, where + ".bai", "version", "sha256");
                settings.BaiVersion = MachineManifest.Text(bai, "version", where + ".bai", required: true);
                settings.BaiSha256 = MachineManifest.Text(bai, "sha256", where + ".bai", required: true);
                if (!Sha.IsMatch(settings.BaiSha256))
                    throw new FormatException(where + ".bai: \"sha256\" must be a SHA256 (64 hex digits).");
            }
            if (r.TryGetValue("packages", out object list))
            {
                if (!(list is IEnumerable items) || list is string || list is Dictionary<string, object>)
                    throw new FormatException(where + ": \"packages\" must be a list.");
                var packages = new List<TwinCatPackage>();
                foreach (object item in items)
                {
                    string at = where + ".packages[" + packages.Count + "]";
                    if (!(item is Dictionary<string, object> p)) throw new FormatException(at + " must be an object.");
                    MachineManifest.Known(p, at, "name", "version", "why", "alias", "lib");
                    packages.Add(new TwinCatPackage
                    {
                        Name = MachineManifest.Text(p, "name", at, required: true), Version = MachineManifest.Text(p, "version", at, required: true),
                        Why = MachineManifest.Text(p, "why", at, required: false) ?? "", Alias = MachineManifest.Text(p, "alias", at, required: false),
                        Lib = MachineManifest.Text(p, "lib", at, required: false),
                    });
                }
                settings.Packages = packages;
            }
            return settings;
        }
    }

    /// <summary>One TwinCAT package (TcPkg) of "release" "packages": its exact version, why it is needed,
    /// an older name of the same package (alias), and the library package that also counts (lib).</summary>
    sealed class TwinCatPackage
    {
        public string Name, Version, Why, Alias, Lib;
    }

    sealed class MachineFamily
    {
        public string Name { get; private set; }
        public string Kind { get; private set; }
        public string Plc { get; private set; }
        public string Platform { get; private set; }
        /// <summary>Source folders, relative to the repository root ('/' separators). Each is
        /// copied to the same relative path in the build source folder.</summary>
        public IReadOnlyList<string> Sources { get; private set; }
        /// <summary>The family folder: shared manifests and one folder per machine.</summary>
        public string Shared { get; private set; }
        /// <summary>GVL_BuildInfo.st, relative to the repository root (and to the build source folder).</summary>
        public string BuildInfo { get; private set; }
        public string Tests { get; private set; }
        public IReadOnlyList<string> Machines { get; private set; }
        public SimulationGasSettings SimulationGas { get; private set; }
        /// <summary>Only for "kind": "native".</summary>
        public NativeSettings Native { get; private set; }
        public bool IsNative => Kind == "native";

        public static readonly string[] Platforms = { "TwinCAT RT (x86)", "TwinCAT RT (x64)", "TwinCAT OS (x64)" };

        internal static MachineFamily Parse(string name, object value, string root, string path)
        {
            string where = path + ": family '" + name + "'";
            if (!MachineManifest.IsIdentifier(name))
                throw new FormatException(where + ": the name must be letters, digits and '_'.");
            if (!(value is Dictionary<string, object> f))
                throw new FormatException(where + " must be an object.");
            MachineManifest.Known(f, where, "kind", "plc", "platform", "sources", "shared", "buildInfo", "tests", "machines", "simulationGas", "native");

            var family = new MachineFamily
            {
                Name = name,
                Kind = MachineManifest.Text(f, "kind", where, required: false) ?? "st",
            };
            if (family.Kind != "st" && family.Kind != "native")
                throw new FormatException(where + ": unknown kind '" + family.Kind + "' (use 'st' or 'native').");

            family.Plc = MachineManifest.Text(f, "plc", where, required: true);
            if (RunOptions.PlcProjectNameProblem(family.Plc) != null)
                throw new FormatException(where + ": \"plc\" is not a valid PLC project name.");
            family.Platform = MachineManifest.Text(f, "platform", where, required: false) ?? "TwinCAT RT (x64)";
            if (!Platforms.Contains(family.Platform))
                throw new FormatException(where + ": \"platform\" must be one of: " + string.Join(", ", Platforms) + ".");
            family.Sources = MachineManifest.List(f, "sources", where).Select(s => Relative(s, where)).ToList();
            family.Shared = Relative(MachineManifest.Text(f, "shared", where, required: true), where);
            family.BuildInfo = Relative(MachineManifest.Text(f, "buildInfo", where, required: true), where);
            if (!family.Sources.Any(s => family.BuildInfo.StartsWith(s + "/", StringComparison.OrdinalIgnoreCase)))
                throw new FormatException(where + ": \"buildInfo\" must be inside one of the \"sources\".");
            string tests = MachineManifest.Text(f, "tests", where, required: false);
            family.Tests = tests == null ? null : Relative(tests, where);
            family.Machines = MachineManifest.List(f, "machines", where);
            string bad = family.Machines.FirstOrDefault(m => !MachineManifest.IsIdentifier(m));
            if (bad != null)
                throw new FormatException(where + ": machine name '" + bad + "' must be letters, digits and '_', starting with a letter.");
            if (f.TryGetValue("simulationGas", out object gas))
                family.SimulationGas = SimulationGasSettings.Parse(gas, where);
            bool hasNative = f.TryGetValue("native", out object native);
            if (family.IsNative != hasNative)
                throw new FormatException(where + (family.IsNative ? ": kind 'native' needs \"native\"." : ": \"native\" is only for kind 'native'."));
            if (family.IsNative)
            {
                family.Native = NativeSettings.Parse(native, where);
                if (family.Machines.Count != 1)
                    throw new FormatException(where + ": a 'native' family has exactly one machine (one native configuration).");
            }
            return family;
        }

        /// <summary>A relative path below the repository root, with '/' separators.</summary>
        internal static string Relative(string value, string where)
        {
            string p = value.Replace('\\', '/').Trim('/');
            if (Path.IsPathRooted(value) || p.Split('/').Any(part => part == ".." || part == "." || part.Length == 0))
                throw new FormatException(string.Format("{0}: '{1}' must be a relative path below the repository root.", where, value));
            return p;
        }

        /// <summary>The folder of one machine (absolute). A native family has one machine: the family folder.</summary>
        public string MachineFolder(string repoRoot, string machine) =>
            IsNative ? Path.Combine(repoRoot, Shared.Replace('/', '\\')) : Path.Combine(repoRoot, Shared.Replace('/', '\\'), machine);
    }

    /// <summary>
    /// "native": the parts of a native family (paths relative to the repository root).
    ///   configuration   - the retained configuration.xml (bai native capture). Every build applies
    ///                     it to a bootstrapped project and writes its mappings as links.xml. The
    ///                     simulation copy (-sim, machine test) also gets SimulationMode="1" on each
    ///                     NC axis; a normal build must not have it.
    ///   safety          - the TwinSAFE folder, copied byte for byte next to the .tsproj (the
    ///                     configuration refers to it) and checked again after XAE closes.
    ///   runtimeLicenses - optional: the license names UmRT_Default needs before machine test
    ///                     activates the project (for example "TC3 NC PTP", "TC3 NC PTP Axis").
    /// </summary>
    sealed class NativeSettings
    {
        public string Configuration { get; private set; }
        public string Safety { get; private set; }
        public IReadOnlyList<string> RuntimeLicenses { get; private set; } = new string[0];

        internal static NativeSettings Parse(object value, string where)
        {
            where += " native";
            if (!(value is Dictionary<string, object> n))
                throw new FormatException(where + " must be an object.");
            MachineManifest.Known(n, where, "configuration", "safety", "runtimeLicenses");
            var s = new NativeSettings
            {
                Configuration = MachineFamily.Relative(MachineManifest.Text(n, "configuration", where, required: true), where),
                Safety = MachineFamily.Relative(MachineManifest.Text(n, "safety", where, required: true), where),
            };
            if (n.ContainsKey("runtimeLicenses"))
                s.RuntimeLicenses = MachineManifest.List(n, "runtimeLicenses", where);
            return s;
        }
    }

    /// <summary>The generated simulation gas model (optional per machine: Machines/&lt;family&gt;/&lt;M&gt;/simulation-gas.json).</summary>
    sealed class SimulationGasSettings
    {
        public string Provenance { get; private set; }
        public string Program { get; private set; }
        public int CycleTimeUs { get; private set; }
        /// <summary>Command and arguments, run in the repository root. {model} = the machine's
        /// model file, {src} = the build source folder.</summary>
        public IReadOnlyList<string> Generator { get; private set; }

        internal static SimulationGasSettings Parse(object value, string where)
        {
            where += " simulationGas";
            if (!(value is Dictionary<string, object> g))
                throw new FormatException(where + " must be an object.");
            MachineManifest.Known(g, where, "provenance", "program", "cycleTimeUs", "generator");
            var s = new SimulationGasSettings
            {
                Provenance = MachineFamily.Relative(MachineManifest.Text(g, "provenance", where, required: true), where),
                Program = MachineManifest.Text(g, "program", where, required: true),
                Generator = MachineManifest.List(g, "generator", where),
            };
            if (!(g.TryGetValue("cycleTimeUs", out object c) && c is int cycle && cycle > 0))
                throw new FormatException(where + ": \"cycleTimeUs\" must be a whole number above 0.");
            s.CycleTimeUs = cycle;
            return s;
        }
    }
}
