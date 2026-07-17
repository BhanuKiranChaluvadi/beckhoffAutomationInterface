using System;
using System.IO;
using System.Linq;

namespace BeckhoffAutomationInterface
{
    /// <summary>
    /// Resolves the single positional path argument accepted by the new subcommand CLI
    /// (e.g. `build &lt;path&gt;`) to a concrete .tsproj file — replacing the two separate
    /// project-targeting mechanisms the legacy flat-flag CLI required (--tsproj directly,
    /// or --source/--dest/--name plus the Dest\Name\Name.sln convention). See
    /// docs/ideas/cli-subcommand-redesign.md for the migration this is the first slice of.
    /// Read-only: never creates or modifies anything on disk.
    /// </summary>
    static class ProjectLocator
    {
        /// <summary>Resolves <paramref name="path"/> to a .tsproj file: used directly if
        /// it already IS one, else the folder is searched (recursively) for exactly one
        /// .tsproj. Throws with a clear message if the path doesn't exist, or a directory
        /// contains zero or more than one .tsproj (ambiguous — caller must point at the
        /// file directly instead).</summary>
        public static string ResolveTsprojPath(string path)
        {
            if (path.EndsWith(".tsproj", StringComparison.OrdinalIgnoreCase))
            {
                if (!File.Exists(path))
                    throw new FileNotFoundException($"tsproj not found: {path}", path);
                return path;
            }

            if (!Directory.Exists(path))
                throw new DirectoryNotFoundException($"path not found: {path}");

            string[] found = Directory.GetFiles(path, "*.tsproj", SearchOption.AllDirectories);
            if (found.Length == 0)
                throw new FileNotFoundException($"No .tsproj file found under: {path}");
            if (found.Length > 1)
            {
                throw new InvalidOperationException(
                    $"Multiple .tsproj files found under: {path}\n"
                    + string.Join("\n", found.Select(f => "  " + f))
                    + "\nPoint the path directly at the intended .tsproj file instead.");
            }

            return found[0];
        }

        /// <summary>The PLC project's real name inside TIPC, defaulting to the .tsproj
        /// file's own base name when no override is given — matches the legacy
        /// --tsproj/--plc-name pairing's existing default (RunOptions.PlcProjectName).</summary>
        public static string ResolvePlcName(string tsprojPath, string plcNameOverride)
        {
            if (!string.IsNullOrEmpty(plcNameOverride))
                return plcNameOverride;
            return Path.GetFileNameWithoutExtension(tsprojPath);
        }
    }
}
