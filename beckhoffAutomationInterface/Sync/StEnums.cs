using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace BeckhoffAutomationInterface.Sync
{
    /// <summary>
    /// The enums declared in the .st files of some source folders (for one family: Lib and
    /// App/&lt;family&gt;), with the number of each member. "E_Enum.Member" gives the number that
    /// the PLC of that family uses (pure file work, no TwinCAT). As in ST, names ignore case, and a
    /// member without ":= value" is the member before + 1 (the first one 0). Comments are skipped;
    /// the base type (none, INT, DINT, UDINT, ...) and attributes do not change a number.
    /// </summary>
    sealed class StEnums
    {
        static readonly Regex EnumHeader = new Regex(@"\bTYPE\s+(\w+)\s*:\s*\(", RegexOptions.IgnoreCase);
        /// <summary>The one form of an enum member name: E_Enum.Member (as ST: case ignored).</summary>
        static readonly Regex QualifiedName = new Regex(@"^(E_\w+)\.(\w+)$", RegexOptions.IgnoreCase);
        static readonly Regex Member = new Regex(@"^(\w+)(?:\s*:=\s*(-?\d+))?$");

        readonly Dictionary<string, Dictionary<string, long>> _enums =
            new Dictionary<string, Dictionary<string, long>>(StringComparer.OrdinalIgnoreCase);
        /// <summary>Enum name to the file that declares it.</summary>
        readonly Dictionary<string, string> _files = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        /// <summary>Reads every .st file below the folders (absolute paths; a missing folder is an
        /// error), the folders in the order given and the files of a folder sorted by path. Throws
        /// FormatException, naming the file, for an enum declared twice, two enums in one file or a
        /// member that cannot be read: a guessed number would move the wrong valve.</summary>
        public static StEnums Read(IEnumerable<string> sourceFolders)
        {
            var enums = new StEnums();
            foreach (string folder in sourceFolders)
                foreach (string file in StFileParser.GetStFiles(folder, null).OrderBy(f => f, StringComparer.OrdinalIgnoreCase))
                    enums.ReadFile(file);
            return enums;
        }

        /// <summary>True for a text of the form E_Enum.Member (spaces around it are ignored).</summary>
        public static bool IsQualifiedName(string text) => text != null && QualifiedName.IsMatch(text.Trim());

        void ReadFile(string file)
        {
            string text = StFileParser.StripComments(File.ReadAllText(file));
            MatchCollection headers = EnumHeader.Matches(text);
            if (headers.Count == 0)
                return; // not an enum (STRUCT, alias, POU, GVL)
            if (headers.Count > 1)
                throw new FormatException(string.Format("{0}: more than one enum TYPE ({1}); one enum per file",
                    file, string.Join(", ", headers.Cast<Match>().Select(h => h.Groups[1].Value))));
            Match header = headers[0];
            string name = header.Groups[1].Value;
            string where = string.Format("{0}: enum '{1}'", file, name);
            if (_files.TryGetValue(name, out string first))
                throw new FormatException(string.Format("enum '{0}' is declared twice: {1} and {2}", name, first, file));

            int open = header.Index + header.Length, close = text.IndexOf(')', open);
            if (close < 0)
                throw new FormatException(where + ": no ')' after the members");
            var members = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
            long next = 0; // IEC 61131-3: the first member without a value is 0, each next one counts on
            foreach (string raw in text.Substring(open, close - open).Split(','))
            {
                string item = Regex.Replace(raw.Trim(), @"\s+", " ");
                Match m = Member.Match(item);
                long value = next;
                if (!m.Success || (m.Groups[2].Success && !long.TryParse(m.Groups[2].Value, out value)))
                    throw new FormatException(string.Format("{0}: cannot read the member '{1}' (Name or Name := whole number)", where, item));
                if (members.ContainsKey(m.Groups[1].Value))
                    throw new FormatException(string.Format("{0}: member '{1}' is declared twice", where, m.Groups[1].Value));
                members.Add(m.Groups[1].Value, value);
                next = value + 1;
            }
            _enums.Add(name, members);
            _files.Add(name, file);
        }

        /// <summary>"E_Enum.Member" to its number; false with the reason when the enum or member is unknown.</summary>
        public bool TryResolve(string qualifiedName, out long value, out string problem)
        {
            value = 0;
            problem = null;
            Match name = QualifiedName.Match(qualifiedName?.Trim() ?? "");
            if (!name.Success)
                problem = string.Format("'{0}' is not an enum member name (E_Enum.Member)", qualifiedName);
            else if (!_enums.TryGetValue(name.Groups[1].Value, out Dictionary<string, long> members))
                problem = string.Format("unknown enum '{0}'", name.Groups[1].Value);
            else if (!members.TryGetValue(name.Groups[2].Value, out value))
                problem = string.Format("enum '{0}' has no member '{1}'", name.Groups[1].Value, name.Groups[2].Value);
            return problem == null;
        }
    }
}
