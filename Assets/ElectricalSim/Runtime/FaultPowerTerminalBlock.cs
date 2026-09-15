using System.Collections.Generic;

namespace ElectricalSim
{
    public static class FaultPowerTerminalBlock
    {
        public const string DeviceId = "FaultPowerTerminalBlock";
        public static readonly IReadOnlyList<string> PortNames = System.Array.AsReadOnly(new[]
        {
            "U1", "V1", "W1", "N1", "U2", "V2", "W2", "N2",
            "24V+_1", "24V-_1", "24V+_2", "24V-_2", "24V+_3", "24V-_3"
        });

        public static ElectricalDeviceRuntime CreateRuntime()
        {
            var runtime = new ElectricalDeviceRuntime(DeviceId, ElectricalDeviceKind.Terminal, PortNames);
            foreach (var port in PortNames)
            {
                var node = port.StartsWith("24V+") ? "TERMINAL_BUS.DC_POSITIVE" :
                    port.StartsWith("24V-") ? "TERMINAL_BUS.DC_NEGATIVE" :
                    port[0] == 'U' ? "POWER.L1" : port[0] == 'V' ? "POWER.L2" :
                    port[0] == 'W' ? "POWER.L3" : "POWER.N";
                runtime.AddFixedLink(port, node);
            }
            return runtime;
        }
    }
}
