using System.Collections.Generic;

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

    /// <summary>
    /// Triggers a solution build via the Visual Studio DTE and reports pass/fail
    /// using the DTE's error list. Validated in docs/ideas/st-source-twincat-sync.md:
    /// dte.ToolWindows.ErrorList.ErrorItems gives a reliable signal.
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

        public static BuildReport Build(EnvDTE80.DTE2 dte)
        {
            dte.Solution.SolutionBuild.Build(WaitForBuildToFinish: true);

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
