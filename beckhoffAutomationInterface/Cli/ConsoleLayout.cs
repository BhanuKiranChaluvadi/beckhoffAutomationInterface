using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;

namespace BeckhoffAutomationInterface.Cli
{
    /// <summary>Plain-text layout shared by help and reports. No terminal control codes,
    /// truncation, or color dependencies, so redirected output remains readable.</summary>
    internal sealed class ConsoleLayout
    {
        readonly TextWriter writer;
        readonly int width;

        internal ConsoleLayout(TextWriter writer, int width = 80)
        {
            this.writer = writer;
            this.width = Math.Max(40, width);
        }

        internal static int TerminalWidth
        {
            get
            {
                try { return Console.IsOutputRedirected ? 80 : Math.Max(40, Math.Min(100, Console.WindowWidth - 1)); }
                catch (IOException) { return 80; }
                catch (PlatformNotSupportedException) { return 80; }
            }
        }

        internal void Blank() => writer.WriteLine();

        internal void Heading(string title)
        {
            writer.WriteLine(title);
            writer.WriteLine(new string('-', Math.Min(title.Length, width)));
        }

        internal void Section(string title)
        {
            Blank();
            writer.WriteLine(title + ":");
        }

        internal void Text(string text, int indent = 2, int? continuation = null)
        {
            foreach (string paragraph in (text ?? "").Replace("\r\n", "\n").Split('\n'))
            {
                string line = new string(' ', indent);
                int padding = indent;
                foreach (string word in paragraph.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries))
                {
                    if (line.Length > padding && line.Length + 1 + word.Length > width)
                    {
                        writer.WriteLine(line);
                        padding = continuation ?? indent;
                        line = new string(' ', padding);
                    }
                    if (line.Length > padding) line += " ";
                    line += word;
                }
                writer.WriteLine(line.TrimEnd());
            }
        }

        internal void Rows(IEnumerable<KeyValuePair<string, string>> rows)
        {
            var items = rows.ToList();
            if (items.Count == 0) return;
            int column = Math.Min(32, items.Max(r => r.Key.Length) + 4);
            foreach (var row in items)
            {
                if (width - column < 30 || row.Key.Length + 4 > column)
                {
                    Text(row.Key);
                    Text(row.Value, 4);
                    continue;
                }
                // Wrap descriptions without changing the label or its indentation.
                using (var buffer = new StringWriter(CultureInfo.InvariantCulture))
                {
                    new ConsoleLayout(buffer, width).Text(row.Value, column);
                    string[] lines = buffer.ToString().TrimEnd('\r', '\n').Split('\n');
                    writer.WriteLine("  " + row.Key.PadRight(column - 2) + lines[0].TrimStart().TrimEnd('\r'));
                    foreach (string line in lines.Skip(1)) writer.WriteLine(line.TrimEnd('\r'));
                }
            }
        }

        internal void Fields(IDictionary values, int indent = 2)
        {
            var entries = new List<DictionaryEntry>();
            foreach (DictionaryEntry entry in values) entries.Add(entry);
            int labelWidth = entries.Select(e => Convert.ToString(e.Key, CultureInfo.InvariantCulture).Length).DefaultIfEmpty(0).Max();
            foreach (var entry in entries)
            {
                string label = Convert.ToString(entry.Key, CultureInfo.InvariantCulture);
                if (entry.Value is IDictionary nested)
                {
                    Text(label + ":", indent);
                    Fields(nested, indent + 2);
                }
                else if (entry.Value is IEnumerable list && !(entry.Value is string))
                {
                    var items = list.Cast<object>().ToList();
                    Text(label + ":" + (items.Count == 0 ? " (none)" : ""), indent);
                    foreach (object item in items)
                    {
                        if (item is IDictionary fields)
                        {
                            Text("-", indent + 2);
                            Fields(fields, indent + 4);
                        }
                        else Text("- " + Scalar(item), indent + 2, indent + 4);
                    }
                }
                else
                {
                    // Field values (paths, hashes, identifiers) remain intact for copying.
                    string prefix = new string(' ', indent) + label.PadRight(labelWidth) + "  ";
                    writer.WriteLine(prefix + Scalar(entry.Value).Replace("\r\n", "\n")
                        .Replace("\n", Environment.NewLine + new string(' ', prefix.Length)));
                }
            }
        }

        static string Scalar(object value) => value == null ? "(none)"
            : value is bool flag ? (flag ? "yes" : "no") : Convert.ToString(value, CultureInfo.InvariantCulture);
    }
}
