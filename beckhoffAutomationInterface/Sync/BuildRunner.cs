using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading;

namespace BeckhoffAutomationInterface.Sync
{
    class BuildError
    {
        public string Level { get; set; }
        public string Description { get; set; }
        public string FileName { get; set; }
        public int Line { get; set; }
    }

    /// <summary>Pass/fail result of a solution build, with any reported compiler errors/warnings.</summary>
    class BuildReport
    {
        public bool Success => Errors.Count == 0;
        public List<BuildError> Errors { get; } = new List<BuildError>();
        public List<BuildError> Warnings { get; } = new List<BuildError>();
    }

    /// <summary>Thrown when a build does not finish within the allotted time \u2014 almost
    /// always because a modal Visual Studio dialog (e.g. TwinCAT's "needs sync
    /// master") is blocking the build and waiting for human input that will never
    /// come in an automated run.</summary>
    class BuildTimeoutException : Exception
    {
        public BuildTimeoutException(string message) : base(message) { }
    }

    /// <summary>
    /// Triggers a solution build via the Visual Studio DTE and reports pass/fail
    /// using the DTE's error list. Validated in docs/ideas/st-source-twincat-sync.md:
    /// dte.ToolWindows.ErrorList.ErrorItems gives a reliable signal.
    ///
    /// The build is kicked off ASYNCHRONOUSLY (Build(false)) and then polled to
    /// completion with a hard timeout, so a modal dialog can never hang the whole
    /// process indefinitely \u2014 if the build doesn't finish in time we raise a
    /// BuildTimeoutException instead of blocking forever.
    ///
    /// IMPORTANT: ErrorItems includes warnings and messages alongside real errors.
    /// EnvDTE.vsBuildErrorLevel is High=3/Medium=2/Low=1 (stable since VS2005); only
    /// High (an actual compiler error) should count toward build failure \u2014
    /// everything else (e.g. "return value ignored" on a method call used as a
    /// statement) is a warning and must not flip Success to false.
    /// </summary>
    static class BuildRunner
    {
        const int VsBuildErrorLevelHigh = 3;
        const int VsBuildStateDone = 3; // EnvDTE.vsBuildState.vsBuildStateDone
        const int DefaultBuildTimeoutMs = 5 * 60 * 1000; // 5 minutes
        const int PollIntervalMs = 2000;

        public static BuildReport Build(EnvDTE80.DTE2 dte, int timeoutMs = DefaultBuildTimeoutMs)
        {
            // Kick off the build asynchronously so our side never blocks on a modal
            // dialog; we drive completion ourselves via polling below.
            dte.Solution.SolutionBuild.Build(false);

            int elapsed = 0;
            bool done = false;
            while (elapsed < timeoutMs)
            {
                try
                {
                    if ((int)dte.Solution.SolutionBuild.BuildState == VsBuildStateDone)
                    {
                        done = true;
                        break;
                    }
                }
                catch (COMException)
                {
                    // VS is momentarily busy (often because a modal dialog is up);
                    // keep counting toward the timeout rather than failing hard.
                }

                Thread.Sleep(PollIntervalMs);
                elapsed += PollIntervalMs;
            }

            if (!done)
                throw new BuildTimeoutException(
                    string.Format("Build did not finish within {0} minute(s). A modal Visual Studio dialog " +
                        "(e.g. TwinCAT's 'needs sync master') is likely blocking it \u2014 an unlinked EtherCAT " +
                        "master requires at least one variable linked to a task variable.", timeoutMs / 60000));

            var report = new BuildReport();
            int itemCount = dte.ToolWindows.ErrorList.ErrorItems.Count;
            for (int i = 1; i <= itemCount; i++)
            {
                dynamic item = dte.ToolWindows.ErrorList.ErrorItems.Item(i);
                var buildError = new BuildError
                {
                    Level = item.ErrorLevel.ToString(),
                    Description = item.Description,
                    FileName = item.FileName,
                    Line = item.Line
                };

                if ((int)item.ErrorLevel == VsBuildErrorLevelHigh)
                    report.Errors.Add(buildError);
                else
                    report.Warnings.Add(buildError);
            }
            return report;
        }
    }
}
