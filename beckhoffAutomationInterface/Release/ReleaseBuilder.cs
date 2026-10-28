using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml;
using BeckhoffAutomationInterface.Machines;
using static BeckhoffAutomationInterface.Clock;
using static BeckhoffAutomationInterface.Release.ArtifactFiles;

namespace BeckhoffAutomationInterface.Release
{
    internal sealed class ReleaseRequest
    {
        public List<string> Machines = new List<string>();
        public string Tag, Bundle, Bai, BaiSha256, TwinCatVersion, LibraryLock, LibraryLockSha256, Toolchain, ToolchainSha256;
        public string Platform, Manifest, Cwd, Shell;
        public string EsiDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), @"Beckhoff\TwinCAT\3.1\Config\Io\EtherCAT");
        public string UserEsiDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), @"Beckhoff\TwinCAT\3.1\Config\IO\EtherCAT");
        public bool WorkingTree;
    }

    /// <summary>The compile step of a release: build the machines of the snapshot (with its
    /// bai.machines.json) into work, with the private BAI copy. Returns the exit code.</summary>
    internal delegate int ReleaseCompiler(string bai, string manifest, string work, IReadOnlyList<string> machines, string platform, string version, bool dirty);

    /// <summary>
    /// 'release build': a machine release in a new, isolated bundle folder, with build evidence.
    /// Source: --tag (the tag must name HEAD), HEAD (version = git describe; HEAD must be pushed),
    /// or --include-uncommitted (HEAD plus uncommitted and untracked files of the build inputs: a
    /// DevelopmentValidation package for a UmRT rehearsal, never for a machine). Without
    /// --include-uncommitted the repository must be clean. Never activates a runtime. The bundle has
    /// source.zip, a detached checkout, a private BAI copy, the generated projects and logs, the
    /// installed ESI files, release.json and verified packages (compiled boot files and a
    /// separate service solution archive). Resolved libraries must match the hashed lock, and
    /// the TwinCAT/XAE archive must match the installed files before and after the build.
    /// It does not prove hardware readiness or prepare a machine rollback backup.
    /// </summary>
    internal static class ReleaseBuilder
    {
        static readonly Regex Sha = new Regex(@"\A[0-9a-fA-F]{64}\z");
        static readonly string[] Limitations =
        {
            "Resolved libraries must match the supplied lock; install those dependencies before building.",
            "The TwinCAT major is checked, not the build; the build of each project is recorded. With --toolchain, the Components and XAE bytes are also archived and checked.",
            "Windows/.NET, package installation, registration and licenses must be provisioned separately.",
            "Machine backup, rollback rehearsal, online identity and hardware acceptance remain required.",
        };

        // ------------------------------------------------------------------ git

        internal static string GitText(string root, params string[] args)
        {
            var psi = new ProcessStartInfo("git", "-C " + BuildInfo.Quote(root) + " " + string.Join(" ", args.Select(BuildInfo.Quote)))
            {
                UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true,
                StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8,
            };
            var error = new StringBuilder();
            using (Process p = Process.Start(psi))
            {
                p.ErrorDataReceived += (s, e) => { if (e.Data != null) lock (error) error.AppendLine(e.Data); };
                p.BeginErrorReadLine();
                string output = p.StandardOutput.ReadToEnd();
                p.WaitForExit();
                if (p.ExitCode != 0)
                    throw new InvalidOperationException("git " + string.Join(" ", args) + " failed: " + error.ToString().Trim());
                return output;
            }
        }

        internal static List<string> Git(string root, params string[] args) =>
            GitText(root, args).Split('\n').Select(l => l.TrimEnd('\r')).Where(l => l.Length > 0).ToList();

        // ------------------------------------------------------------------ evidence

        static string PlatformOf(MachineManifest manifest, IEnumerable<string> machines, string requested)
        {
            var platforms = machines.Select(m => requested ?? manifest.FamilyOf(m).Platform).Distinct().ToList();
            if (platforms.Count != 1)
                throw new ArgumentException("Build these machines separately: their target platforms differ (" + string.Join(", ", platforms) + "). Or give --platform.");
            if (!MachineFamily.Platforms.Contains(platforms[0])) throw new ArgumentException("--platform must be one of: " + string.Join(", ", MachineFamily.Platforms) + ".");
            return platforms[0];
        }

        /// <summary>The machines of "M1,M2" or a family name (all its machines).</summary>
        internal static List<string> Machines(MachineManifest manifest, IEnumerable<string> requested)
        {
            var machines = requested.SelectMany(m => m.Split(',')).Select(m => m.Trim()).Where(m => m.Length > 0)
                .SelectMany(m => manifest.Families.FirstOrDefault(f => string.Equals(f.Name, m, StringComparison.OrdinalIgnoreCase))?.Machines ?? (IEnumerable<string>)new[] { m }).ToList();
            if (machines.Count == 0 || machines.Distinct(StringComparer.OrdinalIgnoreCase).Count() != machines.Count) throw new ArgumentException("Select distinct machines.");
            foreach (string m in machines)
                if (manifest.FamilyOf(m) == null)
                    throw new ArgumentException("Unknown machine or family '" + m + "'. " + MachineManifest.FileName + " lists: "
                        + string.Join("; ", manifest.Families.Select(f => f.Name + " (" + string.Join(", ", f.Machines) + ")")) + ".");
            return machines.Select(manifest.Spelling).ToList();
        }

        /// <summary>The bundle name of the machines: the family name when they are all its machines.</summary>
        static string Label(MachineManifest manifest, List<string> machines)
        {
            var family = manifest.FamilyOf(machines[0]);
            return family.Machines.Count > 1 && machines.Count == family.Machines.Count && machines.All(m => manifest.FamilyOf(m) == family)
                ? family.Name : string.Join("+", machines);
        }

        /// <summary>A family kept as a native project has one application and no TwinCAT variant.</summary>
        static string Variant(MachineLayout l) => l.Family.Kind == "native" ? "" : l.Machine;

        /// <summary>The generated project of one machine: TwinCAT version, compiled platform and
        /// variant, no SIMULATION define, the resolved library cache (with its dependencies),
        /// every generated file and the build log.</summary>
        internal static Dictionary<string, object> ProjectEvidence(MachineLayout l, string twinCatVersion, string platform)
        {
            string machine = l.Machine, variant = Variant(l);
            string version = (string)Xml(l.Tsproj).Root.Attribute("TcVersion");
            if (!SameTwinCatMajor(version, twinCatVersion))
                throw new InvalidDataException(machine + " TwinCAT version '" + version + "' does not match required major '" + (TwinCatMajor(twinCatVersion) ?? twinCatVersion) + "'.");
            string tmc = MachineChecks.CompiledTmc(l.Tsproj), plcProject = Path.ChangeExtension(tmc, ".plcproj");
            var modules = Xml(tmc).Descendants().Where(e => e.Name.LocalName == "Module" && e.Attribute("TargetPlatform") != null).ToList();
            if (modules.Count != 1 || (string)modules[0].Attribute("TargetPlatform") != platform || ((string)modules[0].Attribute("ProjectVariant") ?? "") != variant)
                throw new InvalidDataException(machine + " compiled TMC does not identify variant '" + variant + "' on " + platform + ".");
            string libraries = Path.Combine(Path.GetDirectoryName(tmc), "_Libraries");
            if (!Directory.Exists(libraries) || Inventory(libraries).Count == 0) throw new InvalidDataException(machine + " has no retained compiled libraries.");
            var cache = LibraryBaseline.Capture(l.Tsproj, machine, platform, variant, Path.Combine(l.Src, "libraries.xml"));
            var defines = Xml(plcProject).Descendants().Where(e => e.Name.LocalName == "CompilerDefines").Select(e => e.Value).ToArray();
            if (Regex.IsMatch(string.Join(";", defines), @"(?i)(^|[^A-Z0-9_])SIMULATION([^A-Z0-9_]|$)"))
                throw new InvalidDataException(machine + " generated project contains SIMULATION.");
            return new Dictionary<string, object>
            {
                ["machine"] = machine, ["twinCatVersion"] = version, ["targetPlatform"] = platform, ["projectVariant"] = variant,
                ["compilerDefines"] = defines,
                ["libraryCache"] = Array(cache, "libraries").Select(Object).Select(f => new Dictionary<string, object>
                    { ["path"] = f["path"], ["bytes"] = f["bytes"], ["sha256"] = ((string)f["sha256"]).ToUpperInvariant() }).ToList(),
                ["artifacts"] = Evidence(Path.Combine(l.Dir, machine)),
                ["buildLogSha256"] = Hash(l.Log).ToUpperInvariant(),
            };
        }

        static void VerifyLibraries(Dictionary<string, object> project, Dictionary<string, object> baseline)
        {
            var actual = new Dictionary<string, object> { ["machine"] = project["machine"], ["twinCatVersion"] = project["twinCatVersion"],
                ["targetPlatform"] = project["targetPlatform"], ["libraries"] = ((List<Dictionary<string, object>>)project["libraryCache"]).Cast<object>().ToArray() };
            try { LibraryBaseline.Verify(baseline, actual); }
            catch (InvalidDataException ex) { throw new InvalidDataException(project["machine"] + " resolved libraries differ from the locked baseline. " + ex.Message); }
        }

        // ------------------------------------------------------------------ package

        static void Zip(string folder, string destination)
        {
            using (var stream = new FileStream(destination, FileMode.CreateNew))
            using (var zip = new ZipArchive(stream, ZipArchiveMode.Create))
                foreach (var file in Inventory(folder).OrderBy(p => p.Key, StringComparer.Ordinal))
                    zip.CreateEntryFromFile(Path.Combine(folder, file.Key), file.Key);
        }

        static void CopyTree(string from, string to)
        {
            foreach (var file in Inventory(from))
            {
                string target = Path.Combine(to, file.Key);
                Directory.CreateDirectory(Path.GetDirectoryName(target));
                File.Copy(Path.Combine(from, file.Key), target);
            }
        }

        /// <summary>The verified machine package (packages/&lt;M&gt;.zip and its .sha256): compiled boot
        /// files with the TMC, project.zip (the generated solution for service), evidence.json,
        /// identity.expect.json (the compiled build identity). Schema 2; ReleaseCandidate only from
        /// clean source.</summary>
        internal static Dictionary<string, object> Package(MachineLayout l, Dictionary<string, object> project, Dictionary<string, object> record, string outputDirectory)
        {
            string machine = l.Machine, platform = Text(project, "targetPlatform"), variant = Variant(l);
            if (!MachineManifest.IsIdentifier(machine)) throw new InvalidDataException("Invalid package machine.");
            string solution = Path.Combine(l.Dir, machine);
            BuildLocation.Assert(solution, outputDirectory);
            if (Regex.IsMatch(platform, @"[/\\:]|\.\.")) throw new InvalidDataException("Invalid target platform path.");
            if (project.TryGetValue("projectVariant", out object v) && v != null && (string)v != variant) throw new InvalidDataException("Project variant does not match the selected machine layout.");
            string boot = Path.Combine(Path.GetDirectoryName(l.Tsproj), "_Boot", variant, platform);
            var artifacts = Array(project, "artifacts");
            SameFiles(artifacts, Evidence(solution), "Generated project");
            foreach (string name in new[] { "CurrentConfig.xml", "CurrentProjectInfo.json", @"Plc\Port_851.app", @"Plc\Port_851_boot.tizip", @"Plc\Port_851.crc", @"Plc\Port_851.autostart" })
                if (!File.Exists(Path.Combine(boot, name))) throw new InvalidDataException("Missing compiled boot payload: " + name);
            var info = Object(Field(Read(Path.Combine(boot, "CurrentProjectInfo.json")), "project"));
            var config = Xml(Path.Combine(boot, "CurrentConfig.xml")).Root;
            string Info(string key) => info.TryGetValue(key, out object value) ? value as string : null;
            if (!Names.Equals(Info("name"), machine) || !Names.Equals(Info("platform"), platform) ||
                config.Name.LocalName != "TcBootProject" || !Names.Equals((string)config.Attribute("Platform"), platform))
                throw new InvalidDataException("Compiled boot payload does not match the selected machine/platform.");
            string tmc = MachineChecks.CompiledTmc(l.Tsproj), tmcText = File.ReadAllText(tmc);
            var tmcXml = new XmlDocument { XmlResolver = null }; tmcXml.LoadXml(tmcText);
            var active = tmcXml.SelectSingleNode("//*[local-name()='DataType'][*[local-name()='Name']='GVL_Simulation']/*[local-name()='SubItem'][*[local-name()='Name']='bActive']/*[local-name()='Default']/*[local-name()='Bool']");
            if (active == null || !new[] { "0", "false", "FALSE" }.Contains(active.InnerText)) throw new InvalidDataException("Compiled TMC must explicitly initialize GVL_Simulation.bActive to FALSE.");
            var identity = MachineChecks.CompiledBuildExpectations(tmcText, machine);
            object Compiled(string field) => identity.First(c => (string)c["symbol"] == "GVL_BuildInfo." + field)["equals"];
            if (!(record["sourceDirty"] is bool dirty) || (string)Compiled("sGitCommit") != (string)record["sourceCommit"] ||
                string.IsNullOrWhiteSpace(record["sourceTag"] as string) || (string)Compiled("sGitTag") != (string)record["sourceTag"] || (bool)Compiled("bDirty") != dirty)
                throw new InvalidDataException("Compiled build identity differs from the release record.");
            string kind = dirty ? "DevelopmentValidation" : "ReleaseCandidate";
            string target = Path.Combine(outputDirectory, machine + ".zip");
            if (File.Exists(target)) throw new IOException("Package already exists: " + target);
            Directory.CreateDirectory(outputDirectory);
            string root = Path.GetFullPath(outputDirectory).TrimEnd('\\');
            string stage = Path.Combine(root, "_stage_" + Guid.NewGuid().ToString("N")), metadata = stage + ".metadata.json";
            try
            {
                CopyTree(boot, Path.Combine(stage, "boot"));
                File.Copy(tmc, Path.Combine(stage, @"boot\Plc\Port_851.tmc"), true);
                Zip(solution, Path.Combine(stage, "project.zip"));
                File.WriteAllText(Path.Combine(stage, "evidence.json"), Encode(record), new UTF8Encoding(false));
                File.WriteAllText(Path.Combine(stage, "identity.expect.json"),
                    Encode(new Dictionary<string, object> { ["variants"] = new Dictionary<string, object> { [machine] = identity } }), new UTF8Encoding(false));
                var safety = Directory.GetFiles(solution, "TargetSystemConfig.xml", SearchOption.AllDirectories).OrderBy(p => p, StringComparer.Ordinal).Select(file =>
                {
                    var tsc = Xml(file).Root;
                    string Value(string name) => tsc.Name.LocalName != "TargetSystemConfig" ? "" : (string)tsc.Attribute(name) ?? (string)tsc.Element(name) ?? "";
                    return new Dictionary<string, object> { ["file"] = file.Substring(solution.Length + 1).Replace('\\', '/'), ["projectCrc"] = Value("ProjectCRC"), ["serialNumber"] = Value("SerialNumber") };
                }).ToList();
                var manifest = new Dictionary<string, object>
                {
                    ["schemaVersion"] = 2, ["status"] = "Packaged", ["kind"] = kind, ["deploymentApproved"] = false,
                    ["machine"] = machine, ["targetPlatform"] = platform, ["sourceCommit"] = record["sourceCommit"], ["sourceTag"] = record["sourceTag"],
                    ["createdUtc"] = DateTime.UtcNow.ToString("o"),
                    ["plcCrcFileBase64"] = Convert.ToBase64String(File.ReadAllBytes(Path.Combine(boot, @"Plc\Port_851.crc"))),
                    ["safety"] = safety,
                };
                File.WriteAllText(metadata, Encode(manifest), new UTF8Encoding(false));
                SameFiles(artifacts, Evidence(solution), "Generated project during packaging");
                string digest = Text(ArtifactArchives.CreatePackage(stage, metadata, target), "sha256");
                File.WriteAllText(target + ".sha256", digest + "  " + machine + ".zip\n", Encoding.ASCII);
                return new Dictionary<string, object> { ["machine"] = machine, ["file"] = "packages/" + machine + ".zip", ["sha256"] = digest, ["kind"] = kind };
            }
            finally
            {
                if (File.Exists(metadata)) File.Delete(metadata);
                if (Directory.Exists(stage))
                    try { BuildLocation.Delete(stage); }
                    catch (IOException ex) { Console.WriteLine("WARNING: Could not remove package staging folder {0}: {1}", stage, ex.Message); }
            }
        }

        // ------------------------------------------------------------------ build

        static void CheckToolchain(ReleaseRequest q, string archive)
        {
            try { ArtifactArchives.VerifyToolchain(archive, q.ToolchainSha256, q.TwinCatVersion, installed: true); }
            catch (Exception ex) when (ex is InvalidDataException || ex is IOException)
            { throw new InvalidDataException("Installed toolchain or its archive differs from the selected baseline. " + ex.Message); }
        }

        static List<Dictionary<string, object>> SourceInventory(string snapshot, IEnumerable<string> areas) =>
            areas.SelectMany(a => Evidence(Path.Combine(snapshot, a), a + "/")).ToList();

        /// <summary>Runs the whole release. Throws on a refusal (nothing made when it is found
        /// before the bundle exists) or a failure (release.json then has status Failed and the reason).</summary>
        internal static Dictionary<string, object> Build(ReleaseRequest q, ReleaseCompiler compile)
        {
            if (q.WorkingTree && q.Tag != null) throw new ArgumentException("Use --tag or --include-uncommitted, not both: a tag names a commit; --include-uncommitted also builds uncommitted files.");
            // The toolchain archive is optional: it holds the bytes of one exact TwinCAT build.
            bool archived = q.Toolchain != null || q.ToolchainSha256 != null;
            if (archived && (q.Toolchain == null || q.ToolchainSha256 == null)) throw new ArgumentException("Use --toolchain and --toolchain-sha256 together, or neither.");
            var hashes = new List<(string, string)> { ("--bai-sha256", q.BaiSha256), ("--library-lock-sha256", q.LibraryLockSha256) };
            if (archived) hashes.Add(("--toolchain-sha256", q.ToolchainSha256));
            foreach (var (name, value) in hashes)
                if (!Sha.IsMatch(value ?? "")) throw new ArgumentException(name + " must be a SHA256 (64 hex digits).");
            if (TwinCatMajor(q.TwinCatVersion) == null) throw new ArgumentException("--twincat-version must look like 3.1.4026 (a build such as 3.1.4026.24 also works; only the major is compared).");
            var manifest = MachineManifest.Load(MachineManifest.Find(q.Manifest, q.Cwd ?? Directory.GetCurrentDirectory()));
            string repo = manifest.Root;
            string[] areas = manifest.BuildInputs.ToArray();
            string commit = Git(repo, "rev-parse", "--verify", "HEAD")[0], version;
            if (q.Tag != null)
            {
                if (Git(repo, "rev-parse", "--verify", "refs/tags/" + q.Tag + "^{commit}")[0] != commit) throw new InvalidOperationException("The requested tag must identify HEAD.");
                version = q.Tag;
            }
            else version = Git(repo, "describe", "--tags", "--always")[0]; // the text that GVL_BuildInfo.sGitTag gets
            var changes = Git(repo, "-c", "core.quotePath=false", "status", "--porcelain", "--untracked-files=all");
            var inputChanges = changes.Where(c => { string p = c.Substring(3).Trim('"'); return areas.Any(a => p.StartsWith(a + "/", StringComparison.OrdinalIgnoreCase)); }).ToList();
            if (!q.WorkingTree)
            {
                if (changes.Count > 0) throw new InvalidOperationException("Release requires a clean repository, including tests, documentation and untracked files. Commit them, or use --include-uncommitted for a development package.");
                if (q.Tag == null && Git(repo, "branch", "-r", "--contains", commit).Count == 0)
                    throw new InvalidOperationException("HEAD is not on the server. Push it first (git push), so that others can rebuild this package; or use --tag, or --include-uncommitted for a development package.");
            }
            bool dirty = q.WorkingTree && inputChanges.Count > 0;
            var machines = Machines(manifest, q.Machines);
            string platform = PlatformOf(manifest, machines, q.Platform);
            string bundle = Path.GetFullPath(q.Bundle ?? Path.Combine(manifest.Work, "release",
                string.Format("{0}-{1}{2}-{3:yyyyMMdd-HHmmss}", Label(manifest, machines), version, dirty ? "-dirty" : "", DateTime.Now)));
            string bai = Path.GetFullPath(q.Bai), toolRoot = Path.GetDirectoryName(bai);
            foreach (string source in new[] { repo, toolRoot, q.EsiDirectory, q.UserEsiDirectory }) BuildLocation.Assert(source, bundle);
            if (Directory.Exists(bundle) || File.Exists(bundle)) throw new IOException("Bundle already exists; use a new folder: " + bundle);
            foreach (string m in machines.Where(m => manifest.FamilyOf(m).Kind == "st"))
                MachineChecks.AssertReleaseManifests(manifest.FamilyOf(m).MachineFolder(repo, m));
            string toolchain = archived ? Path.GetFullPath(q.Toolchain) : null;
            if (!Names.Equals(Hash(bai), q.BaiSha256)) throw new InvalidDataException("BAI executable does not match the approved SHA256.");
            if (archived) CheckToolchain(q, toolchain);
            var toolBefore = Evidence(toolRoot);
            var esiRoots = new List<(string Key, string Path)> { ("system", q.EsiDirectory) };
            if (Directory.Exists(q.UserEsiDirectory)) esiRoots.Add(("user", q.UserEsiDirectory));
            List<Dictionary<string, object>> EsiInventory() => esiRoots.SelectMany(r => Evidence(r.Path, r.Key + "/")).ToList();
            var esiBefore = EsiInventory();
            if (!esiBefore.Any(f => ((string)f["path"]).EndsWith(".xml", StringComparison.OrdinalIgnoreCase))) throw new InvalidDataException("No installed ESI XML files found.");
            var libraryLock = LibraryBaseline.ReadLock(q.LibraryLock, q.LibraryLockSha256);
            foreach (string m in machines)
            {
                var entries = Array(libraryLock, "machines").Select(Object).Where(e => (string)e["machine"] == m).ToList();
                if (entries.Count != 1 || !SameTwinCatMajor((string)entries[0]["twinCatVersion"], q.TwinCatVersion) || (string)entries[0]["targetPlatform"] != platform)
                    throw new InvalidDataException("Library baseline does not cover the requested machine/toolchain/platform.");
            }

            Directory.CreateDirectory(bundle);
            var record = new Dictionary<string, object>
            {
                ["schemaVersion"] = 1, ["status"] = "Building", ["startedUtc"] = DateTime.UtcNow.ToString("o"),
                ["sourceCommit"] = commit, ["sourceTag"] = version, ["sourceTagged"] = q.Tag != null,
                ["sourceMode"] = q.WorkingTree ? "WorkingTree" : q.Tag != null ? "Tag" : "Head", ["sourceDirty"] = dirty,
                ["baiSha256"] = q.BaiSha256.ToUpperInvariant(), ["expectedTwinCatVersion"] = TwinCatMajor(q.TwinCatVersion),
                ["toolchainArchiveSha256"] = q.ToolchainSha256?.ToUpperInvariant(), ["expectedTargetPlatform"] = platform,
                ["libraryLockSha256"] = q.LibraryLockSha256.ToUpperInvariant(), ["baiFileVersion"] = FileVersionInfo.GetVersionInfo(bai).FileVersion,
                ["machines"] = machines.ToArray(), ["deploymentApproved"] = false, ["limitations"] = Limitations,
            };
            string recordPath = Path.Combine(bundle, "release.json");
            void Save() => File.WriteAllText(recordPath, Encode(record), new UTF8Encoding(false));
            Save();
            try
            {
                if (archived)
                {
                    File.Copy(toolchain, Path.Combine(bundle, "toolchain.zip"));
                    if (!Names.Equals(Hash(Path.Combine(bundle, "toolchain.zip")), q.ToolchainSha256)) throw new InvalidDataException("Copied toolchain archive hash mismatch.");
                }
                File.Copy(q.LibraryLock, Path.Combine(bundle, "libraries.lock.json"));
                LibraryBaseline.ReadLock(Path.Combine(bundle, "libraries.lock.json"), q.LibraryLockSha256);
                string snapshot = Path.Combine(bundle, "source");
                // --no-local: no hard links to mutable local objects. Check out the captured commit,
                // not a branch or tag that could move while the clone is made.
                GitText(repo, "clone", "--no-local", "--no-checkout", "--quiet", "--", repo, snapshot);
                GitText(snapshot, "checkout", "--quiet", "--detach", commit);
                if (q.WorkingTree) OverlayWorkingTree(repo, snapshot, bundle, areas, inputChanges);
                if (q.Tag == null)
                {
                    // The build writes the describe text of the snapshot. Its abbreviated hash can be
                    // shorter than in the repository (fewer objects), so the snapshot text is the version.
                    version = Git(snapshot, "describe", "--tags", "--always")[0];
                    record["sourceTag"] = version;
                }
                GitText(snapshot, "archive", "--format=zip", "--output=" + Path.Combine(bundle, "source.zip"), commit);
                record["sourceArchiveSha256"] = Hash(Path.Combine(bundle, "source.zip")).ToUpperInvariant();
                var sourceBefore = SourceInventory(snapshot, areas);
                string privateTool = Path.Combine(bundle, "tool");
                CopyTree(toolRoot, privateTool);
                SameFiles(toolBefore, Evidence(privateTool), "BAI copy");
                string privateBai = Path.Combine(privateTool, Path.GetFileName(bai));
                if (!Names.Equals(Hash(privateBai), q.BaiSha256)) throw new InvalidDataException("Copied BAI hash mismatch.");
                record["sourceInputs"] = sourceBefore;
                record["baiFiles"] = toolBefore;
                record["installedEsiFiles"] = esiBefore;
                string esiArchive = Path.Combine(bundle, "esi");
                foreach (var r in esiRoots) CopyTree(r.Path, Path.Combine(esiArchive, r.Key));
                SameFiles(esiBefore, Evidence(esiArchive), "Retained ESI files");
                Save();

                string work = Path.Combine(bundle, "build"), snapshotManifest = Path.Combine(snapshot, MachineManifest.FileName);
                if (!File.Exists(snapshotManifest)) throw new InvalidDataException("The source commit has no " + MachineManifest.FileName + ".");
                int code = compile(privateBai, snapshotManifest, work, machines, platform, version, dirty);
                if (code != 0) throw new InvalidOperationException("Release build failed (exit " + code + "). See the build logs in " + work + ".");
                SameFiles(sourceBefore, SourceInventory(snapshot, areas), "Source snapshot");
                SameFiles(toolBefore, Evidence(privateTool), "Private BAI files");
                SameFiles(esiBefore, EsiInventory(), "Installed ESI inventory");
                if (archived) CheckToolchain(q, Path.Combine(bundle, "toolchain.zip"));
                var built = MachineManifest.Load(snapshotManifest);
                var layouts = machines.Select(m => new MachineLayout(built, m, simulation: false, work: work)).ToList();
                var projects = layouts.Select(l => ProjectEvidence(l, q.TwinCatVersion, platform)).ToList();
                record["projects"] = projects;
                libraryLock = LibraryBaseline.ReadLock(Path.Combine(bundle, "libraries.lock.json"), q.LibraryLockSha256);
                foreach (var project in projects) VerifyLibraries(project, libraryLock);
                record["status"] = "BuildPassed";
                record["packages"] = layouts.Select((l, i) => Package(l, projects[i], record, Path.Combine(bundle, "packages"))).ToList();
                record["completedUtc"] = DateTime.UtcNow.ToString("o");
                Save();
                Console.WriteLine("{0}: {1} built; evidence: {2}. Deployment checks remain open.", Now(),
                    dirty ? "Development package (uncommitted source; UmRT rehearsal only)" : "Release candidate " + version, recordPath);
                return record;
            }
            catch (Exception ex)
            {
                record["status"] = "Failed";
                record["error"] = ex.Message;
                record["completedUtc"] = DateTime.UtcNow.ToString("o");
                Save();
                throw;
            }
        }

        /// <summary>
        /// 'release build': fills in what the options do not give, then builds one bundle for each
        /// family. From bai.machines.json "release": the TwinCAT major and the approved bai hash.
        /// The library lock is &lt;family folder&gt;\libraries.lock.json of the source: git keeps it,
        /// so its hash is taken from the file. No machines: all machines. A --tag that is not HEAD,
        /// or a repository with changes, is built from a clean checkout of the tag (removed at the
        /// end); the tag must be on the server. The working tree is never touched.
        /// </summary>
        internal static List<Dictionary<string, object>> BuildAll(ReleaseRequest q, ReleaseCompiler compile)
        {
            if (q.LibraryLock != null && q.LibraryLockSha256 == null)
                throw new ArgumentException("--library-lock needs --library-lock-sha256: a lock from outside the source has no git history. Without --library-lock, bai takes the lock of the source.");
            string cwd = q.Cwd ?? Directory.GetCurrentDirectory();
            var manifest = MachineManifest.Load(MachineManifest.Find(q.Manifest, cwd));
            string checkout = null;
            try
            {
                if (q.Tag != null && !q.WorkingTree && NeedsCheckout(manifest.Root, q.Tag))
                {
                    string top = Git(manifest.Root, "rev-parse", "--show-toplevel")[0].Replace('/', '\\');
                    string relative = manifest.File.Substring(Path.GetFullPath(top).TrimEnd('\\').Length + 1);
                    checkout = CheckOut(top, q.Tag, Path.Combine(manifest.Work, "checkout"));
                    manifest = MachineManifest.Load(Path.Combine(checkout, relative));
                    q.Cwd = Path.GetDirectoryName(manifest.File);
                    q.Manifest = manifest.File;
                }
                string from = Path.GetFileName(manifest.File) + " \"release\"";
                q.TwinCatVersion = q.TwinCatVersion ?? manifest.Release?.TwinCat
                    ?? throw new ArgumentException("Give --twincat-version, or set \"twincat\" in " + from + ".");
                if (q.BaiSha256 == null)
                {
                    q.BaiSha256 = manifest.Release?.BaiSha256 ?? throw new ArgumentException("Give --bai-sha256, or set \"bai\" in " + from + ".");
                    if (!Names.Equals(Hash(q.Bai), q.BaiSha256))
                        throw new InvalidDataException("This bai does not match the approved SHA256 of " + from + ": bai " + manifest.Release.BaiVersion + ", "
                            + q.BaiSha256 + ". Use that bai, or approve this one in a reviewed commit.");
                }
                var machines = Machines(manifest, q.Machines.Count > 0 ? q.Machines : manifest.AllMachines);
                var families = machines.GroupBy(m => manifest.FamilyOf(m)).ToList();
                if (families.Count > 1 && q.Bundle != null) throw new ArgumentException("--output takes one family; these machines are of " + families.Count + " families.");
                var records = new List<Dictionary<string, object>>();
                foreach (var family in families)
                {
                    string familyLock = q.LibraryLock ?? MachineSources.RepoPath(manifest.Root, family.Key.Shared + "/libraries.lock.json");
                    if (!File.Exists(familyLock)) throw new FileNotFoundException("No library lock: " + familyLock + ". Make one with 'bai release lock', or give --library-lock.");
                    var one = new ReleaseRequest
                    {
                        Machines = family.ToList(), Tag = q.Tag, WorkingTree = q.WorkingTree, Bundle = q.Bundle, Bai = q.Bai, BaiSha256 = q.BaiSha256, TwinCatVersion = q.TwinCatVersion,
                        LibraryLock = familyLock, LibraryLockSha256 = q.LibraryLockSha256 ?? Hash(familyLock), Toolchain = q.Toolchain, ToolchainSha256 = q.ToolchainSha256,
                        Platform = q.Platform, Manifest = q.Manifest, Cwd = q.Cwd, Shell = q.Shell, EsiDirectory = q.EsiDirectory, UserEsiDirectory = q.UserEsiDirectory,
                    };
                    Console.WriteLine("{0}: release build {1}: {2}", Now(), family.Key.Name, string.Join(", ", family));
                    records.Add(Build(one, compile));
                }
                return records;
            }
            finally
            {
                if (checkout != null)
                    try { BuildLocation.Delete(checkout); }
                    catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException) { Console.WriteLine("WARNING: could not remove the checkout {0}: {1}", checkout, ex.Message); }
            }
        }

        /// <summary>True when the tag is not HEAD or the repository has changes.</summary>
        static bool NeedsCheckout(string root, string tag) =>
            Git(root, "rev-parse", "--verify", "refs/tags/" + tag + "^{commit}")[0] != Git(root, "rev-parse", "--verify", "HEAD")[0]
            || Git(root, "status", "--porcelain", "--untracked-files=all").Count > 0;

        /// <summary>A clean checkout of the tag in a new folder below 'parent'. The tag must be on the
        /// server, so that others can build the same release.</summary>
        static string CheckOut(string repo, string tag, string parent)
        {
            string commit = Git(repo, "rev-parse", "--verify", "refs/tags/" + tag + "^{commit}")[0];
            if (Git(repo, "branch", "-r", "--contains", commit).Count == 0)
                throw new InvalidOperationException("The commit of tag " + tag + " is not on the server. Push it first (git push origin " + tag + ").");
            string folder = Path.Combine(parent, Regex.Replace(tag, @"[^A-Za-z0-9._-]", "_") + "-" + DateTime.Now.ToString("yyyyMMdd-HHmmss"));
            Console.WriteLine("{0}: Clean checkout of {1} in {2}", Now(), tag, folder);
            Directory.CreateDirectory(parent);
            GitText(repo, "clone", "--no-local", "--no-checkout", "--quiet", "--", repo, folder);
            GitText(folder, "checkout", "--quiet", "--detach", commit);
            return folder;
        }

        /// <summary>HEAD plus the current source: every tracked and untracked (not ignored) file of the
        /// build inputs, and what the working tree deleted. The difference is kept for review.</summary>
        static void OverlayWorkingTree(string repo, string snapshot, string bundle, string[] areas, List<string> inputChanges)
        {
            var current = Git(repo, new[] { "-c", "core.quotePath=false", "ls-files", "-co", "--exclude-standard", "--" }.Concat(areas).ToArray())
                .Where(p => File.Exists(Path.Combine(repo, p))).ToList();
            var keep = new HashSet<string>(current, StringComparer.Ordinal);
            foreach (string rel in Git(snapshot, new[] { "-c", "core.quotePath=false", "ls-files", "--" }.Concat(areas).ToArray()))
                if (!keep.Contains(rel)) File.Delete(Path.Combine(snapshot, rel));
            foreach (string rel in current)
            {
                string target = Path.Combine(snapshot, rel);
                Directory.CreateDirectory(Path.GetDirectoryName(target));
                File.Copy(Path.Combine(repo, rel), target, true);
            }
            File.WriteAllLines(Path.Combine(bundle, "working-tree-changes.txt"), inputChanges);
            File.WriteAllText(Path.Combine(bundle, "working-tree.patch"), GitText(repo, new[] { "diff", "HEAD", "--" }.Concat(areas).ToArray()));
        }

        /// <summary>The real compile step: the private BAI copy runs 'release compile'.</summary>
        internal static ReleaseCompiler Spawn(string shell) => (bai, manifest, work, machines, platform, version, dirty) =>
        {
            var argv = new List<string> { "release", "compile", "--manifest", manifest, "--work", work, "--platform", platform, "--version", version, "--machines", string.Join(",", machines) };
            if (dirty) argv.Add("--dirty");
            if (shell != null) argv.AddRange(new[] { "--shell", shell });
            Console.WriteLine("{0}: > {1} {2}", Now(), bai, string.Join(" ", argv));
            using (Process p = Process.Start(new ProcessStartInfo(bai, string.Join(" ", argv.Select(BuildInfo.Quote))) { UseShellExecute = false }))
            {
                p.WaitForExit();
                return p.ExitCode;
            }
        };

        /// <summary>'release compile' (run by the private BAI copy): fresh builds of the machines,
        /// without SIMULATION. A clean build must show the version and bDirty FALSE.</summary>
        internal static int Compile(string manifestFile, string work, IReadOnlyList<string> machines, string platform, string version, bool dirty, string shell)
        {
            var manifest = MachineManifest.Load(manifestFile);
            MachineBuilder.ReleaseTag = version;
            bool passed = true;
            foreach (string m in machines)
            {
                var l = new MachineLayout(manifest, m, simulation: false, work: work);
                var request = new MachineBuildRequest { Layout = l, Fresh = true, Release = !dirty, Simulation = false, Platform = platform, Shell = shell, BuildInputs = manifest.BuildInputs };
                MachineRow row;
                using (TeeWriter.Capture(l.Log))
                {
                    try { row = MachineBuilder.Build(request); }
                    catch (Exception ex) when (ex is InvalidOperationException || ex is IOException || ex is UnauthorizedAccessException
                                               || ex is FormatException || ex is ArgumentException || ex is XmlException)
                    {
                        Console.WriteLine("{0}: ERROR: {1}", Now(), ex.Message);
                        row = new MachineRow(l.Machine) { Build = "FAIL (sources)" };
                    }
                }
                Console.WriteLine("{0}: {1}: parse {2}, build {3}, links {4}", Now(), row.Machine, row.Parse, row.Build, row.Links);
                passed &= row.BuildPassed;
            }
            return passed ? 0 : 1;
        }

        /// <summary>'release lock': a candidate library lock from built machines. Review it once, then
        /// keep its SHA256 for later release builds (never regenerate it from each candidate).</summary>
        internal static Dictionary<string, object> Lock(string manifestFile, string work, IEnumerable<string> requested, string twinCatVersion, string platform, string output)
        {
            if (File.Exists(output) || Directory.Exists(output)) throw new IOException("Use a new output path; preserve the previous library baseline.");
            var manifest = MachineManifest.Load(manifestFile);
            var machines = Machines(manifest, requested);
            platform = PlatformOf(manifest, machines, platform);
            var entries = machines.Select(m =>
            {
                var project = ProjectEvidence(new MachineLayout(manifest, m, simulation: false, work: work), twinCatVersion, platform);
                return new Dictionary<string, object> { ["machine"] = m, ["twinCatVersion"] = project["twinCatVersion"], ["targetPlatform"] = platform, ["libraries"] = project["libraryCache"] };
            }).ToList();
            WriteNew(output, new Dictionary<string, object> { ["schemaVersion"] = 1, ["machines"] = entries });
            string hash = Hash(output).ToUpperInvariant();
            LibraryBaseline.ReadLock(output, hash);
            return new Dictionary<string, object> { ["status"] = "Captured", ["file"] = Path.GetFullPath(output), ["sha256"] = hash };
        }
    }
}
