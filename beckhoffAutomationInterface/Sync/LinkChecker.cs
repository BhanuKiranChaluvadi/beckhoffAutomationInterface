using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace BeckhoffAutomationInterface.Sync
{
    /// <summary>One AT %I*/%Q* declaration found in a GVL or PROGRAM source.</summary>
    class DeclaredIoVariable
    {
        public string SourceName { get; }
        public string VariableName { get; }
        public string Direction { get; }
        public string SourceRelativePath { get; }

        /// <summary>Matches the tail of a LinkSpec.PlcVar (e.g. "GVL_Safety.SafetyOk").</summary>
        public string Key => SourceName + "." + VariableName;

        public DeclaredIoVariable(string sourceName, string variableName, string direction, string sourceRelativePath)
        {
            SourceName = sourceName;
            VariableName = variableName;
            Direction = direction;
            SourceRelativePath = sourceRelativePath;
        }
    }

    class LinkCheckReport
    {
        public List<DeclaredIoVariable> Linked { get; } = new List<DeclaredIoVariable>();
        public List<DeclaredIoVariable> Unlinked { get; } = new List<DeclaredIoVariable>();

        /// <summary>io-devices.xml &lt;Link&gt; entries whose PlcVar matches no declared %I/%Q
        /// variable — stale or typo'd, e.g. after a rename.</summary>
        public List<LinkSpec> OrphanedLinks { get; } = new List<LinkSpec>();

        /// <summary>links.xml entries with a resolvable SimpleKey that matches no declared
        /// %I/%Q variable — the links.xml counterpart to OrphanedLinks.</summary>
        public List<VarLinkEntry> OrphanedVarLinks { get; } = new List<VarLinkEntry>();

        /// <summary>links.xml entries with a null SimpleKey (FUNCTION_BLOCK-instance/struct
        /// nested VarA, e.g. "MAIN.fbSpec.inLogicSig[1]") — can't be statically resolved, so
        /// these are reported informationally and NEVER counted as orphaned/stale.</summary>
        public List<VarLinkEntry> Unresolvable { get; } = new List<VarLinkEntry>();
    }

    /// <summary>
    /// Read-only "declared vs linked" check for %I/%Q variables — the read-side counterpart
    /// to Sync/VariableLinkEngine.cs, which DOES apply <Links> entries via COM but never
    /// checks whether every AT %I*/%Q* declaration in .st source actually has one. Useful on
    /// its own as a fast, no-Visual-Studio preflight/CI gate (--check-links): report which
    /// declared %I/%Q variables have no matching &lt;Link&gt; in io-devices.xml, without
    /// writing anything.
    ///
    /// SCOPE: only scans GVL and PROGRAM sources. LinkSpec.PlcVar's format assumes a
    /// globally-unique path ("&lt;GVL/PROGRAM name&gt;.&lt;var&gt;"), which only holds for GVL
    /// globals and PROGRAM instances (both singletons in TwinCAT). A FUNCTION_BLOCK's own
    /// AT %I*/%Q* member, or a STRUCT/DUT member, resolves through wherever that FB/struct is
    /// actually instantiated (e.g. "PRG_MAIN.fbMotor.bEnable") — not derivable from static
    /// source alone, so those are deliberately NOT scanned here rather than silently
    /// mismatched. See tasks/plan.md for why this is a separate, bigger piece of work.
    /// </summary>
    static class LinkChecker
    {
        static readonly Regex AtIoDeclaration = new Regex(
            @"^\s*(\w+)\s+AT\s*(%I\*|%Q\*)\s*:", RegexOptions.IgnoreCase);

        /// <summary>varLinks is optional (links.xml may not exist) — a declared %I/%Q
        /// variable counts as linked if its key matches either an io-devices.xml &lt;Link&gt;
        /// or a links.xml entry with a resolvable SimpleKey (see VarLinkEntry).</summary>
        public static LinkCheckReport Check(IReadOnlyList<StPouSource> sources, IReadOnlyList<LinkSpec> links, IReadOnlyList<VarLinkEntry> varLinks = null)
        {
            varLinks = varLinks ?? new List<VarLinkEntry>();
            List<DeclaredIoVariable> declared = FindDeclaredIoVariables(sources).ToList();

            var linkedKeys = new HashSet<string>(links.Select(l => KeyFromPlcVar(l.PlcVar)), StringComparer.OrdinalIgnoreCase);
            foreach (VarLinkEntry entry in varLinks)
                if (entry.SimpleKey != null)
                    linkedKeys.Add(entry.SimpleKey);

            var declaredKeys = new HashSet<string>(declared.Select(v => v.Key), StringComparer.OrdinalIgnoreCase);

            var report = new LinkCheckReport();
            foreach (DeclaredIoVariable variable in declared)
                (linkedKeys.Contains(variable.Key) ? report.Linked : report.Unlinked).Add(variable);

            foreach (LinkSpec link in links)
                if (!declaredKeys.Contains(KeyFromPlcVar(link.PlcVar)))
                    report.OrphanedLinks.Add(link);

            foreach (VarLinkEntry entry in varLinks)
            {
                if (entry.SimpleKey == null)
                    report.Unresolvable.Add(entry);
                else if (!declaredKeys.Contains(entry.SimpleKey))
                    report.OrphanedVarLinks.Add(entry);
            }

            return report;
        }

        static IEnumerable<DeclaredIoVariable> FindDeclaredIoVariables(IReadOnlyList<StPouSource> sources)
        {
            foreach (StPouSource source in sources)
            {
                if (source.Kind != PouKind.Gvl && source.Kind != PouKind.Program)
                    continue;
                if (source.DeclarationText == null)
                    continue;

                foreach (string rawLine in source.DeclarationText.Replace("\r\n", "\n").Split('\n'))
                {
                    string line = rawLine.TrimStart();
                    if (line.StartsWith("//", StringComparison.Ordinal))
                        continue;

                    Match match = AtIoDeclaration.Match(line);
                    if (!match.Success)
                        continue;

                    string direction = match.Groups[2].Value.StartsWith("%I", StringComparison.OrdinalIgnoreCase) ? "%I" : "%Q";
                    yield return new DeclaredIoVariable(source.Name, match.Groups[1].Value, direction, source.SourceRelativePath);
                }
            }
        }

        /// <summary>PlcVar's tail after its last "^" is "&lt;GVL/PROGRAM&gt;.&lt;var&gt;" (see
        /// LinkSpec's doc comment) — matches DeclaredIoVariable.Key directly.</summary>
        static string KeyFromPlcVar(string plcVar)
        {
            int caret = plcVar.LastIndexOf('^');
            return caret >= 0 ? plcVar.Substring(caret + 1) : plcVar;
        }
    }
}
