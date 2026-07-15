using System;

namespace BeckhoffAutomationInterface
{
    /// <summary>Shared timestamp formatting for console progress lines — was
    /// independently duplicated across Program.cs, TwinCatSession.cs,
    /// TwinCatProjectOpener.cs, and VisualStudioSession.cs.</summary>
    static class Clock
    {
        public static string Now() => DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
    }
}
