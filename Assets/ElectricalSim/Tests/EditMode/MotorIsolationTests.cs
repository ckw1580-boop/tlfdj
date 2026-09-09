using System.Linq;
using NUnit.Framework;
using UnityEngine;

namespace ElectricalSim.Tests
{
    public sealed class MotorIsolationTests
    {
        [TestCase("M1", false)] [TestCase("M2", false)] [TestCase("M3", false)] [TestCase("M_DOUBLE", false)]
        [TestCase("M1", true)] [TestCase("M2", true)] [TestCase("M3", true)] [TestCase("M_DOUBLE", true)]
        public void OnlyWiredMotorRunsAndReverses(string id, bool inverter)
        {
            var graph = new CircuitGraph();
            graph.RegisterDevice(ElectricalDeviceRuntime.CreatePowerSource());
            foreach (var binding in MotorBindingDefinition.All) graph.RegisterDevice(ElectricalDeviceRuntime.CreateMotor(binding.Id));
            var speed = 600f;
            if (inverter)
            {
                graph.RegisterDevice(new InverterDriveRuntime("G120", () => speed, () => false));
                foreach (var phase in new[] { "L1", "L2", "L3" }) graph.AddWire("POWER." + phase, "G120." + phase, Color.red);
            }
            var sources = inverter ? new[] { "G120.U2", "G120.V2", "G120.W2" } : new[] { "POWER.L1", "POWER.L2", "POWER.L3" };
            var ports = new[] { "U", "V", "W" };
            foreach (var reverse in new[] { false, true })
            {
                foreach (var wire in graph.Wires.Where(w => w.EndPort.StartsWith(id + ".")).ToArray()) graph.RemoveWire(wire.Id);
                for (var i = 0; i < 3; i++) graph.AddWire(sources[i], id + "." + ports[reverse && i < 2 ? 1 - i : i], Color.red);
                var snapshot = graph.Solve();
                foreach (var binding in MotorBindingDefinition.All)
                {
                    Assert.That(snapshot.GetMotorSpeedRpm(binding.Id), Is.EqualTo(binding.Id == id ? (reverse ? -1 : 1) * (inverter ? 600 : 1450) : 0));
                    Assert.That(snapshot.GetMotorDirection(binding.Id), Is.EqualTo(binding.Id == id ? (reverse ? MotorDirection.Reverse : MotorDirection.Forward) : MotorDirection.Stopped));
                }
            }
            graph.RemoveWire(graph.Wires.First(w => w.EndPort == id + ".W").Id);
            Assert.That(graph.Solve(3).GetMotorSpeedRpm(id), Is.Zero);
        }

        [TestCase("KB")] [TestCase("KMB")]
        public void LegacyBrakeDoesNotAffectOtherMotors(string brakeId)
        {
            var graph = new CircuitGraph();
            graph.RegisterDevice(ElectricalDeviceRuntime.CreatePowerSource());
            foreach (var binding in MotorBindingDefinition.All)
            {
                graph.RegisterDevice(ElectricalDeviceRuntime.CreateMotor(binding.Id));
                for (var i = 0; i < 3; i++) graph.AddWire("POWER.L" + (i + 1), binding.Id + "." + new[] { "U", "V", "W" }[i], Color.red);
            }
            graph.Solve();
            var brake = ElectricalDeviceRuntime.CreateContactor(brakeId);
            graph.RegisterDevice(brake);
            brake.SetControl(true);
            var snapshot = graph.Solve(1);
            Assert.That(snapshot.GetMotorSpeedRpm("M1"), Is.Zero);
            foreach (var id in new[] { "M2", "M3", "M_DOUBLE" })
            {
                Assert.That(snapshot.GetMotorSpeedRpm(id), Is.EqualTo(1450));
                Assert.That(snapshot.GetMotorDirection(id), Is.EqualTo(MotorDirection.Forward));
            }
        }

        [Test]
        public void TerminalBridgesArchOutwardAndIgnoreCabinetBends()
        {
            var rotation = Quaternion.Euler(23, 65, 18);
            var normal = rotation * Vector3.up;
            var a = new WireEndpointGeometry(rotation * new Vector3(0, 1, 0), null, "M3", normal);
            var b = new WireEndpointGeometry(rotation * new Vector3(0.1f, 1, 0), null, "M3", normal);
            var surface = new WireSurfacePlane(Vector3.zero, Vector3.back, 0.003f);
            var path = WireRenderPath.Build(a, b, new[] { Vector3.zero }, surface, true);
            Assert.That(path.RouteKind, Is.EqualTo(WireRouteKind.MotorTerminalBridge));
            Assert.That(path.Points.First(), Is.EqualTo(a.Position));
            Assert.That(path.Points.Last(), Is.EqualTo(b.Position));
            Assert.That(path.Trunk, Is.Empty);
            Assert.That(Vector3.Dot(path.Points[16] - (a.Position + b.Position) / 2, normal), Is.EqualTo(0.02f).Within(0.00001f));
            var reverse = WireRenderPath.Build(b, a, null, surface, true);
            for (var i = 0; i < path.Points.Length; i++)
                Assert.That(Vector3.Distance(path.Points[i], reverse.Points[32 - i]), Is.LessThan(0.00001f));
            var other = new WireEndpointGeometry(b.Position, null, "M2", normal);
            Assert.That(WireRenderPath.Build(a, other, null, surface, true).RouteKind, Is.EqualTo(WireRouteKind.MotorLead));
            Assert.That(WireRenderPath.Build(a, b, null, surface).RouteKind, Is.EqualTo(WireRouteKind.Cabinet));
        }
    }
}
