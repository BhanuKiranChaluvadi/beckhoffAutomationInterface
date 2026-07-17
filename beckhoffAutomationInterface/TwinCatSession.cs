using System;
using Interop.TCatSysManager;
using static BeckhoffAutomationInterface.Clock;

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
        /// solution to its canonical path (same behavior the monolithic pipeline had) —
        /// EXCEPT when adopting an arbitrary pre-existing project (reverse export, see
        /// RunOptions.IsReverseExport, OR a plain --tsproj given for --build/CI use),
        /// which ALWAYS attaches read-only via OpenExistingReadOnly and NEVER
        /// saves/creates a solution file, regardless of whether a .sln already happens
        /// to exist for that project. Adopting this way is read-only against a real
        /// project by design (it may be someone's live production PLC project, and may
        /// have no .sln at all) — this must hold even when the conventional
        /// dest/name/.sln path resolves to a project that DOES already have a matching
        /// solution.</summary>
        public void EnsureOpen()
        {
            if (IsOpen)
                return;

            Vs = VisualStudioSession.Start();

            if (_options.IsReverseExport || _options.ExistingTsprojPath != null)
            {
                // TsprojFilePath resolves to ExistingTsprojPath (--tsproj) whenever it's
                // given, else the conventional dest/name-derived path — either way, this
                // is the ONE read-only, no-Save/SaveAs attach path. Covers both reverse
                // export AND a plain --tsproj + --build (CI compiling an arbitrary
                // pre-existing project with no .sln, e.g. one hosted natively on GitHub
                // rather than bootstrapped by this tool).
                (Project, SysManager) = TwinCatProjectOpener.OpenExistingReadOnly(Vs.Dte, _options.TsprojFilePath);
                _solutionSavedAs = true; // never save — this project is read-only, adopted as-is
                return;
            }

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
    }
}
