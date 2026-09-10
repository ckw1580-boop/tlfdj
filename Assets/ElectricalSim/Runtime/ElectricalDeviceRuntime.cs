using System;
using System.Collections.Generic;
using System.Linq;

namespace ElectricalSim
{
    public sealed partial class ElectricalDeviceRuntime : IElectricalDevice, IElectricalSource
    {
        private readonly List<string> ports;
        private readonly List<PortPair> fixedLinks = new List<PortPair>();
        private float timerElapsed;
        private bool lastEvaluatedState;

        public ElectricalDeviceRuntime(string deviceId, ElectricalDeviceKind kind, IEnumerable<string> portNames)
        {
            DeviceId = deviceId;
            Kind = kind;
            ports = portNames.Distinct().ToList();
            IsClosed = kind == ElectricalDeviceKind.Breaker || kind == ElectricalDeviceKind.Fuse;
        }

        public string DeviceId { get; }
        public ElectricalDeviceKind Kind { get; }
        public IReadOnlyCollection<string> Ports => ports;
        public IReadOnlyList<PortPair> FixedLinks => fixedLinks;
        public bool IsActive { get; private set; }
        public bool IsClosed { get; private set; }
        public bool IsTripped { get; private set; }
        public bool IsPressed { get; private set; }
        public bool IsNormallyClosedButton { get; set; }
        public float TimerDelaySeconds { get; set; } = 1f;
        public double RelayCoilVoltage { get; private set; }
        public double ContactorCoilVoltage { get; private set; }
        public MotorDirection MotorDirection { get; private set; }
        public float ActualSpeedRpm { get; private set; }
        public float CoastStopSeconds { get; set; } = 3f;
        public float BrakeStopSeconds { get; set; } = 1f;
        private float stoppingRate;
        private bool wasStopping;
        private bool wasBraking;

        public void ResetMotorSpeed()
        {
            ActualSpeedRpm = 0f;
            wasStopping = false;
            MotorDirection = MotorDirection.Stopped;
        }

        internal void AdvanceMotorSpeed(SimulationSnapshot snapshot, float deltaTime)
        {
            var braking = MotorDirection == MotorDirection.Braking;
            var drive = snapshot.MotorDrives.TryGetValue(DeviceId, out var sample) ? sample : default;
            if (!braking && drive.HasDrive)
            {
                ActualSpeedRpm = drive.SpeedRpm;
                wasStopping = false;
                return;
            }
            if (!braking && !drive.Connected && MotorDirection != MotorDirection.Stopped)
            {
                ActualSpeedRpm = MotorDirection == MotorDirection.Reverse ? -1450f : 1450f;
                wasStopping = false;
                return;
            }
            if (!wasStopping || braking != wasBraking)
                stoppingRate = Math.Abs(ActualSpeedRpm) / Math.Max(0.01f, braking ? BrakeStopSeconds : CoastStopSeconds);
            wasStopping = true;
            wasBraking = braking;
            ActualSpeedRpm = UnityEngine.Mathf.MoveTowards(ActualSpeedRpm, 0f, stoppingRate * Math.Max(0f, deltaTime));
        }
        public Action<ElectricalDeviceRuntime> VisualStateChanged;

        public void AddFixedLink(string localPort, string qualifiedTarget)
        {
            if (string.IsNullOrWhiteSpace(localPort) || string.IsNullOrWhiteSpace(qualifiedTarget)) return;
            if (!ports.Contains(localPort)) ports.Add(localPort);
            fixedLinks.Add(new PortPair(localPort, qualifiedTarget));
        }

        public void SetControl(bool active)
        {
            if (ControlTarget != null) { ControlTarget.SetControl(active); return; }
            if (PanelDefinition != null) { SetPanelControl(active); return; }
            switch (Kind)
            {
                case ElectricalDeviceKind.PushButton:
                    IsPressed = active;
                    break;
                case ElectricalDeviceKind.Breaker:
                case ElectricalDeviceKind.Fuse:
                    IsClosed = active;
                    break;
                case ElectricalDeviceKind.ThermalRelay:
                    IsTripped = active;
                    break;
            }
        }

        public IEnumerable<PortPair> GetConductiveLinks()
        {
            foreach (var link in fixedLinks) yield return link;
            if (ControlTarget != null) yield break;
            if (PanelDefinition != null)
            {
                foreach (var link in PanelContacts()) yield return link;
                yield break;
            }
            switch (Kind)
            {
                case ElectricalDeviceKind.Breaker:
                case ElectricalDeviceKind.Fuse:
                    if (IsClosed)
                    {
                        yield return new PortPair("L1", "T1");
                        yield return new PortPair("L2", "T2");
                        yield return new PortPair("L3", "T3");
                    }
                    break;
                case ElectricalDeviceKind.PushButton:
                    var closed = IsNormallyClosedButton ? !IsPressed : IsPressed;
                    if (closed) yield return new PortPair("COM", IsNormallyClosedButton ? "NC" : "NO");
                    break;
                case ElectricalDeviceKind.Contactor:
                    foreach (var contact in ContactorDefinition.Contacts)
                        if (IsActive != contact.NormallyClosed)
                            yield return new PortPair(contact.Input, contact.Output);
                    break;
                case ElectricalDeviceKind.IntermediateRelay:
                    foreach (var contact in IntermediateRelayDefinition.Contacts)
                        yield return new PortPair(contact.Common, IsActive ? contact.NormallyOpen : contact.NormallyClosed);
                    break;
                case ElectricalDeviceKind.TimeRelay:
                    if (IsActive) yield return new PortPair("15", "18");
                    else yield return new PortPair("15", "16");
                    break;
                case ElectricalDeviceKind.ThermalRelay:
                    foreach (var heater in ThermalRelayDefinition.Heaters) yield return heater;
                    yield return IsTripped ? new PortPair("97", "98") : new PortPair("95", "96");
                    break;
                case ElectricalDeviceKind.BrakeUnit:
                    if (IsClosed) yield return new PortPair("IN", "OUT");
                    break;
            }
        }

        public bool Evaluate(SimulationSnapshot snapshot, float deltaTime)
        {
            lastEvaluatedState = IsActive;
            if (ControlTarget != null)
            {
                IsActive = ControlTarget.IsPressed;
                IsPressed = ControlTarget.IsPressed;
                return lastEvaluatedState != IsActive;
            }
            if (PanelDefinition != null)
            {
                EvaluatePanel(snapshot);
                return lastEvaluatedState != IsActive;
            }
            switch (Kind)
            {
                case ElectricalDeviceKind.Contactor:
                    ContactorCoilVoltage = snapshot.GetAcVoltage(Port("A1"), Port("A2"));
                    IsActive = ContactorCoilVoltage == ContactorDefinition.RatedAcVoltage;
                    break;
                case ElectricalDeviceKind.IntermediateRelay:
                    RelayCoilVoltage = snapshot.GetDcVoltage(Port(IntermediateRelayDefinition.CoilPositive), Port(IntermediateRelayDefinition.CoilNegative));
                    IsActive = RelayCoilVoltage == IntermediateRelayDefinition.RatedDcVoltage;
                    break;
                case ElectricalDeviceKind.TimeRelay:
                    if (snapshot.HasControlVoltage(Port("A1"), Port("A2"))) timerElapsed += Math.Max(0f, deltaTime);
                    else timerElapsed = 0f;
                    IsActive = timerElapsed >= TimerDelaySeconds;
                    break;
                case ElectricalDeviceKind.Indicator:
                    IsActive = snapshot.HasControlVoltage(Port("L"), Port("N"));
                    break;
                case ElectricalDeviceKind.Motor:
                    var nextDirection = ResolveMotorDirection(snapshot);
                    IsActive = nextDirection != MotorDirection.Stopped;
                    MotorDirection = nextDirection;
                    break;
                case ElectricalDeviceKind.Breaker:
                case ElectricalDeviceKind.Fuse:
                    IsActive = IsClosed;
                    break;
                case ElectricalDeviceKind.PushButton:
                    IsActive = IsPressed;
                    break;
                case ElectricalDeviceKind.ThermalRelay:
                    IsActive = IsTripped;
                    break;
            }

            return lastEvaluatedState != IsActive;
        }

        public void ApplyVisualState(SimulationSnapshot snapshot)
        {
            VisualStateChanged?.Invoke(this);
        }

        private MotorDirection ResolveMotorDirection(SimulationSnapshot snapshot)
        {
            if (DeviceId == "M1" && (snapshot.IsDeviceActive("KB") || snapshot.IsDeviceActive("KMB")))
                return MotorDirection.Braking;
            if (snapshot.MotorDrives.TryGetValue(DeviceId, out var drive) && drive.Connected)
                return !drive.HasDrive || Math.Abs(drive.SpeedRpm) < 0.1f ? MotorDirection.Stopped :
                    drive.SpeedRpm < 0f ? MotorDirection.Reverse : MotorDirection.Forward;
            var u = snapshot.GetPotential(Port("U"));
            var v = snapshot.GetPotential(Port("V"));
            var w = snapshot.GetPotential(Port("W"));
            if (u == ElectricalPotential.PhaseL1 && v == ElectricalPotential.PhaseL2 && w == ElectricalPotential.PhaseL3)
                return MotorDirection.Forward;
            if (u == ElectricalPotential.PhaseL2 && v == ElectricalPotential.PhaseL1 && w == ElectricalPotential.PhaseL3)
                return MotorDirection.Reverse;
            return MotorDirection.Stopped;
        }

        private string Port(string name) => CircuitGraph.Port(DeviceId, name);

        public static ElectricalDeviceRuntime CreatePowerSource(string id = "POWER")
            => new ElectricalDeviceRuntime(id, ElectricalDeviceKind.PowerSource, new[] { "L1", "L2", "L3", "N", "PE" });

        public static ElectricalDeviceRuntime CreateBreaker(string id)
            => new ElectricalDeviceRuntime(id, ElectricalDeviceKind.Breaker, ThreePhasePorts());

        public static ElectricalDeviceRuntime CreateFuse(string id)
            => new ElectricalDeviceRuntime(id, ElectricalDeviceKind.Fuse, ThreePhasePorts());

        public static ElectricalDeviceRuntime CreatePushButton(string id, bool normallyClosed)
            => new ElectricalDeviceRuntime(id, ElectricalDeviceKind.PushButton, new[] { "COM", normallyClosed ? "NC" : "NO" })
            { IsNormallyClosedButton = normallyClosed };

        public static ElectricalDeviceRuntime CreateContactor(string id)
            => new ElectricalDeviceRuntime(id, ElectricalDeviceKind.Contactor, ContactorDefinition.Ports);

        public static ElectricalDeviceRuntime CreateIntermediateRelay(string id)
            => new ElectricalDeviceRuntime(id, ElectricalDeviceKind.IntermediateRelay, IntermediateRelayDefinition.Ports);

        public static ElectricalDeviceRuntime CreateThermalRelay(string id)
            => new ElectricalDeviceRuntime(id, ElectricalDeviceKind.ThermalRelay,
                ThermalRelayDefinition.Ports);

        public static ElectricalDeviceRuntime CreateTimeRelay(string id, float delay = 1f)
            => new ElectricalDeviceRuntime(id, ElectricalDeviceKind.TimeRelay,
                new[] { "A1", "A2", "15", "16", "18" }) { TimerDelaySeconds = delay };

        public static ElectricalDeviceRuntime CreateMotor(string id)
            => new ElectricalDeviceRuntime(id, ElectricalDeviceKind.Motor,
                new[] { "U", "V", "W", "U2", "V2", "W2" });

        public static ElectricalDeviceRuntime CreateIndicator(string id)
            => new ElectricalDeviceRuntime(id, ElectricalDeviceKind.Indicator, new[] { "L", "N" });

        private static IEnumerable<string> ThreePhasePorts()
            => new[] { "L1", "L2", "L3", "T1", "T2", "T3" };
    }
}
