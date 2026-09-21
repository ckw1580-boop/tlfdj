using System.Linq;
using NUnit.Framework;
using UnityEngine;

namespace ElectricalSim.Tests
{
    public sealed class FrontBreakerCircuitTests
    {
        [Test]
        public void EachBreakerInterruptsItsOwnThreePhasesAndNeutral()
        {
            var graph = new CircuitGraph();
            graph.RegisterDevice(ElectricalDeviceRuntime.CreatePowerSource());
            var breakers = Enumerable.Range(1, 3).Select(i => ElectricalDeviceRuntime.CreateBreaker("QFFRONT" + i, FrontBreakerView.Contacts)).ToArray();
            var supplies = new[] { "N", "L1", "L2", "L3" };
            var inputs = new[] { "N1", "L1", "L3", "L5" };
            var outputs = new[] { "N2", "L2", "L4", "L6" };
            var potentials = new[] { ElectricalPotential.Neutral, ElectricalPotential.PhaseL1, ElectricalPotential.PhaseL2, ElectricalPotential.PhaseL3 };
            foreach (var breaker in breakers)
            {
                graph.RegisterDevice(breaker);
                for (var i = 0; i < 4; i++) graph.AddWire("POWER." + supplies[i], breaker.DeviceId + "." + inputs[i], Color.red);
            }
            foreach (var opened in breakers)
            {
                opened.SetControl(false);
                var snapshot = graph.Solve();
                Assert.That(snapshot.HasShortCircuit, Is.False);
                foreach (var breaker in breakers)
                    for (var i = 0; i < 4; i++)
                        Assert.That(snapshot.GetPotential(breaker.DeviceId + "." + outputs[i]),
                            Is.EqualTo(breaker == opened ? ElectricalPotential.Floating : potentials[i]));
                opened.SetControl(true);
            }
        }

        [Test]
        public void LegacyThreePhaseBreakerKeepsItsOriginalContacts()
        {
            var breaker = ElectricalDeviceRuntime.CreateBreaker("QF");
            Assert.That(breaker.GetConductiveLinks().Select(p => p.A + "-" + p.B),
                Is.EqualTo(new[] { "L1-T1", "L2-T2", "L3-T3" }));
            breaker.SetControl(false);
            Assert.That(breaker.GetConductiveLinks(), Is.Empty);
        }
    }
}
