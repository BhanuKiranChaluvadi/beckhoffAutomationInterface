namespace BeckhoffAutomationInterface.Sync
{
    /// <summary>
    /// One desired PLC-variable-to-IO-channel link, parsed from the &lt;Links&gt;
    /// section of io-devices.xml. Paths are stored WITHOUT the "TIPC^"/"TIID^"
    /// roots (the engine prepends them), so the manifest reads cleanly, e.g.:
    ///   PlcVar   = "Shark Instance^PlcTask Inputs^GVL_Shark.bMotorRunSensor"
    ///   IoChannel= "Device 1 (EtherCAT)^Term 1 (EK1100)^Term 2 (EL1008)^Channel 1^Input"
    /// Format confirmed against Beckhoff's official EtherCATLinking.cs sample
    /// (example/.../Scripting.CSharp.Scripts/Scripts/EtherCATLinking.cs).
    /// </summary>
    class LinkSpec
    {
        public string PlcVar { get; }
        public string IoChannel { get; }

        public LinkSpec(string plcVar, string ioChannel)
        {
            PlcVar = plcVar;
            IoChannel = ioChannel;
        }
    }
}
