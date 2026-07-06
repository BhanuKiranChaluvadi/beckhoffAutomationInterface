using System;
using System.Collections.Generic;

namespace BeckhoffAutomationInterface.Sync
{
    /// <summary>
    /// Read-only naming-convention linter for parsed .st sources. Checks each PLC object's
    /// name against the prefix convention already used consistently throughout the real
    /// Shark source tree (FB_, PRG_, I_, F_, E_, ST_, T_, GVL_) and reports violations as
    /// warnings — it never blocks a sync or rewrites anything. METHODs and PROPERTIES have no
    /// naming convention of their own (they're named after what they do, not what kind of
    /// object they are), so they're skipped.
    /// </summary>
    static class StLinter
    {
        static readonly Dictionary<PouKind, string> ExpectedPrefix = new Dictionary<PouKind, string>
        {
            { PouKind.FunctionBlock, "FB_" },
            { PouKind.Program, "PRG_" },
            { PouKind.Interface, "I_" },
            { PouKind.Function, "F_" },
            { PouKind.EnumDut, "E_" },
            { PouKind.StructDut, "ST_" },
            { PouKind.AliasDut, "T_" },
            { PouKind.Gvl, "GVL_" },
        };

        public static List<string> Lint(IReadOnlyList<StPouSource> sources)
        {
            var issues = new List<string>();
            foreach (StPouSource source in sources)
            {
                if (source.IsMethod || source.IsProperty)
                    continue;

                if (ExpectedPrefix.TryGetValue(source.Kind, out string prefix) &&
                    !source.Name.StartsWith(prefix, StringComparison.Ordinal))
                {
                    string path = source.RelativeFolder.Length > 0 ? source.RelativeFolder + "/" + source.Name : source.Name;
                    issues.Add(string.Format("{0}: {1} name should start with '{2}' (naming convention)", path, source.Kind, prefix));
                }
            }
            return issues;
        }
    }
}
