using System;
using System.Runtime.InteropServices;
using System.Threading;
using static BeckhoffAutomationInterface.Clock;

namespace BeckhoffAutomationInterface
{
    /// <summary>Well-known COM HRESULTs the tool retries around when driving Visual
    /// Studio via the Automation Interface, and the shared retry policy for sync edits.</summary>
    static class ComInterop
    {
        /// <summary>RPC_E_SERVERCALL_RETRYLATER — VS raises this while its message pump
        /// is busy (e.g. mid-load, or mid a large batch of tree edits).</summary>
        public const uint ServerCallRetryLater = 0x8001010A;
        public static void RetryOnBusy(Action action, string description)
        {
            int elapsed = 0;
            while (true)
            {
                try
                {
                    action();
                    return;
                }
                catch (COMException ex) when ((uint)ex.HResult == ComInterop.ServerCallRetryLater && elapsed < 30000)
                {
                    Console.WriteLine("{0}: Visual Studio is busy ({1}), retrying in 1s... ({2}s elapsed)",
                        Now(), description, elapsed / 1000);
                    Thread.Sleep(1000);
                    elapsed += 1000;
                }
            }
        }
    }
}
