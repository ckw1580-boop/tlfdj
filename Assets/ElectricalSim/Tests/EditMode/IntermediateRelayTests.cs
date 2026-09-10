using System.Linq;
using NUnit.Framework;
using UnityEngine;

namespace ElectricalSim.Tests
{
    public sealed class IntermediateRelayTests
    {
        private CircuitGraph graph;
        [SetUp]
        public void Setup()
        {
            graph = new CircuitGraph();
            graph.RegisterDevice(new ElectricalDeviceRuntime("DC", ElectricalDeviceKind.PowerSource,
                new[] { "DC_POSITIVE", "DC_NEGATIVE" }) { IsDcSource = true });
            graph.RegisterDevice(ElectricalDeviceRuntime.CreatePowerSource());
            for (var i = 1; i <= 6; i++) graph.RegisterDevice(ElectricalDeviceRuntime.CreateIntermediateRelay("KA" + i));
        }
        private void Wire(string a, string b) => graph.AddWire(a, b, Color.red);

        [TestCase("KA1")][TestCase("KA2")][TestCase("KA3")]
        [TestCase("KA4")][TestCase("KA5")][TestCase("KA6")]
        public void CoilSwitchesAllFourIsolatedContactsAndReleases(string id)
        {
            var relay = (ElectricalDeviceRuntime)graph.Devices[id];
            Assert.That(relay.Ports, Is.EquivalentTo(Enumerable.Range(1, 14).Select(n => n.ToString())));
            AssertContacts(id, false);
            Wire("DC.DC_POSITIVE", id + ".13");
            Wire("DC.DC_NEGATIVE", id + ".14");
            AssertContacts(id, true);
            foreach (var other in Enumerable.Range(1, 6).Select(i => "KA" + i).Where(other => other != id))
                AssertContacts(other, false);
            graph.ClearWires();
            AssertContacts(id, false);
            relay.SetControl(true);
            AssertContacts(id, false); // Clicking/manual control must never energize the coil.
        }

        private void AssertContacts(string id, bool active)
        {
            var s = graph.Solve();
            Assert.That(s.HasShortCircuit, Is.False);
            Assert.That(s.IsDeviceActive(id), Is.EqualTo(active));
            // Explicit schematic expectations, independent of the production contact definition.
            for (var group = 0; group < 4; group++)
            {
                var common = id + "." + (9 + group);
                Assert.That(s.SameNet(common, id + "." + (1 + group)), Is.EqualTo(!active));
                Assert.That(s.SameNet(common, id + "." + (5 + group)), Is.EqualTo(active));
                Assert.That(s.SameNet(common, id + ".13"), Is.False);
                Assert.That(s.SameNet(common, id + ".14"), Is.False);
                for (var other = group + 1; other < 4; other++)
                    Assert.That(s.SameNet(common, id + "." + (9 + other)), Is.False);
            }
            Assert.That(s.SameNet(id + ".13", id + ".14"), Is.False);
        }

        [TestCase("floating")][TestCase("open-return")][TestCase("reverse")]
        [TestCase("same")][TestCase("ac")][TestCase("conflict")]
        public void InvalidSupplyReleasesPreviouslyEnergizedRelay(string fault)
        {
            Wire("DC.DC_POSITIVE", "KA1.13"); Wire("DC.DC_NEGATIVE", "KA1.14");
            Assert.That(graph.Solve().IsDeviceActive("KA1"), Is.True);
            graph.ClearWires();
            switch (fault)
            {
                case "open-return": Wire("DC.DC_POSITIVE", "KA1.13"); break;
                case "reverse": Wire("DC.DC_NEGATIVE", "KA1.13"); Wire("DC.DC_POSITIVE", "KA1.14"); break;
                case "same": Wire("DC.DC_POSITIVE", "KA1.13"); Wire("DC.DC_POSITIVE", "KA1.14"); break;
                case "ac": Wire("POWER.L1", "KA1.13"); Wire("POWER.N", "KA1.14"); break;
                case "conflict":
                    Wire("DC.DC_POSITIVE", "KA1.13"); Wire("DC.DC_NEGATIVE", "KA1.14");
                    Wire("POWER.L1", "KA1.13"); break;
            }
            var s = graph.Solve();
            Assert.That(s.IsDeviceActive("KA1"), Is.False);
            Assert.That(s.SameNet("KA1.9", "KA1.1"), Is.True);
            Assert.That(s.SameNet("KA1.9", "KA1.5"), Is.False);
            Assert.That(s.HasShortCircuit, Is.EqualTo(fault == "conflict"));
        }

        [Test]
        public void PlcOutputDrivesRelayWhoseDryContactSwitchesAcContactor()
        {
            var plc = new PlcDeviceRuntime("PLC_1");
            graph.RegisterDevice(plc);
            graph.RegisterDevice(ElectricalDeviceRuntime.CreateContactor("KM"));
            foreach (var p in new[] { "L+", "3L+" }) Wire("DC.DC_POSITIVE", "PLC_1." + p);
            foreach (var p in new[] { "M", "3M-" }) Wire("DC.DC_NEGATIVE", "PLC_1." + p);
            Wire("PLC_1.Q0.0", "KA1.13"); Wire("DC.DC_NEGATIVE", "KA1.14");
            Wire("POWER.L1", "KA1.9"); Wire("KA1.5", "KM.A1"); Wire("POWER.N", "KM.A2");
            var outputs = new bool[10];
            foreach (var active in new[] { false, true, false })
            {
                outputs[0] = active;
                plc.SetOutputs(true, outputs);
                var s = graph.Solve();
                Assert.That(s.IsDeviceActive("KA1"), Is.EqualTo(active));
                Assert.That(s.IsDeviceActive("KM"), Is.EqualTo(active));
                Assert.That(s.HasShortCircuit, Is.False, "DC coil and AC contacts must stay isolated");
            }
            outputs[0] = true; plc.SetOutputs(true, outputs); graph.Solve();
            plc.SetOutputs(false, outputs);
            Assert.That(graph.Solve().IsDeviceActive("KA1"), Is.False);
        }
    }
}
