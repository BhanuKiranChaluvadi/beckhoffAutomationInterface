using System;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace BeckhoffAutomationInterface.Cli
{
    /// <summary>
    /// Colors whole console lines by their result word, so a result is easy to see: red for
    /// FAIL/FAILED/ERROR/MISSING/EXPIRED, green for PASS/PASSED/OK/READY, yellow for
    /// WARNING/SKIP/NOTE/TIMEOUT. Only in a real console window: when the output goes to a file,
    /// a pipe or a log (TeeWriter), or NO_COLOR is set, the text stays plain.
    /// </summary>
    sealed class ColorConsole : TextWriter
    {
        static readonly Regex Red = new Regex(@"\b(FAIL|FAILED|ERROR|MISSING|EXPIRED|UNREADABLE)\b|NOT ACTIVATED", RegexOptions.Compiled);
        static readonly Regex Green = new Regex(@"\b(PASS|PASSED)\b|^\s*(OK|READY)\b", RegexOptions.Compiled);
        static readonly Regex Yellow = new Regex(@"^\s*(WARNING|SKIP|NOTE|TIMEOUT)\b|\bSKIP\b", RegexOptions.Compiled);

        /// <summary>The color of one line, or null (default color). Red wins over green.</summary>
        internal static ConsoleColor? ColorOf(string line)
        {
            if (string.IsNullOrEmpty(line)) return null;
            if (Red.IsMatch(line)) return ConsoleColor.Red;
            if (Green.IsMatch(line)) return ConsoleColor.Green;
            if (Yellow.IsMatch(line)) return ConsoleColor.Yellow;
            return null;
        }

        /// <summary>Colors stdout and stderr of this process when each is a console.</summary>
        internal static void Install()
        {
            if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("NO_COLOR"))) return;
            if (!Console.IsOutputRedirected) Console.SetOut(new ColorConsole(Console.Out));
            if (!Console.IsErrorRedirected) Console.SetError(new ColorConsole(Console.Error));
        }

        readonly TextWriter _inner;
        static readonly object Gate = new object();
        ColorConsole(TextWriter inner) { _inner = inner; }
        public override Encoding Encoding => _inner.Encoding;
        public override void Write(char value) => _inner.Write(value);
        public override void Write(string value) => _inner.Write(value);
        public override void Flush() => _inner.Flush();

        public override void WriteLine(string value)
        {
            ConsoleColor? color = ColorOf(value);
            if (color == null) { _inner.WriteLine(value); return; }
            lock (Gate)
            {
                ConsoleColor before = Console.ForegroundColor;
                Console.ForegroundColor = color.Value;
                _inner.WriteLine(value);
                _inner.Flush();
                Console.ForegroundColor = before;
            }
        }
    }
}
