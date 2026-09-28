using System;
using System.Collections.Generic;
using System.Linq;

namespace ElectricalSim
{
    public sealed partial class SimulationSnapshot
    {
        private readonly Dictionary<string, MotorRuntimeState> motorStates = new Dictionary<string, MotorRuntimeState>();
        private readonly Dictionary<string, MotorConfiguration> motorConfigurations = new Dictionary<string, MotorConfiguration>();
        private readonly Dictionary<string, ThermalRelayRuntimeState> thermalStates = new Dictionary<string, ThermalRelayRuntimeState>();
        private readonly List<string> diagnostics = new List<string>();
        private readonly List<(string a, string b, double resistance)> windingResistors = new List<(string, string, double)>();
        public IReadOnlyList<string> Diagnostics => diagnostics.AsReadOnly();
        public IReadOnlyList<string> MotorIds => motorStates.Keys.ToArray();
        public IReadOnlyDictionary<string, MotorRuntimeState> MotorStates => motorStates.ToDictionary(p => p.Key, p => p.Value.Copy());
        public IReadOnlyDictionary<string, MotorConfiguration> MotorConfigurations => motorConfigurations.ToDictionary(p => p.Key, p => p.Value.Clone());
        public MotorRuntimeState GetMotorState(string id) => id != null && motorStates.TryGetValue(id, out var state) ? state.Copy() : null;
        public ThermalRelayRuntimeState GetThermalState(string id) => id != null && thermalStates.TryGetValue(id, out var state) ? state.Copy() : null;
        internal void AddDiagnostic(string diagnostic)
        {
            if (!string.IsNullOrWhiteSpace(diagnostic) && !diagnostics.Contains(diagnostic)) diagnostics.Add(diagnostic);
        }
        internal void CaptureMotorData(IEnumerable<ElectricalDeviceRuntime> motors)
        {
            foreach (var motor in motors)
            {
                motorStates[motor.DeviceId] = motor.MotorState.Copy();
                var config = motor.MotorConfiguration.Clone();
                motorConfigurations[motor.DeviceId] = config;
                AddDiagnostic(string.IsNullOrWhiteSpace(motor.MotorState.Connection?.Diagnostic) ? "" : motor.DeviceId + "：" + motor.MotorState.Connection.Diagnostic);
                if (motor.MotorState.ThermalState >= 1) AddDiagnostic(motor.DeviceId + "：电机过热，请检查负载和保护回路。");
                if (config.IsTwoSpeed)
                {
                    var ring = new[] { "U", "U2", "V", "V2", "W", "W2" };
                    for (var i = 0; i < ring.Length; i++) AddResistor(motor.DeviceId, ring[i], ring[(i + 1) % ring.Length], config.WindingResistanceOhms / 2);
                }
                else foreach (var name in new[] { "U", "V", "W" }) AddResistor(motor.DeviceId, name, name + "2", config.WindingResistanceOhms);
            }
        }
        internal void CaptureThermalData(IEnumerable<ElectricalDeviceRuntime> devices)
        {
            foreach (var device in devices.Where(d => d.Kind == ElectricalDeviceKind.ThermalRelay))
            {
                thermalStates[device.DeviceId] = device.ThermalState.Copy();
                AddDiagnostic(string.IsNullOrWhiteSpace(device.ThermalState.Diagnostic) ? "" : device.DeviceId + "：" + device.ThermalState.Diagnostic);
                if (device.IsTripped) AddDiagnostic(device.DeviceId + "：" + device.ThermalState.TripReason);
            }
        }
        private void AddResistor(string id, string a, string b, double resistance)
        {
            if (roots.TryGetValue(CircuitGraph.Port(id, a), out var ra) && roots.TryGetValue(CircuitGraph.Port(id, b), out var rb) && ra != rb)
                windingResistors.Add((ra, rb, resistance));
        }
        private HashSet<string> WindingComponent(string start)
        {
            var visited = new HashSet<string> { start }; var queue = new Queue<string>(); queue.Enqueue(start);
            while (queue.Count > 0)
            {
                var node = queue.Dequeue();
                foreach (var edge in windingResistors)
                {
                    var next = edge.a == node ? edge.b : edge.b == node ? edge.a : null;
                    if (next != null && visited.Add(next)) queue.Enqueue(next);
                }
            }
            return visited;
        }
        internal bool HasWindingSupply(string port)
        {
            if (!roots.TryGetValue(port, out var root)) return false;
            return WindingComponent(root).Any(r => potentials.TryGetValue(r, out var p) && p != ElectricalPotential.Floating ||
                externalSupplyRoots.Contains(r) || signalEnergized.Contains(r));
        }
        public double GetResistance(string a, string b)
        {
            if (!ContainsPort(a) || !ContainsPort(b)) return double.PositiveInfinity;
            if (SameNet(a, b)) return 0.2;
            var start = roots[a]; var ground = roots[b]; var component = WindingComponent(start);
            if (!component.Contains(ground)) return double.PositiveInfinity;
            var nodes = component.Where(n => n != ground).ToArray();
            var indices = nodes.Select((n, i) => (n, i)).ToDictionary(x => x.n, x => x.i);
            var matrix = new double[nodes.Length, nodes.Length + 1];
            foreach (var edge in windingResistors)
            {
                if (!component.Contains(edge.a)) continue;
                var g = 1 / edge.resistance;
                if (edge.a != ground) matrix[indices[edge.a], indices[edge.a]] += g;
                if (edge.b != ground) matrix[indices[edge.b], indices[edge.b]] += g;
                if (edge.a != ground && edge.b != ground)
                { matrix[indices[edge.a], indices[edge.b]] -= g; matrix[indices[edge.b], indices[edge.a]] -= g; }
            }
            matrix[indices[start], nodes.Length] = 1; // one ampere test current
            for (var pivot = 0; pivot < nodes.Length; pivot++)
            {
                var row = pivot;
                for (var i = pivot + 1; i < nodes.Length; i++) if (Math.Abs(matrix[i, pivot]) > Math.Abs(matrix[row, pivot])) row = i;
                if (Math.Abs(matrix[row, pivot]) < 1e-12) return double.PositiveInfinity;
                for (var j = pivot; j <= nodes.Length; j++) { var tmp = matrix[pivot, j]; matrix[pivot, j] = matrix[row, j]; matrix[row, j] = tmp; }
                var scale = matrix[pivot, pivot];
                for (var j = pivot; j <= nodes.Length; j++) matrix[pivot, j] /= scale;
                for (var i = 0; i < nodes.Length; i++)
                {
                    if (i == pivot) continue;
                    var factor = matrix[i, pivot];
                    for (var j = pivot; j <= nodes.Length; j++) matrix[i, j] -= factor * matrix[pivot, j];
                }
            }
            return matrix[indices[start], nodes.Length];
        }
    }
}
