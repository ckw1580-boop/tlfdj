using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using NUnit.Framework;
using UnityEngine;

namespace ElectricalSim.Tests
{
    public sealed class WiringFileTests
    {
        private string directory;

        [SetUp]
        public void SetUp()
        {
            directory = Path.Combine(Path.GetTempPath(), "接线存档测试 " + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
        }

        [TearDown]
        public void TearDown() => Directory.Delete(directory, true);

#if UNITY_EDITOR_WIN
        [Test]
        public void NativeDialogStructureMatchesWindowsAbi()
        {
            var native = typeof(WindowsFileDialog).GetNestedType("OpenFileName", BindingFlags.NonPublic);
            Assert.That(native, Is.Not.Null);
            Assert.That(Marshal.SizeOf(native), Is.EqualTo(IntPtr.Size == 8 ? 152 : 88));
            Assert.That(Marshal.OffsetOf(native, "file").ToInt32(), Is.EqualTo(IntPtr.Size == 8 ? 48 : 28));
            Assert.That(native.GetFields().All(f => f.FieldType.IsValueType), Is.True,
                "Unity Mono cannot marshal StringBuilder fields in OPENFILENAME; use native pointers.");
        }
#endif

        [Test]
        public void ActualFileRoundTripPreservesAllWireDataAndSupportsOverwrite()
        {
            var graph = new CircuitGraph();
            var front = graph.AddWire("QF.T1", "KM1.L1", new Color(.2f, .4f, .6f, .8f), "ElectricalWire", .025f);
            front.FaultSide = false;
            front.Points.AddRange(new[] { new Vector3(1, 2, 3), new Vector3(4, 5, 6) });
            var rear = graph.AddWire("FR.T1", "M1.U", Color.yellow, "JumperLine", .04f);
            rear.FaultSide = true;
            var path = Path.Combine(directory, "电柜 接线.cc3d");
            Cc3dSerializer.Save(path, Cc3dCircuitAdapter.Export(graph, Array.Empty<DeviceSceneState>()));
            var restored = new CircuitGraph();
            Cc3dCircuitAdapter.ImportWires(Cc3dSerializer.Load(path), restored);
            for (var i = 0; i < graph.Wires.Count; i++)
            {
                var a = graph.Wires[i];
                var b = restored.Wires.Single(w => w.Id == a.Id);
                Assert.That(b.StartPort, Is.EqualTo(a.StartPort));
                Assert.That(b.EndPort, Is.EqualTo(a.EndPort));
                Assert.That(b.Color, Is.EqualTo(a.Color));
                Assert.That(b.Area, Is.EqualTo(a.Area));
                Assert.That(b.LineType, Is.EqualTo(a.LineType));
                Assert.That(b.FaultSide, Is.EqualTo(a.FaultSide));
                Assert.That(b.Points, Is.EqualTo(a.Points));
            }
            restored.ClearWires();
            Cc3dSerializer.Save(path, Cc3dCircuitAdapter.Export(restored, Array.Empty<DeviceSceneState>()));
            Assert.That(Cc3dCircuitAdapter.ReadWires(Cc3dSerializer.Load(path)), Is.Empty);
            Assert.That(Directory.GetFiles(directory), Has.Length.EqualTo(1));
        }

        [Test]
        public void FailedOverwriteKeepsOriginalFileAndRemovesTemporaryFile()
        {
            var path = Path.Combine(directory, "原接线.cc3d");
            Cc3dSerializer.Save(path, new Cc3dDocument());
            var original = File.ReadAllText(path);
            using (var locked = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None))
                Assert.Throws<IOException>(() => Cc3dSerializer.Save(path, new Cc3dDocument()));
            Assert.That(File.ReadAllText(path), Is.EqualTo(original));
            Assert.That(Directory.GetFiles(directory), Has.Length.EqualTo(1));
        }

        [Test]
        public void ExportDoesNotModifyLoadedDocument()
        {
            var original = Cc3dSerializer.Deserialize("{\"line\":{},\"rootFuture\":{\"enabled\":true}}");
            var before = Cc3dSerializer.Serialize(original);
            var graph = new CircuitGraph();
            graph.AddWire("QF.T1", "KM1.L1", Color.red);
            var exported = Cc3dCircuitAdapter.Export(graph, Array.Empty<DeviceSceneState>(), original);
            exported.Extra["rootFuture"]["enabled"] = false;
            Assert.That(Cc3dSerializer.Serialize(original), Is.EqualTo(before));
        }

        [TestCase("")]
        [TestCase("{}")]
        [TestCase("[]")]
        [TestCase("{\"line\":null}")]
        [TestCase("{\"line\":{\"same\":{},\"same\":{}}}")]
        [TestCase("{\"line\":{},\"line\":{}}")]
        public void InvalidDocumentIsRejected(string json)
        {
            Assert.That(() => Cc3dSerializer.Deserialize(json), Throws.Exception);
        }

        [TestCase("missingPoint")]
        [TestCase("duplicateId")]
        [TestCase("badCoordinate")]
        [TestCase("nullLine")]
        [TestCase("emptyEndpoint")]
        [TestCase("badArea")]
        public void InvalidImportNeverClearsExistingGraph(string failure)
        {
            var graph = new CircuitGraph();
            var existing = graph.AddWire("QF.T1", "KM1.L1", Color.red);
            var document = new Cc3dDocument();
            var line = new Cc3dLine { StartDeviceId = "FR", StartPortName = "T1", EndDeviceId = "M1", EndPortName = "U" };
            document.Lines["wire"] = line;
            switch (failure)
            {
                case "missingPoint": line.Points.Add("missing"); break;
                case "duplicateId": document.RopeLines["wire"] = new Cc3dRopeLine(); break;
                case "badCoordinate":
                    line.Points.Add("p");
                    document.CustomPoints["p"] = new Cc3dPoint { Position = new[] { float.NaN, 0f, 0f } };
                    break;
                case "nullLine": document.Lines["wire"] = null; break;
                case "emptyEndpoint": line.StartPortName = ""; break;
                case "badArea": line.Area = -1; break;
            }
            Assert.Throws<InvalidDataException>(() => Cc3dCircuitAdapter.ImportWires(document, graph));
            Assert.That(graph.Wires.Single(), Is.SameAs(existing));
        }
    }
}
