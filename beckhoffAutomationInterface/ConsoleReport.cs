using System;
using System.Collections.Generic;

namespace BeckhoffAutomationInterface
{
    /// <summary>Shared console-report printing helper, used by both Program (--parse-only's
    /// lint output) and SyncPipeline (every stage's Created/Updated/Deleted/Warnings lines).</summary>
    static class ConsoleReport
    {
        public static void Heading(string title) =>
            new Cli.ConsoleLayout(Console.Out, Cli.ConsoleLayout.TerminalWidth).Heading(title);

        public static void Fields(System.Collections.IDictionary values) =>
            new Cli.ConsoleLayout(Console.Out, Cli.ConsoleLayout.TerminalWidth).Fields(values);

        public static void Result(string command, System.Collections.IDictionary values)
        {
            Heading("PASS: " + command);
            Fields(values);
        }

        /// <summary>Prints report items with continuation lines aligned beneath the text.
        /// Prefixes retain their existing status markers for log consumers.</summary>
        public static void PrintLines(string prefix, IEnumerable<string> items)
        {
            foreach (string item in items)
            {
                string[] lines = (item ?? "").Replace("\r\n", "\n").Split('\n');
                Console.WriteLine("    {0}{1}", prefix, lines[0]);
                for (int i = 1; i < lines.Length; i++)
                    Console.WriteLine(new string(' ', 4 + prefix.Length) + lines[i]);
            }
        }
    }
}
