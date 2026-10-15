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
        /// <summary>Authoritative pass/fail from SolutionBuild.LastBuildInfo (number of failed
        /// projects == 0). TwinCAT PLC compile errors do NOT reliably arrive at ErrorLevel
        /// High in the DTE error list, so counting error items is not a trustworthy signal.</summary>
        public bool Succeeded { get; set; }
        public bool Success => Succeeded;
        public List<BuildError> Errors { get; } = new List<BuildError>();
        public List<BuildError> Warnings { get; } = new List<BuildError>();

        /// <summary>Texts of the PLC's message pragmas ({warning '...'}, C0373) from the build
        /// log — shows which conditional-compilation branch was compiled.</summary>
        public List<string> PragmaMessages { get; } = new List<string>();

        /// <summary>A failed build with no error: the last lines of the Build output pane. The
        /// cause is then outside the PLC code (I/O, task or safety configuration).</summary>
        public List<string> OutputTail { get; } = new List<string>();
    }

    /// <summary>Thrown when a build does not finish within the allotted time — almost
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
    /// process indefinitely — if the build doesn't finish in time we raise a
    /// BuildTimeoutException instead of blocking forever.
    ///
    /// IMPORTANT: ErrorItems includes warnings and messages alongside real errors.
    /// EnvDTE.vsBuildErrorLevel is High=3/Medium=2/Low=1 (stable since VS2005); only
    /// High (an actual compiler error) should count toward build failure —
    /// everything else (e.g. "return value ignored" on a method call used as a
    /// statement) is a warning and must not flip Success to false.
    /// </summary>
    static class BuildRunner
    {
        const int VsBuildErrorLevelHigh = 3;
        const int VsBuildStateDone = 3; // EnvDTE.vsBuildState.vsBuildStateDone
        const int DefaultBuildTimeoutMs = 5 * 60 * 1000; // 5 minutes
        const int PollIntervalMs = 2000;

        /// <summary>Watchdog limit for one whole Build() call (clean + build, 5 min each, plus
        /// margin). Past it the caller's VisualStudioSession.Watchdog closes the shell.</summary>
        public static readonly TimeSpan HangLimit = TimeSpan.FromMinutes(15);

        /// <summary>True for the COM errors of a shell that is gone (closed by the watchdog,
        /// or crashed): waiting longer cannot help (pure, unit-tested).</summary>
        internal static bool IsShellGone(int hresult)
        {
            switch ((uint)hresult)
            {
                case 0x80010007: // RPC_E_SERVER_DIED
                case 0x80010012: // RPC_E_SERVER_DIED_DNE
                case 0x80010108: // RPC_E_DISCONNECTED
                case 0x800706BA: // RPC_S_SERVER_UNAVAILABLE
                case 0x800706BE: // RPC_S_CALL_FAILED
                    return true;
                default:
                    return false;
            }
        }

        public static BuildReport Build(EnvDTE80.DTE2 dte, bool clean = false, int timeoutMs = DefaultBuildTimeoutMs)
        {
            if (clean)
            {
                // Same asynchronous start + polled wait as the build, for the same reason.
                Console.WriteLine("{0}: Cleaning solution...", Clock.Now());
                Start(() => dte.Solution.SolutionBuild.Clean(false), "Clean");
                WaitUntilDone(dte, timeoutMs, "Clean");
            }

            // Kick off the build asynchronously so our side never blocks on a modal
            // dialog; we drive completion ourselves via polling below.
            Console.WriteLine("{0}: Building solution...", Clock.Now());
            Start(() => dte.Solution.SolutionBuild.Build(false), "Build");
            WaitUntilDone(dte, timeoutMs, "Build");

            var report = new BuildReport();
            // Authoritative pass/fail: number of projects that failed to build. TwinCAT PLC
            // compile errors are NOT reliably flagged at ErrorLevel High in the error list,
            // so we must not infer success from "0 high-level error items".
            report.Succeeded = dte.Solution.SolutionBuild.LastBuildInfo == 0;

            EnvDTE80.ErrorList errorList = dte.ToolWindows.ErrorList;
            EnvDTE80.ErrorItems errorItems = errorList.ErrorItems;
            int itemCount = errorItems.Count;
            for (int i = 1; i <= itemCount; i++)
            {
                // Use the strongly-typed EnvDTE80.ErrorItem rather than `dynamic`: the raw
                // COM object is a __ComObject that C# dynamic can't late-bind ("does not
                // contain a definition for 'ErrorLevel'"), which only surfaces once there is
                // at least one error/warning to enumerate.
                EnvDTE80.ErrorItem item = errorItems.Item(i);
                var buildError = new BuildError
                {
                    Level = item.ErrorLevel.ToString(),
                    Description = item.Description,
                    FileName = item.FileName,
                    Line = item.Line
                };

                // When the build failed, surface every message as an error to fix (their
                // ErrorLevel is unreliable); when it passed, keep the level-based split.
                if (!report.Succeeded || (int)item.ErrorLevel == VsBuildErrorLevelHigh)
                    report.Errors.Add(buildError);
                else
                    report.Warnings.Add(buildError);
            }

            string paneText = ReadBuildOutputPane(dte);
            report.PragmaMessages.AddRange(BuildOutputParser.ParsePragmaMessages(paneText));

            if (ShouldUseOutputPane(report))
            {
                List<BuildError> fromPane = BuildOutputParser.ParseErrors(paneText);
                if (fromPane.Count > 0)
                {
                    report.Errors.Clear();
                    report.Errors.AddRange(fromPane);
                }
            }
            if (!report.Succeeded && report.Errors.All(e => string.IsNullOrWhiteSpace(e.Description)))
                report.OutputTail.AddRange(TailLines(paneText, 30));
            return report;
        }

        /// <summary>The last non-empty lines of a text (empty for null).</summary>
        internal static List<string> TailLines(string text, int count)
        {
            List<string> lines = (text ?? "").Split('\n').Select(l => l.TrimEnd('\r')).Where(l => l.Trim().Length > 0).ToList();
            return lines.Skip(Math.Max(0, lines.Count - count)).ToList();
        }

        /// <summary>A failed build whose error list has no usable text (empty, or only
        /// blank descriptions — seen when the XAE window is hidden) falls back to the
        /// "Build" output pane, which always holds the compiler messages.</summary>
        internal static bool ShouldUseOutputPane(BuildReport report) =>
            !report.Succeeded && report.Errors.All(e => string.IsNullOrWhiteSpace(e.Description));

        static string ReadBuildOutputPane(EnvDTE80.DTE2 dte)
        {
            try
            {
                EnvDTE.OutputWindowPane pane = dte.ToolWindows.OutputWindow.OutputWindowPanes.Item("Build");
                EnvDTE.TextDocument doc = pane.TextDocument;
                return doc.StartPoint.CreateEditPoint().GetText(doc.EndPoint);
            }
            catch (Exception ex) when (ex is COMException || ex is ArgumentException)
            {
                return null; // no pane, or not readable (hidden window): keep the error list result
            }
        }

        /// <summary>Polls SolutionBuild.BuildState until the running clean/build is done, so
        /// a modal dialog can never hang the process: past timeoutMs it throws.</summary>
        /// <summary>Starts a clean or build. When the shell rejects the call for longer than
        /// MessageFilter.RetryLimitMs (it stays busy), this is a timeout, not a crash.</summary>
        static void Start(Action kickOff, string step)
        {
            try
            {
                kickOff();
            }
            catch (COMException ex) when (IsShellGone(ex.HResult))
            {
                throw new BuildTimeoutException(string.Format(
                    "{0} did not finish: the shell hung and was closed ({1}).", step, ex.Message));
            }
            catch (COMException ex)
            {
                throw new BuildTimeoutException(string.Format(
                    "{0} could not start: the shell stayed busy for {1} minute(s) and rejected the call ({2}).",
                    step, MessageFilter.RetryLimitMs / 60000, ex.Message));
            }
        }

        static void WaitUntilDone(EnvDTE80.DTE2 dte, int timeoutMs, string step)
        {
            // Real time, not the sum of the sleeps: one rejected call can take up to
            // MessageFilter.RetryLimitMs before it fails.
            var clock = System.Diagnostics.Stopwatch.StartNew();
            while (clock.ElapsedMilliseconds < timeoutMs)
            {
                try
                {
                    if ((int)dte.Solution.SolutionBuild.BuildState == VsBuildStateDone)
                        return;
                }
                catch (COMException ex) when (IsShellGone(ex.HResult))
                {
                    throw new BuildTimeoutException(string.Format(
                        "{0} did not finish: the shell hung and was closed ({1}).", step, ex.Message));
                }
                catch (COMException)
                {
                    // VS is momentarily busy (often because a modal dialog is up);
                    // keep counting toward the timeout rather than failing hard.
                }

                Thread.Sleep(PollIntervalMs);
            }

            throw new BuildTimeoutException(
                string.Format("{0} did not finish within {1} minute(s). A modal Visual Studio dialog " +
                    "(e.g. TwinCAT's 'needs sync master') is likely blocking it — an unlinked EtherCAT " +
                    "master requires at least one variable linked to a task variable.", step, timeoutMs / 60000));
        }
    }
}
