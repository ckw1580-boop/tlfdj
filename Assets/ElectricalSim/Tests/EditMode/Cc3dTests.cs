using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityEngine;

namespace ElectricalSim.Tests
{
    public sealed class Cc3dTests
    {
        [Test]
        public void UnknownFieldsSurviveRoundTrip()
        {
            const string json = "{\"element\":{\"1\":{\"id\":\"abc\",\"_path\":\"model\",\"type\":\"Contactor\",\"Name\":null,\"rotation\":[0,0,0],\"futureField\":42}},\"customPoints\":{},\"line\":{},\"ropeLine\":{},\"rootFuture\":{\"enabled\":true}}";
            var document = Cc3dSerializer.Deserialize(json);
            var serialized = Cc3dSerializer.Serialize(document);
            var root = JObject.Parse(serialized);
            Assert.That((int)root["element"]["1"]["futureField"], Is.EqualTo(42));
            Assert.That((bool)root["rootFuture"]["enabled"], Is.True);
        }

        [Test]
        public void RopeLineImportsAndExportsWithStableIdentity()
        {
            var document = new Cc3dDocument();
            document.RopeLines["wire-1"] = new Cc3dRopeLine
            {
                StartDeviceId = "QF",
                StartPortName = "T1",
                EndDeviceId = "KM1",
                EndPortName = "L1",
                LineColor = new[] { 1f, 0f, 0f, 1f }
            };
            var graph = new CircuitGraph();
            Cc3dCircuitAdapter.ImportWires(document, graph);
            Assert.That(graph.Wires.Count, Is.EqualTo(1));
            Assert.That(graph.Wires[0].Id, Is.EqualTo("wire-1"));

            var exported = Cc3dCircuitAdapter.Export(graph, new List<DeviceSceneState>());
            Assert.That(exported.RopeLines.ContainsKey("wire-1"), Is.True);
            Assert.That(exported.RopeLines["wire-1"].StartDeviceId, Is.EqualTo("QF"));
            Assert.That(exported.RopeLines["wire-1"].EndPortName, Is.EqualTo("L1"));
        }

        [Test]
        public void LinePointsAndColorSurviveAdapterRoundTrip()
        {
            var graph = new CircuitGraph();
            var wire = graph.AddWire("QF.T1", "KM1.L1", Color.yellow, "Elec", 0.025f);
            wire.Points.Add(new Vector3(1f, 2f, 3f));
            var document = Cc3dCircuitAdapter.Export(graph, new List<DeviceSceneState>());
            var imported = new CircuitGraph();
            Cc3dCircuitAdapter.ImportWires(document, imported);
            Assert.That(imported.Wires[0].Points.Count, Is.EqualTo(1));
            Assert.That(imported.Wires[0].Points[0], Is.EqualTo(new Vector3(1f, 2f, 3f)));
            Assert.That(imported.Wires[0].Area, Is.EqualTo(0.025f));
        }
    }
}
