using System;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;

namespace BeckhoffAutomationInterface
{
    /// <summary>
    /// Background safety net that auto-dismisses known unattended-build-blocking dialogs
    /// TwinCAT/Visual Studio can pop up despite dte.SuppressUI (see VisualStudioSession) —
    /// extension-raised dialogs don't reliably respect that flag. Confirmed safe to
    /// auto-accept for CI use (live-tested against PLC_NFL_SHARK_V2 on the bench PC):
    ///
    ///   - "Target System Process Image Update" (AmsNetId mismatch after opening a
    ///     project on a different machine than it was last saved on) -> click "Yes".
    ///   - A missing-".tmc" file warning (expected on a fresh checkout — .tmc is a
    ///     gitignored, per-machine build-regenerated artifact, never committed) -> "OK".
    ///
    /// Matches on BOTH the dialog's title AND its body text before clicking anything, so
    /// it only ever fires for these two known cases — not just any dialog VS happens to
    /// title "Microsoft Visual Studio" (several of those are real errors worth seeing).
    /// Polls rather than hooking window-creation events: simplest thing that works, and
    /// the ~500ms latency is irrelevant against a multi-minute VS build.
    /// </summary>
    class DialogAutoDismisser : IDisposable
    {
        const uint BM_CLICK = 0x00F5;
        const int POLL_INTERVAL_MS = 500;

        static readonly (string TitleContains, string BodyContains, string ButtonText)[] KnownDialogs =
        {
            ("Target System Process Image Update", "AmsNetId", "Yes"),
            ("Microsoft Visual Studio", ".tmc", "OK"),
        };

        readonly int _devenvPid;
        readonly Thread _thread;
        volatile bool _stop;

        public DialogAutoDismisser(int devenvPid)
        {
            _devenvPid = devenvPid;
            _thread = new Thread(Poll) { IsBackground = true };
            _thread.Start();
        }

        void Poll()
        {
            while (!_stop)
            {
                try { ScanOnce(); }
                catch { /* best-effort watcher — a scan failure must never take down the build */ }
                Thread.Sleep(POLL_INTERVAL_MS);
            }
        }

        void ScanOnce()
        {
            EnumWindows((hWnd, _) =>
            {
                if (GetWindowThreadProcessId(hWnd, out int pid) == 0 || pid != _devenvPid)
                    return true; // keep enumerating

                string title = GetText(hWnd);
                if (string.IsNullOrEmpty(title))
                    return true;

                foreach ((string titleContains, string bodyContains, string buttonText) in KnownDialogs)
                {
                    if (title.IndexOf(titleContains, StringComparison.OrdinalIgnoreCase) < 0)
                        continue;
                    if (bodyContains != null && !DialogBodyContains(hWnd, bodyContains))
                        continue;

                    ClickButton(hWnd, buttonText);
                    break;
                }
                return true;
            }, IntPtr.Zero);
        }

        static bool DialogBodyContains(IntPtr dialogHandle, string text)
        {
            bool found = false;
            EnumChildWindows(dialogHandle, (hWndChild, _) =>
            {
                if (GetText(hWndChild).IndexOf(text, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    found = true;
                    return false; // stop enumerating, already confirmed
                }
                return true;
            }, IntPtr.Zero);
            return found;
        }

        static void ClickButton(IntPtr dialogHandle, string buttonText)
        {
            IntPtr button = IntPtr.Zero;
            EnumChildWindows(dialogHandle, (hWndChild, _) =>
            {
                if (string.Equals(GetText(hWndChild).Trim(), buttonText, StringComparison.OrdinalIgnoreCase))
                {
                    button = hWndChild;
                    return false; // stop enumerating, found it
                }
                return true;
            }, IntPtr.Zero);

            if (button != IntPtr.Zero)
            {
                Console.WriteLine("{0}: [auto-dismiss] Clicking '{1}' on '{2}' to keep the unattended build moving.",
                    Clock.Now(), buttonText, GetText(dialogHandle));
                SendMessage(button, BM_CLICK, IntPtr.Zero, IntPtr.Zero);
            }
        }

        static string GetText(IntPtr hWnd)
        {
            var sb = new StringBuilder(512);
            GetWindowText(hWnd, sb, sb.Capacity);
            return sb.ToString();
        }

        public void Dispose()
        {
            _stop = true;
            _thread.Join(2000);
        }

        delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

        [DllImport("user32.dll")]
        static extern bool EnumWindows(EnumWindowsProc enumProc, IntPtr lParam);

        [DllImport("user32.dll")]
        static extern bool EnumChildWindows(IntPtr hWndParent, EnumWindowsProc enumProc, IntPtr lParam);

        [DllImport("user32.dll")]
        static extern int GetWindowThreadProcessId(IntPtr hWnd, out int processId);

        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        static extern int GetWindowText(IntPtr hWnd, StringBuilder text, int count);

        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        static extern IntPtr SendMessage(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);
    }
}
