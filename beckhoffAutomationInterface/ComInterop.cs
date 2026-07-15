namespace BeckhoffAutomationInterface
{
    /// <summary>Well-known COM HRESULTs the tool retries around when driving Visual
    /// Studio via the Automation Interface. Timeout/interval policy stays local to each
    /// retry loop (Program.RetryOnBusy vs VisualStudioSession.WaitForVsToLoad have
    /// different give-up behavior) — only the HRESULT constant itself was duplicated.</summary>
    static class ComInterop
    {
        /// <summary>RPC_E_SERVERCALL_RETRYLATER — VS raises this while its message pump
        /// is busy (e.g. mid-load, or mid a large batch of tree edits).</summary>
        public const uint ServerCallRetryLater = 0x8001010A;
    }
}
