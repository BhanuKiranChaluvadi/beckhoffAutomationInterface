using System.IO;

namespace BeckhoffAutomationInterface.Sync
{
    /// <summary>
    /// Tracks the last-synced git commit SHA in a small text file at the source folder root
    /// (".st-sync-state"), so --incremental knows what commit to diff against. Written after
    /// every successful .st sync (full or incremental) when the source folder is inside a
    /// git repo; skipped silently otherwise (see GitDiffHelper.TryGetHeadSha).
    /// </summary>
    static class SyncState
    {
        public static string Read(string path)
        {
            return File.Exists(path) ? File.ReadAllText(path).Trim() : null;
        }

        public static void Write(string path, string commitSha)
        {
            File.WriteAllText(path, commitSha.Trim());
        }
    }
}
