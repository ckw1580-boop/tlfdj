using System;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using Object = UnityEngine.Object;

namespace ElectricalSim.Tests
{
    public sealed class OptimizationRegressionTests
    {
        [Test]
        public void StaticWireRetainsPathAcross300RefreshesAndStyleChanges()
        {
            var obj = new GameObject("CachedWire");
            var material = new Material(Shader.Find("Sprites/Default"));
            try
            {
                var wire = new WireConnection { StartPort = "a", EndPort = "b", LineType = "ElectricalWire" };
                wire.Points.Add(Vector3.one * 0.2f);
                var end = Vector3.right;
                var view = obj.AddComponent<ElectricalWireView>();
                var surface = new WireSurfacePlane(Vector3.zero, Vector3.forward, 0.003f);
                view.Initialize(wire, port => port == "a" ? Vector3.zero : end, material, surface);
                var path = view.RenderPath;
                for (var i = 0; i < 300; i++) view.Refresh();
                Assert.That(view.GeometryBuildCount, Is.EqualTo(1));
                Assert.That(view.RenderPath, Is.SameAs(path));
                view.SetSelected(true);
                wire.Color = Color.blue; wire.Area = 0.02f;
                view.Refresh();
                Assert.That(view.RenderPath, Is.SameAs(path));
                Assert.That(view.LineRenderer.startColor, Is.EqualTo(Color.blue));
                Assert.That(view.HighlightRenderer.enabled, Is.True);
                wire.Points[0] += Vector3.up;
                view.Refresh();
                Assert.That(view.GeometryBuildCount, Is.EqualTo(2));
                end += Vector3.right;
                view.Refresh();
                Assert.That(view.RenderedPoints[view.RenderedPoints.Count - 1], Is.EqualTo(end));
                Assert.That(view.GeometryBuildCount, Is.EqualTo(3));
                var replacement = CircuitGraph.CloneWire(wire);
                view.Rebind(replacement, port => port == "a" ? Vector3.zero : end, surface);
                Assert.That(view.Connection, Is.SameAs(replacement));
                Assert.That(view.GeometryBuildCount, Is.EqualTo(3));
                view.Refresh(true);
                Assert.That(view.GeometryBuildCount, Is.EqualTo(4));
            }
            finally { Object.DestroyImmediate(obj); Object.DestroyImmediate(material); }
        }

        [Test]
        public void MovingBodyInvalidatesLeadsEvenWhenTerminalIsStationary()
        {
            var body = GameObject.CreatePrimitive(PrimitiveType.Cube);
            var obj = new GameObject("BodyWire");
            var material = new Material(Shader.Find("Sprites/Default"));
            try
            {
                var geometry = new WireBodyGeometry(body.transform);
                var view = obj.AddComponent<ElectricalWireView>();
                var wire = new WireConnection { StartPort = "a", EndPort = "b" };
                view.Initialize(wire, p => p == "a" ? Vector3.zero : Vector3.right, material,
                    new WireSurfacePlane(Vector3.zero, Vector3.forward, 0.003f),
                    p => new WireEndpointGeometry(p == "a" ? Vector3.zero : Vector3.right, geometry));
                var before = view.RenderPath;
                body.transform.localScale = Vector3.one * 2;
                view.Refresh();
                Assert.That(view.RenderPath, Is.Not.SameAs(before));
                Assert.That(view.GeometryBuildCount, Is.EqualTo(2));
            }
            finally { Object.DestroyImmediate(obj); Object.DestroyImmediate(body); Object.DestroyImmediate(material); }
        }

        [Test]
        public void WiringCacheTracksDirectMutationReplacementAndKeepsOldSnapshot()
        {
            var graph = new CircuitGraph();
            graph.RegisterDevice(ElectricalDeviceRuntime.CreatePowerSource());
            var wire = graph.AddWire("POWER.L1", "TEST.a", Color.red);
            var original = graph.Solve(0);
            wire.StartPort = "POWER.L2";
            Assert.That(graph.Solve(0).GetPotential("TEST.a"), Is.EqualTo(ElectricalPotential.PhaseL2));
            Assert.That(original.GetPotential("TEST.a"), Is.EqualTo(ElectricalPotential.PhaseL1));
            graph.ReplaceWires(new[] { new WireConnection { StartPort = "POWER.N", EndPort = "TEST.b" } });
            var replaced = graph.Solve(0);
            Assert.That(replaced.ContainsPort("TEST.a"), Is.False);
            Assert.That(replaced.GetPotential("TEST.b"), Is.EqualTo(ElectricalPotential.Neutral));
            graph.ClearWires();
            Assert.That(graph.AreConnectedByWiring("POWER.N", "TEST.b"), Is.False);
            graph.RegisterDevice(new SwitchingDevice("POWER", false));
            Assert.That(graph.Solve(0).ContainsPort("POWER.L1"), Is.False);
            graph.ClearDevices();
            Assert.That(graph.Solve(0).ContainsPort("POWER.a"), Is.False);
        }

        [Test]
        public void DynamicContactsNeverEnterPermanentWiringCache()
        {
            var graph = new CircuitGraph();
            var device = new SwitchingDevice("SW", false);
            graph.RegisterDevice(device);
            Assert.That(graph.Solve(0).SameNet("SW.a", "SW.b"), Is.False);
            device.Closed = true;
            Assert.That(graph.Solve(0).SameNet("SW.a", "SW.b"), Is.True);
            Assert.That(graph.AreConnectedByWiring("SW.a", "SW.b"), Is.False);
            device.Closed = false;
            Assert.That(graph.Solve(0).SameNet("SW.a", "SW.b"), Is.False);
        }

        [Test]
        public void OscillationIsReportedSeparatelyFromShortCircuit()
        {
            var graph = new CircuitGraph();
            graph.RegisterDevice(new SwitchingDevice("SW", true));
            var snapshot = graph.Solve(0, 3);
            Assert.That(snapshot.IsConverged, Is.False);
            Assert.That(snapshot.IterationCount, Is.EqualTo(3));
            Assert.That(snapshot.HasShortCircuit, Is.False);
            graph.RegisterDevice(new SwitchingDevice("SW", false));
            snapshot = graph.Solve(0);
            Assert.That(snapshot.IsConverged, Is.True);
            Assert.That(snapshot.IterationCount, Is.EqualTo(1));
            Assert.Throws<ArgumentOutOfRangeException>(() => graph.Solve(0, 0));
        }

        private sealed class SwitchingDevice : IElectricalDevice
        {
            private readonly bool oscillate;
            public bool Closed;
            public SwitchingDevice(string id, bool oscillate) { DeviceId = id; this.oscillate = oscillate; }
            public string DeviceId { get; }
            public ElectricalDeviceKind Kind => ElectricalDeviceKind.PushButton;
            public IReadOnlyCollection<string> Ports { get; } = new[] { "a", "b" };
            public bool IsActive => Closed;
            public IEnumerable<PortPair> GetConductiveLinks()
            { if (Closed) yield return new PortPair("a", "b"); }
            public bool Evaluate(SimulationSnapshot snapshot, float deltaTime)
            { if (!oscillate) return false; Closed = !snapshot.SameNet(DeviceId + ".a", DeviceId + ".b"); return true; }
            public void ApplyVisualState(SimulationSnapshot snapshot) { }
        }
    }
}
