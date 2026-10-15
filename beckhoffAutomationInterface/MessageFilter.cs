using System;
using System.Runtime.InteropServices;

namespace BeckhoffAutomationInterface
{
    /// <summary>
    /// An IOleMessageFilter that auto-retries COM calls rejected with
    /// SERVERCALL_RETRYLATER, instead of letting them surface as
    /// RPC_E_SERVERCALL_RETRYLATER (0x8001010A) exceptions. Visual Studio's DTE
    /// frequently rejects calls while its background compiler/UI is busy — which
    /// happens constantly when driving a large batch of PLC-object creates/edits.
    /// Registering this filter on the (STA) calling thread makes those calls wait
    /// and retry transparently. Standard pattern from Beckhoff/Microsoft DTE
    /// automation samples (example/.../ScriptingTestContainerBase/MessageFilter.cs).
    /// Requires the calling thread to be STA (see [STAThread] on Main).
    /// </summary>
    public class MessageFilter : IOleMessageFilter
    {
        public static void Register()
        {
            IOleMessageFilter newFilter = new MessageFilter();
            CoRegisterMessageFilter(newFilter, out _);
        }

        public static void Revoke()
        {
            CoRegisterMessageFilter(null, out _);
        }

        // SERVERCALL_ISHANDLED = 0
        int IOleMessageFilter.HandleInComingCall(int dwCallType, IntPtr hTaskCaller, int dwTickCount, IntPtr lpInterfaceInfo)
            => 0;

        /// <summary>How long one call may stay rejected before it is cancelled. Without a limit,
        /// a shell that stays busy (seen: stuck in Clean during activate) hangs the tool for ever,
        /// and no timeout above this filter can run.</summary>
        public const int RetryLimitMs = 10 * 60 * 1000;

        int IOleMessageFilter.RetryRejectedCall(IntPtr hTaskCallee, int dwTickCount, int dwRejectType)
            => RetryDelay(dwRejectType, dwTickCount, RetryLimitMs);

        /// <summary>The filter's answer (pure, unit-tested). dwRejectType 2 = SERVERCALL_RETRYLATER.
        /// A value in [0,100) retries after that many milliseconds (99 = almost at once);
        /// -1 cancels the call, which then fails with RPC_E_CALL_REJECTED (a COMException).
        /// elapsedMs = dwTickCount, the time since the call was first made.</summary>
        internal static int RetryDelay(int rejectType, int elapsedMs, int limitMs)
        {
            if (rejectType == 2 && elapsedMs < limitMs)
                return 99;
            return -1; // past the limit, or any other reject type
        }

        // PENDINGMSG_WAITDEFPROCESS = 2
        int IOleMessageFilter.MessagePending(IntPtr hTaskCallee, int dwTickCount, int dwPendingType)
            => 2;

        [DllImport("Ole32.dll")]
        private static extern int CoRegisterMessageFilter(IOleMessageFilter newFilter, out IOleMessageFilter oldFilter);
    }

    [ComImport, Guid("00000016-0000-0000-C000-000000000046"),
     InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IOleMessageFilter
    {
        [PreserveSig]
        int HandleInComingCall(int dwCallType, IntPtr hTaskCaller, int dwTickCount, IntPtr lpInterfaceInfo);

        [PreserveSig]
        int RetryRejectedCall(IntPtr hTaskCallee, int dwTickCount, int dwRejectType);

        [PreserveSig]
        int MessagePending(IntPtr hTaskCallee, int dwTickCount, int dwPendingType);
    }
}
