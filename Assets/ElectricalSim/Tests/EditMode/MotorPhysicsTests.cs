using System.Linq;
using NUnit.Framework;
using UnityEngine;

namespace ElectricalSim.Tests
{
    public sealed class MotorPhysicsTests
    {
        private CircuitGraph graph;
        private ElectricalDeviceRuntime motor;
        private void Setup(string id = "M1")
        { graph = new CircuitGraph(); graph.RegisterDevice(ElectricalDeviceRuntime.CreatePowerSource()); motor = ElectricalDeviceRuntime.CreateMotor(id); graph.RegisterDevice(motor); }
        private void Wire(string a, string b) => graph.AddWire(a, b, Color.red);
        private void Supply(int[] phases = null, bool tails = false)
        {
            phases = phases ?? new[] { 1, 2, 3 };
            for (var i = 0; i < 3; i++) Wire("POWER.L" + phases[i], motor.DeviceId + "." + new[] { "U", "V", "W" }[i] + (tails ? "2" : ""));
        }
        private void Delta()
        { Wire(motor.DeviceId + ".U", motor.DeviceId + ".W2"); Wire(motor.DeviceId + ".V", motor.DeviceId + ".U2"); Wire(motor.DeviceId + ".W", motor.DeviceId + ".V2"); }
        [Test]
        public void ThermalResetThresholdMustBeReachableByExponentialCooling()
        {
            Assert.Throws<System.ArgumentException>(() => new ThermalRelayConfiguration { ResetThreshold = 0 }.Validate());
            Assert.DoesNotThrow(() => new ThermalRelayConfiguration { ResetThreshold = .01f }.Validate());
        }
        [TestCase("M1")] [TestCase("M2")] [TestCase("M3")]
        public void AllSixPermutationsHaveCorrectDirectionForStarAndDelta(string id)
        {
            var permutations = new[] { new[] {1,2,3}, new[] {2,3,1}, new[] {3,1,2}, new[] {2,1,3}, new[] {1,3,2}, new[] {3,2,1} };
            foreach (var star in new[] { false, true })
                for (var i = 0; i < permutations.Length; i++)
                {
                    Setup(id); Supply(permutations[i]);
                    if (star) { Wire(id + ".U2", id + ".V2"); Wire(id + ".V2", id + ".W2"); } else Delta();
                    var state = graph.Solve(.1f).GetMotorState(id);
                    Assert.That(state.Connection.DirectionSign, Is.EqualTo(i < 3 ? 1 : -1));
                    Assert.That(state.SpeedRpm * (i < 3 ? 1 : -1), Is.GreaterThan(0));
                    Assert.That(state.Connection.Kind, Is.EqualTo(star ? MotorConnectionKind.Star : MotorConnectionKind.Delta));
                }
        }
        [Test]
        public void OpenTailsCannotStartAndAreNotReportedAsShortCircuit()
        { Setup(); Supply(); var s = graph.Solve(1); Assert.That(s.GetMotorSpeedRpm("M1"), Is.Zero); Assert.That(s.HasShortCircuit, Is.False); Assert.That(s.Diagnostics.Any(d => d.Contains("绕组")), Is.True); }
        [Test]
        public void LoadIncreasesSlipAndCurrentAndStartIsContinuous()
        {
            Setup(); Supply(); Delta();
            var early = graph.Solve(.02f).GetMotorState("M1"); Assert.That(early.SpeedRpm, Is.InRange(1, 1400));
            var light = graph.Solve(2).GetMotorState("M1"); motor.SetMotorLoad(1);
            var rated = graph.Solve(2).GetMotorState("M1"); Assert.That(rated.SpeedRpm, Is.EqualTo(1450).Within(2));
            Assert.That(rated.SpeedRpm, Is.LessThan(light.SpeedRpm)); Assert.That(rated.CurrentAmps, Is.GreaterThan(light.CurrentAmps));
            Assert.That(rated.SynchronousSpeedRpm, Is.EqualTo(1500));
        }
        [Test]
        public void StarStartsLightLoadButCannotStartRatedLoadAt380Volts()
        {
            Setup(); Supply(); Wire("M1.U2", "M1.V2"); Wire("M1.V2", "M1.W2"); motor.SetMotorLoad(1);
            Assert.That(graph.Solve(1).GetMotorSpeedRpm("M1"), Is.Zero);
            motor.SetMotorLoad(.2f); Assert.That(graph.Solve(2).GetMotorSpeedRpm("M1"), Is.GreaterThan(1300));
        }
        [Test]
        public void DoubleSpeedUsesDedicatedLowAndHighWindingConnections()
        {
            Setup("M_DOUBLE"); Supply(); motor.SetMotorLoad(1);
            Assert.That(graph.Solve(2).GetMotorSpeedRpm("M_DOUBLE"), Is.EqualTo(1450).Within(2));
            graph.ClearWires(); Supply(tails:true); Wire("M_DOUBLE.U", "M_DOUBLE.V"); Wire("M_DOUBLE.V", "M_DOUBLE.W");
            var changed = graph.Solve(.02f).GetMotorState("M_DOUBLE"); Assert.That(changed.SpeedRpm, Is.InRange(1450, 2890));
            Assert.That(graph.Solve(2).GetMotorSpeedRpm("M_DOUBLE"), Is.EqualTo(2900).Within(5), "Small teaching friction adds to the external rated load.");
            Wire("POWER.L1", "M_DOUBLE.U"); Assert.That(graph.Solve(0).GetMotorState("M_DOUBLE").Connection.Kind, Is.EqualTo(MotorConnectionKind.Invalid));
        }
        [Test]
        public void MissingPhaseCannotSelfStartButHeatsAndStallHasNoRotation()
        {
            Setup(); Supply(); Delta(); graph.RemoveWire(graph.Wires.First(w => w.StartPort == "POWER.L3").Id);
            var missing = graph.Solve(1).GetMotorState("M1"); Assert.That(missing.SpeedRpm, Is.Zero); Assert.That(missing.Connection.PhaseLoss, Is.True); Assert.That(missing.ThermalState, Is.GreaterThan(0));
            Wire("POWER.L3", "M1.W"); motor.SetMotorStalled(true); var stalled = graph.Solve(2).GetMotorState("M1");
            Assert.That(stalled.SpeedRpm, Is.Zero); Assert.That(stalled.CurrentAmps, Is.GreaterThan(15)); Assert.That(stalled.ThermalState, Is.GreaterThan(missing.ThermalState));
        }
        [Test]
        public void SubsteppedTimeMatchesSmallStepsAndZeroTimeDoesNotChangeSpeedOrHeat()
        {
            Setup(); Supply(); Delta(); var large = graph.Solve(.5f, 32).GetMotorState("M1");
            Setup(); Supply(); Delta(); for (var i = 0; i < 25; i++) graph.Solve(.02f, 8);
            var small = graph.Solve(0).GetMotorState("M1"); Assert.That(small.SpeedRpm, Is.EqualTo(large.SpeedRpm).Within(.01)); Assert.That(small.ThermalState, Is.EqualTo(large.ThermalState).Within(.00001));
            for (var i = 0; i < 10; i++) graph.Solve(0); Assert.That(motor.ActualSpeedRpm, Is.EqualTo(small.SpeedRpm));
        }
    }
}
