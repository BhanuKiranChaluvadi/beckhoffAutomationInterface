using System;
using Interop.TCatSysManager;

namespace BeckhoffAutomationInterface
{
    /// <summary>
    /// Mutable holder for the Visual Studio session and the project handles the
    /// pipeline stages share. Stages call EnsureOpen()/EnsureClosed() instead of
    /// owning VisualStudioSession directly, because the pipeline legitimately
    /// closes and reopens Visual Studio mid-run (the .tsproj file edits must
    /// happen with no DTE holding the file — see TsprojPlcDataTypeEditor /
    /// TsprojEventClassEditor), and some stage combinations never need VS at all
    /// (e.g. --sync-events alone). Replaces the old `ref VisualStudioSession`
    /// threading through Program.RunSync.
    /// </summary>
    class TwinCatSession : IDisposable
    {
        readonly RunOptions _options;
        bool _solutionSavedAs;

        public VisualStudioSession Vs { get; private set; }
        public EnvDTE.Project Project { get; private set; }
        public ITcSysManager SysManager { get; private set; }

        public bool IsOpen => Vs != null;
        public EnvDTE80.DTE2 Dte => Vs.Dte;

        public TwinCatSession(RunOptions options)
        {
            _options = options;
        }

        /// <summary>Starts Visual Studio and opens (or, with --init, bootstraps) the
        /// project, if not already open. The first open of a run also saves the
        /// solution to its canonical path (same behavior the monolithic pipeline had).</summary>
        public void EnsureOpen()
        {
            if (IsOpen)
                return;

            Vs = VisualStudioSession.Start();
            (EnvDTE.Project project, ITcSysManager sysManager) = TwinCatProjectOpener.Open(Vs.Dte, _options);
            Project = project;
            SysManager = sysManager;

            if (!_solutionSavedAs)
            {
                Project.Save();
                Vs.Dte.Solution.SaveAs(_options.SolutionFilePath);
                Console.WriteLine("{0}: Solution saved.", Now());
                _solutionSavedAs = true;
            }
        }

        /// <summary>Closes Visual Studio (releasing the .tsproj for direct file edits).
        /// Safe to call when already closed.</summary>
        public void EnsureClosed()
        {
            if (!IsOpen)
                return;
            Vs.Dispose();
            Vs = null;
            Project = null;
            SysManager = null;
        }

        public void Dispose() => EnsureClosed();

        static string Now() => DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
    }
}
