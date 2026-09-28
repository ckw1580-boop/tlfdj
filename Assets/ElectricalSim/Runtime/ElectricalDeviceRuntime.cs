using System;
using System.Collections.Generic;
using System.Linq;

namespace ElectricalSim
{
    public sealed partial class ElectricalDeviceRuntime : IElectricalDevice, IElectricalSource
    {
        private readonly List<string> ports;
        private readonly List<PortPair> fixedLinks = new List<PortPair>();
        private IReadOnlyList<PortPair> breakerContacts;
        private bool lastEvaluatedState;

        public ElectricalDeviceRuntime(string deviceId, ElectricalDeviceKind kind, IEnumerable<string> portNames)
        {
            DeviceId = deviceId;
            Kind = kind;
            ports = portNames.Distinct().ToList();
            IsClosed = kind == ElectricalDeviceKind.Breaker || kind == ElectricalDeviceKind.Fuse;
            if (kind == ElectricalDeviceKind.Motor) MotorConfiguration = MotorConfiguration.CreateDefault(deviceId);
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
        public double RelayCoilVoltage { get; private set; }
        public double ContactorCoilVoltage { get; private set; }
        public MotorDirection MotorDirection { get; private set; }
        public float ActualSpeedRpm { get; private set; }
        public float CoastStopSeconds { get; set; } = 3f;
        public float BrakeStopSeconds { get; set; } = 1f;
        public MotorConfiguration MotorConfiguration { get; private set; }
        public MotorRuntimeState MotorState { get; private set; } = new MotorRuntimeState();

        public void ConfigureMotor(MotorConfiguration configuration)
        {
            if (Kind != ElectricalDeviceKind.Motor) throw new InvalidOperationException("Only motors have a motor configuration.");
            if (configuration == null) throw new ArgumentNullException(nameof(configuration));
            if (!configuration.Validate(out var error)) throw new ArgumentException(error, nameof(configuration));
            MotorConfiguration = configuration.Clone();
            ResetMotorSpeed();
        }

        public void SetMotorLoad(float factor)
        {
            if (Kind != ElectricalDeviceKind.Motor || float.IsNaN(factor) || float.IsInfinity(factor) || factor < 0f)
                throw new ArgumentOutOfRangeException(nameof(factor));
            MotorConfiguration.LoadFactor = factor;
        }

        public void SetMotorStalled(bool stalled)
        {
            if (Kind != ElectricalDeviceKind.Motor) throw new InvalidOperationException("Only motors can be stalled.");
            MotorState.IsStalled = stalled;
            if (stalled) MotorState.SpeedRpm = ActualSpeedRpm = 0f;
        }

        public void ResetMotorSpeed()
        {
            ActualSpeedRpm = 0f;
            MotorState = new MotorRuntimeState();
            MotorDirection = MotorDirection.Stopped;
        }

        internal void AdvanceMotorSpeed(SimulationSnapshot snapshot, float deltaTime)
        {
            MotorState.Connection = MotorConnectionResolver.Resolve(DeviceId, MotorConfiguration, snapshot);
            MotorPhysics.Advance(MotorConfiguration, MotorState, deltaTime,
                MotorDirection == MotorDirection.Braking, CoastStopSeconds, BrakeStopSeconds);
            ActualSpeedRpm = MotorState.SpeedRpm;
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
                    TrySetThermalTripped(active);
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
                        if (breakerContacts != null)
                        {
                            foreach (var contact in breakerContacts) yield return contact;
                        }
                        else
                        {
                            yield return new PortPair("L1", "T1");
                            yield return new PortPair("L2", "T2");
                            yield return new PortPair("L3", "T3");
                        }
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
                case ElectricalDeviceKind.Indicator:
                    IsActive = snapshot.HasControlVoltage(Port("L"), Port("N"));
                    break;
                case ElectricalDeviceKind.Motor:
                    MotorState.Connection = MotorConnectionResolver.Resolve(DeviceId, MotorConfiguration, snapshot);
                    var nextDirection = ResolveMotorDirection(snapshot);
                    IsActive = nextDirection != MotorDirection.Stopped;
                    MotorDirection = nextDirection;
                    MotorPhysics.Refresh(MotorConfiguration, MotorState, nextDirection == MotorDirection.Braking);
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
            var connection = MotorState.Connection;
            if (!connection.IsValid || !connection.Energized || connection.DirectionSign == 0 ||
                connection.PhaseLoss && Math.Abs(ActualSpeedRpm) < 1f) return MotorDirection.Stopped;
            return connection.DirectionSign < 0 ? MotorDirection.Reverse : MotorDirection.Forward;
        }

        private string Port(string name) => CircuitGraph.Port(DeviceId, name);

        public static ElectricalDeviceRuntime CreatePowerSource(string id = "POWER")
            => new ElectricalDeviceRuntime(id, ElectricalDeviceKind.PowerSource, new[] { "L1", "L2", "L3", "N", "PE" });

        public static ElectricalDeviceRuntime CreateBreaker(string id)
            => new ElectricalDeviceRuntime(id, ElectricalDeviceKind.Breaker, ThreePhasePorts());

        public static ElectricalDeviceRuntime CreateBreaker(string id, IEnumerable<PortPair> contacts)
        {
            if (contacts == null) throw new ArgumentNullException(nameof(contacts));
            var pairs = contacts.ToArray();
            if (pairs.Length == 0 || pairs.Any(p => string.IsNullOrWhiteSpace(p.A) ||
                string.IsNullOrWhiteSpace(p.B) || p.A == p.B))
                throw new ArgumentException("Breaker contacts require distinct named terminals.", nameof(contacts));
            return new ElectricalDeviceRuntime(id, ElectricalDeviceKind.Breaker,
                pairs.SelectMany(p => new[] { p.A, p.B })) { breakerContacts = Array.AsReadOnly(pairs) };
        }

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


        public static ElectricalDeviceRuntime CreateMotor(string id)
            => new ElectricalDeviceRuntime(id, ElectricalDeviceKind.Motor,
                new[] { "U", "V", "W", "U2", "V2", "W2" });

        public static ElectricalDeviceRuntime CreateIndicator(string id)
            => new ElectricalDeviceRuntime(id, ElectricalDeviceKind.Indicator, new[] { "L", "N" });

        private static IEnumerable<string> ThreePhasePorts()
            => new[] { "L1", "L2", "L3", "T1", "T2", "T3" };
    }
}
