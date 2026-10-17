using System;
using System.Collections.Generic;
using System.IO;
using System.Xml;
using System.Xml.Linq;

namespace BeckhoffAutomationInterface.Sync
{
    /// <summary>
    /// The TwinSAFE project of a machine. Source of truth: an optional "safety.xml" manifest
    /// next to the other manifests:
    /// <code>
    /// &lt;Safety Name="SHARK_Safety_prj" Folder="safety/SHARK_Safety_prj" /&gt;
    /// </code>
    /// Folder (relative to the source folder) holds the project as the TwinCAT safety editor
    /// saved it: &lt;Name&gt;.splcproj, TargetSystemConfig.xml, the groups and their alias
    /// devices. The tool copies it unchanged next to the .tsproj (see SafetyProjectSync).
    /// Optional TargetSystemConfig="&lt;file&gt;" (relative to the source folder) replaces the
    /// project's TargetSystemConfig.xml in the copy: one safety logic, one file per terminal
    /// (serial number). Other attributes are ignored (a build script may keep its own data).
    /// </summary>
    sealed class SafetyManifest
    {
        public const string TargetSystemConfigFileName = "TargetSystemConfig.xml";

        /// <summary>The safety project name: tree item TISC^&lt;Name&gt;, folder &lt;Name&gt; next to the .tsproj.</summary>
        public string Name { get; }

        /// <summary>Full path of the safety project folder in the source folder.</summary>
        public string Folder { get; }

        /// <summary>Full path of the replacement TargetSystemConfig.xml, or null.</summary>
        public string TargetSystemConfig { get; }

        public SafetyManifest(string name, string folder, string targetSystemConfig)
        {
            Name = name;
            Folder = folder;
            TargetSystemConfig = targetSystemConfig;
        }

        public string SplcprojFileName => Name + ".splcproj";

        /// <summary>The TargetSystemConfig.xml the copy gets: the replacement, else the project's own.</summary>
        public string EffectiveTargetSystemConfig => TargetSystemConfig ?? Path.Combine(Folder, TargetSystemConfigFileName);

        /// <summary>Every file of the copy: path relative to the project folder -> source file.
        /// TargetSystemConfig.xml comes from the replacement when one is given.</summary>
        public SortedDictionary<string, string> SourceFiles()
        {
            var files = new SortedDictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (string file in Directory.EnumerateFiles(Folder, "*", SearchOption.AllDirectories))
                files[file.Substring(Folder.Length).TrimStart('\\', '/')] = file;
            if (TargetSystemConfig != null)
                files[TargetSystemConfigFileName] = TargetSystemConfig;
            return files;
        }
    }

    sealed class SafetyManifestException : Exception
    {
        public SafetyManifestException(string path, IReadOnlyList<string> problems)
            : base(string.Format("{0} is invalid:{1}  {2}", path, Environment.NewLine, string.Join(Environment.NewLine + "  ", problems)))
        {
        }
    }

    static class SafetyManifestParser
    {
        /// <summary>Null when the file does not exist (the project then has no safety project,
        /// as before). Throws SafetyManifestException listing every problem.</summary>
        public static SafetyManifest Parse(string manifestFilePath, string sourceFolder)
        {
            if (!File.Exists(manifestFilePath))
                return null;
            return Parse(XDocument.Load(manifestFilePath, LoadOptions.SetLineInfo), manifestFilePath, sourceFolder);
        }

        internal static SafetyManifest Parse(XDocument doc, string pathForMessages, string sourceFolder)
        {
            XElement root = doc.Root;
            if (root == null || root.Name.LocalName != "Safety")
                throw new SafetyManifestException(pathForMessages, new[] { "root element must be <Safety>" });

            var problems = new List<string>();
            string name = ((string)root.Attribute("Name") ?? "").Trim();
            string folder = ((string)root.Attribute("Folder") ?? "").Trim();
            string targetSystemConfig = ((string)root.Attribute("TargetSystemConfig"))?.Trim();

            if (name.Length == 0)
                problems.Add(string.Format("line {0}: Name is missing (the safety project name, e.g. Name=\"SHARK_Safety_prj\")", Line(root)));
            else if (name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || name.Contains("^"))
                problems.Add(string.Format("line {0}: Name '{1}' is not a valid folder name", Line(root), name));

            string folderPath = null;
            if (folder.Length == 0)
                problems.Add(string.Format("line {0}: Folder is missing (the safety project folder, relative to the source folder)", Line(root)));
            else
            {
                folderPath = Resolve(sourceFolder, folder);
                if (!Directory.Exists(folderPath))
                    problems.Add(string.Format("line {0}: Folder '{1}' does not exist ({2})", Line(root), folder, folderPath));
                else if (name.Length > 0)
                {
                    if (!File.Exists(Path.Combine(folderPath, name + ".splcproj")))
                        problems.Add(string.Format("line {0}: Folder '{1}' has no {2}.splcproj (the file name must be the Name)", Line(root), folder, name));
                    if (targetSystemConfig == null && !File.Exists(Path.Combine(folderPath, SafetyManifest.TargetSystemConfigFileName)))
                        problems.Add(string.Format("line {0}: Folder '{1}' has no {2}", Line(root), folder, SafetyManifest.TargetSystemConfigFileName));
                }
            }

            string targetSystemConfigPath = null;
            if (targetSystemConfig != null)
            {
                targetSystemConfigPath = targetSystemConfig.Length == 0 ? null : Resolve(sourceFolder, targetSystemConfig);
                if (targetSystemConfigPath == null || !File.Exists(targetSystemConfigPath))
                    problems.Add(string.Format("line {0}: TargetSystemConfig '{1}' does not exist", Line(root), targetSystemConfig));
            }

            if (problems.Count > 0)
                throw new SafetyManifestException(pathForMessages, problems);
            return new SafetyManifest(name, folderPath, targetSystemConfigPath);
        }

        static string Resolve(string sourceFolder, string relative) =>
            Path.GetFullPath(Path.Combine(sourceFolder, relative.Replace('/', Path.DirectorySeparatorChar))).TrimEnd('\\', '/');

        static int Line(XObject node) => ((IXmlLineInfo)node).HasLineInfo() ? ((IXmlLineInfo)node).LineNumber : 0;
    }
}
