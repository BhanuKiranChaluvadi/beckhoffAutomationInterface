using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Interop.TCatSysManager;

namespace BeckhoffAutomationInterface.Sync
{
    class VariableLinkReport
    {
        public List<string> Linked { get; } = new List<string>();
        public List<string> Failed { get; } = new List<string>();
        public bool AllLinked => Failed.Count == 0;
    }

    /// <summary>
    /// Reconciles PLC-variable-to-IO-channel links declared in io-devices.xml against
    /// the project, using ITcSysManager.LinkVariables. Path format confirmed against
    /// Beckhoff's official EtherCATLinking.cs sample:
    ///   PLC side: TIPC^&lt;Plc&gt;^&lt;Plc&gt; Instance^PlcTask Inputs^GVL_X.varName
    ///   IO  side: TIID^Device 1 (EtherCAT)^Term 1 (EK1100)^Term 2 (EL1008)^Channel 1^Input
    ///
    /// Two things make this work (validated 2026-07-05 against the live Shark project,
    /// with the master ENABLED and the build passing green, no popup):
    /// 1. Call ITcPlcProject.CompileProject() first (cast TIPC^&lt;Plc&gt;) so the PLC
    ///    instance I/O image (PlcTask Inputs/Outputs) is generated and the PLC-side
    ///    path resolves.
    /// 2. Use the *instance* path above, NOT the GVL declaration path
    ///    (TIPC^...^GVLs^GVL_X^var) that earlier attempts wrongly used.
    /// Note: a tree DUMP shows the instance and terminal nodes with ChildCount=0, but
    /// LinkVariables still resolves these paths by name — no Activate Configuration or
    /// runtime target is required. Linking is naturally idempotent (CompileProject
    /// regenerates the mappings, so the links re-apply cleanly on every run).
    ///
    /// If a link can't be resolved in some other environment, the caller falls back to
    /// disabling the master (IoSyncEngine.DisableAllMasters) so the build stays green.
    /// </summary>
    static class VariableLinkEngine
    {
        public static VariableLinkReport Sync(ITcSysManager sysManager, string plcName, IReadOnlyList<LinkSpec> links)
        {
            var report = new VariableLinkReport();
            if (links.Count == 0)
                return report;

            // Compile the PLC project first so the instance I/O image (PlcTask
            // Inputs/Outputs) is generated wherever the environment supports it.
            try
            {
                var plcProject = (ITcPlcProject)sysManager.LookupTreeItem("TIPC^" + plcName);
                plcProject.CompileProject();
            }
            catch (Exception ex)
            {
                Console.WriteLine("    (CompileProject before linking failed: {0})", ex.Message);
            }

            foreach (LinkSpec link in links)
            {
                string plcPath = "TIPC^" + plcName + "^" + link.PlcVar;
                string ioPath = "TIID^" + link.IoChannel;
                try
                {
                    sysManager.LinkVariables(plcPath, ioPath, 0, 0, 0);
                    report.Linked.Add($"{link.PlcVar}  <->  {link.IoChannel}");
                }
                catch (COMException ex)
                {
                    report.Failed.Add($"{link.PlcVar}  <->  {link.IoChannel}  ({ex.Message})");
                }
            }

            return report;
        }
    }
}
