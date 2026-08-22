using System;
using System.Collections.Generic;
using UnityEngine;

namespace ElectricalSim
{
    public enum SimulationMode
    {
        View,
        Drag,
        Wiring,
        Simulate,
        Fault
    }

    public enum ElectricalDeviceKind
    {
        PowerSource,
        Breaker,
        Fuse,
        PushButton,
        Contactor,
        ThermalRelay,
        TimeRelay,
        IntermediateRelay,
        Motor,
        Indicator,
        BrakeUnit,
        Terminal,
        Transformer,
        Rectifier,
        SelectorSwitch,
        Sensor,
        VariableFrequencyDrive,
        Plc,
        PlcIo
    }

    public enum ContactKind
    {
        NormallyOpen,
        NormallyClosed,
        AlwaysClosed
    }

    public enum InstrumentKind
    {
        Multimeter,
        VoltageProbe,
        Oscilloscope,
        Tachometer
    }

    public enum MeasurementKind
    {
        DcVoltage,
        AcVoltage,
        Resistance,
        Continuity,
        Frequency,
        RotationSpeed
    }

    public enum MotorDirection
    {
        Stopped,
        Forward,
        Reverse,
        Braking
    }

    [Serializable]
    public sealed class PortDefinition
    {
        public string Name = string.Empty;
        public string DisplayName = string.Empty;
        public Vector3 LocalPosition;
    }

    [Serializable]
    public sealed class ContactDefinition
    {
        public string PortA = string.Empty;
        public string PortB = string.Empty;
        public ContactKind Kind;
        public bool IsMainContact;
    }

    [CreateAssetMenu(menuName = "Electrical Sim/Device Definition", fileName = "DeviceDefinition")]
    public sealed class DeviceDefinition : ScriptableObject
    {
        public string TypeId = string.Empty;
        public string DisplayName = string.Empty;
        public ElectricalDeviceKind Kind;
        public List<PortDefinition> Ports = new List<PortDefinition>();
        public List<ContactDefinition> Contacts = new List<ContactDefinition>();
        public string CoilPortA = "A1";
        public string CoilPortB = "A2";
        public float TimerDelaySeconds = 1f;
        public GameObject VisualPrefab;
    }

    [Serializable]
    public sealed class PortPair
    {
        public string A = string.Empty;
        public string B = string.Empty;

        public PortPair() { }

        public PortPair(string a, string b)
        {
            A = a;
            B = b;
        }
    }

    [Serializable]
    public sealed class TaskActionStep
    {
        public string DeviceId = string.Empty;
        public bool Active;
        public float HoldSeconds = 0.1f;
        public string ExpectedDeviceId = string.Empty;
        public MotorDirection ExpectedMotorDirection;
    }

    [CreateAssetMenu(menuName = "Electrical Sim/Circuit Task", fileName = "CircuitTask")]
    public sealed class CircuitTaskDefinition : ScriptableObject
    {
        public string TaskId = string.Empty;
        public string DisplayName = string.Empty;
        [TextArea] public string Description = string.Empty;
        public List<PortPair> RequiredConnections = new List<PortPair>();
        public List<PortPair> ForbiddenConnections = new List<PortPair>();
        public List<TaskActionStep> ActionSteps = new List<TaskActionStep>();
    }

    public interface IElectricalDevice
    {
        string DeviceId { get; }
        ElectricalDeviceKind Kind { get; }
        IReadOnlyCollection<string> Ports { get; }
        bool IsActive { get; }
        IEnumerable<PortPair> GetConductiveLinks();
        bool Evaluate(SimulationSnapshot snapshot, float deltaTime);
        void ApplyVisualState(SimulationSnapshot snapshot);
    }

    public interface IElectricalInstrument
    {
        InstrumentKind Kind { get; }
        double Sample(MeasurementKind measurement, string portA, string portB, SimulationSnapshot snapshot);
    }
}
