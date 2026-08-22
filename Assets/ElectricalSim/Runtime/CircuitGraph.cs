using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace ElectricalSim
{
    public enum ElectricalPotential
    {
        Floating,
        Neutral,
        PhaseL1,
        PhaseL2,
        PhaseL3,
        Conflict
    }

    [Serializable]
    public sealed class WireConnection
    {
        public string Id = Guid.NewGuid().ToString();
        public string StartPort = string.Empty;
        public string EndPort = string.Empty;
        public Color Color = Color.red;
        public float Area = 0.01f;
        public string LineType = "JumperLine";
        public List<Vector3> Points = new List<Vector3>();
    }

    public sealed class SimulationSnapshot
    {
        private readonly Dictionary<string, string> roots;
        private readonly Dictionary<string, ElectricalPotential> potentials;
        private readonly Dictionary<string, bool> activeDevices;
        private readonly Dictionary<string, MotorDirection> motorDirections;

        public SimulationSnapshot(
            Dictionary<string, string> roots,
            Dictionary<string, ElectricalPotential> potentials,
            Dictionary<string, bool> activeDevices,
            Dictionary<string, MotorDirection> motorDirections,
            IReadOnlyList<string> errors)
        {
            this.roots = roots;
            this.potentials = potentials;
            this.activeDevices = activeDevices;
            this.motorDirections = motorDirections;
            Errors = errors;
        }

        public IReadOnlyList<string> Errors { get; }
        public bool HasShortCircuit => Errors.Count > 0;

        public bool SameNet(string a, string b)
        {
            return roots.TryGetValue(a, out var rootA) && roots.TryGetValue(b, out var rootB) && rootA == rootB;
        }

        public ElectricalPotential GetPotential(string port)
        {
            if (!roots.TryGetValue(port, out var root)) return ElectricalPotential.Floating;
            return potentials.TryGetValue(root, out var potential) ? potential : ElectricalPotential.Floating;
        }

        public bool HasControlVoltage(string portA, string portB)
        {
            var a = GetPotential(portA);
            var b = GetPotential(portB);
            var aPhase = a == ElectricalPotential.PhaseL1 || a == ElectricalPotential.PhaseL2 || a == ElectricalPotential.PhaseL3;
            var bPhase = b == ElectricalPotential.PhaseL1 || b == ElectricalPotential.PhaseL2 || b == ElectricalPotential.PhaseL3;
            return (aPhase && b == ElectricalPotential.Neutral) || (bPhase && a == ElectricalPotential.Neutral);
        }

        public bool IsDeviceActive(string deviceId) => activeDevices.TryGetValue(deviceId, out var active) && active;

        public MotorDirection GetMotorDirection(string deviceId)
        {
            return motorDirections.TryGetValue(deviceId, out var direction) ? direction : MotorDirection.Stopped;
        }
    }

    public sealed class CircuitGraph
    {
        private readonly List<WireConnection> wires = new List<WireConnection>();
        private readonly Dictionary<string, IElectricalDevice> devices = new Dictionary<string, IElectricalDevice>();

        public IReadOnlyList<WireConnection> Wires => wires;
        public IReadOnlyDictionary<string, IElectricalDevice> Devices => devices;

        public void RegisterDevice(IElectricalDevice device)
        {
            devices[device.DeviceId] = device;
        }

        public void ClearDevices() => devices.Clear();

        public WireConnection AddWire(string startPort, string endPort, Color color, string lineType = "JumperLine", float area = 0.01f)
        {
            if (string.IsNullOrWhiteSpace(startPort) || string.IsNullOrWhiteSpace(endPort))
                throw new ArgumentException("Wire endpoints cannot be empty.");
            if (startPort == endPort) throw new ArgumentException("A wire must connect two different ports.");

            var existing = wires.FirstOrDefault(w =>
                (w.StartPort == startPort && w.EndPort == endPort) ||
                (w.StartPort == endPort && w.EndPort == startPort));
            if (existing != null) return existing;

            var wire = new WireConnection
            {
                StartPort = startPort,
                EndPort = endPort,
                Color = color,
                LineType = lineType,
                Area = area
            };
            wires.Add(wire);
            return wire;
        }

        public void AddWire(WireConnection wire)
        {
            if (wire == null) throw new ArgumentNullException(nameof(wire));
            if (wires.All(item => item.Id != wire.Id)) wires.Add(wire);
        }

        public bool RemoveWire(string id)
        {
            return wires.RemoveAll(w => w.Id == id) > 0;
        }

        public void ClearWires() => wires.Clear();

        public void ReplaceWires(IEnumerable<WireConnection> replacement)
        {
            wires.Clear();
            if (replacement == null) return;
            foreach (var wire in replacement) wires.Add(CloneWire(wire));
        }

        public static WireConnection CloneWire(WireConnection source)
        {
            return new WireConnection
            {
                Id = source.Id,
                StartPort = source.StartPort,
                EndPort = source.EndPort,
                Color = source.Color,
                Area = source.Area,
                LineType = source.LineType,
                Points = new List<Vector3>(source.Points)
            };
        }

        public bool AreConnectedByWiring(string portA, string portB)
        {
            var union = BuildUnion(includeDeviceContacts: false);
            return union.Contains(portA) && union.Contains(portB) && union.Find(portA) == union.Find(portB);
        }

        public SimulationSnapshot Solve(float deltaTime = 0.02f, int maxIterations = 16)
        {
            SimulationSnapshot snapshot = null;
            for (var iteration = 0; iteration < maxIterations; iteration++)
            {
                snapshot = BuildSnapshot();
                var changed = false;
                foreach (var device in devices.Values)
                    changed |= device.Evaluate(snapshot, deltaTime);
                if (!changed) break;
            }

            snapshot = BuildSnapshot();
            foreach (var device in devices.Values) device.ApplyVisualState(snapshot);
            return snapshot;
        }

        private SimulationSnapshot BuildSnapshot()
        {
            var union = BuildUnion(includeDeviceContacts: true);
            var potentialsByRoot = new Dictionary<string, ElectricalPotential>();
            var errors = new List<string>();

            foreach (var source in devices.Values.Where(d => d.Kind == ElectricalDeviceKind.PowerSource))
            {
                AddPotential(union, potentialsByRoot, Port(source.DeviceId, "L1"), ElectricalPotential.PhaseL1, errors);
                AddPotential(union, potentialsByRoot, Port(source.DeviceId, "L2"), ElectricalPotential.PhaseL2, errors);
                AddPotential(union, potentialsByRoot, Port(source.DeviceId, "L3"), ElectricalPotential.PhaseL3, errors);
                AddPotential(union, potentialsByRoot, Port(source.DeviceId, "N"), ElectricalPotential.Neutral, errors);
            }

            var rootMap = union.Items.ToList().ToDictionary(item => item, union.Find);
            var active = devices.Values.ToDictionary(d => d.DeviceId, d => d.IsActive);
            var directions = devices.Values.ToDictionary(d => d.DeviceId, d =>
                d is ElectricalDeviceRuntime runtime ? runtime.MotorDirection : MotorDirection.Stopped);
            return new SimulationSnapshot(rootMap, potentialsByRoot, active, directions, errors);
        }

        private DisjointSet BuildUnion(bool includeDeviceContacts)
        {
            var union = new DisjointSet();
            foreach (var device in devices.Values)
                foreach (var port in device.Ports)
                    union.Add(Port(device.DeviceId, port));

            foreach (var wire in wires)
                union.Union(wire.StartPort, wire.EndPort);

            if (includeDeviceContacts)
            {
                foreach (var device in devices.Values)
                    foreach (var link in device.GetConductiveLinks())
                        union.Union(Qualify(device.DeviceId, link.A), Qualify(device.DeviceId, link.B));
            }

            return union;
        }

        private static void AddPotential(
            DisjointSet union,
            Dictionary<string, ElectricalPotential> potentials,
            string port,
            ElectricalPotential potential,
            List<string> errors)
        {
            union.Add(port);
            var root = union.Find(port);
            if (!potentials.TryGetValue(root, out var current) || current == ElectricalPotential.Floating)
            {
                potentials[root] = potential;
                return;
            }

            if (current != potential && current != ElectricalPotential.Conflict)
            {
                potentials[root] = ElectricalPotential.Conflict;
                errors.Add($"Short circuit: {current} is connected to {potential}.");
            }
        }

        public static string Port(string deviceId, string portName) => $"{deviceId}.{portName}";

        private static string Qualify(string deviceId, string port)
        {
            return port.Contains(".") ? port : Port(deviceId, port);
        }

        private sealed class DisjointSet
        {
            private readonly Dictionary<string, string> parents = new Dictionary<string, string>();
            public IEnumerable<string> Items => parents.Keys;

            public bool Contains(string item) => parents.ContainsKey(item);

            public void Add(string item)
            {
                if (!parents.ContainsKey(item)) parents[item] = item;
            }

            public string Find(string item)
            {
                Add(item);
                if (parents[item] != item) parents[item] = Find(parents[item]);
                return parents[item];
            }

            public void Union(string a, string b)
            {
                var rootA = Find(a);
                var rootB = Find(b);
                if (rootA != rootB) parents[rootB] = rootA;
            }
        }
    }
}
