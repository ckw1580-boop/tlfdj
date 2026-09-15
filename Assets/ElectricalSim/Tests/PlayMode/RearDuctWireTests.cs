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

        private static int CaptureRedPixels(Camera camera, string name)
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
                // Central duct interior, excluding device labels and terminal leads.
                for (var y = 220; y < 480; y++)
                    for (var x = 340; x < 360; x++)
                    {
                        var p = pixels.GetPixel(x, y);
                        if (p.r > 0.7f && p.g < 0.3f && p.b < 0.3f) count++;
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
