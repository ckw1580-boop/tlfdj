using System;
using NUnit.Framework;
using UnityEngine;

namespace ElectricalSim.Tests
{
    public sealed class MotorSpeedTests
    {
        private CircuitGraph graph;
        private ElectricalDeviceRuntime motor;
        private float output;
        private bool fault;

        [SetUp]
        public void SetUp()
        {
            output = 600f;
            fault = false;
            graph = new CircuitGraph();
            graph.RegisterDevice(ElectricalDeviceRuntime.CreatePowerSource());
            motor = ElectricalDeviceRuntime.CreateMotor("M1");
            graph.RegisterDevice(motor);
            graph.RegisterDevice(ElectricalDeviceRuntime.CreateMotor("M2"));
            graph.RegisterDevice(new InverterDriveRuntime("G120", () => output, () => fault));
            foreach (var phase in new[] { "L1", "L2", "L3" }) Wire("POWER." + phase, "G120." + phase);
        }
        private WireConnection Wire(string a, string b) => graph.AddWire(a, b, Color.red);
        private void Connect(string id = "M1", bool reverse = false)
        {
            Wire("G120.U2", id + (reverse ? ".V" : ".U"));
            Wire("G120.V2", id + (reverse ? ".U" : ".V"));
            Wire("G120.W2", id + ".W");
        }
        [Test]
        public void SelectedMotorReadsActualOutputAndUnwiredMotorStaysStopped()
        {
            Connect();
            foreach (var speed in new[] { 30f, 600f, 900f, 350f, 0f, -500f })
            {
                output = speed;
                var snapshot = graph.Solve();
                Assert.That(snapshot.GetMotorSpeedRpm("M1"), Is.EqualTo(speed));
                Assert.That(snapshot.GetMotorSpeedRpm("M2"), Is.Zero);
                Assert.That(new ElectricalInstrument(InstrumentKind.Tachometer).SampleMotorSpeed("M1", snapshot), Is.EqualTo(speed));
            }
        }
        [Test]
        public void SwappingOutputPhasesReversesTheMotor()
        {
            Connect(reverse: true);
            var snapshot = graph.Solve();
            Assert.That(snapshot.GetMotorSpeedRpm("M1"), Is.EqualTo(-600f));
            Assert.That(snapshot.GetMotorDirection("M1"), Is.EqualTo(MotorDirection.Reverse));
        }
        [TestCase("G120.L1")]
        [TestCase("M1.U")]
        public void MissingInputOrOutputPhasePreventsStarting(string disconnected)
        {
            Connect();
            var wire = System.Linq.Enumerable.First(graph.Wires, w => w.EndPort == disconnected);
            graph.RemoveWire(wire.Id);
            Assert.That(graph.Solve().GetMotorSpeedRpm("M1"), Is.Zero);
        }
        [TestCase("POWER.L1", "G120.U2")]
        [TestCase("G120.V2", "G120.U2")]
        [TestCase("POWER.PE", "G120.U2")]
        public void UnsafeOutputConnectionPreventsDriveAndReportsError(string a, string b)
        {
            Connect();
            Wire(a, b);
            var snapshot = graph.Solve();
            Assert.That(snapshot.Errors, Is.Not.Empty);
            Assert.That(snapshot.GetMotorSpeedRpm("M1"), Is.Zero);
        }
        [Test]
        public void LossOfDriveCoastsForThreeSecondsAndDoesNotAdvanceDuringConvergence()
        {
            Connect();
            graph.Solve();
            fault = true;
            Assert.That(graph.Solve(0.5f, 32).GetMotorSpeedRpm("M1"), Is.EqualTo(500f).Within(0.01f));
            Assert.That(graph.Solve(2.5f).GetMotorSpeedRpm("M1"), Is.Zero);
        }
        [Test]
        public void ContactorsAndTerminalLinksAreUsedToResolveDrive()
        {
            var contactor = ElectricalDeviceRuntime.CreateContactor("KM");
            graph.RegisterDevice(contactor);
            var terminal = new ElectricalDeviceRuntime("TB", ElectricalDeviceKind.Terminal, new[] { "U" });
            terminal.AddFixedLink("U", "G120.U2");
            graph.RegisterDevice(terminal);
            Wire("TB.U", "KM.L1"); Wire("G120.V2", "KM.L2"); Wire("G120.W2", "KM.L3");
            Wire("KM.T1", "M1.U"); Wire("KM.T2", "M1.V"); Wire("KM.T3", "M1.W");
            Assert.That(graph.Solve().GetMotorSpeedRpm("M1"), Is.Zero);
            contactor.SetControl(true);
            Assert.That(graph.Solve().GetMotorSpeedRpm("M1"), Is.EqualTo(600));
            contactor.SetControl(false);
            Assert.That(graph.Solve(3).GetMotorSpeedRpm("M1"), Is.Zero);
        }
        [Test]
        public void BrakingStopsAnAlreadySpinningMotorInOneSecond()
        {
            foreach (var pair in new[] { ("L1", "U"), ("L2", "V"), ("L3", "W") })
                Wire("POWER." + pair.Item1, "M1." + pair.Item2);
            Assert.That(graph.Solve().GetMotorSpeedRpm("M1"), Is.EqualTo(1450));
            var brake = ElectricalDeviceRuntime.CreateContactor("KB");
            graph.RegisterDevice(brake);
            brake.SetControl(true);
            Assert.That(graph.Solve(0.5f).GetMotorSpeedRpm("M1"), Is.EqualTo(725));
            Assert.That(graph.Solve(0.5f).GetMotorSpeedRpm("M1"), Is.Zero);
        }
        [Test]
        public void OldSnapshotsAreImmutableAndResetClearsSpeed()
        {
            Connect();
            var previous = graph.Solve();
            output = 200;
            graph.Solve();
            Assert.That(previous.GetMotorSpeedRpm("M1"), Is.EqualTo(600));
            motor.ResetMotorSpeed();
            Assert.That(motor.ActualSpeedRpm, Is.Zero);
            Assert.That(double.IsNaN(new ElectricalInstrument(InstrumentKind.Tachometer).SampleMotorSpeed("M1", null)), Is.True);
        }
    }
}
