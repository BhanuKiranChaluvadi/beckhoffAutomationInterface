using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Web.Script.Serialization;
using System.Xml.Linq;

namespace BeckhoffAutomationInterface.Runtime
{
    /// <summary>
    /// The runtimes this tool may activate and verify on. User decision (2026-10-14): only two —
    ///   umrt  = the User Mode Runtime "UmRT_Default" on this PC,
    ///   local = this PC's own TwinCAT runtime.
    /// Real machines are out of scope for now, so there is no free AMS Net ID option.
    /// </summary>
    static class RuntimeTargets
    {
        public const string UmRt = "umrt";
        public const string Local = "local";
        public static readonly IReadOnlyList<string> Names = new[] { UmRt, Local };

        static string TwinCatData =>
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "Beckhoff", "TwinCAT", "3.1");

        public static string UmRtFolder => Path.Combine(TwinCatData, "Runtimes", "UmRT_Default", "3.1");

        public static string Normalize(string name)
        {
            string n = (name ?? "").Trim().ToLowerInvariant();
            if (!Names.Contains(n))
                throw new ArgumentException(string.Format("unknown target '{0}'. Use one of: {1}.", name, string.Join(", ", Names)));
            return n;
        }

        /// <summary>AMS Net ID of the target: UmRT from its own TcRegistry.xml, local from the ADS router.</summary>
        public static string NetId(string target)
        {
            switch (Normalize(target))
            {
                case UmRt:
                    string registry = Path.Combine(UmRtFolder, "TcRegistry.xml");
                    if (!File.Exists(registry))
                        throw new InvalidOperationException("UmRT_Default is not installed (no " + registry + ").");
                    return NetIdFromTcRegistry(File.ReadAllText(registry));
                default:
                    return AdsClient.LocalNetId();
            }
        }

        /// <summary>Folder whose CurrentProjectInfo.json describes the boot project on the target.</summary>
        public static string BootFolder(string target) =>
            Normalize(target) == UmRt ? Path.Combine(UmRtFolder, "Boot") : Path.Combine(TwinCatData, "Boot");

        /// <summary>Folders that hold the target's license files (*.tclrs).</summary>
        public static IReadOnlyList<string> LicenseFolders(string target)
        {
            if (Normalize(target) == UmRt)
                return new[] { Path.Combine(UmRtFolder, "Target", "License") };
            return new[]
            {
                Path.Combine(TwinCatData, "Target", "License"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Beckhoff", "TwinCAT", "3.1", "Target", "License"),
            };
        }

        /// <summary>Latest license expiry on the target: null = no license file; DateTime.MaxValue =
        /// a license without expiry.</summary>
        public static DateTime? LicenseExpiry(string target)
        {
            DateTime? latest = null;
            foreach (string folder in LicenseFolders(target).Where(Directory.Exists))
                foreach (string file in Directory.GetFiles(folder, "*.tclrs"))
                {
                    DateTime expiry = Doctor.LicenseExpiry(File.ReadAllText(file)) ?? DateTime.MaxValue;
                    if (latest == null || expiry > latest)
                        latest = expiry;
                }
            return latest;
        }

        /// <summary>"name (created …)" of the boot project on the target, or null when there is none.</summary>
        public static string CurrentBootProject(string target) => DescribeBootFolder(BootFolder(target));

        /// <summary>CurrentProjectInfo.json describes the last SYSTEM activation. When only the PLC
        /// changed (same system configuration, for example two machines of one family with the
        /// I/O disabled), TwinCAT rewrites just Plc/Port_851*, so the PLC boot time is added.</summary>
        internal static string DescribeBootFolder(string bootFolder)
        {
            string info = Path.Combine(bootFolder, "CurrentProjectInfo.json");
            if (!File.Exists(info))
                return null;
            string plcBoot = Path.Combine(bootFolder, "Plc", "Port_851_boot.tizip");
            return WithPlcUpdate(DescribeProjectInfo(File.ReadAllText(info)), File.GetLastWriteTime(info),
                File.Exists(plcBoot) ? File.GetLastWriteTime(plcBoot) : (DateTime?)null);
        }

        internal static string WithPlcUpdate(string description, DateTime infoWritten, DateTime? plcBootWritten) =>
            plcBootWritten.HasValue && plcBootWritten.Value > infoWritten.AddSeconds(30)
                ? string.Format("{0}; PLC (port 851) updated {1:yyyy-MM-ddTHH:mm:ss}", description, plcBootWritten.Value)
                : description;

        /// <summary>Reads &lt;Value Name="AmsNetId" Type="BIN"&gt;C7042AFA0101&lt;/Value&gt; → "199.4.42.250.1.1".</summary>
        internal static string NetIdFromTcRegistry(string xml)
        {
            XElement value = XDocument.Parse(xml).Descendants("Value")
                .FirstOrDefault(v => (string)v.Attribute("Name") == "AmsNetId");
            string hex = value?.Value.Trim();
            if (hex == null || hex.Length != 12)
                throw new FormatException("TcRegistry.xml has no 6-byte AmsNetId value.");
            var bytes = new byte[6];
            for (int i = 0; i < 6; i++)
                bytes[i] = Convert.ToByte(hex.Substring(i * 2, 2), 16);
            return AdsClient.FormatNetId(bytes);
        }

        internal static string DescribeProjectInfo(string json)
        {
            var root = new JavaScriptSerializer().DeserializeObject(json) as Dictionary<string, object>;
            if (root == null || !(root.TryGetValue("project", out object p) && p is Dictionary<string, object> project))
                return "(unknown project)";
            project.TryGetValue("name", out object name);
            project.TryGetValue("created", out object created);
            return string.Format("{0} (activated {1})", name ?? "(no name)", created ?? "?");
        }
    }
}
