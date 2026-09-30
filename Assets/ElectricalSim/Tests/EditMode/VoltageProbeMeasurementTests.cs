using System;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;

namespace ElectricalSim.Tests
{
    public sealed class VoltageProbeMeasurementTests
    {
        private CircuitGraph graph;
        private bool supply;
        private readonly ElectricalInstrument probe = new ElectricalInstrument(InstrumentKind.VoltageProbe);
        [SetUp]
        public void SetUp()
        {
            graph = new CircuitGraph(); supply = true;
            var power = ElectricalDeviceRuntime.CreatePowerSource(); power.SupplyEnabled = () => supply; graph.RegisterDevice(power);
            graph.RegisterDevice(new ElectricalDeviceRuntime("TERMINAL_BUS", ElectricalDeviceKind.PowerSource,
                new[] { "DC_POSITIVE", "DC_NEGATIVE" }) { IsDcSource = true, SupplyEnabled = () => supply });
            graph.RegisterDevice(new ElectricalDeviceRuntime("TB", ElectricalDeviceKind.Terminal, new[] { "A", "B" }));
        }
        private VoltageProbeReading Read(VoltageProbeMode mode, string port) => probe.MeasureVoltageProbe(mode, port, graph.Solve(0));
        [TestCase(VoltageProbeMode.AC, "POWER.L1", 220)]
        [TestCase(VoltageProbeMode.AC, "POWER.L2", 220)]
        [TestCase(VoltageProbeMode.AC, "POWER.L3", 220)]
        [TestCase(VoltageProbeMode.AC, "POWER.N", 0)]
        [TestCase(VoltageProbeMode.DC, "TERMINAL_BUS.DC_POSITIVE", 24)]
        [TestCase(VoltageProbeMode.DC, "TERMINAL_BUS.DC_NEGATIVE", 0)]
        public void MeasuresAgainstTheDeclaredSystemReference(VoltageProbeMode mode, string port, double expected)
        {
            var result = Read(mode, port);
            Assert.That(result.State, Is.EqualTo(VoltageProbeState.Valid));
            Assert.That(result.Value, Is.EqualTo(expected));
            Assert.That(result.ReferencePort, Is.EqualTo(mode == VoltageProbeMode.AC ? "POWER.N" : "TERMINAL_BUS.DC_NEGATIVE"));
            Assert.That(result.DisplayText, Does.EndWith(".0 V"));
        }
        [TestCase(VoltageProbeMode.AC, "TERMINAL_BUS.DC_POSITIVE")]
        [TestCase(VoltageProbeMode.AC, "TERMINAL_BUS.DC_NEGATIVE")]
        [TestCase(VoltageProbeMode.DC, "POWER.L1")]
        [TestCase(VoltageProbeMode.DC, "POWER.N")]
        public void WrongRangeDoesNotMasqueradeAsZero(VoltageProbeMode mode, string port)
        {
            var result = Read(mode, port);
            Assert.That(result.State, Is.EqualTo(VoltageProbeState.ModeMismatch));
            Assert.That(result.DisplayValue, Is.EqualTo("—"));
        }
        [Test]
        public void MissingContactFloatingPointAndDisabledReferenceAreDistinct()
        {
            Assert.That(Read(VoltageProbeMode.AC, null).State, Is.EqualTo(VoltageProbeState.MissingContact));
            Assert.That(Read(VoltageProbeMode.AC, "missing").State, Is.EqualTo(VoltageProbeState.MissingContact));
            Assert.That(Read(VoltageProbeMode.AC, "TB.A").State, Is.EqualTo(VoltageProbeState.UndefinedReference));
            Assert.That(probe.MeasureVoltageProbe(VoltageProbeMode.AC, "POWER.L1", null).State, Is.EqualTo(VoltageProbeState.Unavailable));
            supply = false;
            Assert.That(Read(VoltageProbeMode.AC, "POWER.L1").State, Is.EqualTo(VoltageProbeState.UndefinedReference));
            Assert.That(Read(VoltageProbeMode.DC, "TERMINAL_BUS.DC_POSITIVE").State, Is.EqualTo(VoltageProbeState.UndefinedReference));
            var empty = new CircuitGraph(); empty.RegisterDevice(new ElectricalDeviceRuntime("TB", ElectricalDeviceKind.Terminal, new[] { "A" }));
            Assert.That(probe.MeasureVoltageProbe(VoltageProbeMode.DC, "TB.A", empty.Solve(0)).State, Is.EqualTo(VoltageProbeState.UndefinedReference));
        }
        [Test]
        public void ContactAndWireChangesUpdateWithoutCreatingConnections()
        {
            var button = ElectricalDeviceRuntime.CreatePushButton("SB", false); graph.RegisterDevice(button);
            graph.AddWire("POWER.L1", "SB.COM", Color.red);
            var wire = graph.AddWire("SB.NO", "TB.A", Color.red);
            Assert.That(Read(VoltageProbeMode.AC, "TB.A").State, Is.EqualTo(VoltageProbeState.UndefinedReference));
            button.SetControl(true);
            Assert.That(Read(VoltageProbeMode.AC, "TB.A").Value, Is.EqualTo(220));
            Assert.That(graph.Wires.Count, Is.EqualTo(2));
            graph.RemoveWire(wire.Id);
            Assert.That(Read(VoltageProbeMode.AC, "TB.A").DisplayValue, Is.EqualTo("—"));
            graph.AddWire("POWER.N", "TB.A", Color.blue);
            Assert.That(Read(VoltageProbeMode.AC, "TB.A").Value, Is.Zero);
        }
        [TestCase(7.5)]
        [TestCase(-8.2)]
        public void AnalogVoltagesRequireACommonReferenceAndPreserveSign(double volts)
        {
            graph.RegisterDevice(new SignalSource("SIGNAL", volts));
            Assert.That(Read(VoltageProbeMode.DC, "SIGNAL.P").State, Is.EqualTo(VoltageProbeState.UndefinedReference));
            graph.AddWire("SIGNAL.M", VoltageProbeReading.DcReference, Color.black);
            Assert.That(Read(VoltageProbeMode.DC, "SIGNAL.P").Value, Is.EqualTo(volts).Within(0.00001));
            Assert.That(Read(VoltageProbeMode.AC, "SIGNAL.P").State, Is.EqualTo(VoltageProbeState.ModeMismatch));
        }
        [Test]
        public void ConflictsUnsupportedOutputsAndNonconvergenceAreExplicit()
        {
            graph.AddWire("POWER.L1", "POWER.N", Color.red);
            Assert.That(Read(VoltageProbeMode.AC, "POWER.L1").DisplayValue, Is.EqualTo("Err"));
            SetUp();
            graph.RegisterDevice(new SignalSource("SIGNAL", 7));
            graph.AddWire("SIGNAL.M", VoltageProbeReading.DcReference, Color.black);
            graph.AddWire("SIGNAL.P", VoltageProbeReading.DcReference, Color.black);
            Assert.That(Read(VoltageProbeMode.DC, "SIGNAL.P").State, Is.EqualTo(VoltageProbeState.Conflict));
            SetUp();
            graph.RegisterDevice(new InverterDriveRuntime("G120", () => 600, () => false));
            Assert.That(Read(VoltageProbeMode.AC, "G120.U2").State, Is.EqualTo(VoltageProbeState.Unsupported));
            var snapshot = graph.Solve(0);
            typeof(SimulationSnapshot).GetProperty("IsConverged").GetSetMethod(true).Invoke(snapshot, new object[] { false });
            Assert.That(probe.MeasureVoltageProbe(VoltageProbeMode.AC, "POWER.L1", snapshot).State, Is.EqualTo(VoltageProbeState.Unavailable));
        }
        private sealed class SignalSource : IElectricalDevice, IControlSignalSource
        {
            private readonly double volts;
            public SignalSource(string id, double value) { DeviceId = id; volts = value; }
            public string DeviceId { get; }
            public ElectricalDeviceKind Kind => ElectricalDeviceKind.Sensor;
            public IReadOnlyCollection<string> Ports => new[] { "P", "M" };
            public bool IsActive => true;
            public IEnumerable<PortPair> CurrentReceivers => Array.Empty<PortPair>();
            public IEnumerable<PortPair> GetConductiveLinks() => Array.Empty<PortPair>();
            public bool Evaluate(SimulationSnapshot snapshot, float deltaTime) => false;
            public void ApplyVisualState(SimulationSnapshot snapshot) { }
            public IEnumerable<ControlSignal> GetControlSignals(SimulationSnapshot topology)
            { yield return new ControlSignal(DeviceId + ".P", DeviceId + ".M", volts); }
        }
    }
}
