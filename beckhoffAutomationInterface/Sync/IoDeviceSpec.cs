using System.Collections.Generic;

namespace BeckhoffAutomationInterface.Sync
{
    /// <summary>
    /// Desired-state model for one EtherCAT master device and its bus topology,
    /// parsed from an io-devices.xml manifest. Mirrors the real hardware tree:
    /// Device (master) -> Box (e.g. an EK1100 coupler) -> Terminal (e.g. EL1008).
    /// </summary>
    class IoDeviceSpec
    {
        public string Name { get; }
        public bool Disabled { get; }
        public List<IoBoxSpec> Boxes { get; }

        public IoDeviceSpec(string name, bool disabled, List<IoBoxSpec> boxes)
        {
            Name = name;
            Disabled = disabled;
            Boxes = boxes;
        }
    }

    class IoBoxSpec
    {
        public string Name { get; }
        public string Product { get; }
        public List<IoTerminalSpec> Terminals { get; }

        public IoBoxSpec(string name, string product, List<IoTerminalSpec> terminals)
        {
            Name = name;
            Product = product;
            Terminals = terminals;
        }
    }

    class IoTerminalSpec
    {
        public string Name { get; }
        public string Product { get; }

        public IoTerminalSpec(string name, string product)
        {
            Name = name;
            Product = product;
        }
    }
}
