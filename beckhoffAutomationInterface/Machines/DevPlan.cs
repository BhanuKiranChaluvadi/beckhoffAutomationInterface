using System;
using System.Collections.Generic;
using System.Linq;

namespace BeckhoffAutomationInterface.Machines
{
    /// <summary>What 'machine dev' does after files changed.</summary>
    enum DevStep
    {
        /// <summary>No source, scenario or manifest changed.</summary>
        None,
        /// <summary>Only scenarios changed: run them again on the running PLC.</summary>
        TestsOnly,
        /// <summary>A .st file changed: sync the code, build, activate, verify, run the scenarios.</summary>
        Rebuild,
        /// <summary>A manifest changed (I/O, links, variants, libraries, tasks, safety): it needs 'machine test'.</summary>
        Stop,
    }

    static class DevPlan
    {
        /// <param name="changed">Changed paths, relative to the repository root.</param>
        /// <param name="sources">The family's source folders (bai.machines.json "sources").</param>
        /// <param name="tests">The family's scenario folder.</param>
        /// <param name="manifests">Folders or files whose change needs a full machine run.</param>
        public static DevStep For(IEnumerable<string> changed, IReadOnlyList<string> sources, string tests, IReadOnlyList<string> manifests)
        {
            DevStep step = DevStep.None;
            foreach (string raw in changed)
            {
                string path = raw.Replace('\\', '/').TrimStart('/');
                if (manifests.Any(m => Under(path, m)))
                    return DevStep.Stop;
                if (path.EndsWith(".st", StringComparison.OrdinalIgnoreCase) && sources.Any(s => Under(path, s)))
                    step = DevStep.Rebuild;
                else if (step == DevStep.None && path.EndsWith(".json", StringComparison.OrdinalIgnoreCase) && Under(path, tests))
                    step = DevStep.TestsOnly;
            }
            return step;
        }

        /// <summary>path is root itself or inside the folder root (not a sibling such as "App/NFLiteOld").</summary>
        static bool Under(string path, string root)
        {
            root = root.Replace('\\', '/').Trim('/');
            return path.Equals(root, StringComparison.OrdinalIgnoreCase)
                || path.StartsWith(root + "/", StringComparison.OrdinalIgnoreCase);
        }
    }
}
