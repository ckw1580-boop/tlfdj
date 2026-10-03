using System;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

namespace ElectricalSim.Tests
{
    public sealed class OscilloscopeMeasurementTests
    {
        private CircuitGraph graph;
        [SetUp] public void Setup()
        {
            graph = new CircuitGraph(); graph.RegisterDevice(ElectricalDeviceRuntime.CreatePowerSource());
            graph.RegisterDevice(new ElectricalDeviceRuntime("DC", ElectricalDeviceKind.PowerSource, new[] { "DC_POSITIVE", "DC_NEGATIVE" }) { IsDcSource = true });
            graph.RegisterDevice(new ElectricalDeviceRuntime("TB", ElectricalDeviceKind.Terminal, new[] { "A", "B" }));
        }
        private OscilloscopeSignal Read(string a, string b) => OscilloscopeMeasurement.Read(a, b, graph.Solve(0));
        [TestCase("POWER.L1", "POWER.N", 220)]
        [TestCase("POWER.L2", "POWER.N", 220)]
        [TestCase("POWER.L3", "POWER.N", 220)]
        [TestCase("POWER.L1", "POWER.L2", 380)]
        public void MainsWaveformHasExpectedRmsAndFrequency(string a, string b, double rms)
        {
            var s = Read(a, b); Assert.That(s.Valid, Is.True); Assert.That(s.FrequencyHz, Is.EqualTo(50));
            double sum = 0; for (var i = 0; i < 1000; i++) { var v = s.Sample(i * .02 / 1000, OscilloscopeCoupling.DC); sum += v * v; }
            Assert.That(Math.Sqrt(sum / 1000), Is.EqualTo(rms).Within(.001));
        }
        [Test] public void ChannelsPreservePhaseAndReversingLeadsNegatesWaveform()
        {
            var a = Read("POWER.L1", "POWER.N"); var b = Read("POWER.L2", "POWER.N");
            Assert.That(b.Phase - a.Phase, Is.EqualTo(-2 * Math.PI / 3).Within(1e-6));
            var forward = Read("POWER.L1", "POWER.L2"); var reverse = Read("POWER.L2", "POWER.L1");
            for (var i = 0; i < 17; i++) Assert.That(forward.Sample(i * .001, OscilloscopeCoupling.DC), Is.EqualTo(-reverse.Sample(i * .001, OscilloscopeCoupling.DC)).Within(1e-6));
        }
        [Test] public void DcPolarityAndCouplingAreExplicit()
        {
            var s = Read("DC.DC_POSITIVE", "DC.DC_NEGATIVE"); Assert.That(s.Dc, Is.EqualTo(24));
            Assert.That(s.Sample(.01, OscilloscopeCoupling.DC), Is.EqualTo(24)); Assert.That(s.Sample(.01, OscilloscopeCoupling.AC), Is.Zero);
            Assert.That(s.Rms(OscilloscopeCoupling.AC), Is.Zero); Assert.That(s.FrequencyHz, Is.Zero);
            Assert.That(Read("DC.DC_NEGATIVE", "DC.DC_POSITIVE").Dc, Is.EqualTo(-24));
        }
        [Test] public void AnalogSourcesNeedTheirOwnReferenceAndDoNotBecomeCommonGround()
        {
            graph.RegisterDevice(new Analog("A", -7.5)); graph.RegisterDevice(new Analog("B", 3));
            Assert.That(Read("A.P", "A.N").Dc, Is.EqualTo(-7.5)); Assert.That(Read("B.P", "B.N").Dc, Is.EqualTo(3));
            Assert.That(Read("A.P", "B.N").State, Is.EqualTo(OscilloscopeSignalState.UndefinedReference));
            Assert.That(graph.Wires.Count, Is.Zero); Assert.That(graph.Solve(0).SameNet("A.N", "B.N"), Is.False);
        }
        [TestCase(null, "POWER.N", OscilloscopeSignalState.MissingContact)]
        [TestCase("TB.A", "TB.B", OscilloscopeSignalState.UndefinedReference)]
        [TestCase("POWER.L1", "DC.DC_NEGATIVE", OscilloscopeSignalState.UndefinedReference)]
        [TestCase("TB.A", "TB.A", OscilloscopeSignalState.Valid)]
        public void InvalidReferencesAreNotZeroVoltReadings(string a, string b, OscilloscopeSignalState state) => Assert.That(Read(a, b).State, Is.EqualTo(state));
        [Test] public void ConflictAndNonconvergenceDoNotPoisonTheOtherChannel()
        {
            graph.AddWire("POWER.L1", "POWER.N", Color.red);
            Assert.That(Read("POWER.L1", "POWER.N").State, Is.EqualTo(OscilloscopeSignalState.Conflict));
            Assert.That(Read("DC.DC_POSITIVE", "DC.DC_NEGATIVE").Valid, Is.True);
            var snapshot = graph.Solve(0);
            typeof(SimulationSnapshot).GetProperty("IsConverged").GetSetMethod(true).Invoke(snapshot, new object[] { false });
            Assert.That(OscilloscopeMeasurement.Read("DC.DC_POSITIVE", "DC.DC_NEGATIVE", snapshot).State, Is.EqualTo(OscilloscopeSignalState.Unavailable));
        }
        [Test] public void StoppedInverterIsZeroAndDisconnectedWireLosesItsSignal()
        {
            graph.RegisterDevice(new InverterDriveRuntime("G120", () => 600, () => false));
            Assert.That(Read("G120.U2", "G120.V2").AcRms, Is.Zero);
            graph.AddWire("POWER.L1", "TB.A", Color.red); Assert.That(Read("TB.A", "POWER.N").AcRms, Is.EqualTo(220));
            graph.ClearWires(); Assert.That(Read("TB.A", "POWER.N").State, Is.EqualTo(OscilloscopeSignalState.UndefinedReference));
        }
        [Test] public void AutoScaleClippingAndDisabledChannelsHaveDefinedBehavior()
        {
            var frame = new OscilloscopeFrame(); frame.TimeIndex = 8; frame.AutoScale(); Assert.That(frame.TimeIndex, Is.EqualTo(8));
            frame.Channels[0].Signal = Read("POWER.L1", "POWER.L2"); frame.Channels[1].Signal = Read("DC.DC_POSITIVE", "DC.DC_NEGATIVE");
            frame.AutoScale(); frame.Render(); Assert.That(frame.Channels[0].Clipped, Is.False); Assert.That(frame.Channels[1].Clipped, Is.False);
            frame.Channels[0].VoltageIndex = 0; frame.Render(); Assert.That(frame.Channels[0].Clipped, Is.True);
            frame.Channels[0].Enabled = false; frame.Render(); Assert.That(frame.Channels[0].Clipped, Is.False); Assert.That(frame.Channels[0].Samples, Is.All.Zero);
            frame.Channels[1].Coupling = OscilloscopeCoupling.AC; frame.Render(); Assert.That(frame.Channels[1].Samples, Is.All.Zero);
        }
        private sealed class Analog : IElectricalDevice, IControlSignalSource
        {
            private readonly double value; public Analog(string id, double v) { DeviceId = id; value = v; }
            public string DeviceId { get; } public ElectricalDeviceKind Kind => ElectricalDeviceKind.Sensor;
            public IReadOnlyCollection<string> Ports => new[] { "P", "N" }; public bool IsActive => true;
            public IEnumerable<PortPair> CurrentReceivers => Array.Empty<PortPair>(); public IEnumerable<PortPair> GetConductiveLinks() => Array.Empty<PortPair>();
            public bool Evaluate(SimulationSnapshot s, float dt) => false; public void ApplyVisualState(SimulationSnapshot s) { }
            public IEnumerable<ControlSignal> GetControlSignals(SimulationSnapshot s) { yield return new ControlSignal(DeviceId + ".P", DeviceId + ".N", value); }
        }
    }
}
