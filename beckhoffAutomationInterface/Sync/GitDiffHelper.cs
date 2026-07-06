using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;

namespace BeckhoffAutomationInterface.Sync
{
    /// <summary>Which .st files changed (created/modified/renamed) or were deleted between
    /// a given commit and HEAD, paths relative to the source folder passed to
    /// <see cref="GitDiffHelper.GetChangedStFiles"/>.</summary>
    class GitDiffResult
    {
        public List<string> Changed { get; } = new List<string>();
        public List<string> Deleted { get; } = new List<string>();
    }

    /// <summary>
    /// Computes which .st files changed/were deleted since a given commit, for the
    /// incremental sync mode (only re-sync what actually changed instead of every object).
    /// Read-only — just shells out to `git diff` and parses its output, no repo mutation.
    /// </summary>
    static class GitDiffHelper
    {
        /// <summary>
        /// <summary>Returns the current HEAD commit SHA for the repo containing folder, or
        /// null if folder isn't inside a git repository (or git itself isn't available) —
        /// callers should treat that as "can't establish a sync baseline here", not an error.</summary>
        public static string TryGetHeadSha(string folder)
        {
            try
            {
                return RunGit(folder, "rev-parse HEAD").Trim();
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// sourceFolder must be inside a git repository (it can be the repo root or any
        /// subdirectory). sinceSha is any commit-ish (SHA, tag, "HEAD~5", etc.) to diff
        /// against the current HEAD. Returned paths are relative to sourceFolder (uses
        /// `git diff --relative` so this works correctly even when sourceFolder is a
        /// subdirectory of a larger repo), forward-slash separated, filtered to *.st only.
        /// </summary>
        public static GitDiffResult GetChangedStFiles(string sourceFolder, string sinceSha)
        {
            string output = RunGit(sourceFolder, $"diff --name-status --relative \"{sinceSha}\" HEAD");

            var result = new GitDiffResult();
            foreach (string line in output.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
            {
                string[] parts = line.Split('\t');
                string status = parts[0];
                // Renames/copies: "R100\told\tnew" — the new path is what exists now.
                string path = parts[parts.Length - 1];

                if (!path.EndsWith(".st", StringComparison.OrdinalIgnoreCase))
                    continue;

                if (status.StartsWith("D", StringComparison.Ordinal))
                    result.Deleted.Add(path);
                else
                    result.Changed.Add(path);
            }
            return result;
        }

        static string RunGit(string workingDirectory, string arguments)
        {
            var psi = new ProcessStartInfo("git", arguments)
            {
                WorkingDirectory = workingDirectory,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };

            using (Process process = Process.Start(psi))
            {
                string stdout = process.StandardOutput.ReadToEnd();
                string stderr = process.StandardError.ReadToEnd();
                process.WaitForExit();

                if (process.ExitCode != 0)
                    throw new InvalidOperationException($"git {arguments} failed (exit {process.ExitCode}): {stderr.Trim()}");

                return stdout;
            }
        }
    }
}
