using System;
using NUnit.Framework;
using UnityEngine;

namespace ElectricalSim.Tests
{
    public sealed class InverterVoltageTests
    {
        private CircuitGraph graph;
        private InverterDriveRuntime drive;
        private float speed;
        [SetUp] public void Setup()
        {
            graph = new CircuitGraph(); graph.RegisterDevice(ElectricalDeviceRuntime.CreatePowerSource());
            drive = new InverterDriveRuntime("VFD", () => speed, () => false); graph.RegisterDevice(drive);
            foreach (var phase in new[] { "L1", "L2", "L3" }) graph.AddWire("POWER." + phase, "VFD." + phase, Color.red);
        }
        private OscilloscopeSignal Read(float hz, float dt = 0)
        { speed = hz * 1450 / 50; return graph.Solve(dt).ReadVoltage("VFD.U2", "VFD.V2"); }
        [TestCase(0, 0)] [TestCase(5, 38)] [TestCase(25, 190)] [TestCase(50, 380)] [TestCase(75, 380)]
        public void VoltageAndPwmFundamentalMatchVf(float hz, double rms)
        {
            var s = Read(hz); Assert.That(s.Valid, Is.True); Assert.That(s.AcRms, Is.EqualTo(rms).Within(.001));
            Assert.That(s.FrequencyHz, Is.EqualTo(hz).Within(.001));
            if (hz == 0) { Assert.That(s.Inverter.Pwm(0, 1, .001), Is.Zero); return; }
            // Integrate the exact rectangular intervals against sine/cosine, avoiding sample aliasing.
            var duration = 3d / hz; var omega = 2 * Math.PI * hz; double sine = 0, cosine = 0;
            s.Inverter.Segments(0, 1, duration, (a, b, v) => {
                Assert.That(v == 0 || Math.Abs(v) == s.Inverter.DcBus, Is.True);
                sine += v * (Math.Cos(omega * a) - Math.Cos(omega * b)) / omega;
                cosine += v * (Math.Sin(omega * b) - Math.Sin(omega * a)) / omega;
            });
            var measured = Math.Sqrt(2) * Math.Sqrt(sine * sine + cosine * cosine) / duration;
            Assert.That(measured, Is.EqualTo(rms).Within(rms * .01));
        }
        [Test] public void QueriesZeroSolvesAndOldSnapshotsNeverAdvancePhase()
        {
            var old = Read(25, .007f); var value = old.Sample(0, OscilloscopeCoupling.DC);
            var phase = old.Inverter.Phase;
            Assert.That(old.Sample(0, OscilloscopeCoupling.DC), Is.EqualTo(Math.Sqrt(2) * old.AcRms * Math.Sin(old.Phase)).Within(1e-6));
            for (var i = 0; i < 10; i++) Assert.That(Read(25).Inverter.Phase, Is.EqualTo(phase));
            Read(25, .013f); Assert.That(old.Sample(0, OscilloscopeCoupling.DC), Is.EqualTo(value));
            drive.ResetWaveform(); var once = Read(25, .02f);
            drive.ResetWaveform(); Read(25, .01f); var split = Read(25, .01f);
            Assert.That(split.Inverter.Phase, Is.EqualTo(once.Inverter.Phase).Within(1e-6));
            Assert.That(split.Inverter.Clock, Is.EqualTo(once.Inverter.Clock).Within(1e-8));
            drive.ResetWaveform(); Assert.That(Read(25).Inverter.Phase, Is.Zero);
        }
        [Test] public void PolarityDirectionAndThreePhaseRelationshipArePreserved()
        {
            var s = Read(25); var snapshot = graph.Solve(0); var reverse = snapshot.ReadVoltage("VFD.V2", "VFD.U2");
            var vw = snapshot.ReadVoltage("VFD.V2", "VFD.W2"); var wu = snapshot.ReadVoltage("VFD.W2", "VFD.U2");
            for (var i = 0; i < 30; i++) {
                var t = i * .0011;
                Assert.That(s.Sample(t, OscilloscopeCoupling.DC), Is.EqualTo(-reverse.Sample(t, OscilloscopeCoupling.DC)).Within(1e-6));
                Assert.That(s.Inverter.Pwm(0, 1, t), Is.EqualTo(-s.Inverter.Pwm(1, 0, t)));
                Assert.That(s.Sample(t, OscilloscopeCoupling.DC) + vw.Sample(t, OscilloscopeCoupling.DC) + wu.Sample(t, OscilloscopeCoupling.DC), Is.Zero.Within(1e-6));
            }
            var backward = Read(-25); Assert.That(backward.AcRms, Is.EqualTo(s.AcRms));
            Assert.That(backward.Sample(.003, OscilloscopeCoupling.DC), Is.EqualTo(s.Sample(-.003, OscilloscopeCoupling.DC)).Within(1e-5));
        }
        [TestCase("VFD.V2")] [TestCase("POWER.PE")] [TestCase("POWER.L1")]
        public void OutputFaultStopsDriveAndDoesNotAffectOtherChannels(string target)
        {
            Read(25); graph.AddWire("VFD.U2", target, Color.red); var snapshot = graph.Solve(0);
            Assert.That(drive.IsActive, Is.False); Assert.That(snapshot.ReadVoltage("VFD.U2", "VFD.W2").State, Is.EqualTo(OscilloscopeSignalState.Conflict));
            Assert.That(snapshot.ReadVoltage("POWER.L2", "POWER.N").Valid, Is.True);
            Assert.That(snapshot.GetAcVoltage("VFD.U2", "VFD.W2"), Is.NaN);
        }
        [Test] public void MultipleSourcesAndIndependentReferencesAreExplicit()
        {
            Read(25); graph.RegisterDevice(new InverterDriveRuntime("B", () => speed, () => false));
            var separate = graph.Solve(0); Assert.That(separate.ReadVoltage("VFD.U2", "B.V2").State, Is.EqualTo(OscilloscopeSignalState.UndefinedReference));
            graph.AddWire("VFD.U2", "B.U2", Color.red); var mixed = graph.Solve(0);
            Assert.That(drive.IsActive, Is.False); Assert.That(mixed.ReadVoltage("VFD.V2", "VFD.W2").State, Is.EqualTo(OscilloscopeSignalState.Conflict));
        }
        [Test] public void TerminalsDisconnectStopAndMetersUseCapturedValues()
        {
            graph.RegisterDevice(new ElectricalDeviceRuntime("TB", ElectricalDeviceKind.Terminal, new[] { "A", "B" }));
            graph.AddWire("VFD.U2", "TB.A", Color.red); Read(25); var snapshot = graph.Solve(0);
            var meter = new ElectricalInstrument(InstrumentKind.Multimeter);
            var ac = meter.Measure(MultimeterMode.AcVoltage, "TB.A", "VFD.V2", snapshot);
            Assert.That(ac.Value, Is.EqualTo(190).Within(.001)); Assert.That(ac.Hint, Is.EqualTo("变频基波"));
            Assert.That(meter.Measure(MultimeterMode.DcVoltage, "TB.A", "VFD.V2", snapshot).Value, Is.Zero);
            Assert.That(meter.Measure(MultimeterMode.Continuity, "TB.A", "VFD.V2", snapshot).State, Is.EqualTo(MultimeterReadingState.Energized));
            Assert.That(snapshot.ReadVoltage("VFD.U2", "POWER.N").State, Is.EqualTo(OscilloscopeSignalState.UndefinedReference));
            Assert.That(snapshot.GetDcVoltage("VFD.U2", "POWER.PE"), Is.NaN);
            Assert.That(Read(0).AcRms, Is.Zero);
            graph.ClearWires(); Assert.That(graph.Solve(0).ReadVoltage("TB.A", "VFD.V2").State, Is.EqualTo(OscilloscopeSignalState.UndefinedReference));
        }
        [Test] public void DisplayPreservesPulsesModesAndHighFrequencyBoundary()
        {
            var frame = new OscilloscopeFrame(); var c = frame.Channels[0]; c.Signal = Read(5); frame.TimeIndex = 9;
            frame.Render(); Assert.That(c.PwmIntervals.Count, Is.GreaterThan(1600));
            Assert.That(c.PwmPeakToPeak, Is.EqualTo(2 * c.Signal.Inverter.DcBus).Within(.001));
            Assert.That(Array.Exists(c.PwmMax, v => v > 500), Is.True);
            c.Waveform = OscilloscopeWaveform.Fundamental; frame.AutoScale(); frame.Render(); Assert.That(c.PwmIntervals.Count, Is.Zero);
            c.Signal = Read(2000); c.Waveform = OscilloscopeWaveform.PWM; frame.Render(); Assert.That(c.DrawFundamental, Is.False); Assert.That(c.DrawPwm, Is.False);
            Assert.That(frame.DescribeChannel(0), Does.Contain("超出教学 PWM 模型范围"));
            c.Waveform = OscilloscopeWaveform.Overlay; frame.Render(); Assert.That(c.DrawFundamental, Is.True);
        }
        [Test] public void SourceFollowsClosedContactorAndLosesReferenceWhenOpened()
        {
            var km = ElectricalDeviceRuntime.CreateContactor("KM"); graph.RegisterDevice(km);
            graph.AddWire("POWER.L1", "KM.A1", Color.red); graph.AddWire("POWER.N", "KM.A2", Color.blue);
            graph.AddWire("VFD.U2", "KM.L1", Color.yellow); Read(25);
            var s = graph.Solve(0); Assert.That(s.ReadVoltage("KM.T1", "VFD.V2").AcRms, Is.EqualTo(190).Within(.001));
            graph.RemoveWire(graph.Wires[3].Id);
            s = graph.Solve(0); Assert.That(s.ReadVoltage("KM.T1", "VFD.V2").State, Is.EqualTo(OscilloscopeSignalState.UndefinedReference));
        }
    }
}
