using System;
using System.Collections.Generic;
using System.Linq;

namespace ElectricalSim
{
    internal struct MotorDriveSample
    {
        public bool Connected;
        public bool HasDrive;
        public float SpeedRpm;
    }

    // The DC link isolates the input and output: never union their electrical nets.
    public sealed class InverterDriveRuntime : IElectricalDevice
    {
        private static readonly string[] Terminals = { "L1", "L2", "L3", "U2", "V2", "W2", "PE" };
        private static readonly string[] Outputs = { "U2", "V2", "W2" };
        private readonly Func<float> readSpeed;
        private readonly Func<bool> readFault;
        public string DeviceId { get; }
        public ElectricalDeviceKind Kind => ElectricalDeviceKind.VariableFrequencyDrive;
        public IReadOnlyCollection<string> Ports => Terminals;
        public bool IsActive { get; private set; }
        public bool HasSupply { get; private set; }
        public bool OutputValid { get; private set; }

        public InverterDriveRuntime(string id, Func<float> speed, Func<bool> fault)
        {
            DeviceId = id;
            readSpeed = speed;
            readFault = fault;
        }

        public IEnumerable<PortPair> GetConductiveLinks() { yield break; }
        public bool Evaluate(SimulationSnapshot snapshot, float deltaTime) => false;
        public void ApplyVisualState(SimulationSnapshot snapshot) { }

        internal void Validate(SimulationSnapshot snapshot, List<string> errors)
        {
            var supply = new[] { "L1", "L2", "L3" }.Select(p => snapshot.GetPotential(Port(p))).ToArray();
            HasSupply = supply.All(IsPhase) && supply.Distinct().Count() == 3;
            OutputValid = true;
            for (var a = 0; a < Outputs.Length; a++)
            {
                if (snapshot.GetPotential(Port(Outputs[a])) != ElectricalPotential.Floating)
                    OutputValid = false;
                for (var b = a + 1; b < Outputs.Length; b++)
                    if (snapshot.SameNet(Port(Outputs[a]), Port(Outputs[b]))) OutputValid = false;
                if (snapshot.SameNet(Port(Outputs[a]), Port("PE")) ||
                    snapshot.SameNet(Port(Outputs[a]), "POWER.PE")) OutputValid = false;
            }
            if (!OutputValid) errors.Add("G120 输出接线故障：输出短接、接地或与工频电源混接。");
            IsActive = HasSupply && OutputValid && !readFault() && Math.Abs(readSpeed()) > 0.1f;
        }

        internal MotorDriveSample SampleMotor(string motorId, SimulationSnapshot snapshot)
        {
            var motorPorts = new[] { "U", "V", "W" };
            var phases = new int[3];
            var result = new MotorDriveSample();
            for (var i = 0; i < 3; i++)
            {
                phases[i] = -1;
                for (var j = 0; j < 3; j++)
                    if (snapshot.SameNet(CircuitGraph.Port(motorId, motorPorts[i]), Port(Outputs[j])))
                    {
                        result.Connected = true;
                        phases[i] = j;
                    }
            }
            result.HasDrive = result.Connected && HasSupply && OutputValid && !readFault() &&
                phases.All(p => p >= 0) && phases.Distinct().Count() == 3;
            if (result.HasDrive)
            {
                var inversions = 0;
                for (var i = 0; i < 3; i++)
                    for (var j = i + 1; j < 3; j++) if (phases[i] > phases[j]) inversions++;
                var speed = readSpeed();
                result.SpeedRpm = float.IsNaN(speed) || float.IsInfinity(speed) ? 0f : speed * (inversions % 2 == 0 ? 1f : -1f);
            }
            return result;
        }

        private string Port(string name) => CircuitGraph.Port(DeviceId, name);
        private static bool IsPhase(ElectricalPotential p) =>
            p == ElectricalPotential.PhaseL1 || p == ElectricalPotential.PhaseL2 || p == ElectricalPotential.PhaseL3;
    }
}
