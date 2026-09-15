using System.Linq;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

namespace ElectricalSim.Tests
{
    public sealed class CircuitGraphTests
    {
        [Test]
        public void MotorRuntimeKeepsAllSixOriginalWindingTerminals()
        {
            var motor = ElectricalDeviceRuntime.CreateMotor("M1");
            Assert.That(motor.Ports, Is.EquivalentTo(new[] { "U", "V", "W", "U2", "V2", "W2" }));
        }

        [Test]
        public void SolverDetectsPhaseToNeutralShortCircuit()
        {
            var graph = new CircuitGraph();
            graph.RegisterDevice(ElectricalDeviceRuntime.CreatePowerSource());
            graph.AddWire("POWER.L1", "POWER.N", Color.red);
            var snapshot = graph.Solve();
            Assert.That(snapshot.HasShortCircuit, Is.True);
        }

        [Test]
        public void PointControlRunsMotorOnlyWhileButtonIsPressed()
        {
            var graph = BuildPointControlGraph(out var button);
            button.SetControl(false);
            Assert.That(graph.Solve().GetMotorDirection("M1"), Is.EqualTo(MotorDirection.Stopped));

            button.SetControl(true);
            Assert.That(graph.Solve().GetMotorDirection("M1"), Is.EqualTo(MotorDirection.Forward));

            button.SetControl(false);
            Assert.That(graph.Solve().GetMotorDirection("M1"), Is.EqualTo(MotorDirection.Stopped));
        }

        [Test]
        public void MultimeterReportsExpectedThreePhaseVoltage()
        {
            var graph = new CircuitGraph();
            graph.RegisterDevice(ElectricalDeviceRuntime.CreatePowerSource());
            var snapshot = graph.Solve();
            var meter = new ElectricalInstrument(InstrumentKind.Multimeter);
            Assert.That(meter.Sample(MeasurementKind.AcVoltage, "POWER.L1", "POWER.N", snapshot), Is.EqualTo(220d));
            Assert.That(meter.Sample(MeasurementKind.AcVoltage, "POWER.L1", "POWER.L2", snapshot), Is.EqualTo(380d));
        }

        private static CircuitGraph BuildPointControlGraph(out ElectricalDeviceRuntime button)
        {
            var graph = new CircuitGraph();
            var power = ElectricalDeviceRuntime.CreatePowerSource();
            var breaker = ElectricalDeviceRuntime.CreateBreaker("QF");
            var contactor = ElectricalDeviceRuntime.CreateContactor("KM1");
            var relay = ElectricalDeviceRuntime.CreateThermalRelay("FR1");
            var motor = ElectricalDeviceRuntime.CreateMotor("M1");
            button = ElectricalDeviceRuntime.CreatePushButton("SB1", false);
            foreach (var device in new[] { power, breaker, contactor, relay, motor, button }) graph.RegisterDevice(device);

            var ports = new[]
            {
                "POWER.L1", "QF.L1", "POWER.L2", "QF.L2", "POWER.L3", "QF.L3",
                "QF.T1", "KM1.L1", "QF.T2", "KM1.L2", "QF.T3", "KM1.L3",
                "KM1.T1", "FR1.L1", "KM1.T2", "FR1.L2", "KM1.T3", "FR1.L3",
                "FR1.T1", "M1.U", "FR1.T2", "M1.V", "FR1.T3", "M1.W",
                "POWER.L1", "SB1.COM", "SB1.NO", "KM1.A1", "KM1.A2", "POWER.N"
            };
            for (var i = 0; i < ports.Length; i += 2) graph.AddWire(ports[i], ports[i + 1], Color.red);
            return graph;
        }

    }
}
