using System.Collections;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace ElectricalSim.Tests
{
    public sealed class RearDuctWireTests
    {
        [UnityTest]
        public IEnumerator RearBreakersRouteAroundBodiesAndAcrossMountingPlates()
        {
            SceneManager.LoadScene("ElectricalTraining", LoadSceneMode.Single);
            yield return null;
            yield return null;
            var controller = Object.FindObjectOfType<SimulationController>();
            controller.SetMode(SimulationMode.Wiring);
            var cameraController = Object.FindObjectOfType<TrainingCameraController>();
            cameraController.SetFaultView();
            yield return null;
            var ports = Object.FindObjectsOfType<ElectricalPortView>();
            var target = ports.Single(p => p.QualifiedPort == "KMBACK1.L1");
            var camera = Camera.main;
            var cameraPosition = camera.transform.position;
            var cameraRotation = camera.transform.rotation;
            var meshRoot = GameObject.Find("OriginalLabEnvironment/Bench/ElectricBench/mesh").transform;
            var covers = meshRoot.Find("xiancaogai_1").GetComponentsInChildren<MeshFilter>(true);
            foreach (var id in new[] { "QF122", "QF106" })
            {
                var breakerPorts = ports.Where(p => p.DeviceId == id).ToArray();
                Assert.That(breakerPorts.Length, Is.GreaterThanOrEqualTo(6));
                Assert.That(breakerPorts.All(p => p.RearWireBody != null), Is.True);
                foreach (var terminal in new[] { "L1", "L2" })
                {
                    var start = breakerPorts.Single(p => p.PortName == terminal);
                    var connection = controller.Graph.AddWire(start.QualifiedPort, target.QualifiedPort, Color.red, "ElectricalWire");
                    controller.AddBendPointToLastWire(start.CurrentAnchorPosition + Vector3.up * 0.1f);
                    yield return null;
                    var wire = Object.FindObjectsOfType<ElectricalWireView>().Single(v => v.Connection.Id == connection.Id);
                    var surface = wire.Surface;
                    var inverse = Quaternion.Inverse(surface.Rotation);
                    Assert.That(start.RearWireBody.TryGetBounds(surface, out var body), Is.True);
                    var lidBounds = covers.Select(lid => MeshBounds(lid, inverse)).ToArray();
                    var side = lidBounds.Where(b => b.size.y > b.size.x)
                        .OrderBy(b => Mathf.Abs(b.center.x - body.center.x)).First();
                    var below = lidBounds.Where(b => b.size.x > b.size.y && b.center.y < body.min.y)
                        .OrderByDescending(b => b.center.y).First();
                    var material = new Material(Resources.Load<Shader>("CabinetWire"));
                    var draftObject = new GameObject("BreakerDraft");
                    var draft = draftObject.AddComponent<ElectricalWireDraftView>();
                    draft.Initialize(() => start.CurrentAnchorPosition, material, Color.red, 0.01f, surface,
                        () => start.EndpointGeometry(start.CurrentAnchorPosition, true));
                    try
                    {
                        foreach (var throughSide in new[] { true, false })
                        {
                            connection.Points.Clear();
                            var exitY = terminal == "L1" ? body.max.y + 0.025f : body.min.y - 0.025f;
                            var x = throughSide ? side.center.x : body.center.x;
                            var baseZ = (inverse * surface.Origin).z;
                            connection.Points.Add(surface.Rotation * new Vector3(x, exitY, baseZ));
                            connection.Points.Add(surface.Rotation * new Vector3(x, below.center.y, baseZ));
                            var saved = connection.Points.ToArray();
                            wire.Refresh();
                            draft.Refresh(connection.Points, target.CurrentAnchorPosition,
                                target.EndpointGeometry(target.CurrentAnchorPosition, true));
                            CollectionAssert.AreEqual(wire.RenderPath.Points, draft.RenderPath.Points);
                            Assert.That(wire.RenderedPoints.First(), Is.EqualTo(start.CurrentAnchorPosition));
                            Assert.That(wire.RenderedPoints.Last(), Is.EqualTo(target.CurrentAnchorPosition));
                            Assert.That(wire.RenderPath.StartLead.Length, Is.EqualTo(4));
                            var exit = inverse * wire.RenderPath.StartLead[2];
                            Assert.That(exit.x < body.min.x || exit.x > body.max.x ||
                                exit.y < body.min.y || exit.y > body.max.y, Is.True);
                            draft.SetVisible(false);
                            var focus = surface.Rotation * new Vector3(body.center.x,
                                (body.center.y + below.center.y) * 0.5f, baseZ);
                            foreach (var oblique in new[] { false, true })
                            {
                                camera.transform.position = focus + surface.Normal * 1.3f +
                                    surface.Rotation * Vector3.right * (oblique ? 0.25f : 0f);
                                camera.transform.LookAt(focus);
                                camera.orthographic = true;
                                camera.orthographicSize = 0.38f;
                                var edit = surface.Project(saved[0]);
                                Assert.That(surface.Raycast(camera.ScreenPointToRay(camera.WorldToScreenPoint(edit)), out var hit), Is.True);
                                Assert.That(Vector3.Distance(hit, edit), Is.LessThan(0.0002f));
                                wire.SetSelected(true, 0);
                                Assert.That(wire.TryHitNode(camera, camera.WorldToScreenPoint(edit), 4f, out var node), Is.True);
                                Assert.That(node, Is.Zero);
                                wire.SetSelected(false);
                                if (SystemInfo.graphicsDeviceType != UnityEngine.Rendering.GraphicsDeviceType.Null)
                                {
                                    CaptureRedPixels(camera, $"rear-breaker-{id}-{terminal}-{throughSide}-{oblique}.png");
                                    if (!throughSide)
                                    {
                                        var probe = wire.RenderPath.Trunk.Where(p => (inverse * p).y < body.min.y - 0.01f &&
                                                (inverse * p).y > below.max.y + 0.01f)
                                            .OrderBy(p => Mathf.Abs((inverse * p).y - (body.min.y - 0.04f))).First();
                                        Assert.That(CaptureRedPixels(camera, null, probe), Is.GreaterThan(2),
                                            $"{id}/{terminal}: exposed wire below breaker must remain visible (oblique={oblique})");
                                        var ductProbe = wire.RenderPath.Trunk.OrderBy(p =>
                                            Mathf.Abs((inverse * p).y - below.center.y)).First();
                                        Assert.That((inverse * ductProbe).z, Is.LessThan(below.max.z),
                                            "Wire must stay behind the lid's outer face (the imported rail overlaps its thickness)");
                                        Assert.That(CaptureRedPixels(camera, null, ductProbe), Is.GreaterThan(2),
                                            "Wire must be visible through the open upper duct");
                                        wire.SetSelected(true);
                                        controller.SetMode(SimulationMode.View);
                                        Assert.That(CaptureRedPixels(camera, null, ductProbe), Is.Zero,
                                            "Closing the upper duct must occlude both the wire and its highlight");
                                        controller.SetMode(SimulationMode.Wiring);
                                        wire.SetSelected(false);
                                    }
                                }
                            }
                            CollectionAssert.AreEqual(saved, connection.Points);
                            cameraController.ResetView();
                            wire.Refresh();
                            CollectionAssert.AreEqual(saved, connection.Points);
                            cameraController.SetFaultView();
                        }
                    }
                    finally
                    {
                        Object.Destroy(draftObject);
                        Object.Destroy(material);
                    }
                }
            }
            camera.transform.SetPositionAndRotation(cameraPosition, cameraRotation);
        }

        private static Bounds MeshBounds(MeshFilter mesh, Quaternion inverse)
        {
            if (!mesh.sharedMesh.isReadable)
            {
                var world = mesh.GetComponent<Renderer>().bounds;
                var result = new Bounds(inverse * world.center, Vector3.zero);
                for (var i = 0; i < 8; i++)
                    result.Encapsulate(inverse * (world.center + Vector3.Scale(world.extents,
                        new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1))));
                return result;
            }
            var vertices = mesh.sharedMesh.vertices;
            var bounds = new Bounds(inverse * mesh.transform.TransformPoint(vertices[0]), Vector3.zero);
            foreach (var vertex in vertices) bounds.Encapsulate(inverse * mesh.transform.TransformPoint(vertex));
            return bounds;
        }

        [UnityTest]
        public IEnumerator BothRearSideDuctsShowWiresAndMatchingDrafts()
        {
            SceneManager.LoadScene("ElectricalTraining", LoadSceneMode.Single);
            yield return null;
            yield return null;
            var controller = Object.FindObjectOfType<SimulationController>();
            controller.SetMode(SimulationMode.Wiring);
            Object.FindObjectOfType<TrainingCameraController>().SetFaultView();
            yield return null;
            var ports = Object.FindObjectsOfType<ElectricalPortView>();
            var start = ports.Single(p => p.QualifiedPort == "KMBACK1.L1");
            var end = ports.Single(p => p.QualifiedPort == "KMBACK1.L2");
            var connection = controller.Graph.AddWire(start.QualifiedPort, end.QualifiedPort, Color.red, "ElectricalWire");
            controller.AddBendPointToLastWire(start.CurrentAnchorPosition + Vector3.up * 0.1f);
            yield return null;
            var wire = Object.FindObjectsOfType<ElectricalWireView>().Single(v => v.Connection.Id == connection.Id);
            var surface = wire.Surface;
            Assert.That(surface.Ducts, Is.Not.Null);
            var inverse = Quaternion.Inverse(surface.Rotation);
            var covers = GameObject.Find("OriginalLabEnvironment/Bench/ElectricBench/mesh").transform.Find("xiancaogai_1");
            var sideLids = covers.GetComponentsInChildren<MeshFilter>(true)
                .Where(lid => lid.name.StartsWith("gaizi_4") || lid.name.StartsWith("gaizi_5")).ToArray();
            Assert.That(sideLids.Length, Is.EqualTo(2));
            var material = new Material(Resources.Load<Shader>("CabinetWire"));
            var draftObject = new GameObject("SideDuctDraft");
            var draft = draftObject.AddComponent<ElectricalWireDraftView>();
            draft.Initialize(() => start.CurrentAnchorPosition, material, Color.red, 0.01f, surface,
                () => start.EndpointGeometry(start.CurrentAnchorPosition, true));
            try
            {
                foreach (var lid in sideLids)
                {
                    var vertices = lid.sharedMesh.vertices.Select(p => inverse * lid.transform.TransformPoint(p)).ToArray();
                    var x = (vertices.Min(p => p.x) + vertices.Max(p => p.x)) * 0.5f;
                    var z = vertices.Min(p => p.z) - 0.01f;
                    connection.Points.Clear();
                    // Existing CC3D routes have bends at the old mounting-plane depth.
                    foreach (var y in new[] { 1.25f, 0.85f })
                        connection.Points.Add(surface.Rotation * new Vector3(x, y, (inverse * surface.Origin).z));
                    var savedBends = connection.Points.ToArray();
                    wire.Refresh();
                    draft.Refresh(connection.Points, end.CurrentAnchorPosition, end.EndpointGeometry(end.CurrentAnchorPosition, true));
                    CollectionAssert.AreEqual(wire.RenderPath.Points, draft.RenderPath.Points);
                    Assert.That(wire.RenderedPoints.First(), Is.EqualTo(start.CurrentAnchorPosition));
                    Assert.That(wire.RenderedPoints.Last(), Is.EqualTo(end.CurrentAnchorPosition));
                    var interior = wire.RenderPath.Trunk.Select(p => inverse * p)
                        .Where(p => p.y > 0.9f && p.y < 1.2f && Mathf.Abs(p.x - x) < 0.001f).ToArray();
                    Assert.That(interior.Length, Is.GreaterThan(4));
                    Assert.That(interior.All(p => Mathf.Abs(p.z - z) < 0.0001f), Is.True);
                    var midpoint = surface.Rotation * new Vector3(x, 1.05f, z);
                    var camera = Camera.main;
                    camera.transform.position = midpoint + surface.Normal * 1.3f;
                    camera.transform.LookAt(midpoint);
                    camera.orthographic = true;
                    camera.orthographicSize = 0.26f;
                    draft.SetVisible(false);
                    if (SystemInfo.graphicsDeviceType != UnityEngine.Rendering.GraphicsDeviceType.Null)
                    {
                        var label = lid.name.StartsWith("gaizi_4") ? "left" : "right";
                        var visible = CaptureRedPixels(camera, "rear-duct-" + label + ".png");
                        Assert.That(visible, Is.GreaterThan(100), "The wire must render inside the open side duct");
                        wire.SetSurface(new WireSurfacePlane(surface.SurfacePoint, surface.Normal, surface.SurfaceOffset,
                            surface.SurfaceBounds));
                        var occluded = CaptureRedPixels(camera, "rear-duct-" + label + "-before.png");
                        Assert.That(occluded, Is.LessThan(visible / 4), "The old plane must reproduce the hidden wire");
                        wire.SetSurface(surface);
                        wire.SetSelected(true, 0);
                        CaptureRedPixels(camera, "rear-duct-" + label + "-selected.png");
                        controller.SetMode(SimulationMode.View);
                        Assert.That(CaptureRedPixels(camera, null), Is.LessThan(visible / 4),
                            "Closed duct lids must still occlude wires and selection highlights");
                        controller.SetMode(SimulationMode.Wiring);
                        wire.SetSelected(false);
                    }
                    camera.transform.position += surface.Rotation * Vector3.right * 0.25f;
                    camera.transform.LookAt(midpoint);
                    var editPoint = surface.Project(connection.Points[0]);
                    var ray = camera.ScreenPointToRay(camera.WorldToScreenPoint(editPoint));
                    Assert.That(surface.Raycast(ray, out var hit), Is.True);
                    Assert.That(Vector3.Distance(hit, editPoint), Is.LessThan(0.0002f));
                    wire.SetSelected(true);
                    Assert.That(wire.TryHitNode(camera, camera.WorldToScreenPoint(editPoint), 4f, out var node), Is.True);
                    Assert.That(node, Is.Zero);
                    Assert.That(wire.TryHitLine(camera, camera.WorldToScreenPoint(midpoint), 4f,
                        out _, out var insertion, out _), Is.True);
                    Assert.That(insertion, Is.EqualTo(1));
                    wire.SetSelected(false);
                    CollectionAssert.AreEqual(savedBends, connection.Points);
                }
            }
            finally
            {
                Object.Destroy(draftObject);
                Object.Destroy(material);
            }
        }

        private static int CaptureRedPixels(Camera camera, string name, Vector3? probe = null)
        {
            var target = RenderTexture.GetTemporary(700, 700, 24);
            var oldTarget = camera.targetTexture;
            var oldActive = RenderTexture.active;
            var pixels = new Texture2D(700, 700, TextureFormat.RGB24, false);
            try
            {
                camera.targetTexture = target;
                camera.Render();
                RenderTexture.active = target;
                pixels.ReadPixels(new Rect(0, 0, 700, 700), 0, 0);
                pixels.Apply();
                if (name != null)
                {
                    System.IO.Directory.CreateDirectory("Build/Reports");
                    System.IO.File.WriteAllBytes("Build/Reports/" + name, pixels.EncodeToPNG());
                }
                var count = 0;
                // Either a specific exposed wire sample or the central duct interior.
                var screen = probe.HasValue ? camera.WorldToViewportPoint(probe.Value) * 700f : Vector3.zero;
                var minY = probe.HasValue ? Mathf.Clamp(Mathf.RoundToInt(screen.y) - 4, 0, 699) : 220;
                var maxY = probe.HasValue ? Mathf.Clamp(Mathf.RoundToInt(screen.y) + 5, 0, 700) : 480;
                var minX = probe.HasValue ? Mathf.Clamp(Mathf.RoundToInt(screen.x) - 4, 0, 699) : 340;
                var maxX = probe.HasValue ? Mathf.Clamp(Mathf.RoundToInt(screen.x) + 5, 0, 700) : 360;
                for (var y = minY; y < maxY; y++)
                    for (var x = minX; x < maxX; x++)
                    {
                        var p = pixels.GetPixel(x, y);
                        if (p.r > 0.7f && (probe.HasValue || p.g < 0.3f) && p.b < 0.3f) count++;
                    }
                return count;
            }
            finally
            {
                camera.targetTexture = oldTarget;
                RenderTexture.active = oldActive;
                RenderTexture.ReleaseTemporary(target);
                Object.Destroy(pixels);
            }
        }
    }
}
