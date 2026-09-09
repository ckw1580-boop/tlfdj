using System;
using System.Collections.Generic;
using System.Linq;

namespace ElectricalSim
{
    public sealed class PlcDeviceRuntime : IElectricalDevice
    {
        public string DeviceId { get; }
        public ElectricalDeviceKind Kind => ElectricalDeviceKind.Plc;
        public IReadOnlyCollection<string> Ports { get; } = PlcConfiguration.InputTerminals.Concat(PlcConfiguration.OutputTerminals).Concat(PlcConfiguration.SupplyTerminals).ToArray();
        public bool IsActive { get; private set; }
        public bool OutputSupplyReady { get; private set; }
        public bool[] InputStates { get; private set; } = new bool[14];
        private bool enabled;
        private bool[] outputs = new bool[10];
        public PlcDeviceRuntime(string id) { DeviceId = id; }
        public void SetOutputs(bool active, bool[] values) { enabled = active; outputs = values == null ? new bool[10] : (bool[])values.Clone(); }
        public string Port(string name) => DeviceId + "." + name;
        public bool Evaluate(SimulationSnapshot snapshot, float deltaTime)
        {
            var powered = snapshot.GetDcVoltage(Port("L+"), Port("M")) == 24d;
            var outputSupply = powered && snapshot.GetDcVoltage(Port("3L+"), Port("3M-")) == 24d;
            var changed = IsActive != powered || OutputSupplyReady != outputSupply;
            IsActive = powered; OutputSupplyReady = outputSupply;
            for (var i = 0; i < InputStates.Length; i++)
                InputStates[i] = powered && snapshot.GetDcVoltage(Port(PlcConfiguration.InputTerminals[i]), Port("1M")) == 24d;
            return changed;
        }
        public IEnumerable<PortPair> GetConductiveLinks()
        {
            if (!enabled || !OutputSupplyReady) yield break;
            for (var i = 0; i < outputs.Length; i++)
                if (outputs[i]) yield return new PortPair("3L+", PlcConfiguration.OutputTerminals[i]);
        }
        public void ApplyVisualState(SimulationSnapshot snapshot) { }
    }
}
