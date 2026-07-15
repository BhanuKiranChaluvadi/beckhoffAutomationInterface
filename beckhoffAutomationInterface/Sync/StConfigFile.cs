using System;
using System.Collections.Generic;
using System.IO;

namespace BeckhoffAutomationInterface.Sync
{
    /// <summary>
    /// Simple "key=value" defaults file, so common flags (--source/--dest/--name/etc.)
    /// don't need retyping on every invocation. Discovered in the CURRENT WORKING
    /// DIRECTORY (not --source — --source itself can be one of the defaulted values, so
    /// looking it up via --source would be circular), named ".stconfig". Mirrors
    /// IgnoreRules.Load's own parsing style (one entry per line, "#" comments and blank
    /// lines skipped) for consistency with .stignore.
    ///
    /// Deliberately excludes: --ignore patterns (that's .stignore's job, not duplicated
    /// here) and the safety-gated flags --init/--confirm-delete/--confirm-delete-io,
    /// which RunOptions.Parse never even attempts to read from this file — seeing
    /// "key=value" support here does not mean every flag is safe to default. See
    /// RunOptions.Parse for the wiring and tasks/plan.md history for why.
    /// </summary>
    static class StConfigFile
    {
        /// <summary>Returns an empty (never null) case-insensitive dictionary if no
        /// ".stconfig" exists in `directory`.</summary>
        public static IReadOnlyDictionary<string, string> Load(string directory)
        {
            var config = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            string path = Path.Combine(directory, ".stconfig");
            if (!File.Exists(path))
                return config;

            foreach (string line in File.ReadAllLines(path))
            {
                string trimmed = line.Trim();
                if (trimmed.Length == 0 || trimmed.StartsWith("#"))
                    continue;

                int eq = trimmed.IndexOf('=');
                if (eq < 0)
                    continue; // silently skip malformed lines rather than fail the whole run

                string key = trimmed.Substring(0, eq).Trim();
                string value = trimmed.Substring(eq + 1).Trim();
                if (key.Length > 0)
                    config[key] = value;
            }

            return config;
        }

        /// <summary>Truthy check for a boolean-flavored config value: "true"/"1"/"yes"
        /// (case-insensitive) are true; a missing key or anything else is false.</summary>
        public static bool GetBool(this IReadOnlyDictionary<string, string> config, string key)
        {
            if (!config.TryGetValue(key, out string value))
                return false;
            return value.Equals("true", StringComparison.OrdinalIgnoreCase)
                || value.Equals("1", StringComparison.Ordinal)
                || value.Equals("yes", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>Null (not empty string) when the key is absent, so callers can chain
        /// with `?? otherDefault` the same way GetOption(args, ...) already does.</summary>
        public static string GetString(this IReadOnlyDictionary<string, string> config, string key) =>
            config.TryGetValue(key, out string value) ? value : null;
    }
}
