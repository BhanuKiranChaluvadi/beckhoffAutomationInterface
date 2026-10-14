using System;
using System.IO;
using System.Linq;

namespace BeckhoffAutomationInterface
{
    /// <summary>
    /// A temporary copy of an adopted TwinCAT project folder (the folder that holds the .tsproj).
    /// TwinCAT saves the project by itself during a build that switches variants (TcProjectVariant,
    /// TmcHash) and during activation (TargetNetId, disabled devices, …) — measured 2026-10-14 —
    /// even when the tool never calls Save. Working on a copy is the only way to keep the
    /// adopted project's files byte-identical. Dispose deletes the copy.
    /// </summary>
    sealed class ProjectShadowCopy : IDisposable
    {
        // Build output and IDE state: regenerated, not worth copying.
        static readonly string[] SkippedFolders = { "_Boot", "_CompileInfo", ".vs" };

        public string OriginalDir { get; }
        public string ShadowDir { get; }
        public string TsprojPath { get; }
        readonly string _root;

        ProjectShadowCopy(string originalDir, string root, string shadowDir, string tsprojPath)
        {
            OriginalDir = originalDir;
            _root = root;
            ShadowDir = shadowDir;
            TsprojPath = tsprojPath;
        }

        public static ProjectShadowCopy Create(string tsprojPath, string tempRoot = null)
        {
            string originalDir = Path.GetDirectoryName(Path.GetFullPath(tsprojPath));
            string root = Path.Combine(tempRoot ?? Path.GetTempPath(), "bai_shadow_" + Guid.NewGuid().ToString("N").Substring(0, 12));
            string shadowDir = Path.Combine(root, Path.GetFileName(originalDir));
            CopyDirectory(originalDir, shadowDir);
            return new ProjectShadowCopy(originalDir, root, shadowDir, Path.Combine(shadowDir, Path.GetFileName(tsprojPath)));
        }

        /// <summary>Maps a path inside the copy back to the adopted project (other paths unchanged).</summary>
        public string ToOriginalPath(string path)
        {
            if (string.IsNullOrEmpty(path) || !path.StartsWith(ShadowDir, StringComparison.OrdinalIgnoreCase))
                return path;
            return OriginalDir + path.Substring(ShadowDir.Length);
        }

        public void Dispose()
        {
            try
            {
                Directory.Delete(_root, recursive: true);
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                // A just-closed shell can hold a file for a moment; a temp folder left behind is harmless.
                Console.WriteLine("{0}: Note: could not delete the shadow copy '{1}' ({2}).", Clock.Now(), _root, ex.Message);
            }
        }

        static void CopyDirectory(string source, string target)
        {
            Directory.CreateDirectory(target);
            foreach (string file in Directory.GetFiles(source))
                File.Copy(file, Path.Combine(target, Path.GetFileName(file)));
            foreach (string dir in Directory.GetDirectories(source))
            {
                string name = Path.GetFileName(dir);
                if (!SkippedFolders.Contains(name, StringComparer.OrdinalIgnoreCase))
                    CopyDirectory(dir, Path.Combine(target, name));
            }
        }
    }
}
