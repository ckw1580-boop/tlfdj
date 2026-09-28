using System.Linq;
using NUnit.Framework;
using UnityEngine;

namespace ElectricalSim.Tests
{
    public sealed class MotorProtectionTests
    {
        private static void Wire(CircuitGraph graph, string a, string b) => graph.AddWire(a, b, Color.red);
        private static ElectricalDeviceRuntime AddMotor(CircuitGraph graph, string id, string upstream = "POWER", bool stalled = false)
        {
            var motor = ElectricalDeviceRuntime.CreateMotor(id); graph.RegisterDevice(motor);
            var names = new[] { "U", "V", "W" };
            for (var i = 0; i < 3; i++) Wire(graph, upstream + "." + (upstream == "POWER" ? "L" : "T") + (i + 1), id + "." + names[i]);
            Wire(graph, id + ".U", id + ".W2"); Wire(graph, id + ".V", id + ".U2"); Wire(graph, id + ".W", id + ".V2");
            motor.SetMotorStalled(stalled); return motor;
        }
        private static ElectricalDeviceRuntime AddRelay(CircuitGraph graph, string id, string upstream = "POWER")
        {
            var relay = ElectricalDeviceRuntime.CreateThermalRelay(id); graph.RegisterDevice(relay);
            for (var i = 1; i <= 3; i++) Wire(graph, upstream + "." + (upstream == "POWER" ? "L" : "T") + i, id + ".L" + i);
            return relay;
        }
        [Test]
        public void WindingResistanceIsFiniteWithoutMergingPhaseNetworks()
        {
            var graph = new CircuitGraph(); graph.RegisterDevice(ElectricalDeviceRuntime.CreateMotor("M1"));
            var snapshot = graph.Solve(0);
            Assert.That(snapshot.SameNet("M1.U", "M1.U2"), Is.False);
            Assert.That(snapshot.GetResistance("M1.U", "M1.U2"), Is.EqualTo(10).Within(0.001));
            Assert.That(snapshot.GetResistance("M1.U", "M1.V"), Is.EqualTo(double.PositiveInfinity));
            Wire(graph, "M1.U2", "M1.V2"); Wire(graph, "M1.V2", "M1.W2");
            Assert.That(graph.Solve(0).GetResistance("M1.U", "M1.V"), Is.EqualTo(20).Within(0.001));
            graph.ClearWires(); Wire(graph, "M1.U", "M1.W2"); Wire(graph, "M1.V", "M1.U2"); Wire(graph, "M1.W", "M1.V2");
            Assert.That(graph.Solve(0).GetResistance("M1.U", "M1.V"), Is.EqualTo(20d / 3).Within(0.001));
        }
        [Test]
        public void WindingTailIsEnergizedEvenThoughItIsNotAnIdealWire()
        {
            var graph = new CircuitGraph(); graph.RegisterDevice(ElectricalDeviceRuntime.CreatePowerSource());
            graph.RegisterDevice(ElectricalDeviceRuntime.CreateMotor("M1")); Wire(graph, "POWER.L1", "M1.U");
            var snapshot = graph.Solve(0);
            Assert.That(snapshot.HasExternalSupply("M1.U2"), Is.True);
            var meter = new ElectricalInstrument(InstrumentKind.Multimeter);
            Assert.That(meter.Measure(MultimeterMode.Continuity, "M1.U", "M1.U2", snapshot).State, Is.EqualTo(MultimeterReadingState.Energized));
        }
        [Test]
        public void SeriesAndBranchRelaysMeasureActualMotorCurrents()
        {
            var graph = new CircuitGraph(); graph.RegisterDevice(ElectricalDeviceRuntime.CreatePowerSource());
            var first = AddRelay(graph, "FR1"); var second = AddRelay(graph, "FR2", "FR1");
            var m1 = AddMotor(graph, "M1", "FR2"); var m2 = AddMotor(graph, "M2", "FR2");
            graph.Solve(2);
            for (var i = 0; i < 3; i++)
            {
                Assert.That(second.ThermalState.Currents[i], Is.EqualTo(m1.MotorState.PhaseCurrentsAmps[i] + m2.MotorState.PhaseCurrentsAmps[i]).Within(0.001));
                Assert.That(first.ThermalState.Currents[i], Is.EqualTo(second.ThermalState.Currents[i]).Within(0.001));
            }
        }
        [Test]
        public void BypassedHeaterReportsUnknownInsteadOfInventingSafeCurrent()
        {
            var graph = new CircuitGraph(); graph.RegisterDevice(ElectricalDeviceRuntime.CreatePowerSource());
            var relay = AddRelay(graph, "FR1"); AddMotor(graph, "M1", "FR1");
            Wire(graph, "FR1.L1", "FR1.T1"); var snapshot = graph.Solve();
            Assert.That(float.IsNaN(relay.ThermalState.Currents[0]), Is.True);
            Assert.That(snapshot.Diagnostics.Any(d => d.Contains("旁路")), Is.True);
            Assert.That(snapshot.HasShortCircuit, Is.False);
        }
        [TestCase(false)] [TestCase(true)]
        public void AutomaticTripOnlyStopsMotorWhenAuxiliaryIsActuallyWired(bool wireAuxiliary)
        {
            var graph = new CircuitGraph(); graph.RegisterDevice(ElectricalDeviceRuntime.CreatePowerSource());
            var relay = AddRelay(graph, "FR1");
            var contactor = ElectricalDeviceRuntime.CreateContactor("KM"); graph.RegisterDevice(contactor);
            for (var i = 1; i <= 3; i++) Wire(graph, "FR1.T" + i, "KM.L" + i);
            if (wireAuxiliary) { Wire(graph, "POWER.L1", "FR1.95"); Wire(graph, "FR1.96", "KM.A1"); }
            else Wire(graph, "POWER.L1", "KM.A1");
            Wire(graph, "POWER.N", "KM.A2");
            var motor = AddMotor(graph, "M1", "KM", true);
            graph.Solve(8);
            Assert.That(relay.IsTripped, Is.True);
            Assert.That(contactor.IsActive, Is.EqualTo(!wireAuxiliary));
            Assert.That(motor.MotorState.CurrentAmps > 1, Is.EqualTo(!wireAuxiliary));
            Assert.That(relay.ThermalState.CanReset, Is.False);
            relay.SetControl(false); Assert.That(relay.IsTripped, Is.True);
            graph.RemoveWire(graph.Wires.First(w => w.EndPort == "KM.A1").Id);
            relay.ConfigureThermalRelay(new ThermalRelayConfiguration { TimeConstantSeconds = 1 });
            relay.SetControl(true); graph.Solve(1); relay.SetControl(false);
            Assert.That(relay.IsTripped, Is.False);
        }
        [Test]
        public void ZeroTimeSolveDoesNotAccumulateHeatAndOldSnapshotsAreIndependent()
        {
            var graph = new CircuitGraph(); graph.RegisterDevice(ElectricalDeviceRuntime.CreatePowerSource());
            var relay = AddRelay(graph, "FR1"); AddMotor(graph, "M1", "FR1", true);
            var previous = graph.Solve(0.2f); var heat = relay.ThermalState.Heat;
            for (var i = 0; i < 20; i++) graph.Solve(0, 32);
            Assert.That(relay.ThermalState.Heat, Is.EqualTo(heat));
            graph.Solve(0.2f);
            Assert.That(previous.GetThermalState("FR1").Heat, Is.EqualTo(heat));
            var old = previous.GetMotorState("M1"); old.PhaseCurrentsAmps[0] = 999;
            Assert.That(previous.GetMotorState("M1").PhaseCurrentsAmps[0], Is.Not.EqualTo(999));
        }
        [Test]
        public void ThermalTripMustCoolBeforeManualReset()
        {
            var graph = new CircuitGraph(); graph.RegisterDevice(ElectricalDeviceRuntime.CreatePowerSource());
            var relay = AddRelay(graph, "FR1"); relay.ConfigureThermalRelay(new ThermalRelayConfiguration { TimeConstantSeconds = 1 });
            AddMotor(graph, "M1", "FR1", true); graph.Solve(.2f);
            Assert.That(relay.IsTripped, Is.True); relay.SetControl(false); Assert.That(relay.IsTripped, Is.True);
            foreach (var wire in graph.Wires.Where(w => w.StartPort.StartsWith("POWER.")).ToArray()) graph.RemoveWire(wire.Id);
            graph.Solve(4); Assert.That(relay.ThermalState.CanReset, Is.True); relay.SetControl(false); Assert.That(relay.IsTripped, Is.False);
        }
        [TestCase(725f)] [TestCase(-725f)]
        public void InverterInputAndOutputRelaysFollowTheirPhysicalBranches(float command)
        {
            var graph = new CircuitGraph(); graph.RegisterDevice(ElectricalDeviceRuntime.CreatePowerSource());
            var input = AddRelay(graph, "FR1");
            var drive = new InverterDriveRuntime("DRIVE", () => command, () => false); graph.RegisterDevice(drive);
            for (var i = 1; i <= 3; i++) Wire(graph, "FR1.T" + i, "DRIVE.L" + i);
            var output = ElectricalDeviceRuntime.CreateThermalRelay("FR2"); graph.RegisterDevice(output);
            for (var i = 0; i < 3; i++) Wire(graph, "DRIVE." + new[] { "U2", "V2", "W2" }[i], "FR2.L" + (i + 1));
            var motor = AddMotor(graph, "M1", "FR2"); graph.Solve(1);
            for (var i = 0; i < 3; i++)
            {
                Assert.That(output.ThermalState.Currents[i], Is.EqualTo(motor.MotorState.CurrentAmps).Within(.001));
                Assert.That(input.ThermalState.Currents[i], Is.EqualTo(motor.MotorState.CurrentAmps * .5f).Within(.001));
            }
        }
        [TestCase(false, false)] [TestCase(false, true)] [TestCase(true, false)]
        public void WindingBranchRelaysMeasureDeltaCoilOrStarPhaseCurrent(bool star, bool heaterAtHead)
        {
            var graph = new CircuitGraph(); graph.RegisterDevice(ElectricalDeviceRuntime.CreatePowerSource());
            var motor = ElectricalDeviceRuntime.CreateMotor("M1"); graph.RegisterDevice(motor); motor.SetMotorStalled(true);
            var relay = ElectricalDeviceRuntime.CreateThermalRelay("FR1"); graph.RegisterDevice(relay);
            relay.ConfigureThermalRelay(new ThermalRelayConfiguration { TimeConstantSeconds = 1 });
            WireWindingRelay(graph, star, heaterAtHead, new[] { "POWER.L1", "POWER.L2", "POWER.L3" });
            graph.Solve(0);
            var expected = motor.MotorState.CurrentAmps / (star ? 1f : Mathf.Sqrt(3));
            Assert.That(expected, Is.GreaterThan(3.1f));
            for (var i = 0; i < 3; i++) Assert.That(relay.ThermalState.Currents[i], Is.EqualTo(expected).Within(.001));
            Assert.That(relay.ThermalState.Diagnostic, Is.Empty);
            graph.Solve(2);
            Assert.That(relay.IsTripped, Is.True, "An inside-winding heater must accumulate the current actually crossing it.");
            Assert.That(motor.MotorState.CurrentAmps, Is.GreaterThan(0), "An unwired auxiliary must not stop the motor.");
        }
        [TestCase(false)] [TestCase(true)]
        public void MissingPhaseWindingBranchCurrentIsUnknownRatherThanZero(bool star)
        {
            var graph = new CircuitGraph(); graph.RegisterDevice(ElectricalDeviceRuntime.CreatePowerSource());
            graph.RegisterDevice(ElectricalDeviceRuntime.CreateMotor("M1"));
            var relay = ElectricalDeviceRuntime.CreateThermalRelay("FR1"); graph.RegisterDevice(relay);
            WireWindingRelay(graph, star, false, new[] { "POWER.L1", "POWER.L2", "POWER.L3" });
            graph.RemoveWire(graph.Wires.First(w => w.StartPort == "POWER.L3").Id);
            var snapshot = graph.Solve(0);
            Assert.That(relay.ThermalState.Currents.All(float.IsNaN), Is.True);
            Assert.That(snapshot.Diagnostics.Any(d => d.Contains("缺相时绕组支路电流")), Is.True);
        }
        [TestCase(725f)] [TestCase(-725f)]
        public void InverterInsideDeltaRelayUsesWindingCurrentInEitherDirection(float command)
        {
            var graph = new CircuitGraph(); graph.RegisterDevice(ElectricalDeviceRuntime.CreatePowerSource());
            graph.RegisterDevice(new InverterDriveRuntime("DRIVE", () => command, () => false));
            for (var i = 1; i <= 3; i++) Wire(graph, "POWER.L" + i, "DRIVE.L" + i);
            var motor = ElectricalDeviceRuntime.CreateMotor("M1"); graph.RegisterDevice(motor); motor.SetMotorStalled(true);
            var relay = ElectricalDeviceRuntime.CreateThermalRelay("FR1"); graph.RegisterDevice(relay);
            WireWindingRelay(graph, false, false, new[] { "DRIVE.U2", "DRIVE.V2", "DRIVE.W2" });
            graph.Solve(0);
            foreach (var amps in relay.ThermalState.Currents)
                Assert.That(amps, Is.EqualTo(motor.MotorState.CurrentAmps / Mathf.Sqrt(3)).Within(.001));
        }
        [Test]
        public void DoubleHighInternalParallelHeatersReportUnknownInsteadOfZero()
        {
            var graph = new CircuitGraph(); graph.RegisterDevice(ElectricalDeviceRuntime.CreatePowerSource());
            graph.RegisterDevice(ElectricalDeviceRuntime.CreateMotor("M_DOUBLE"));
            var relay = ElectricalDeviceRuntime.CreateThermalRelay("FR1"); graph.RegisterDevice(relay);
            var names = new[] { "U", "V", "W" };
            for (var i = 0; i < 3; i++)
            {
                Wire(graph, "POWER.L" + (i + 1), "M_DOUBLE." + names[i] + "2");
                Wire(graph, "M_DOUBLE." + names[i], "FR1.L" + (i + 1));
            }
            Wire(graph, "FR1.T1", "FR1.T2"); Wire(graph, "FR1.T2", "FR1.T3");
            var snapshot = graph.Solve(0);
            Assert.That(snapshot.GetMotorState("M_DOUBLE").Connection.Kind, Is.EqualTo(MotorConnectionKind.DoubleHigh));
            Assert.That(relay.ThermalState.Currents.All(float.IsNaN), Is.True);
            Assert.That(snapshot.Diagnostics.Any(d => d.Contains("YY公共点")), Is.True);
            foreach (var wire in graph.Wires.Where(w => w.StartPort.StartsWith("POWER.")).ToArray()) graph.RemoveWire(wire.Id);
            graph.Solve(0);
            Assert.That(relay.ThermalState.Currents.All(i => i == 0), Is.True, "A de-energized internal branch has no heating current.");
        }

        private static void WireWindingRelay(CircuitGraph graph, bool star, bool heaterAtHead, string[] sources)
        {
            var names = new[] { "U", "V", "W" };
            for (var i = 0; i < 3; i++)
            {
                if (star)
                {
                    Wire(graph, sources[i], "M1." + names[i]);
                    Wire(graph, "M1." + names[i] + "2", "FR1.L" + (i + 1));
                }
                else if (heaterAtHead)
                {
                    Wire(graph, sources[i], "FR1.T" + (i + 1));
                    Wire(graph, "M1." + names[i], "FR1.L" + (i + 1));
                    Wire(graph, "FR1.T" + (i + 1), "M1." + names[(i + 2) % 3] + "2");
                }
                else
                {
                    Wire(graph, sources[i], "M1." + names[i]);
                    Wire(graph, "M1." + names[i] + "2", "FR1.L" + (i + 1));
                    Wire(graph, "FR1.T" + (i + 1), "M1." + names[(i + 1) % 3]);
                }
            }
            if (star) { Wire(graph, "FR1.T1", "FR1.T2"); Wire(graph, "FR1.T2", "FR1.T3"); }
        }
    }
}
