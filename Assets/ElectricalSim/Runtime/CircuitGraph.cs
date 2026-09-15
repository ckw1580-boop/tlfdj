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
        Conflict,
        DcPositive24,
        DcNegative
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
        public bool? FaultSide;
    }

    public sealed class SimulationSnapshot
    {
        private readonly Dictionary<string, string> roots;
        private readonly Dictionary<string, ElectricalPotential> potentials;
        private readonly Dictionary<string, bool> activeDevices;
        private readonly Dictionary<string, MotorDirection> motorDirections;
        private readonly Dictionary<string, float> motorSpeeds;
        private readonly HashSet<string> unsupportedVoltageRoots = new HashSet<string>();
        private readonly HashSet<string> externalSupplyRoots = new HashSet<string>();
        internal readonly Dictionary<string, MotorDriveSample> MotorDrives = new Dictionary<string, MotorDriveSample>();

        public SimulationSnapshot(
            Dictionary<string, string> roots,
            Dictionary<string, ElectricalPotential> potentials,
            Dictionary<string, bool> activeDevices,
            Dictionary<string, MotorDirection> motorDirections,
            IReadOnlyList<string> errors,
            Dictionary<string, float> motorSpeeds = null)
        {
            this.roots = roots;
            this.potentials = potentials;
            this.activeDevices = activeDevices;
            this.motorDirections = motorDirections;
            this.motorSpeeds = motorSpeeds ?? new Dictionary<string, float>();
            Errors = errors;
        }

        public IReadOnlyList<string> Errors { get; }
        public bool HasShortCircuit => Errors.Count > 0;

        public bool ContainsPort(string port) => port != null && roots.ContainsKey(port);
        public bool IsVoltageUnsupported(string port) => ContainsPort(port) && unsupportedVoltageRoots.Contains(roots[port]);
        public bool HasExternalSupply(string port) => ContainsPort(port) &&
            (GetPotential(port) != ElectricalPotential.Floating || externalSupplyRoots.Contains(roots[port]));
        internal void MarkUnmodeledVoltageOutput(string port, bool energized)
        {
            if (!ContainsPort(port)) return;
            unsupportedVoltageRoots.Add(roots[port]);
            if (energized) externalSupplyRoots.Add(roots[port]);
        }

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

        public double GetDcVoltage(string portA, string portB)
        {
            var a = GetPotential(portA);
            var b = GetPotential(portB);
            if (a == ElectricalPotential.Conflict || b == ElectricalPotential.Conflict) return double.NaN;
            if (a == ElectricalPotential.DcPositive24 && b == ElectricalPotential.DcNegative) return 24d;
            if (b == ElectricalPotential.DcPositive24 && a == ElectricalPotential.DcNegative) return -24d;
            return 0d;
        }

        public double GetAcVoltage(string portA, string portB)
        {
            var a = GetPotential(portA); var b = GetPotential(portB);
            if (a == ElectricalPotential.Conflict || b == ElectricalPotential.Conflict) return double.NaN;
            if (a == b) return 0d;
            bool Phase(ElectricalPotential p) => p == ElectricalPotential.PhaseL1 || p == ElectricalPotential.PhaseL2 || p == ElectricalPotential.PhaseL3;
            if (Phase(a) && b == ElectricalPotential.Neutral || Phase(b) && a == ElectricalPotential.Neutral) return 220d;
            return Phase(a) && Phase(b) ? 380d : 0d;
        }

        public bool IsDeviceActive(string deviceId) => activeDevices.TryGetValue(deviceId, out var active) && active;

        public MotorDirection GetMotorDirection(string deviceId)
        {
            return motorDirections.TryGetValue(deviceId, out var direction) ? direction : MotorDirection.Stopped;
        }

        public float GetMotorSpeedRpm(string deviceId)
            => deviceId != null && motorSpeeds.TryGetValue(deviceId, out var speed) ? speed : 0f;
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
                FaultSide = source.FaultSide,
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
            // Time advances once, after contact convergence, never in the iteration loop.
            foreach (var motor in devices.Values.OfType<ElectricalDeviceRuntime>().Where(d => d.Kind == ElectricalDeviceKind.Motor))
                if (deltaTime > 0) motor.AdvanceMotorSpeed(snapshot, deltaTime);
            snapshot = BuildSnapshot();
            foreach (var device in devices.Values) device.ApplyVisualState(snapshot);
            return snapshot;
        }

        private SimulationSnapshot BuildSnapshot()
        {
            var union = BuildUnion(includeDeviceContacts: true);
            var potentialsByRoot = new Dictionary<string, ElectricalPotential>();
            var errors = new List<string>();

            foreach (var source in devices.Values.OfType<IElectricalSource>())
            {
                foreach (var output in source.GetSourcePotentials())
                    AddPotential(union, potentialsByRoot, output.Key, output.Value, errors);
            }

            var rootMap = union.Items.ToList().ToDictionary(item => item, union.Find);
            var active = devices.Values.ToDictionary(d => d.DeviceId, d => d.IsActive);
            var directions = devices.Values.ToDictionary(d => d.DeviceId, d =>
                d is ElectricalDeviceRuntime runtime ? runtime.MotorDirection : MotorDirection.Stopped);
            var speeds = devices.Values.OfType<ElectricalDeviceRuntime>()
                .Where(d => d.Kind == ElectricalDeviceKind.Motor).ToDictionary(d => d.DeviceId, d => d.ActualSpeedRpm);
            var snapshot = new SimulationSnapshot(rootMap, potentialsByRoot, active, directions, errors, speeds);
            foreach (var sensor in devices.Values.OfType<SceneIoDeviceRuntime>())
                if (sensor.Powered && sensor.Wet && sensor.SignalShortCircuit)
                    errors.Add(sensor.Definition.Name + "：SIGNAL与GND短接，输出已保护断开。");
            foreach (var drive in devices.Values.OfType<InverterDriveRuntime>())
            {
                drive.Validate(snapshot, errors);
                foreach (var output in new[] { "U2", "V2", "W2" })
                    snapshot.MarkUnmodeledVoltageOutput(Port(drive.DeviceId, output), drive.IsActive);
                foreach (var motor in devices.Values.Where(d => d.Kind == ElectricalDeviceKind.Motor))
                {
                    var sample = drive.SampleMotor(motor.DeviceId, snapshot);
                    if (sample.Connected) snapshot.MotorDrives[motor.DeviceId] = sample;
                }
            }
            return snapshot;
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

        private string Qualify(string deviceId, string port)
        {
            // PLC local terminal names (Q0.0 / PLC_1_M0.0) contain dots too.
            // Registered local ports take precedence over the cross-device notation.
            if (devices.TryGetValue(deviceId, out var device) && device.Ports.Contains(port)) return Port(deviceId, port);
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
