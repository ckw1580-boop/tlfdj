using System.Collections.Generic;

namespace ElectricalSim
{
    public sealed class ThermalRelayDefinition
    {
        public static readonly IReadOnlyList<string> Ports = System.Array.AsReadOnly(new[]
            { "L1", "L2", "L3", "T1", "T2", "T3", "95", "96", "97", "98" });
        public static readonly IReadOnlyList<PortPair> Heaters = System.Array.AsReadOnly(new[]
            { new PortPair("L1", "T1"), new PortPair("L2", "T2"), new PortPair("L3", "T3") });
        public static readonly IReadOnlyList<ThermalRelayDefinition> All = System.Array.AsReadOnly(new[]
            { new ThermalRelayDefinition(1, 33), new ThermalRelayDefinition(2, 34), new ThermalRelayDefinition(3, 114) });
        public string Id { get; }
        public string RuntimeId { get; }
        public string ModelPath { get; }
        public bool IsRear { get; }
        private ThermalRelayDefinition(int index, int nut)
        {
            Id = "FR" + index; IsRear = index == 3; RuntimeId = IsRear ? "FR" : Id;
            ModelPath = "Bench/ElectricBench/Nuts/" + nut + "/ReJiDianQi_NR2-25";
        }
        public static string TerminalLabel(string port) => ContactorDefinition.TerminalLabel(port);
        public static string PortRole(string port) => port == "95" || port == "96" ? "辅助常闭" :
            port == "97" || port == "98" ? "辅助常开" : "热元件主回路";
        public string BindingName(string port) => Id + "_" + TerminalLabel(port) +
            (port == "95" || port == "96" ? "NC" : port == "97" || port == "98" ? "NO" : "");
    }
}
