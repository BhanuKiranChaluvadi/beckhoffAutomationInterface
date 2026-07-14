using System.Collections.Generic;

namespace BeckhoffAutomationInterface.Sync
{
    /// <summary>
    /// Desired-state model for one EtherCAT master device and its bus topology,
    /// parsed from an io-devices.xml manifest. Mirrors the real hardware tree:
    /// Device (master) -> Box (e.g. a CU2508 junction or EK1100 coupler) -> Box/
    /// Terminal, nested arbitrarily deep (e.g. Device -> CU2508 -> EK1100 -> EL2008).
    /// </summary>
    class IoDeviceSpec
    {
        public string Name { get; }
        public bool Disabled { get; }
        public List<IoNodeSpec> Children { get; }

        public IoDeviceSpec(string name, bool disabled, List<IoNodeSpec> children)
        {
            Name = name;
            Disabled = disabled;
            Children = children;
        }
    }

    /// <summary>
    /// One Box or Terminal in the bus topology. TwinCAT creates both identically
    /// (CreateChild(name, TREEITEMTYPE_TERM=6, "", product) — see IoSyncEngine), so a
    /// "Box" (e.g. a coupler with terminals plugged into it) and a "Terminal" (a leaf
    /// module) are the same underlying concept here: a named node with a product code
    /// and, possibly, its own children. Nesting is unbounded to match real topologies
    /// like Device -> Box(CU2508) -> Box(EK1100) -> Terminal(EL2008).
    /// </summary>
    class IoNodeSpec
    {
        public string Name { get; }
        public string Product { get; }
        public List<IoNodeSpec> Children { get; }

        /// <summary>Mirrors the terminal's "Plc" tab "Create PLC Data Type" setting
        /// (null = leave unchanged/off; "Device" or "Channel" = the matching
        /// granularity radio button — see IoSyncEngine.ApplyPlcDataTypeSetting).
        /// Needed for terminals like EL3174/EL3214 whose analog channels are only
        /// resolvable as a named PLC type (e.g. MDP5001_300_7E2119CA) once this is
        /// turned on; plain digital terminals (EL2008 etc.) typically leave it null.</summary>
        public string CreatePlcType { get; }

        public IoNodeSpec(string name, string product, List<IoNodeSpec> children, string createPlcType = null)
        {
            Name = name;
            Product = product;
            Children = children;
            CreatePlcType = createPlcType;
        }
    }
}
