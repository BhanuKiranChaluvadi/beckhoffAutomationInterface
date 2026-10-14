using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Interop.TCatSysManager;

namespace BeckhoffAutomationInterface.Sync
{
    /// <summary>
    /// Extra PLC compiler defines for one build (--define, e.g. SIMULATION for a test run on
    /// UmRT). They live in the PLC project's CompilerSettings/CompilerDefines (ProduceXml of
    /// "TIPC^Plc^Plc Project", 2026-10-14), the same field TwinCAT fills with the selected
    /// variant's defines, so call Add AFTER CurrentProjectVariant is set. Only build and
    /// activate use this: both work on a read-only shadow copy, so a define never reaches a
    /// saved project.
    /// </summary>
    static class CompilerDefines
    {
        static readonly Regex Identifier = new Regex(@"^[A-Za-z_][A-Za-z0-9_]*$");
        static readonly Regex DefinesElement = new Regex(@"<CompilerDefines\s*/>|<CompilerDefines>(.*?)</CompilerDefines>", RegexOptions.Singleline);

        /// <summary>Null when every name is a valid define, else the message for the bad one.</summary>
        public static string Problem(IEnumerable<string> defines) =>
            defines.Where(d => !Identifier.IsMatch(d ?? ""))
                   .Select(d => string.Format("--define '{0}' is not a valid name: letters, digits and '_' only, not starting with a digit.", d))
                   .FirstOrDefault();

        /// <summary>The CompilerDefines value of a PLC project item's XML ("" when empty or absent).</summary>
        internal static string Read(string projectXml)
        {
            Match m = DefinesElement.Match(projectXml ?? "");
            return m.Success ? m.Groups[1].Value : "";
        }

        /// <summary>current ("A,B," as TwinCAT writes it) plus every extra define not yet in it.</summary>
        internal static string Merge(string current, IEnumerable<string> extra)
        {
            var names = (current ?? "").Split(',').Select(s => s.Trim()).Where(s => s.Length > 0).ToList();
            foreach (string d in extra)
                if (!names.Contains(d, StringComparer.OrdinalIgnoreCase))
                    names.Add(d);
            return string.Join(",", names);
        }

        /// <summary>Adds the defines to the PLC project and returns the resulting list.</summary>
        public static string Add(ITcSysManager sysManager, string plcProjectPath, IReadOnlyList<string> defines)
        {
            ITcSmTreeItem project = sysManager.LookupTreeItem(plcProjectPath);
            string merged = Merge(Read(project.ProduceXml()), defines);
            project.ConsumeXml("<TreeItem><IECProjectDef><CompilerSettings><CompilerDefines>" + merged +
                               "</CompilerDefines></CompilerSettings></IECProjectDef></TreeItem>");
            string now = Read(project.ProduceXml());
            foreach (string d in defines)
                if (!Merge(now, new string[0]).Split(',').Contains(d, StringComparer.OrdinalIgnoreCase))
                    throw new InvalidOperationException(string.Format("TwinCAT did not keep the compiler define '{0}' (defines now: '{1}').", d, now));
            return now;
        }
    }
}
