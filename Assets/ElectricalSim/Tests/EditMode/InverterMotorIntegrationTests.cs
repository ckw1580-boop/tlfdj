using System.Linq;
using NUnit.Framework;
using UnityEngine;
using Object = UnityEngine.Object;

namespace ElectricalSim.Tests
{
    public sealed class InverterMotorIntegrationTests
    {
        private GameObject host;
        private InverterPanelController panel;
        private InverterDriveRuntime drive;
        private CircuitGraph graph;

        [SetUp]
        public void Setup()
        {
            host = new GameObject("Motor inverter integration");
            panel = host.AddComponent<InverterPanelController>();
            panel.Initialize(null, null);
            panel.TrySetParameter("P1120", 0);
            panel.ToggleHandAuto();
            drive = new InverterDriveRuntime(panel);
            graph = new CircuitGraph();
            graph.RegisterDevice(ElectricalDeviceRuntime.CreatePowerSource());
            graph.RegisterDevice(drive);
            foreach (var phase in new[] { "L1", "L2", "L3" }) Wire("POWER." + phase, "G120." + phase);
        }

        [TearDown] public void TearDown() => Object.DestroyImmediate(host);
        private void Wire(string a, string b) => graph.AddWire(a, b, Color.red);
        private void Run(float command) { panel.TrySetParameter("SP", command); panel.PressRun(); }
        private ElectricalDeviceRuntime ConnectDelta(string id)
        {
            var motor = ElectricalDeviceRuntime.CreateMotor(id);
            graph.RegisterDevice(motor);
            Wire("G120.U2", id + ".U"); Wire("G120.V2", id + ".V"); Wire("G120.W2", id + ".W");
            Wire(id + ".U", id + ".W2"); Wire(id + ".V", id + ".U2"); Wire(id + ".W", id + ".V2");
            return motor;
        }

        [Test]
        public void DefaultsMatchTeachingMotorAndNonFiniteParametersAreRejected()
        {
            Assert.That(panel.TryGetParameter("P304", out var voltage), Is.True);
            Assert.That(voltage, Is.EqualTo(380));
            panel.TryGetParameter("P311", out var speed);
            Assert.That(speed, Is.EqualTo(1450));
            Assert.That(panel.TrySetParameter("P310", float.NaN), Is.False);
            Assert.That(panel.TrySetParameter("P311", float.PositiveInfinity), Is.False);
        }

        [Test]
        public void RampCommandCreatesFrequencyAndVoltageWhileActualShaftAccelerates()
        {
            ConnectDelta("M1"); Run(725);
            var snapshot = graph.Solve(.02f);
            Assert.That(panel.OutputSpeedRpm, Is.EqualTo(725));
            Assert.That(drive.OutputFrequencyHz, Is.EqualTo(25).Within(.01));
            Assert.That(drive.OutputLineVoltage, Is.EqualTo(190).Within(.01));
            Assert.That(snapshot.GetMotorState("M1").SynchronousSpeedRpm, Is.EqualTo(750).Within(.01));
            Assert.That(panel.ActualSpeedRpm, Is.GreaterThan(0).And.LessThan(725));
            Assert.That(panel.ActualSpeedRpm, Is.EqualTo(snapshot.GetMotorSpeedRpm("M1")));
        }

        [Test]
        public void FrequencyUsesMotorParametersAndVoltageSaturatesAboveBaseFrequency()
        {
            ConnectDelta("M1");
            panel.TrySetParameter("P311", 725); Run(1450); graph.Solve(.02f);
            Assert.That(drive.OutputFrequencyHz, Is.EqualTo(100).Within(.01));
            Assert.That(drive.OutputLineVoltage, Is.EqualTo(380).Within(.01));
            panel.SetFault(true); graph.Solve(.02f);
            Assert.That(drive.OutputFrequencyHz, Is.Zero);
            Assert.That(drive.OutputLineVoltage, Is.Zero);
        }

        [Test]
        public void HighSpeedDoubleMotorCanBeFedThroughItsSecondTerminalRow()
        {
            graph.RegisterDevice(ElectricalDeviceRuntime.CreateMotor("M_DOUBLE"));
            Wire("M_DOUBLE.U", "M_DOUBLE.V"); Wire("M_DOUBLE.V", "M_DOUBLE.W");
            Wire("G120.U2", "M_DOUBLE.U2"); Wire("G120.V2", "M_DOUBLE.V2"); Wire("G120.W2", "M_DOUBLE.W2");
            Run(1450); var state = graph.Solve(.02f).GetMotorState("M_DOUBLE");
            Assert.That(state.Connection.Kind, Is.EqualTo(MotorConnectionKind.DoubleHigh));
            Assert.That(state.SourceId, Is.EqualTo("G120"));
            Assert.That(state.SynchronousSpeedRpm, Is.EqualTo(3000).Within(.01));
            Assert.That(state.SpeedRpm, Is.GreaterThan(0));
        }

        [Test]
        public void CurrentFeedbackUsesLoadUntilExplicitTestOverrideIsEnabled()
        {
            ConnectDelta("M1"); Run(1450);
            drive.Control.SimulatedMotorCurrent = 77;
            var snapshot = graph.Solve(.02f);
            Assert.That(drive.Control.UseSimulatedMotorCurrent, Is.False);
            Assert.That(drive.Control.ComputedMotorCurrent, Is.EqualTo(snapshot.GetMotorState("M1").CurrentAmps).Within(.001));
            Assert.That(drive.Control.EffectiveMotorCurrent, Is.Not.EqualTo(77));
            drive.Control.UseSimulatedMotorCurrent = true;
            graph.Solve(0);
            Assert.That(drive.Control.EffectiveMotorCurrent, Is.EqualTo(77));
            Assert.That(graph.Solve(0).GetMotorState("M1").ThermalState, Is.EqualTo(snapshot.GetMotorState("M1").ThermalState));
        }

        [Test]
        public void MultipleMotorsExposeIndividualSpeedsAndAggregateCurrentWithoutASingleAxisFeedback()
        {
            ConnectDelta("M1"); ConnectDelta("M2"); Run(1450);
            var snapshot = graph.Solve(.02f);
            Assert.That(panel.ConnectedMotorSpeeds.Keys, Is.EquivalentTo(new[] { "M1", "M2" }));
            Assert.That(panel.HasSingleMotorFeedback, Is.False);
            Assert.That(panel.ActualSpeedRpm, Is.Zero);
            Assert.That(drive.Control.Outputs[0].Reading.State, Is.EqualTo(ControlSignalState.Floating));
            Assert.That(drive.Control.ComputedMotorCurrent, Is.EqualTo(snapshot.GetMotorState("M1").CurrentAmps + snapshot.GetMotorState("M2").CurrentAmps).Within(.001));
        }

        [Test]
        public void ParameterMismatchIsReportedWithoutChangingMotorConfiguration()
        {
            var motor = ConnectDelta("M1");
            panel.TrySetParameter("P304", 400);
            var snapshot = graph.Solve(0);
            Assert.That(drive.Control.MotorParameterDiagnostics.Any(d => d.Contains("P304")), Is.True);
            Assert.That(snapshot.Diagnostics.Any(d => d.Contains("P304")), Is.True);
            Assert.That(motor.MotorConfiguration.RatedVoltageDelta, Is.EqualTo(380));
        }
    }
}
