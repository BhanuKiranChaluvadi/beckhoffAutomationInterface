using System;
using System.Runtime.InteropServices;

namespace BeckhoffAutomationInterface
{
    /// <summary>
    /// An IOleMessageFilter that auto-retries COM calls rejected with
    /// SERVERCALL_RETRYLATER, instead of letting them surface as
    /// RPC_E_SERVERCALL_RETRYLATER (0x8001010A) exceptions. Visual Studio's DTE
    /// frequently rejects calls while its background compiler/UI is busy \u2014 which
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

        int IOleMessageFilter.RetryRejectedCall(IntPtr hTaskCallee, int dwTickCount, int dwRejectType)
        {
            // dwRejectType == 2 => SERVERCALL_RETRYLATER. Returning a value in [0,100)
            // retries the call after that many milliseconds (99 = retry almost immediately).
            if (dwRejectType == 2)
                return 99;
            return -1; // cancel the call for any other reject type
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
