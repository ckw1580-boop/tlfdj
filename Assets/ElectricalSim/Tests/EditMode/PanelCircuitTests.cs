using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine;

namespace ElectricalSim.Tests
{
    public sealed class PanelCircuitTests
    {
        private CircuitGraph graph;
        private Dictionary<string, ElectricalDeviceRuntime> controls;
        private PanelPowerState power;
        [SetUp]
        public void SetUp()
        {
            graph = new CircuitGraph();
            controls = PanelDeviceCatalog.Create().ToDictionary(d => d.Id, ElectricalDeviceRuntime.CreatePanel);
            power = new PanelPowerState(controls);
            foreach (var runtime in controls.Values) graph.RegisterDevice(runtime);
            var ac = ElectricalDeviceRuntime.CreatePowerSource();
            ac.SupplyEnabled = () => power.Enabled;
            graph.RegisterDevice(ac);
            graph.RegisterDevice(new ElectricalDeviceRuntime("TERMINAL_BUS", ElectricalDeviceKind.PowerSource, new[] { "DC_POSITIVE", "DC_NEGATIVE" })
                { IsDcSource = true, SupplyEnabled = () => power.Enabled });
        }
        private void Wire(string a, string b) => graph.AddWire(a, b, Color.red);

        [TestCase("SB1")][TestCase("SB2")][TestCase("SB3")][TestCase("SB4")]
        [TestCase("SB5")][TestCase("SB6")][TestCase("SB7")][TestCase("SB8")]
        [TestCase("SA1")][TestCase("SA2")]
        public void IndependentContactsFollowStateWithoutBridgingCommons(string id)
        {
            foreach (var pressed in new[] { false, true, false })
            {
                controls[id].SetControl(pressed);
                var s = graph.Solve();
                Assert.That(s.SameNet(id + ".COM1", id + ".NO1"), Is.EqualTo(pressed));
                Assert.That(s.SameNet(id + ".COM2", id + ".NC2"), Is.EqualTo(!pressed));
                Assert.That(s.SameNet(id + ".COM1", id + ".COM2"), Is.False);
            }
        }

        [Test]
        public void PowerRequiresStartAndNeverRestartsAfterEmergencyReset()
        {
            controls["PANEL_START"].SetControl(true);
            controls["PANEL_START"].SetControl(false);
            Assert.That(power.Enabled, Is.False);
            controls["PANEL_KEY"].SetControl(true);
            Assert.That(power.Enabled, Is.False);
            controls["PANEL_START"].SetControl(true);
            controls["PANEL_START"].SetControl(false);
            Assert.That(power.Enabled, Is.True);
            Assert.That(graph.Solve().GetPotential("POWER.L1"), Is.EqualTo(ElectricalPotential.PhaseL1));
            controls["PANEL_ESTOP"].SetControl(true);
            Assert.That(power.Enabled, Is.False);
            controls["PANEL_ESTOP"].SetControl(false);
            Assert.That(power.Enabled, Is.False);
            Assert.That(graph.Solve().GetDcVoltage("TERMINAL_BUS.DC_POSITIVE", "TERMINAL_BUS.DC_NEGATIVE"), Is.Zero);
            controls["PANEL_START"].SetControl(true);
            controls["PANEL_START"].SetControl(false);
            Assert.That(power.Enabled, Is.True);
            controls["PANEL_STOP"].SetControl(true);
            controls["PANEL_STOP"].SetControl(false);
            Assert.That(power.Enabled, Is.False);
            power.StartForAssessment();
            controls["PANEL_KEY"].SetControl(false);
            controls["PANEL_KEY"].SetControl(true);
            Assert.That(power.Enabled, Is.False);
        }

        [TestCase("SB1")][TestCase("SA1")]
        public void WiredControlDrivesLampAndSignedMeterTogether(string id)
        {
            Wire("TERMINAL_BUS.DC_POSITIVE", id + ".COM1");
            Wire(id + ".NO1", "HL1.L");
            Wire("HL1.N", "TERMINAL_BUS.DC_NEGATIVE");
            power.StartForAssessment();
            graph.Solve();
            Assert.That(controls["HL1"].IsActive, Is.False);
            controls[id].SetControl(true);
            var s = graph.Solve();
            var meter = new ElectricalInstrument(InstrumentKind.Multimeter);
            Assert.That(controls["HL1"].IsActive, Is.True);
            Assert.That(meter.Sample(MeasurementKind.DcVoltage, "HL1.L", "HL1.N", s), Is.EqualTo(24d));
            Assert.That(meter.Sample(MeasurementKind.DcVoltage, "HL1.N", "HL1.L", s), Is.EqualTo(-24d));
            Assert.That(meter.Sample(MeasurementKind.AcVoltage, "HL1.L", "HL1.N", s), Is.Zero);
            controls[id].SetControl(false);
            graph.Solve();
            Assert.That(controls["HL1"].IsActive, Is.False);
        }

        [TestCase("reverse")][TestCase("ac")][TestCase("short")][TestCase("mixed")]
        public void InvalidSupplyCannotLightLamp(string fault)
        {
            power.StartForAssessment();
            if (fault == "reverse") { Wire("TERMINAL_BUS.DC_POSITIVE", "HL1.N"); Wire("TERMINAL_BUS.DC_NEGATIVE", "HL1.L"); }
            else if (fault == "ac") { Wire("POWER.L1", "HL1.L"); Wire("POWER.N", "HL1.N"); }
            else
            {
                Wire("TERMINAL_BUS.DC_POSITIVE", "HL1.L"); Wire("TERMINAL_BUS.DC_NEGATIVE", "HL1.N");
                Wire("TERMINAL_BUS.DC_POSITIVE", fault == "short" ? "TERMINAL_BUS.DC_NEGATIVE" : "POWER.L1");
            }
            var s = graph.Solve();
            Assert.That(controls["HL1"].IsActive, Is.False);
            Assert.That(s.HasShortCircuit, Is.EqualTo(fault == "short" || fault == "mixed"));
            Assert.That(controls["HL1"].PanelStatus, Does.Contain(fault == "reverse" ? "反接" : fault == "ac" ? "交流" : "冲突"));
        }

        [Test]
        public void LegacyNoAliasesDoNotJoinSecondCommon()
        {
            foreach (var id in new[] { "SB1", "SB2" })
            {
                var s = graph.Solve();
                Assert.That(s.SameNet(id + ".COM", id + ".COM1"), Is.True);
                Assert.That(s.SameNet(id + ".NO", id + ".NO1"), Is.True);
                Assert.That(s.SameNet(id + ".COM", id + ".COM2"), Is.False);
            }
        }

        [Test]
        public void OriginalConfigurationBindsAll64PanelAndDcTerminals()
        {
            var map = OriginalTerminalBoardMap.Load(System.IO.Path.Combine(Application.streamingAssetsPath, OriginalTerminalBoardMap.RelativeConfigurationPath));
            var count = 0;
            foreach (var b in map.Bindings)
            {
                var split = b.LogicalNode.IndexOf('.');
                var id = b.LogicalNode.Substring(0, split);
                if (id == "POWER") continue;
                Assert.That(graph.Devices.ContainsKey(id), Is.True, b.DisplayName);
                Assert.That(graph.Devices[id].Ports.Contains(b.LogicalNode.Substring(split + 1)), Is.True, b.DisplayName);
                count++;
            }
            Assert.That(count, Is.EqualTo(64));
            Assert.That(map.Find("a43").LogicalNode, Is.EqualTo("SB1.COM2"));
        }
    }
}
