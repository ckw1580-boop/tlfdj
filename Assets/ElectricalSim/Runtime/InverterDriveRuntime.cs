using System;
using System.Collections.Generic;
using System.Linq;

namespace ElectricalSim
{
    internal struct MotorDriveSample
    {
        public bool Connected;
        public bool HasDrive;
        public float FrequencyHz;
        public float LineVoltage;
        public int[] TerminalPhases;
        public string SourceId;
        public string Diagnostic;
    }

    // The DC link isolates the input and output: never union their electrical nets.
    public sealed class InverterDriveRuntime : IElectricalDevice, IControlSignalSource
    {
        private static readonly string[] Terminals = { "L1", "L2", "L3", "U2", "V2", "W2", "PE" };
        private static readonly string[] Outputs = { "U2", "V2", "W2" };
        private readonly Func<float> readSpeed;
        private readonly Func<bool> readFault;
        private readonly InverterPanelController panel;
        public string DeviceId { get; }
        public ElectricalDeviceKind Kind => ElectricalDeviceKind.VariableFrequencyDrive;
        public IReadOnlyCollection<string> Ports => Control == null ? Terminals : controlPorts;
        private readonly string[] controlPorts = Terminals.Concat(G120TerminalCatalog.All.Select(t => t.Port))
            .Concat(new[] { "DI0", "DI1", "DI2", "DI3", "DI4", "DI5", "DI1_COM1", "DI1_COM2", "A1" }).ToArray();
        public G120ControlRuntime Control { get; }
        public bool IsActive { get; private set; }
        public bool HasSupply { get; private set; }
        public bool OutputValid { get; private set; }
        public float OutputFrequencyHz => IsActive ? Math.Abs(FiniteSpeed) * Parameter("P310", 50f) / Math.Max(0.001f, Parameter("P311", 1450f)) : 0f;
        public float OutputLineVoltage => Math.Min(InputLineVoltage, Math.Min(Parameter("P304", 380f), Parameter("P304", 380f) * OutputFrequencyHz / Math.Max(0.001f, Parameter("P310", 50f))));
        public float InputLineVoltage { get; private set; }
        private double phase, clock;
        public void ResetWaveform() { phase = clock = 0; }
        internal void AdvanceWaveform(float deltaTime)
        { if (deltaTime <= 0) return; phase = (phase + Math.Sign(FiniteSpeed) * 2 * Math.PI * OutputFrequencyHz * deltaTime) % (2 * Math.PI); clock += deltaTime; }
        internal InverterVoltage CaptureVoltage() => new InverterVoltage(DeviceId, IsActive, OutputValid, OutputFrequencyHz, OutputLineVoltage, Math.Sqrt(2) * InputLineVoltage, phase, clock, FiniteSpeed < 0 ? -1 : 1);
        internal void RejectSharedSource() { OutputValid = false; IsActive = false; }
        private float FiniteSpeed { get { var speed = readSpeed(); return float.IsNaN(speed) || float.IsInfinity(speed) ? 0f : speed; } }
        private float Parameter(string key, float fallback) => panel != null && panel.TryGetParameter(key, out var value) ? value : fallback;

        public InverterDriveRuntime(string id, Func<float> speed, Func<bool> fault)
        {
            DeviceId = id;
            readSpeed = speed;
            readFault = fault;
        }

        public InverterDriveRuntime(InverterPanelController panel) : this("G120", () => panel.OutputSpeedRpm, () => panel.HasFault)
        { this.panel = panel; Control = new G120ControlRuntime(panel); panel.FactorySettingsReset += ResetWaveform; }
        public IEnumerable<PortPair> GetConductiveLinks() => Control != null ? Control.Links() : Enumerable.Empty<PortPair>();
        public bool Evaluate(SimulationSnapshot snapshot, float deltaTime) => Control != null && Control.Evaluate(snapshot);
        public void ApplyVisualState(SimulationSnapshot snapshot) => Control?.RefreshReadings(snapshot);
        public IEnumerable<ControlSignal> GetControlSignals(SimulationSnapshot topology) => Control != null ? Control.GetSignals(topology) : Enumerable.Empty<ControlSignal>();
        public IEnumerable<PortPair> CurrentReceivers => Control != null ? Control.CurrentReceivers : Enumerable.Empty<PortPair>();

        internal void Validate(SimulationSnapshot snapshot, List<string> errors)
        {
            var supply = new[] { "L1", "L2", "L3" }.Select(p => snapshot.GetPotential(Port(p))).ToArray();
            HasSupply = supply.All(IsPhase) && supply.Distinct().Count() == 3;
            InputLineVoltage = HasSupply ? (float)snapshot.GetAcVoltage(Port("L1"), Port("L2")) : 0;
            if (float.IsNaN(InputLineVoltage) || float.IsInfinity(InputLineVoltage)) { HasSupply = false; InputLineVoltage = 0; }
            OutputValid = true;
            for (var a = 0; a < Outputs.Length; a++)
            {
                if (snapshot.GetPotential(Port(Outputs[a])) != ElectricalPotential.Floating || snapshot.HasControlSignal(Port(Outputs[a])) || snapshot.HasSignalConflict(Port(Outputs[a])))
                    OutputValid = false;
                for (var b = a + 1; b < Outputs.Length; b++)
                    if (snapshot.SameNet(Port(Outputs[a]), Port(Outputs[b]))) OutputValid = false;
                if (snapshot.IsEarthConnection(Port(Outputs[a]))) OutputValid = false;
            }
            if (!OutputValid) errors.Add(DeviceId + " 输出接线故障：输出短接、接地或与工频电源混接。");
            IsActive = HasSupply && OutputValid && !readFault() && Math.Abs(FiniteSpeed) > 0.1f;
        }

        internal MotorDriveSample SampleMotor(string motorId, SimulationSnapshot snapshot)
        {
            var motorPorts = new[] { "U", "V", "W", "U2", "V2", "W2" };
            var phases = Enumerable.Repeat(-1, motorPorts.Length).ToArray();
            var result = new MotorDriveSample { TerminalPhases = phases, SourceId = DeviceId };
            for (var i = 0; i < motorPorts.Length; i++)
            {
                phases[i] = -1;
                for (var j = 0; j < 3; j++)
                    if (snapshot.SameNet(CircuitGraph.Port(motorId, motorPorts[i]), Port(Outputs[j])))
                    {
                        result.Connected = true;
                        phases[i] = FiniteSpeed < 0 && j != 0 ? 3 - j : j;
                    }
            }
            // Winding closure and missing phases belong to the six-terminal motor resolver.
            result.HasDrive = result.Connected && IsActive;
            var voltage = CaptureVoltage();
            result.FrequencyHz = result.HasDrive ? (float)voltage.FrequencyHz : 0f;
            result.LineVoltage = result.HasDrive ? (float)voltage.LineRms : 0f;
            return result;
        }

        private string Port(string name) => CircuitGraph.Port(DeviceId, name);
        private static bool IsPhase(ElectricalPotential p) =>
            p == ElectricalPotential.PhaseL1 || p == ElectricalPotential.PhaseL2 || p == ElectricalPotential.PhaseL3;
    }
}
