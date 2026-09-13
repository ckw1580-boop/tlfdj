using System;
using System.Linq;
using NUnit.Framework;
using UnityEngine;

namespace ElectricalSim.Tests
{
    public sealed class LiquidSimulationTests
    {
        [Test]
        public void PumpBindingsMatchFrontCentreAndRightMotors()
        {
            Assert.That(SceneIoCatalog.Pumps[0].MotorId, Is.EqualTo("M_DOUBLE"));
            Assert.That(SceneIoCatalog.Pumps[0].Mount, Is.EqualTo(14));
            Assert.That(SceneIoCatalog.Pumps[1].MotorId, Is.EqualTo("M1"));
            Assert.That(SceneIoCatalog.Pumps[1].Mount, Is.EqualTo(13));
        }
        [Test]
        public void EighteenLabelsChangeWithoutChangingLogicalEndpoints()
        {
            var board = OriginalCabinetTerminalBoardMap.Boards.Single(b => b.DeviceId == "DuanZiPai_8");
            var count = 0;
            foreach (var d in SceneIoCatalog.Devices)
                foreach (var signal in d.Ports)
                {
                    var port = d.Prefix + "_" + signal;
                    Assert.That(OriginalCabinetTerminalBoardMap.GetDisplayName(board, port), Is.EqualTo(d.Name + "_" + signal));
                    Assert.That(OriginalCabinetTerminalBoardMap.ResolveLogicalNode(board, port), Is.EqualTo(d.Id + "." + signal));
                    count++;
                }
            Assert.That(count, Is.EqualTo(18));
        }
        [TestCase(1450, 0, true, false, 0.5)]
        [TestCase(725, 0, true, false, 0.25)]
        [TestCase(0, 1450, false, true, 0.5)]
        [TestCase(1450, 1450, true, true, 1)]
        [TestCase(1450, 1450, false, false, 0)]
        [TestCase(-1450, 0, true, true, 0)]
        public void ActualForwardSpeedAndValveGateDetermineFlow(float rpm1, float rpm2, bool v1, bool v2, double expected)
        {
            var liquid = new LiquidSimulationRuntime();
            for (var i = 0; i < 750; i++) liquid.Advance(0.02, rpm1, rpm2, v1, v2, false);
            Assert.That(liquid.Level, Is.EqualTo(expected).Within(1e-9));
        }
        [Test]
        public void ConcurrentDrainClampsAtEmptyAndOverflowIsRecordedWithoutClosingValves()
        {
            var liquid = new LiquidSimulationRuntime();
            liquid.Advance(15, 1450, 1450, true, true, true);
            Assert.That(liquid.Level, Is.EqualTo(0.25).Within(1e-9));
            liquid.Advance(20, 0, 0, false, false, true);
            Assert.That(liquid.Level, Is.Zero);
            liquid.Advance(20, 1450, 1450, true, true, false);
            Assert.That(liquid.Level, Is.EqualTo(1));
            Assert.That(liquid.OverflowVolume, Is.EqualTo(1d / 3).Within(1e-9));
            Assert.That(liquid.IsOverflowing, Is.True);
            Assert.That(liquid.Pump1Flow, Is.GreaterThan(0));
            liquid.Reset(); Assert.That(liquid.Level, Is.Zero); Assert.That(liquid.OverflowVolume, Is.Zero);
        }
        [Test]
        public void ConfigurationIsValidatedAndDefensivelyCopied()
        {
            var liquid = new LiquidSimulationRuntime();
            var config = new LiquidConfiguration { InitialLevelPercent = 50, Pump1FillSeconds = 60 };
            liquid.Configure(config, true); config.Pump1FillSeconds = 0;
            liquid.Configuration.Pump1FillSeconds = 0;
            liquid.Advance(15, 1450, 0, true, false, false);
            Assert.That(liquid.Level, Is.EqualTo(0.75).Within(1e-9));
            Assert.Throws<ArgumentException>(() => liquid.Configure(new LiquidConfiguration { DrainSeconds = float.NaN }, true));
            Assert.That(liquid.Level, Is.EqualTo(0.75).Within(1e-9));
        }
        [Test]
        public void PoweredWetSensorDrivesPlcAndSignalShortDoesNotFeedSupply()
        {
            var graph = new CircuitGraph();
            var supply = new ElectricalDeviceRuntime("DC", ElectricalDeviceKind.PowerSource, new[] { "DC_POSITIVE", "DC_NEGATIVE" }) { IsDcSource = true };
            var sensor = new SceneIoDeviceRuntime(SceneIoCatalog.Devices[0]) { TriggerLevel = 0.5f };
            var plc = new PlcDeviceRuntime("PLC_1");
            graph.RegisterDevice(supply); graph.RegisterDevice(sensor); graph.RegisterDevice(plc);
            foreach (var p in new[] { "SENSOR_A.VCC", "PLC_1.L+" }) graph.AddWire("DC.DC_POSITIVE", p, Color.red);
            foreach (var p in new[] { "SENSOR_A.GND", "PLC_1.M", "PLC_1.1M" }) graph.AddWire("DC.DC_NEGATIVE", p, Color.blue);
            graph.AddWire(sensor.Port("SIGNAL"), plc.Port(PlcConfiguration.InputTerminals[0]), Color.green);
            sensor.SetLevel(0.5); var snapshot = graph.Solve(0);
            Assert.That(sensor.IsActive, Is.True); Assert.That(plc.InputStates[0], Is.True);
            sensor.SetLevel(0.497); graph.Solve(0); Assert.That(sensor.IsActive, Is.True);
            sensor.SetLevel(0.49); snapshot = graph.Solve(0);
            Assert.That(plc.InputStates[0], Is.False); Assert.That(snapshot.GetPotential(sensor.Port("SIGNAL")), Is.EqualTo(ElectricalPotential.Floating));
            sensor.SetLevel(0.8); graph.AddWire(sensor.Port("SIGNAL"), "DC.DC_NEGATIVE", Color.blue);
            snapshot = graph.Solve(0);
            Assert.That(sensor.Powered, Is.True);
            Assert.That(sensor.IsActive, Is.False);
            Assert.That(snapshot.HasShortCircuit, Is.True);
            Assert.That(snapshot.GetPotential("DC.DC_POSITIVE"), Is.EqualTo(ElectricalPotential.DcPositive24));
            Assert.That(plc.InputStates[0], Is.False);
        }
        [TestCase(false)]
        [TestCase(true)]
        public void ValveRequiresCorrectPolarityAndClosesWhenDisconnected(bool reversed)
        {
            var graph = new CircuitGraph();
            var supply = new ElectricalDeviceRuntime("DC", ElectricalDeviceKind.PowerSource, new[] { "DC_POSITIVE", "DC_NEGATIVE" }) { IsDcSource = true };
            var valve = new SceneIoDeviceRuntime(SceneIoCatalog.Devices[4]);
            graph.RegisterDevice(supply); graph.RegisterDevice(valve);
            graph.AddWire("DC.DC_POSITIVE", valve.Port(reversed ? "GND" : "VCC"), Color.red);
            graph.AddWire("DC.DC_NEGATIVE", valve.Port(reversed ? "VCC" : "GND"), Color.blue);
            graph.Solve(); Assert.That(valve.IsActive, Is.EqualTo(!reversed));
            graph.ClearWires(); graph.Solve(); Assert.That(valve.IsActive, Is.False);
        }

        [TestCase(0)]
        [TestCase(4)]
        public void AcOrConflictingSupplyCannotActivateSceneDevices(int definitionIndex)
        {
            var graph = new CircuitGraph(); graph.RegisterDevice(ElectricalDeviceRuntime.CreatePowerSource());
            var device = new SceneIoDeviceRuntime(SceneIoCatalog.Devices[definitionIndex]); device.SetLevel(1);
            graph.RegisterDevice(device);
            graph.AddWire("POWER.L1", device.Port("VCC"), Color.red); graph.AddWire("POWER.N", device.Port("GND"), Color.blue);
            graph.Solve(); Assert.That(device.IsActive, Is.False); Assert.That(device.SupplyStatus, Does.Contain("交流"));
            graph.RegisterDevice(new ElectricalDeviceRuntime("DC", ElectricalDeviceKind.PowerSource, new[] { "DC_POSITIVE", "DC_NEGATIVE" }) { IsDcSource = true });
            graph.AddWire("DC.DC_POSITIVE", device.Port("VCC"), Color.red);
            var snapshot = graph.Solve(); Assert.That(device.IsActive, Is.False); Assert.That(snapshot.HasShortCircuit, Is.True);
        }

        [Test]
        public void SensorReverseSupplyAndPowerLossLeaveSignalHighImpedance()
        {
            var graph = new CircuitGraph(); var powered = true;
            graph.RegisterDevice(new ElectricalDeviceRuntime("DC", ElectricalDeviceKind.PowerSource, new[] { "DC_POSITIVE", "DC_NEGATIVE" }) { IsDcSource = true, SupplyEnabled = () => powered });
            var sensor = new SceneIoDeviceRuntime(SceneIoCatalog.Devices[0]); sensor.SetLevel(1); graph.RegisterDevice(sensor);
            graph.AddWire("DC.DC_POSITIVE", sensor.Port("GND"), Color.red); graph.AddWire("DC.DC_NEGATIVE", sensor.Port("VCC"), Color.blue);
            var snapshot = graph.Solve(); Assert.That(sensor.IsActive, Is.False);
            Assert.That(snapshot.GetPotential(sensor.Port("SIGNAL")), Is.EqualTo(ElectricalPotential.Floating));
            graph.ClearWires(); graph.AddWire("DC.DC_POSITIVE", sensor.Port("VCC"), Color.red); graph.AddWire("DC.DC_NEGATIVE", sensor.Port("GND"), Color.blue);
            graph.Solve(); Assert.That(sensor.IsActive, Is.True);
            powered = false; snapshot = graph.Solve(); Assert.That(sensor.IsActive, Is.False);
            Assert.That(snapshot.GetPotential(sensor.Port("SIGNAL")), Is.EqualTo(ElectricalPotential.Floating));
        }
    }
}
