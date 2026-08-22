using System;
using System.Collections.Generic;
using System.Linq;

namespace ElectricalSim
{
    public sealed class ElectricalDeviceRuntime : IElectricalDevice
    {
        private readonly List<string> ports;
        private float timerElapsed;
        private bool lastEvaluatedState;
        private bool manualOverride;

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
        public bool IsActive { get; private set; }
        public bool IsClosed { get; private set; }
        public bool IsTripped { get; private set; }
        public bool IsPressed { get; private set; }
        public bool IsNormallyClosedButton { get; set; }
        public float TimerDelaySeconds { get; set; } = 1f;
        public MotorDirection MotorDirection { get; private set; }
        public Action<ElectricalDeviceRuntime> VisualStateChanged;

        public void SetControl(bool active)
        {
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
                case ElectricalDeviceKind.Contactor:
                case ElectricalDeviceKind.IntermediateRelay:
                case ElectricalDeviceKind.BrakeUnit:
                    manualOverride = active;
                    break;
            }
        }

        public IEnumerable<PortPair> GetConductiveLinks()
        {
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
                case ElectricalDeviceKind.IntermediateRelay:
                    if (IsActive)
                    {
                        yield return new PortPair("L1", "T1");
                        yield return new PortPair("L2", "T2");
                        yield return new PortPair("L3", "T3");
                        yield return new PortPair("13", "14");
                    }
                    else
                    {
                        yield return new PortPair("21", "22");
                    }
                    break;
                case ElectricalDeviceKind.TimeRelay:
                    if (IsActive) yield return new PortPair("15", "18");
                    else yield return new PortPair("15", "16");
                    break;
                case ElectricalDeviceKind.ThermalRelay:
                    if (!IsTripped)
                    {
                        yield return new PortPair("L1", "T1");
                        yield return new PortPair("L2", "T2");
                        yield return new PortPair("L3", "T3");
                        yield return new PortPair("95", "96");
                    }
                    else
                    {
                        yield return new PortPair("97", "98");
                    }
                    break;
                case ElectricalDeviceKind.BrakeUnit:
                    if (IsClosed) yield return new PortPair("IN", "OUT");
                    break;
            }
        }

        public bool Evaluate(SimulationSnapshot snapshot, float deltaTime)
        {
            lastEvaluatedState = IsActive;
            switch (Kind)
            {
                case ElectricalDeviceKind.Contactor:
                case ElectricalDeviceKind.IntermediateRelay:
                    IsActive = manualOverride || snapshot.HasControlVoltage(Port("A1"), Port("A2"));
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
            if (snapshot.IsDeviceActive("KB") || snapshot.IsDeviceActive("KMB"))
                return MotorDirection.Braking;
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
            => new ElectricalDeviceRuntime(id, ElectricalDeviceKind.Contactor,
                ThreePhasePorts().Concat(new[] { "A1", "A2", "13", "14", "21", "22" }));

        public static ElectricalDeviceRuntime CreateThermalRelay(string id)
            => new ElectricalDeviceRuntime(id, ElectricalDeviceKind.ThermalRelay,
                ThreePhasePorts().Concat(new[] { "95", "96", "97", "98" }));

        public static ElectricalDeviceRuntime CreateTimeRelay(string id, float delay = 1f)
            => new ElectricalDeviceRuntime(id, ElectricalDeviceKind.TimeRelay,
                new[] { "A1", "A2", "15", "16", "18" }) { TimerDelaySeconds = delay };

        public static ElectricalDeviceRuntime CreateMotor(string id)
            => new ElectricalDeviceRuntime(id, ElectricalDeviceKind.Motor, new[] { "U", "V", "W", "PE" });

        public static ElectricalDeviceRuntime CreateIndicator(string id)
            => new ElectricalDeviceRuntime(id, ElectricalDeviceKind.Indicator, new[] { "L", "N" });

        private static IEnumerable<string> ThreePhasePorts()
            => new[] { "L1", "L2", "L3", "T1", "T2", "T3" };
    }
}
