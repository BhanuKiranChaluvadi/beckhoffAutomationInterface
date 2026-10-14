using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace BeckhoffAutomationInterface.Sync
{
    class VariantBuildResult
    {
        public string Variant { get; set; }
        public bool Passed { get; set; }
        public int ErrorCount { get; set; }
        public bool TimedOut { get; set; }
    }

    /// <summary>Which variants a build covers, and the end-of-run summary (pure, unit-tested).</summary>
    static class VariantBuildPlan
    {
        /// <summary>
        /// Project without variants: nothing to select (a plain build), and asking for a
        /// variant is an error. Project with variants: --variant picks one (name matched
        /// case-insensitively, returned in the project's spelling); otherwise ALL variants —
        /// the compiler does not check code in a branch that is not selected, so building
        /// only one variant would hide errors in the others.
        /// </summary>
        public static List<string> Select(IReadOnlyList<string> projectVariants, string requestedVariant, bool allVariants)
        {
            if (requestedVariant != null && allVariants)
                throw new ArgumentException("use either --variant or --all-variants, not both.");
            if (projectVariants.Count == 0)
            {
                if (requestedVariant != null || allVariants)
                    throw new ArgumentException("the project has no variants (add a variants.xml and run 'sync variants').");
                return new List<string>();
            }

            if (requestedVariant != null)
            {
                string match = projectVariants.FirstOrDefault(v => v.Equals(requestedVariant, StringComparison.OrdinalIgnoreCase));
                if (match == null)
                    throw new ArgumentException(string.Format("unknown variant '{0}'. The project has: {1}.",
                        requestedVariant, string.Join(", ", projectVariants)));
                return new List<string> { match };
            }

            return projectVariants.ToList();
        }

        public static string Summary(IReadOnlyList<VariantBuildResult> results)
        {
            var sb = new StringBuilder();
            int width = Math.Max(7, results.Max(r => r.Variant.Length));
            sb.AppendLine("  " + "Variant".PadRight(width) + "  Result");
            foreach (VariantBuildResult r in results)
            {
                string result = r.TimedOut ? "TIMED OUT"
                    : r.Passed ? "PASSED"
                    : string.Format("FAILED ({0} error(s))", r.ErrorCount);
                sb.AppendLine("  " + r.Variant.PadRight(width) + "  " + result);
            }
            int failed = results.Count(r => !r.Passed);
            sb.Append(failed == 0
                ? string.Format("ALL {0} VARIANT(S) PASSED", results.Count)
                : string.Format("{0} OF {1} VARIANT(S) FAILED", failed, results.Count));
            return sb.ToString();
        }
    }
}
