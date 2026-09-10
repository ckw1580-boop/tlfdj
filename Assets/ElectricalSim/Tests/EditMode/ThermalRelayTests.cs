using System.Linq;
using NUnit.Framework;
using UnityEngine;

namespace ElectricalSim.Tests
{
    public sealed class ThermalRelayTests
    {
        [TestCase("FR1")][TestCase("FR2")][TestCase("FR")]
        public void TripAndResetOnlySwitchAuxiliariesOfSelectedRelay(string selected)
        {
            var graph = new CircuitGraph();
            foreach (var id in new[] { "FR1", "FR2", "FR" }) graph.RegisterDevice(ElectricalDeviceRuntime.CreateThermalRelay(id));
            foreach (var trip in new[] { false, true, false })
            {
                ((ElectricalDeviceRuntime)graph.Devices[selected]).SetControl(trip);
                var snapshot = graph.Solve(); Assert.That(snapshot.HasShortCircuit, Is.False);
                foreach (var id in new[] { "FR1", "FR2", "FR" })
                {
                    var active = id == selected && trip;
                    Assert.That(snapshot.IsDeviceActive(id), Is.EqualTo(active));
                    Assert.That(snapshot.SameNet(id + ".95", id + ".96"), Is.EqualTo(!active));
                    Assert.That(snapshot.SameNet(id + ".97", id + ".98"), Is.EqualTo(active));
                    for (int i = 1; i <= 3; i++) Assert.That(snapshot.SameNet(id + ".L" + i, id + ".T" + i), Is.True);
                    var groups = new[] { "L1", "L2", "L3", "95", "97" };
                    for (int i = 0; i < groups.Length; i++)
                        for (int j = i + 1; j < groups.Length; j++)
                            Assert.That(snapshot.SameNet(id + "." + groups[i], id + "." + groups[j]), Is.False);
                    foreach (var other in new[] { "FR1", "FR2", "FR" }.Where(x => x != id))
                        foreach (var port in ThermalRelayDefinition.Ports)
                            Assert.That(snapshot.SameNet(id + "." + port, other + "." + port), Is.False);
                }
            }
        }
        [Test] public void OverloadOpensContactorControlCircuitInsteadOfHeaterPaths()
        {
            var graph = new CircuitGraph(); graph.RegisterDevice(ElectricalDeviceRuntime.CreatePowerSource());
            var relay = ElectricalDeviceRuntime.CreateThermalRelay("FR"); graph.RegisterDevice(relay);
            graph.RegisterDevice(ElectricalDeviceRuntime.CreateContactor("KM"));
            graph.AddWire("POWER.L1", "FR.95", Color.red); graph.AddWire("FR.96", "KM.A1", Color.red);
            graph.AddWire("KM.A2", "POWER.N", Color.red);
            Assert.That(graph.Solve().IsDeviceActive("KM"), Is.True);
            relay.SetControl(true); Assert.That(graph.Solve().IsDeviceActive("KM"), Is.False);
            relay.SetControl(false); Assert.That(graph.Solve().IsDeviceActive("KM"), Is.True);
        }
    }
}
