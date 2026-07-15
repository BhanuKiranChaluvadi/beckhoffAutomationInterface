using System;
using System.Collections.Generic;

namespace BeckhoffAutomationInterface
{
    /// <summary>Shared console-report printing helper, used by both Program (--parse-only's
    /// lint output) and SyncPipeline (every stage's Created/Updated/Deleted/Warnings lines).</summary>
    static class ConsoleReport
    {
        /// <summary>Prints one indented line per item — collapses the repeated
        /// `foreach (...) Console.WriteLine("    &lt;symbol&gt; {0}", item);` shape that used
        /// to be duplicated at every report-printing call site. `prefix` carries its own
        /// trailing alignment padding exactly as each call site wants it (e.g.
        /// "+ created  "), so output stays identical to before this was extracted.</summary>
        public static void PrintLines(string prefix, IEnumerable<string> items)
        {
            foreach (string item in items)
                Console.WriteLine("    {0}{1}", prefix, item);
        }
    }
}
