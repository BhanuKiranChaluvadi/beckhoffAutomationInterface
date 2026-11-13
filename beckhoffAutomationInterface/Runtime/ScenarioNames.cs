using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using BeckhoffAutomationInterface.Machines;
using BeckhoffAutomationInterface.Sync;

namespace BeckhoffAutomationInterface.Runtime
{
    /// <summary>
    /// The enum member names of a scenario file and their numbers, from the ST enums of the family
    /// under test (StEnums): "SetValve" in "command" (a bare E_Command member), "E_Enum.Member" in a
    /// parameter value, a symbol index and a compared value. The same name has a different number
    /// in each family, so a name needs the family. Without one, a file with numbers only still
    /// loads, and the first name stops the load with the reason. The enums are read at the first
    /// name, once.
    /// </summary>
    sealed class ScenarioNames
    {
        /// <summary>A string of this form is a name; any other string stays a string.</summary>
        static readonly Regex Name = new Regex(@"^E_\w+\.\w+$", RegexOptions.IgnoreCase); // as ST: case ignored

        readonly string _family;
        readonly Func<StEnums> _read;
        readonly string _noFamily;
        StEnums _enums;
        string _readProblem;

        ScenarioNames(string family, Func<StEnums> read, string noFamily)
        {
            _family = family;
            _read = read;
            _noFamily = noFamily;
        }

        /// <summary>The names of a family; read() gives its enums (FamilyOf(..).ReadEnums(root)).</summary>
        public static ScenarioNames Of(string family, Func<StEnums> read) => new ScenarioNames(family, read, null);

        /// <summary>No family: numbers only; a name is refused with this reason.</summary>
        public static ScenarioNames Without(string reason) => new ScenarioNames(null, null, reason);

        public static readonly ScenarioNames None = Without("the family under test is not known (give --family <name>)");

        /// <summary>
        /// The family under test of 'test' and 'acceptance': --family, else the one family whose name
        /// is a folder of the scenario path (Tests\Acceptance\NFPro). The families and their sources
        /// come from bai.machines.json: --manifest, else the first one above the scenario path.
        /// When neither tells the family, the files may use numbers only. Throws ArgumentException
        /// for a --family or --manifest that cannot be used.
        /// </summary>
        public static ScenarioNames ForScenarios(string scenarios, string family, string manifestPath)
        {
            MachineManifest manifest;
            try
            {
                manifest = MachineManifest.Load(MachineManifest.Find(manifestPath, scenarios));
            }
            catch (Exception ex) when (ex is ArgumentException || ex is FormatException || ex is IOException || ex is UnauthorizedAccessException)
            {
                if (family != null || manifestPath != null)
                    throw new ArgumentException((family != null ? "--family " + family + ": " : "") + ex.Message, ex);
                return Without(string.Format("the family is not known ({0}); give --manifest <file> and --family <name>", ex.Message.TrimEnd('.')));
            }

            string families = string.Join(", ", manifest.Families.Select(f => f.Name));
            List<MachineFamily> found;
            if (family != null)
            {
                found = manifest.Families.Where(f => f.Name.Equals(family, StringComparison.OrdinalIgnoreCase)).ToList();
                if (found.Count == 0)
                    throw new ArgumentException(string.Format("--family {0}: not a family of {1} ({2}).", family, manifest.File, families));
            }
            else
            {
                string full = Path.GetFullPath(scenarios), root = manifest.Root.TrimEnd('\\', '/') + Path.DirectorySeparatorChar;
                string below = full.StartsWith(root, StringComparison.OrdinalIgnoreCase) ? full.Substring(root.Length) : full;
                var folders = new HashSet<string>(below.Split('\\', '/'), StringComparer.OrdinalIgnoreCase);
                found = manifest.Families.Where(f => folders.Contains(f.Name)).ToList();
                if (found.Count != 1)
                    return Without(string.Format("the scenario path names {0} family ({1}); give --family <name>",
                        found.Count == 0 ? "no" : "more than one", families));
            }
            MachineFamily chosen = found[0];
            return Of(chosen.Name, () => chosen.ReadEnums(manifest.Root));
        }

        /// <summary>True for a text of the form E_Enum.Member.</summary>
        public static bool IsName(object value) => value is string s && Name.IsMatch(s.Trim());

        /// <summary>A bare E_Command member to its number.</summary>
        public bool TryResolveCommand(string member, out long value, out string problem) =>
            TryResolve(StEnums.CommandEnum + "." + member.Trim(), member.Trim(), out value, out problem);

        /// <summary>"E_Enum.Member" to its number; false with the reason (it names the name).</summary>
        public bool TryResolve(string name, out long value, out string problem) =>
            TryResolve(name.Trim(), name.Trim(), out value, out problem);

        bool TryResolve(string qualified, string written, out long value, out string problem)
        {
            value = 0;
            StEnums enums = Enums(out problem);
            if (enums == null)
            {
                problem = string.Format("'{0}': {1}", written, problem);
                return false;
            }
            if (enums.TryResolve(qualified, out value, out problem))
                return true;
            problem = string.Format("'{0}': {1} (family {2})", written, problem, _family);
            return false;
        }

        StEnums Enums(out string problem)
        {
            problem = null;
            if (_read == null)
            {
                problem = _noFamily;
                return null;
            }
            if (_enums == null && _readProblem == null)
            {
                try
                {
                    _enums = _read();
                }
                catch (Exception ex) when (ex is FormatException || ex is IOException || ex is UnauthorizedAccessException)
                {
                    _readProblem = string.Format("cannot read the enums of family {0}: {1}", _family, ex.Message);
                }
            }
            problem = _readProblem;
            return _enums;
        }
    }
}
