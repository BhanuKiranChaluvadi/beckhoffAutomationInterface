using System;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;

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
        const uint RPC_E_SERVERCALL_RETRYLATER = 0x8001010A;
        const int VS_LOAD_TIMEOUT_MS = 30000; // 30 seconds max wait for VS to load
        const int VS_LOAD_RETRY_INTERVAL_MS = 1000;
        const int VS_QUIT_TIMEOUT_MS = 30000; // 30 seconds for a graceful dte.Quit() before force-killing

        public EnvDTE80.DTE2 Dte { get; }

        readonly int _devenvPid;

        VisualStudioSession(EnvDTE80.DTE2 dte, int devenvPid)
        {
            Dte = dte;
            _devenvPid = devenvPid;
        }

        /// <summary>Creates and waits for a new Visual Studio DTE instance to finish loading.</summary>
        public static VisualStudioSession Start()
        {
            Console.WriteLine("{0}: Getting Visual Studio DTE type...", Now());
            Type dteType = Type.GetTypeFromProgID("VisualStudio.DTE.17.0");
            if (dteType == null)
            {
                Console.Error.WriteLine("ERROR: VisualStudio.DTE.17.0 is not registered.");
                Console.Error.WriteLine("Ensure Visual Studio 2022 (17.x) is installed.");
                Environment.Exit(1);
            }
            Console.WriteLine("{0}: Visual Studio DTE type resolved.", Now());

            Console.WriteLine("{0}: Creating the DTE instance...", Now());
            // Register a COM message filter so calls VS rejects while busy are auto-retried
            // (essential when driving a large batch of PLC-object edits, which otherwise
            // throws RPC_E_SERVERCALL_RETRYLATER). Requires the STA thread (see [STAThread]
            // on Program.Main).
            MessageFilter.Register();
            // Snapshot existing devenv PIDs so we can reliably identify the one WE spawn
            // (HWND-based capture is fragile once a modal dialog is up).
            var devenvBefore = new System.Collections.Generic.HashSet<int>(
                System.Diagnostics.Process.GetProcessesByName("devenv").Select(p => p.Id));
            EnvDTE80.DTE2 dte = (EnvDTE80.DTE2)Activator.CreateInstance(dteType);
            dte.SuppressUI = false;

            Console.WriteLine("{0}: Waiting for Visual Studio to finish loading...", Now());
            WaitForVsToLoad(dte);
            Console.WriteLine("{0}: Visual Studio is ready.", Now());

            // Identify our devenv process (the one that appeared after CreateInstance), so we
            // can force-kill it later if a graceful dte.Quit() leaves it alive (e.g. behind a
            // modal dialog). Fall back to the HWND method if the diff is inconclusive.
            int devenvPid = System.Diagnostics.Process.GetProcessesByName("devenv")
                .Select(p => p.Id)
                .FirstOrDefault(id => !devenvBefore.Contains(id));
            if (devenvPid == 0)
            {
                try { GetWindowThreadProcessId((IntPtr)dte.MainWindow.HWnd, out devenvPid); }
                catch { /* non-fatal: we just lose the force-kill fallback */ }
            }

            return new VisualStudioSession(dte, devenvPid);
        }

        /// <summary>Closes Visual Studio, force-killing it if a graceful Quit() doesn't
        /// complete in time (e.g. a modal dialog is blocking shutdown).</summary>
        public void Dispose()
        {
            Console.WriteLine("{0}: Closing Visual Studio...", Now());
            TryQuit(VS_QUIT_TIMEOUT_MS);
            EnsureExited();
            MessageFilter.Revoke();
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
                if (!p.WaitForExit(3000))
                {
                    p.Kill();
                    Console.WriteLine("{0}: Force-killed lingering devenv (pid {1}).", Now(), _devenvPid);
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
                catch (COMException ex) when ((uint)ex.HResult == RPC_E_SERVERCALL_RETRYLATER)
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

        static string Now() => DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
    }
}
