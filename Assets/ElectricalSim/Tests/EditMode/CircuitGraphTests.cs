using System.Linq;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

namespace ElectricalSim.Tests
{
    public sealed class CircuitGraphTests
    {
        [Test]
        public void CatalogContainsAllTenPlannedTasks()
        {
            var tasks = CircuitTaskCatalog.CreateAll();
            Assert.That(tasks.Count, Is.EqualTo(10));
            Assert.That(tasks.Select(task => task.Id).Distinct().Count(), Is.EqualTo(10));
        }

        [Test]
        public void StandardReferenceTopologyPassesForEveryTask()
        {
            foreach (var task in CircuitTaskCatalog.CreateAll())
            {
                var graph = new CircuitGraph();
                foreach (var pair in task.RequiredConnections) graph.AddWire(pair.A, pair.B, Color.red);
                var result = CircuitTaskEvaluator.EvaluateTopology(graph, task);
                Assert.That(result.Passed, Is.True, task.Name + ": " + result.Summary());
            }
        }

        [Test]
        public void MissingConnectionFailsTopologyEvaluation()
        {
            var task = CircuitTaskCatalog.CreateAll().First();
            var graph = new CircuitGraph();
            foreach (var pair in task.RequiredConnections.Skip(1)) graph.AddWire(pair.A, pair.B, Color.red);
            var result = CircuitTaskEvaluator.EvaluateTopology(graph, task);
            Assert.That(result.Passed, Is.False);
            Assert.That(result.MissingConnections, Is.Not.Empty);
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

        [Test]
        public void EveryReferenceTaskPassesItsActionSequence()
        {
            foreach (var task in CircuitTaskCatalog.CreateAll())
            {
                var graph = BuildCompleteTrainingGraph(out var devices);
                foreach (var pair in task.RequiredConnections) graph.AddWire(pair.A, pair.B, Color.red);

                foreach (var step in task.Actions)
                {
                    Assert.That(devices.ContainsKey(step.DeviceId), Is.True, task.Name + ": missing " + step.DeviceId);
                    devices[step.DeviceId].SetControl(step.Active);
                    SimulationSnapshot snapshot = null;
                    var ticks = Mathf.Max(8, Mathf.CeilToInt(step.HoldSeconds / 0.02f));
                    for (var i = 0; i < ticks; i++) snapshot = graph.Solve(0.02f);

                    Assert.That(snapshot.GetMotorDirection(step.ExpectedDeviceId),
                        Is.EqualTo(step.ExpectedMotorDirection),
                        task.Name + ": action " + step.DeviceId + "=" + step.Active);
                }
            }
        }

        private static CircuitGraph BuildPointControlGraph(out ElectricalDeviceRuntime button)
        {
            var graph = new CircuitGraph();
            var power = ElectricalDeviceRuntime.CreatePowerSource();
            var breaker = ElectricalDeviceRuntime.CreateBreaker("QF");
            var contactor = ElectricalDeviceRuntime.CreateContactor("KM1");
            var relay = ElectricalDeviceRuntime.CreateThermalRelay("FR");
            var motor = ElectricalDeviceRuntime.CreateMotor("M1");
            button = ElectricalDeviceRuntime.CreatePushButton("SB1", false);
            foreach (var device in new[] { power, breaker, contactor, relay, motor, button }) graph.RegisterDevice(device);

            var task = CircuitTaskCatalog.CreateAll().First(item => item.Id == "point");
            foreach (var pair in task.RequiredConnections) graph.AddWire(pair.A, pair.B, Color.red);
            return graph;
        }

        private static CircuitGraph BuildCompleteTrainingGraph(out Dictionary<string, ElectricalDeviceRuntime> devices)
        {
            var result = new Dictionary<string, ElectricalDeviceRuntime>();
            void Add(ElectricalDeviceRuntime device)
            {
                result.Add(device.DeviceId, device);
            }

            Add(ElectricalDeviceRuntime.CreatePowerSource());
            Add(ElectricalDeviceRuntime.CreateBreaker("QF"));
            foreach (var id in new[] { "KM1", "KM2", "KMF", "KMR", "KMB", "KB" })
                Add(ElectricalDeviceRuntime.CreateContactor(id));
            Add(ElectricalDeviceRuntime.CreateThermalRelay("FR"));
            Add(ElectricalDeviceRuntime.CreateTimeRelay("KT", 0.8f));
            foreach (var id in new[] { "SB0", "SB0A", "SB0B" })
                Add(ElectricalDeviceRuntime.CreatePushButton(id, true));
            foreach (var id in new[] { "SB1", "SB2", "SBF", "SBR", "SBB", "SBE", "SB1A", "SB1B" })
                Add(ElectricalDeviceRuntime.CreatePushButton(id, false));
            Add(new ElectricalDeviceRuntime("BRAKE", ElectricalDeviceKind.BrakeUnit, new[] { "IN", "OUT" }));
            Add(ElectricalDeviceRuntime.CreateMotor("M1"));
            Add(ElectricalDeviceRuntime.CreateMotor("M2"));

            var graph = new CircuitGraph();
            foreach (var device in result.Values) graph.RegisterDevice(device);
            devices = result;
            return graph;
        }
    }
}
