using System.Linq;
using NUnit.Framework;
using UnityEngine;

namespace ElectricalSim.Tests
{
    public sealed class ContactorTests
    {
        private CircuitGraph graph;
        private static readonly string[] Ids = { "KMF", "KM1", "KMR", "KM2", "KMBACK1", "KMBACK2", "KMBACK3" };
        // Expectations transcribed from the reference, independent of the definition.
        private static readonly string[][] Pairs = {
            new[] { "L1", "T1" }, new[] { "L2", "T2" }, new[] { "L3", "T3" },
            new[] { "13", "14" }, new[] { "53", "54" }, new[] { "83", "84" },
            new[] { "61", "62" }, new[] { "71", "72" }
        };
        [SetUp] public void Setup()
        {
            graph = new CircuitGraph(); graph.RegisterDevice(ElectricalDeviceRuntime.CreatePowerSource());
            graph.RegisterDevice(new ElectricalDeviceRuntime("DC", ElectricalDeviceKind.PowerSource,
                new[] { "DC_POSITIVE", "DC_NEGATIVE" }) { IsDcSource = true });
            foreach (var id in Ids) graph.RegisterDevice(ElectricalDeviceRuntime.CreateContactor(id));
        }
        private WireConnection Wire(string a, string b) => graph.AddWire(a, b, Color.red);
        private void AssertContacts(string id, bool active)
        {
            var snapshot = graph.Solve();
            Assert.That(snapshot.HasShortCircuit, Is.False);
            Assert.That(snapshot.IsDeviceActive(id), Is.EqualTo(active));
            for (var i = 0; i < Pairs.Length; i++)
            {
                var first = id + "." + Pairs[i][0];
                Assert.That(snapshot.SameNet(first, id + "." + Pairs[i][1]), Is.EqualTo(i >= 6 ? !active : active));
                Assert.That(snapshot.SameNet(first, id + ".A1"), Is.False);
                Assert.That(snapshot.SameNet(first, id + ".A2"), Is.False);
                for (var j = i + 1; j < Pairs.Length; j++)
                    Assert.That(snapshot.SameNet(first, id + "." + Pairs[j][0]), Is.False);
            }
            Assert.That(snapshot.SameNet(id + ".A1", id + ".A2"), Is.False);
        }
        [TestCase("KMF")][TestCase("KM1")][TestCase("KMR")][TestCase("KM2")]
        [TestCase("KMBACK1")][TestCase("KMBACK2")][TestCase("KMBACK3")]
        public void AllEightContactsSwitchIndependentlyAndRelease(string id)
        {
            Assert.That(graph.Devices[id].Ports.Count, Is.EqualTo(18));
            AssertContacts(id, false);
            Wire("POWER.L1", id + ".A1"); Wire("POWER.N", id + ".A2");
            AssertContacts(id, true);
            foreach (var other in Ids.Where(other => other != id)) AssertContacts(other, false);
            graph.ClearWires(); AssertContacts(id, false);
            ((ElectricalDeviceRuntime)graph.Devices[id]).SetControl(true);
            AssertContacts(id, false);
        }
        [TestCase("floating")][TestCase("open-return")][TestCase("same")]
        [TestCase("380V")][TestCase("DC")][TestCase("conflict")]
        public void WrongSupplyReleasesCoil(string fault)
        {
            Wire("POWER.L1", "KMF.A1"); Wire("POWER.N", "KMF.A2");
            Assert.That(graph.Solve().IsDeviceActive("KMF"), Is.True);
            graph.ClearWires();
            switch (fault)
            {
                case "open-return": Wire("POWER.L1", "KMF.A1"); break;
                case "same": Wire("POWER.L1", "KMF.A1"); Wire("POWER.L1", "KMF.A2"); break;
                case "380V": Wire("POWER.L1", "KMF.A1"); Wire("POWER.L2", "KMF.A2"); break;
                case "DC": Wire("DC.DC_POSITIVE", "KMF.A1"); Wire("DC.DC_NEGATIVE", "KMF.A2"); break;
                case "conflict": Wire("POWER.L1", "KMF.A1"); Wire("POWER.L2", "KMF.A1"); Wire("POWER.N", "KMF.A2"); break;
            }
            var snapshot = graph.Solve();
            Assert.That(snapshot.IsDeviceActive("KMF"), Is.False);
            Assert.That(snapshot.HasShortCircuit, Is.EqualTo(fault == "conflict"));
            Assert.That(snapshot.SameNet("KMF.61", "KMF.62"), Is.True);
            Assert.That(snapshot.SameNet("KMF.71", "KMF.72"), Is.True);
            var voltage = ((ElectricalDeviceRuntime)graph.Devices["KMF"]).ContactorCoilVoltage;
            if (fault == "conflict") Assert.That(double.IsNaN(voltage), Is.True);
            else Assert.That(voltage, Is.EqualTo(fault == "380V" ? 380d : 0d));
        }
        [Test] public void AcCoilHasNoPolarity()
        {
            Wire("POWER.N", "KMF.A1"); Wire("POWER.L3", "KMF.A2"); AssertContacts("KMF", true);
        }
        [Test] public void Auxiliary53LatchesWithoutConnecting13Or83()
        {
            var start = ElectricalDeviceRuntime.CreatePushButton("START", false); graph.RegisterDevice(start);
            var stop = ElectricalDeviceRuntime.CreatePushButton("STOP", true); graph.RegisterDevice(stop);
            Wire("POWER.L1", "STOP.COM"); Wire("STOP.NC", "START.COM");
            Wire("START.NO", "KMF.A1"); Wire("POWER.N", "KMF.A2");
            Wire("START.COM", "KMF.53"); Wire("START.NO", "KMF.54");
            start.SetControl(true); Assert.That(graph.Solve().IsDeviceActive("KMF"), Is.True);
            start.SetControl(false); var snapshot = graph.Solve();
            Assert.That(snapshot.IsDeviceActive("KMF"), Is.True);
            Assert.That(snapshot.SameNet("KMF.53", "KMF.13"), Is.False);
            Assert.That(snapshot.SameNet("KMF.53", "KMF.83"), Is.False);
            stop.SetControl(true); Assert.That(graph.Solve().IsDeviceActive("KMF"), Is.False);
        }
        [Test] public void NormallyClosedContactsInterlockTwoCoils()
        {
            var forward = ElectricalDeviceRuntime.CreatePushButton("F", false); graph.RegisterDevice(forward);
            var reverse = ElectricalDeviceRuntime.CreatePushButton("R", false); graph.RegisterDevice(reverse);
            Wire("POWER.L1", "F.COM"); Wire("F.NO", "KMR.71"); Wire("KMR.72", "KMF.A1"); Wire("KMF.A2", "POWER.N");
            Wire("POWER.L1", "R.COM"); Wire("R.NO", "KMF.61"); Wire("KMF.62", "KMR.A1"); Wire("KMR.A2", "POWER.N");
            forward.SetControl(true); Assert.That(graph.Solve().IsDeviceActive("KMF"), Is.True);
            reverse.SetControl(true); var snapshot = graph.Solve();
            Assert.That(snapshot.IsDeviceActive("KMF"), Is.True); Assert.That(snapshot.IsDeviceActive("KMR"), Is.False);
            forward.SetControl(false); snapshot = graph.Solve();
            Assert.That(snapshot.IsDeviceActive("KMF"), Is.False); Assert.That(snapshot.IsDeviceActive("KMR"), Is.True);
            Assert.That(snapshot.HasShortCircuit, Is.False);
        }
    }
}
