using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.Win32;

namespace BeckhoffAutomationInterface
{
    /// <summary>
    /// A Visual Studio-based shell that can host the TwinCAT Automation Interface.
    /// TcXaeShell ships with every TwinCAT XAE install; full Visual Studio 2022 only
    /// exists where someone installed it (and integrated TwinCAT into it).
    /// </summary>
    sealed class DteShell
    {
        public string Name { get; }        // value of --shell / .stconfig "shell"
        public string ProgId { get; }
        public string ProcessName { get; } // process name without ".exe"
        public string ExePath { get; }     // set only on a resolved shell

        DteShell(string name, string progId, string processName, string exePath)
        {
            Name = name;
            ProgId = progId;
            ProcessName = processName;
            ExePath = exePath;
        }

        public DteShell WithExe(string exePath) => new DteShell(Name, ProgId, ProcessName, exePath);

        public static readonly DteShell Xae = new DteShell("xae", "TcXaeShell.DTE.17.0", "TcXaeShell", null);
        public static readonly DteShell Vs2022 = new DteShell("vs2022", "VisualStudio.DTE.17.0", "devenv", null);

        /// <summary>Preference order when no shell is requested: XAE first, because every
        /// TwinCAT engineering PC has it.</summary>
        public static readonly IReadOnlyList<DteShell> All = new[] { Xae, Vs2022 };
    }

    /// <summary>The two registry reads the resolver needs, behind an interface so the
    /// selection logic is unit-testable without touching the real registry.</summary>
    interface IComRegistry
    {
        /// <summary>CLSID string for a ProgID, or null when the ProgID is not registered.</summary>
        string GetClsid(string progId);

        /// <summary>Raw LocalServer32 command line for a CLSID, or null when absent.</summary>
        string GetLocalServer(string clsid);
    }

    sealed class WindowsComRegistry : IComRegistry
    {
        public string GetClsid(string progId)
        {
            using (RegistryKey key = Registry.ClassesRoot.OpenSubKey(progId + @"\CLSID"))
                return key?.GetValue(null) as string;
        }

        public string GetLocalServer(string clsid)
        {
            using (RegistryKey key = Registry.ClassesRoot.OpenSubKey(@"CLSID\" + clsid + @"\LocalServer32"))
                return key?.GetValue(null) as string;
        }
    }

    /// <summary>
    /// Picks the DTE shell to automate. A ProgID can stay registered after its exe is
    /// gone (or point at a class that is not registered at all — the "Class not
    /// registered {33ABD590-…}" crash on an XAE-only bench PC), so a candidate only
    /// counts when its LocalServer32 exe exists on disk.
    /// </summary>
    static class DteShellResolver
    {
        public static bool IsKnownName(string name) =>
            DteShell.All.Any(s => s.Name.Equals(name, StringComparison.OrdinalIgnoreCase));

        /// <summary>Returns the requested shell (or the first usable one when
        /// requestedName is null), with its exe path filled in. Throws
        /// InvalidOperationException listing every candidate's problem otherwise.</summary>
        public static DteShell Resolve(string requestedName, IComRegistry registry, Func<string, bool> fileExists)
        {
            IEnumerable<DteShell> candidates = DteShell.All;
            if (requestedName != null)
            {
                if (!IsKnownName(requestedName))
                    throw new InvalidOperationException(string.Format(
                        "Unknown shell '{0}'. Use one of: {1}.", requestedName, string.Join(", ", DteShell.All.Select(s => s.Name))));
                candidates = DteShell.All.Where(s => s.Name.Equals(requestedName, StringComparison.OrdinalIgnoreCase));
            }

            var problems = new List<string>();
            foreach (DteShell shell in candidates)
            {
                string problem = Check(shell, registry, fileExists, out string exePath);
                if (problem == null)
                    return shell.WithExe(exePath);
                problems.Add(string.Format("  {0} ({1}): {2}", shell.Name, shell.ProgId, problem));
            }

            throw new InvalidOperationException(
                (requestedName != null
                    ? string.Format("Shell '{0}' is not usable on this PC:", requestedName)
                    : "No usable DTE shell found (TwinCAT XAE or Visual Studio 2022):")
                + Environment.NewLine + string.Join(Environment.NewLine, problems));
        }

        /// <summary>Every known shell with its exe path (usable) or its problem — for `doctor`.</summary>
        public static IEnumerable<(DteShell Shell, string Detail, bool Usable)> Status(IComRegistry registry, Func<string, bool> fileExists)
        {
            foreach (DteShell shell in DteShell.All)
            {
                string problem = Check(shell, registry, fileExists, out string exePath);
                yield return (shell, problem ?? exePath, problem == null);
            }
        }

        static string Check(DteShell shell, IComRegistry registry, Func<string, bool> fileExists, out string exePath)
        {
            exePath = null;
            string clsid = registry.GetClsid(shell.ProgId);
            if (string.IsNullOrEmpty(clsid))
                return "ProgID is not registered";

            string server = registry.GetLocalServer(clsid);
            if (string.IsNullOrEmpty(server))
                return string.Format("class {0} is not registered (no LocalServer32)", clsid);

            exePath = ExeFromLocalServer(server);
            if (!fileExists(exePath))
                return string.Format("registered exe does not exist: {0}", exePath);
            return null;
        }

        /// <summary>LocalServer32 holds a command line: the exe path (quoted or not),
        /// optionally followed by arguments.</summary>
        internal static string ExeFromLocalServer(string command)
        {
            string text = command.Trim();
            if (text.StartsWith("\""))
            {
                int close = text.IndexOf('"', 1);
                return close > 0 ? text.Substring(1, close - 1) : text.Trim('"');
            }
            int exe = text.IndexOf(".exe", StringComparison.OrdinalIgnoreCase);
            return exe >= 0 ? text.Substring(0, exe + 4) : text;
        }
    }
}
