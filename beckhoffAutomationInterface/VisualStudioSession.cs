using System;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using Interop.TCatSysManager;
using static BeckhoffAutomationInterface.Clock;

namespace BeckhoffAutomationInterface
{
    /// <summary>
    /// Owns the lifetime of one Visual Studio DTE instance used to drive the TwinCAT
    /// Automation Interface: creation, waiting for it to finish loading, and shutdown
    /// (always, even on failure — otherwise every run leaks a devenv.exe process, which
    /// eventually causes COM calls to fail with RPC_E_SERVERCALL_RETRYLATER as instances
    /// pile up). Extracted from Program so the "how do we get/release a DTE" concern is
    /// separate from "what do we sync".
    /// </summary>
    class VisualStudioSession : IDisposable
    {
        const int VS_LOAD_TIMEOUT_MS = 30000; // 30 seconds max wait for VS to load
        const int VS_LOAD_RETRY_INTERVAL_MS = 1000;
        const int VS_QUIT_TIMEOUT_MS = 30000; // 30 seconds for a graceful dte.Quit() before force-killing

        public EnvDTE80.DTE2 Dte { get; }
        public DteShell Shell { get; }

        readonly int _devenvPid;

        // TwinCAT silent mode is a persistent XAE setting: remember the user's value and put
        // it back on Dispose, so their interactive XAE still asks its questions.
        ITcAutomationSettings _automationSettings;
        bool _previousSilentMode;
        readonly DialogAutoDismisser _dialogAutoDismisser;

        VisualStudioSession(EnvDTE80.DTE2 dte, DteShell shell, int devenvPid)
        {
            Dte = dte;
            Shell = shell;
            _devenvPid = devenvPid;
            // Only meaningful once we actually have a devenv PID to watch — falls back to
            // relying on SuppressUI alone in the rare case that lookup failed (see Start()).
            if (devenvPid > 0)
                _dialogAutoDismisser = new DialogAutoDismisser(devenvPid);
        }

        /// <summary>Creates and waits for a new DTE instance to finish loading.
        /// shellName: "xae", "vs2022", or null for the first usable one (see DteShellResolver).</summary>
        public static VisualStudioSession Start(string shellName)
        {
            DteShell shell;
            try
            {
                shell = DteShellResolver.Resolve(shellName, new WindowsComRegistry(), System.IO.File.Exists);
            }
            catch (InvalidOperationException ex)
            {
                Console.Error.WriteLine("ERROR: " + ex.Message);
                Environment.Exit(1);
                return null; // unreachable
            }
            Console.WriteLine("{0}: Using shell '{1}' ({2}): {3}", Now(), shell.Name, shell.ProgId, shell.ExePath);
            Type dteType = Type.GetTypeFromProgID(shell.ProgId);

            Console.WriteLine("{0}: Creating the DTE instance...", Now());
            // Register a COM message filter so calls VS rejects while busy are auto-retried
            // (essential when driving a large batch of PLC-object edits, which otherwise
            // throws RPC_E_SERVERCALL_RETRYLATER). Requires the STA thread (see [STAThread]
            // on Program.Main).
            MessageFilter.Register();
            // Snapshot existing shell PIDs (devenv or TcXaeShell, per the resolved shell) so we
            // can reliably identify the one WE spawn (HWND-based capture is fragile once a
            // modal dialog is up).
            var devenvBefore = new System.Collections.Generic.HashSet<int>(
                System.Diagnostics.Process.GetProcessesByName(shell.ProcessName).Select(p => p.Id));
            EnvDTE80.DTE2 dte = (EnvDTE80.DTE2)Activator.CreateInstance(dteType);
            // Unattended automation (CI) must never block on a modal dialog with no one
            // to click it — SuppressUI tells VS to auto-answer automation-triggered
            // prompts with their default instead of popping a dialog. Previously false
            // with no recorded rationale (likely an unconsidered scaffolding default);
            // flipped after live CI runs hit both a "Target System Process Image Update"
            // AmsNetId-mismatch prompt and a missing-.tmc dialog hanging the job.
            dte.SuppressUI = true;

            Console.WriteLine("{0}: Waiting for Visual Studio to finish loading...", Now());
            WaitForVsToLoad(dte);
            Console.WriteLine("{0}: Visual Studio is ready.", Now());

            // SuppressUI covers Visual Studio's own prompts; TwinCAT's (e.g. "Target System
            // Process Image Update", Yes/No) need TwinCAT's silent mode. An unattended run
            // must never wait for a click.
            ITcAutomationSettings automationSettings = EnableSilentMode(dte, out bool previousSilentMode);

            // Identify our shell process, so we can force-kill it later if a graceful dte.Quit()
            // leaves it alive (e.g. behind a modal dialog) and the watchdog can close it. The
            // window handle of OUR DTE is exact. The process-list diff is not: with parallel
            // runs (build_nflite_machine.ps1 -Parallel) several shells start at once, and every
            // run picked the same new pid - one run then killed another run's shell (2026-10-15).
            int hwndPid = 0;
            try { GetWindowThreadProcessId((IntPtr)dte.MainWindow.HWnd, out hwndPid); }
            catch { /* no window yet: use the process list below */ }
            var newPids = System.Diagnostics.Process.GetProcessesByName(shell.ProcessName)
                .Select(p => p.Id)
                .Where(id => !devenvBefore.Contains(id))
                .ToList();
            int devenvPid = ChooseShellPid(hwndPid, newPids, out string pidSource);
            Console.WriteLine("{0}: Shell process: {1} (pid {2}, found by {3}).", Now(), shell.ProcessName, devenvPid, pidSource);

            var session = new VisualStudioSession(dte, shell, devenvPid);
            session._automationSettings = automationSettings;
            session._previousSilentMode = previousSilentMode;
            return session;
        }

        /// <summary>Closes Visual Studio, force-killing it if a graceful Quit() doesn't
        /// complete in time (e.g. a modal dialog is blocking shutdown). The dialog watcher
        /// stays running through Quit/force-kill (it could even help an otherwise-stuck
        /// Quit() by clearing a blocking dialog first) and is only stopped once devenv is
        /// confirmed gone — leaving it running indefinitely would otherwise leak the
        /// background thread for the rest of the process's life.</summary>
        /// <summary>Turns TwinCAT silent mode on. Returns null (and warns) if the TwinCAT
        /// automation settings object is not available — the run continues, with
        /// SuppressUI and DialogAutoDismisser as the remaining safety nets.</summary>
        static ITcAutomationSettings EnableSilentMode(EnvDTE80.DTE2 dte, out bool previous)
        {
            previous = false;
            try
            {
                var settings = (ITcAutomationSettings)dte.GetObject("TcAutomationSettings");
                previous = settings.SilentMode;
                settings.SilentMode = true;
                Console.WriteLine("{0}: TwinCAT silent mode on (was: {1}).", Now(), previous ? "on" : "off");
                return settings;
            }
            catch (Exception ex) when (ex is COMException || ex is InvalidCastException)
            {
                Console.WriteLine("{0}: WARNING: could not enable TwinCAT silent mode ({1}); TwinCAT dialogs may appear.", Now(), ex.Message);
                return null;
            }
        }

        void RestoreSilentMode()
        {
            if (_automationSettings == null || _previousSilentMode)
                return;
            try
            {
                _automationSettings.SilentMode = false;
                Console.WriteLine("{0}: TwinCAT silent mode restored to off.", Now());
            }
            catch (COMException)
            {
                Console.WriteLine("{0}: WARNING: could not restore TwinCAT silent mode to off.", Now());
            }
            _automationSettings = null;
        }

        /// <summary>Which shell process is ours (pure, unit-tested). The window handle of our own
        /// DTE wins. Without it, the process-list diff is used only when exactly one new shell
        /// appeared. Otherwise 0: unknown, and then nothing is ever force-killed - killing a
        /// guess could close another run's shell or the user's XAE.</summary>
        internal static int ChooseShellPid(int hwndPid, System.Collections.Generic.IReadOnlyList<int> newPids, out string source)
        {
            if (hwndPid > 0)
            {
                source = "window handle";
                return hwndPid;
            }
            if (newPids.Count == 1)
            {
                source = "process list";
                return newPids[0];
            }
            source = newPids.Count == 0 ? "nothing: no new shell process"
                                        : string.Format("nothing: {0} new shell processes, not sure which", newPids.Count);
            return 0;
        }

        /// <summary>Watchdog for one step (dispose it when the step ends). If the step still runs
        /// after the limit, the shell THIS session started is closed: a COM call that the shell
        /// accepted but never finishes (seen 2026-10-15: Clean deadlocked with the background
        /// PLC precompile) cannot time out by itself, and the message filter only sees rejected
        /// calls. The blocked call then fails with a COM error and the tool exits.</summary>
        public IDisposable Watchdog(string step, TimeSpan limit)
        {
            if (_devenvPid <= 0)
                return new Timer(_ => { }); // no pid: nothing to close (rare, see Start)
            int pid = _devenvPid;
            string processName = Shell.ProcessName;
            return new Timer(_ =>
            {
                Console.WriteLine("{0}: TIMEOUT — {1} did not finish in {2} minute(s); closing the {3} this tool started (pid {4}).",
                    Now(), step, (int)limit.TotalMinutes, processName, pid);
                try { System.Diagnostics.Process.GetProcessById(pid).Kill(); }
                catch { /* already gone */ }
            }, null, limit, Timeout.InfiniteTimeSpan);
        }

        public void Dispose()
        {
            // A target-fatal-error dialog can block this property setter before
            // TryQuit's timeout is reached. Bound the restoration too; the watchdog
            // only closes the shell whose process this session identified.
            using (Watchdog("restoring TwinCAT silent mode", TimeSpan.FromMilliseconds(VS_QUIT_TIMEOUT_MS)))
                RestoreSilentMode();
            Console.WriteLine("{0}: Closing Visual Studio...", Now());
            TryQuit(VS_QUIT_TIMEOUT_MS);
            EnsureExited();
            MessageFilter.Revoke();
            _dialogAutoDismisser?.Dispose();
        }

        /// <summary>Runs dte.Quit() on a background thread and waits up to timeoutMs for it
        /// to finish. Returns false if it didn't complete in time.</summary>
        bool TryQuit(int timeoutMs)
        {
            var quitThread = new Thread(() => { try { Dte.Quit(); } catch { /* ignore */ } })
            {
                IsBackground = true
            };
            quitThread.Start();
            return quitThread.Join(timeoutMs);
        }

        /// <summary>Verifies the devenv process actually exited after a graceful Quit; if it's
        /// still alive after a short grace period, force-kills it so no devenv.exe is leaked.</summary>
        void EnsureExited()
        {
            if (_devenvPid <= 0) return;
            try
            {
                var p = System.Diagnostics.Process.GetProcessById(_devenvPid);
                if (!p.WaitForExit(10000)) // TcXaeShell often needs >3 s to exit after Quit()
                {
                    p.Kill();
                    Console.WriteLine("{0}: Force-killed lingering {1} (pid {2}).", Now(), Shell.ProcessName, _devenvPid);
                }
            }
            catch { /* already gone — the happy path */ }
        }

        /// <summary>
        /// Waits for VS to finish loading by retrying a COM call until it succeeds
        /// or the timeout is reached. Handles RPC_E_SERVERCALL_RETRYLATER (0x8001010A)
        /// which VS raises while its message pump is busy initializing.
        /// </summary>
        static void WaitForVsToLoad(EnvDTE80.DTE2 dte)
        {
            int elapsed = 0;
            while (elapsed < VS_LOAD_TIMEOUT_MS)
            {
                try
                {
                    // Accessing MainWindow forces a COM round-trip; if VS is busy it throws
                    dte.MainWindow.Visible = true;
                    return; // success
                }
                catch (COMException ex) when ((uint)ex.HResult == ComInterop.ServerCallRetryLater)
                {
                    Console.WriteLine("{0}: Visual Studio is loading, retrying in 1s... ({1}s elapsed)",
                        Now(), elapsed / 1000);
                    Thread.Sleep(VS_LOAD_RETRY_INTERVAL_MS);
                    elapsed += VS_LOAD_RETRY_INTERVAL_MS;
                }
            }
            throw new TimeoutException("Visual Studio did not finish loading within the timeout period.");
        }

        [DllImport("user32.dll")]
        static extern int GetWindowThreadProcessId(IntPtr hWnd, out int processId);
    }
}
