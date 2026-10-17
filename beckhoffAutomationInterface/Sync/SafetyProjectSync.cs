using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Xml.Linq;
using Interop.TCatSysManager;

namespace BeckhoffAutomationInterface.Sync
{
    /// <summary>One file of the safety project copy next to the .tsproj.</summary>
    sealed class SafetyFileCopy
    {
        public string RelativePath { get; }
        public string SourcePath { get; }
        public string DestinationPath { get; }
        public string SourceHash { get; }

        public SafetyFileCopy(string relativePath, string sourcePath, string destinationPath)
        {
            RelativePath = relativePath;
            SourcePath = sourcePath;
            DestinationPath = destinationPath;
            SourceHash = SafetyProjectSync.Hash(sourcePath);
        }

        public bool DestinationEqual => File.Exists(DestinationPath) && SafetyProjectSync.Hash(DestinationPath) == SourceHash;
    }

    /// <summary>The Standard In/Out channels the safety logic terminal must show after the
    /// project is added. Standard alias devices "In" (PLC -> safety) are under StandardOutputs
    /// as "Standard In Var &lt;SDSID&gt;"; "Out" (safety -> PLC) under StandardInputs as
    /// "Standard Out Var &lt;SDSID&gt;" (seen on Shark, spike S3.1).</summary>
    sealed class SafetyChannels
    {
        public const string InputsGroup = "StandardInputs";
        public const string OutputsGroup = "StandardOutputs";

        public List<string> StandardInputs { get; } = new List<string>();
        public List<string> StandardOutputs { get; } = new List<string>();
        public bool NeedsInputs { get; set; }
        public bool NeedsOutputs { get; set; }
    }

    /// <summary>What the safety stage must do for one machine (all read from files, no TwinCAT).</summary>
    sealed class SafetyPlan
    {
        public SafetyManifest Manifest { get; set; }
        public string ProjectFolder { get; set; }   // <.tsproj folder>\<Name>
        public string SplcprojPath => Path.Combine(ProjectFolder, Manifest.SplcprojFileName);
        public List<SafetyObjectId> ObjectIds { get; set; }
        public SafetyObjectId LogicTerminal => ObjectIds[0]; // from TargetSystemConfig.xml
        public List<SafetyFileCopy> Files { get; set; }
        public SafetyChannels Channels { get; set; }
        public ObjectIdEditResult IdChanges { get; set; } // dry run on the .tsproj as it is now

        public List<SafetyFileCopy> FilesToCopy => Files.Where(f => !f.DestinationEqual).ToList();

        /// <summary>True when the .tsproj or the copy must change: the shell must be closed.</summary>
        public bool NeedsFileWork => IdChanges.Changed || FilesToCopy.Count > 0;
    }

    /// <summary>
    /// Adds a TwinSAFE project (safety.xml, see SafetyManifest) to the project. Method from spike
    /// S3.1 (BeckhoffLib tasks/2026-10-16-plc-release):
    ///   1. shell closed: give the boxes/modules the ids the safety files refer to
    ///      (TsprojObjectIdEditor), copy the safety folder unchanged to &lt;.tsproj folder&gt;\&lt;Name&gt;
    ///      and check that each copied file is byte-equal to its source;
    ///   2. shell open: TISC.CreateChild(Name, 0, "", &lt;.splcproj&gt;) when TISC^Name does not exist
    ///      (the project is used in place);
    ///   3. check that the logic terminal now has its Standard In/Out channels. With wrong ids
    ///      TwinCAT adds the project with no error and no channels, so this is the only signal.
    /// The safety files are never changed: a changed TargetSystemConfig.xml makes XAE show a
    /// modal "Target system CRC checksum verification failed" dialog that SuppressUI does not stop.
    /// </summary>
    static class SafetyProjectSync
    {
        /// <summary>Reads everything from the files (the .tsproj only to see which ids would change).</summary>
        public static SafetyPlan Plan(SafetyManifest manifest, string tsprojPath)
        {
            string projectFolder = Path.Combine(Path.GetDirectoryName(tsprojPath), manifest.Name);
            List<SafetyFileCopy> files = manifest.SourceFiles()
                .Select(f => new SafetyFileCopy(f.Key, f.Value, Path.Combine(projectFolder, f.Key))).ToList();
            List<string> aliasDevices = files.Where(f => f.RelativePath.EndsWith(".sds", StringComparison.OrdinalIgnoreCase))
                .Select(f => f.SourcePath).ToList();
            List<SafetyObjectId> ids = SafetyObjectIds.Read(manifest.EffectiveTargetSystemConfig, aliasDevices);
            return new SafetyPlan
            {
                Manifest = manifest,
                ProjectFolder = projectFolder,
                ObjectIds = ids,
                Files = files,
                Channels = ExpectedChannels(manifest.EffectiveTargetSystemConfig, aliasDevices),
                IdChanges = TsprojObjectIdEditor.Apply(tsprojPath, ids, dryRun: true),
            };
        }

        /// <summary>Copies the files that are missing or different. Files that TwinCAT adds to
        /// the folder (GVLs, User FBs, bin, .tmc) stay. Returns the copied relative paths.</summary>
        public static List<string> Copy(SafetyPlan plan)
        {
            var copied = new List<string>();
            foreach (SafetyFileCopy file in plan.FilesToCopy)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(file.DestinationPath));
                if (File.Exists(file.DestinationPath))
                    File.SetAttributes(file.DestinationPath, FileAttributes.Normal);
                File.Copy(file.SourcePath, file.DestinationPath, overwrite: true);
                copied.Add(file.RelativePath);
            }
            return copied;
        }

        /// <summary>The copied files that are not byte-equal to their source (empty = all equal).</summary>
        public static List<string> Differences(SafetyPlan plan) =>
            plan.Files.Where(f => !f.DestinationEqual).Select(f => f.RelativePath).ToList();

        public static string Hash(string path)
        {
            using (var sha = SHA256.Create())
            using (FileStream stream = File.OpenRead(path))
                return BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", "");
        }

        /// <summary>From the standard alias devices (one BIT "In" or "Out" each). Exact channel
        /// names only when TwinCAT does not take the alias names into the process image.</summary>
        internal static SafetyChannels ExpectedChannels(string targetSystemConfigPath, IEnumerable<string> aliasDeviceFiles)
        {
            var channels = new SafetyChannels();
            bool aliasNames = string.Equals(((string)XDocument.Load(targetSystemConfigPath).Root
                ?.Element("TakeOverStandardAliasDeviceNamesInProcessImage"))?.Trim(), "true", StringComparison.OrdinalIgnoreCase);
            foreach (string file in aliasDeviceFiles)
            {
                XElement root = XDocument.Load(file).Root;
                XElement standard = root?.Element("StandardAliasDevice");
                if (standard == null)
                    continue;
                List<XElement> ios = standard.Elements("IO").ToList();
                string direction = ios.Count == 1 ? ((string)ios[0].Element("Name"))?.Trim() : null;
                string sdsId = ((string)root.Element("SDSID"))?.Trim();
                bool exact = !aliasNames && sdsId != null && ios.Count == 1 && (string)ios[0].Element("DataType") == "BIT";
                if (direction == "In")
                {
                    channels.NeedsOutputs = true;
                    if (exact)
                        channels.StandardOutputs.Add("Standard In Var " + sdsId);
                }
                else if (direction == "Out")
                {
                    channels.NeedsInputs = true;
                    if (exact)
                        channels.StandardInputs.Add("Standard Out Var " + sdsId);
                }
            }
            return channels;
        }

        /// <summary>Checks ProduceXml of the logic terminal: its object id, and the Standard
        /// In/Out channels. found: one line per channel group. Returns the problems.</summary>
        internal static List<string> CheckChannels(string boxXml, SafetyObjectId logicTerminal, SafetyChannels expected, out List<string> found)
        {
            var problems = new List<string>();
            found = new List<string>();
            XElement root = XDocument.Parse(boxXml).Root;

            int wantedObjectId = (SafetyObjectIds.BoxObjectType << 16) | logicTerminal.Id;
            string objectId = (string)root?.Element("ObjectId");
            string wantedText = "#x" + wantedObjectId.ToString("x8");
            if (!string.Equals(objectId, wantedText, StringComparison.OrdinalIgnoreCase))
                problems.Add(string.Format("'{0}' has object id {1}, the safety project expects {2}", logicTerminal.BoxName, objectId ?? "(none)", wantedText));

            Dictionary<string, List<string>> groups = root == null ? new Dictionary<string, List<string>>() : root.Descendants()
                .Where(e => e.Name.LocalName == "TxPdo" || e.Name.LocalName == "RxPdo")
                .Where(p => (string)p.Element("Name") == SafetyChannels.InputsGroup || (string)p.Element("Name") == SafetyChannels.OutputsGroup)
                .GroupBy(p => (string)p.Element("Name"))
                .ToDictionary(g => g.Key, g => g.SelectMany(p => p.Elements("Entry")).Select(e => (string)e.Element("Name")).Where(n => n != null).ToList());

            CheckGroup(groups, SafetyChannels.InputsGroup, expected.NeedsInputs, expected.StandardInputs, logicTerminal.BoxName, problems, found);
            CheckGroup(groups, SafetyChannels.OutputsGroup, expected.NeedsOutputs, expected.StandardOutputs, logicTerminal.BoxName, problems, found);
            return problems;
        }

        static void CheckGroup(Dictionary<string, List<string>> groups, string group, bool needed, List<string> names, string boxName,
            List<string> problems, List<string> found)
        {
            if (!groups.TryGetValue(group, out List<string> present))
            {
                if (needed)
                    problems.Add(string.Format("'{0}' has no {1} channels", boxName, group));
                return;
            }
            found.Add(string.Format("{0}: {1}", group, present.Count == 0 ? "(no channel)" : string.Join(", ", present)));
            foreach (string name in names.Where(n => !present.Contains(n)))
                problems.Add(string.Format("'{0}' {1} has no channel '{2}'", boxName, group, name));
        }

        // ------------------------------------------------------------ TwinCAT (shell open)

        /// <summary>Adds the project under TISC when TISC^name does not exist. True = added.</summary>
        public static bool AddProject(ITcSysManager sysManager, string name, string splcprojPath, out List<string> otherProjects)
        {
            ITcSmTreeItem tisc = sysManager.LookupTreeItem("TISC");
            otherProjects = new List<string>();
            for (int i = 1; i <= tisc.ChildCount; i++)
            {
                string child = tisc.get_Child(i).Name;
                if (child != name)
                    otherProjects.Add(child);
            }
            TreeItemFactory.GetOrCreate(sysManager, tisc, name, 0, splcprojPath, out bool isNew);
            return isNew;
        }

        /// <summary>ProduceXml of the box with this name in the I/O tree (devices and boxes only).
        /// Null when there is no such box.</summary>
        public static string ProduceBoxXml(ITcSysManager sysManager, string boxName)
        {
            ITcSmTreeItem box = FindBox(sysManager.LookupTreeItem("TIID"), boxName);
            return box?.ProduceXml(true);
        }

        const int TreeItemTypeDevice = 2;
        const int TreeItemTypeBox = 5;

        static ITcSmTreeItem FindBox(ITcSmTreeItem parent, string boxName)
        {
            for (int i = 1; i <= parent.ChildCount; i++)
            {
                ITcSmTreeItem child = parent.get_Child(i);
                int type = child.ItemType;
                if (type != TreeItemTypeDevice && type != TreeItemTypeBox)
                    continue;
                if (type == TreeItemTypeBox && child.Name == boxName)
                    return child;
                ITcSmTreeItem inner = FindBox(child, boxName);
                if (inner != null)
                    return inner;
            }
            return null;
        }
    }
}
