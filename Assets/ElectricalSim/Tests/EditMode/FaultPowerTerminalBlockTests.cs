using System.Linq;
using NUnit.Framework;
using UnityEngine;

namespace ElectricalSim.Tests
{
    public sealed class FaultPowerTerminalBlockTests
    {
        private static string Node(string port) => FaultPowerTerminalBlock.DeviceId + "." + port;
        [Test]
        public void AllFourteenPortsMapToExistingSourcesWithoutBridgingDifferentPotentials()
        {
            var block = FaultPowerTerminalBlock.CreateRuntime();
            Assert.That(block.DeviceId, Is.EqualTo("FaultPowerTerminalBlock"));
            Assert.That(block.Ports.Count(), Is.EqualTo(14));
            Assert.That(block.FixedLinks.Count(), Is.EqualTo(14));
            var graph = new CircuitGraph();
            graph.RegisterDevice(block);
            graph.RegisterDevice(ElectricalDeviceRuntime.CreatePowerSource());
            graph.RegisterDevice(new ElectricalDeviceRuntime("TERMINAL_BUS", ElectricalDeviceKind.PowerSource,
                new[] { "DC_POSITIVE", "DC_NEGATIVE" }) { IsDcSource = true });
            var snapshot = graph.Solve();
            Assert.That(snapshot.HasShortCircuit, Is.False);
            var phases = new[] { "U", "V", "W", "N" };
            var nodes = new[] { "POWER.L1", "POWER.L2", "POWER.L3", "POWER.N" };
            for (var phase = 0; phase < phases.Length; phase++)
            for (var group = 1; group <= 2; group++)
                Assert.That(snapshot.SameNet(Node(phases[phase] + group), nodes[phase]), Is.True);
            for (var group = 1; group <= 3; group++)
            {
                Assert.That(snapshot.SameNet(Node("24V+_" + group), "TERMINAL_BUS.DC_POSITIVE"), Is.True);
                Assert.That(snapshot.SameNet(Node("24V-_" + group), "TERMINAL_BUS.DC_NEGATIVE"), Is.True);
            }
            Assert.That(snapshot.SameNet(Node("N1"), Node("24V-_1")), Is.False);
            graph.AddWire(Node("U1"), Node("V2"), Color.red);
            Assert.That(graph.Solve().HasShortCircuit, Is.True, "A real cross-phase wire must still trigger conflict detection.");
        }
    }
}
