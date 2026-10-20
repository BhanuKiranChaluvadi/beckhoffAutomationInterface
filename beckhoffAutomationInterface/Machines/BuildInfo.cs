using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using static BeckhoffAutomationInterface.Clock;

namespace BeckhoffAutomationInterface.Machines
{
    /// <summary>The build identity that goes into the build copy of GVL_BuildInfo.st.</summary>
    sealed class BuildIdentity
    {
        public string Tag;
        public string Commit;
        public bool Dirty;
        public string Machine;
        public string BuildTime; // yyyy-MM-ddTHH:mm:ssZ (UTC)
    }

    /// <summary>
    /// Writes the real build identity into the BUILD COPY of GVL_BuildInfo.st, never into the
    /// repository (same rules as the PLC repository's Tools/build_info.ps1):
    ///   sGitTag = git describe --tags --always, sGitCommit = git rev-parse HEAD,
    ///   bDirty = git status --porcelain reports a change in a build input, sMachine, sBuildTime (UTC).
    /// Only the values change. Strings are printable ASCII, cut to their STRING(n) length and
    /// IEC-escaped ($ and '). Without git, or when the source root is not the top of a git work
    /// tree, the placeholders stay and a warning is printed.
    /// </summary>
    static class BuildInfo
    {
        static readonly string[] Fields = { "sGitTag", "sGitCommit", "bDirty", "sMachine", "sBuildTime" };

        /// <summary>Runs git: (exit code, stdout lines). Exit code -1 when git cannot start.</summary>
        internal delegate (int Code, List<string> Lines) GitRunner(string root, params string[] args);

        /// <summary>Reads the identity from git; null (with a warning) when git cannot give it.</summary>
        public static BuildIdentity FromGit(string repoRoot, string machine, IReadOnlyList<string> buildInputs, GitRunner git, DateTime utcNow)
        {
            var top = git(repoRoot, "rev-parse", "--show-toplevel");
            if (top.Code != 0 || top.Lines.Count == 0 || Normalize(top.Lines[0]) != Normalize(repoRoot))
            {
                Warn(top.Code == -1 ? "git not found" : repoRoot + " is not the top of a git work tree");
                return null;
            }
            var tag = git(repoRoot, "describe", "--tags", "--always");
            var commit = git(repoRoot, "rev-parse", "HEAD");
            var status = git(repoRoot, new[] { "status", "--porcelain", "--" }.Concat(buildInputs).ToArray());
            if (tag.Code != 0 || commit.Code != 0 || status.Code != 0 || tag.Lines.Count == 0 || commit.Lines.Count == 0)
            {
                Warn("a git command failed");
                return null;
            }
            return new BuildIdentity
            {
                Tag = tag.Lines[0],
                Commit = commit.Lines[0],
                Dirty = status.Lines.Any(l => l.Trim().Length > 0),
                Machine = machine,
                BuildTime = utcNow.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture),
            };
        }

        /// <summary>Writes the identity into the file (ASCII). Returns false (file unchanged) when id is null.</summary>
        public static bool Write(string file, BuildIdentity id)
        {
            if (!File.Exists(file))
                throw new InvalidOperationException("GVL_BuildInfo.st not found in the build copy: " + file);
            if (id == null)
                return false;
            File.WriteAllText(file, Apply(File.ReadAllText(file), id), Encoding.ASCII);
            Console.WriteLine("{0}: Build info: tag {1}, commit {2}, dirty {3}, machine {4}, {5}", Now(), id.Tag, id.Commit, id.Dirty, id.Machine, id.BuildTime);
            return true;
        }

        /// <summary>The file text with the five values replaced (pure).</summary>
        internal static string Apply(string text, BuildIdentity id)
        {
            foreach (string name in Fields)
            {
                var rx = new Regex(@"(?m)^(?<head>[ \t]*" + name + @"[ \t]*:[ \t]*(?:STRING\((?<len>\d+)\)|BOOL)[ \t]*:=[ \t]*)[^;\r\n]*;");
                Match hit = rx.Match(text);
                if (!hit.Success)
                    throw new InvalidOperationException(string.Format("GVL_BuildInfo.st: no declaration '{0} : STRING(n) := ...;' or '{0} : BOOL := ...;'.", name));
                string literal;
                if (name == "bDirty")
                    literal = id.Dirty ? "TRUE" : "FALSE";
                else
                {
                    string value = name == "sGitTag" ? id.Tag : name == "sGitCommit" ? id.Commit : name == "sMachine" ? id.Machine : id.BuildTime;
                    string s = Regex.Replace(value ?? "", @"[^\x20-\x7E]", "?");
                    if (hit.Groups["len"].Success)
                    {
                        int len = int.Parse(hit.Groups["len"].Value, CultureInfo.InvariantCulture);
                        if (s.Length > len)
                            s = s.Substring(0, len);
                    }
                    literal = "'" + s.Replace("$", "$$").Replace("'", "$'") + "'";
                }
                text = text.Substring(0, hit.Index) + hit.Groups["head"].Value + literal + ";" + text.Substring(hit.Index + hit.Length);
            }
            return text;
        }

        /// <summary>True when the build copy carries this tag and bDirty FALSE (release check, pure).</summary>
        internal static bool ShowsCleanTag(string text, string tag) =>
            Regex.IsMatch(text, @"sGitTag\s*:\s*STRING\(\d+\)\s*:=\s*'" + Regex.Escape(tag) + "'") &&
            Regex.IsMatch(text, @"bDirty\s*:\s*BOOL\s*:=\s*FALSE");

        static string Normalize(string path) => Path.GetFullPath(path).TrimEnd('\\', '/').ToLowerInvariant();

        static void Warn(string why) =>
            Console.WriteLine("{0}: WARNING: {1} - GVL_BuildInfo keeps its placeholders (tag 'dev', dirty TRUE).", Now(), why);

        /// <summary>The real git runner. Never throws: -1 when git cannot start.</summary>
        public static (int Code, List<string> Lines) RunGit(string root, params string[] args)
        {
            var psi = new ProcessStartInfo("git")
            {
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
                StandardOutputEncoding = Encoding.UTF8,
            };
            psi.Arguments = "-C " + Quote(root) + " " + string.Join(" ", args.Select(Quote));
            try
            {
                using (Process p = Process.Start(psi))
                {
                    p.ErrorDataReceived += (s, e) => { };
                    p.BeginErrorReadLine();
                    string output = p.StandardOutput.ReadToEnd();
                    p.WaitForExit();
                    return (p.ExitCode, output.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries).ToList());
                }
            }
            catch (System.ComponentModel.Win32Exception)
            {
                return (-1, new List<string>());
            }
        }

        /// <summary>Windows command-line quoting of one argument.</summary>
        internal static string Quote(string arg)
        {
            if (arg.Length > 0 && arg.IndexOfAny(new[] { ' ', '\t', '"' }) < 0)
                return arg;
            var sb = new StringBuilder("\"");
            int backslashes = 0;
            foreach (char c in arg)
            {
                if (c == '\\') { backslashes++; continue; }
                if (c == '"') sb.Append('\\', backslashes * 2 + 1).Append('"');
                else sb.Append('\\', backslashes).Append(c);
                backslashes = 0;
            }
            return sb.Append('\\', backslashes * 2).Append('"').ToString();
        }
    }
}
