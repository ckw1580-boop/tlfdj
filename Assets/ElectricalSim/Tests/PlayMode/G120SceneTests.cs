using System.Collections;
using System.IO;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UI;

namespace ElectricalSim.Tests
{
    public sealed class G120SceneTests
    {
        private SimulationController controller;
        [UnitySetUp] public IEnumerator Load()
        {
            SceneManager.LoadScene("ElectricalTraining"); yield return null; yield return null;
            controller = Object.FindObjectOfType<SimulationController>();
        }
        [UnityTest] public IEnumerator AddedTerminalsUseRealFreeModulesAndPreserveOriginalPorts()
        {
            controller.SetMode(SimulationMode.Wiring); controller.SetWireStyle(Color.red, .01f, "ElectricalWire"); yield return null;
            var environment = GameObject.Find("OriginalLabEnvironment").transform;
            var views = Object.FindObjectsOfType<ElectricalDeviceView>();
            foreach (var upper in new[] { true, false })
            {
                var id = upper ? "DuanZiPai_3" : "DuanZiPai_4";
                var board = environment.GetComponentsInChildren<Transform>(true).Single(t => t.name == id && t.Find("point") != null);
                var view = views.Single(v => v.Runtime.DeviceId == id); var points = board.Find("point");
                Assert.That(view.Ports.Count, Is.EqualTo(upper ? 71 : 62));
                var reference = points.Find(upper ? "G120_l1" : "G120_U2");
                var modules = board.Find("mesh").Cast<Transform>().Where(t => t.gameObject.activeInHierarchy && t.name.StartsWith("DuanZiPai"))
                    .OrderBy(t => points.InverseTransformPoint(t.position).x).ToArray();
                var nearest = modules.OrderBy(t => Mathf.Abs(points.InverseTransformPoint(t.position).x - reference.localPosition.x)).First();
                var offset = reference.position - nearest.position;
                var added = upper ? G120TerminalCatalog.AddedUpper : G120TerminalCatalog.AddedLower;
                var lastX = float.NegativeInfinity;
                foreach (var number in added)
                {
                    var d = G120TerminalCatalog.Find(number); var port = view.Ports.Single(p => p.PortName == d.BoardPort);
                    Assert.That(port.IsVisible, Is.True); Assert.That(port.HoverLabel, Does.Contain(d.Label));
                    var x = points.InverseTransformPoint(port.CurrentAnchorPosition).x;
                    Assert.That(x, Is.GreaterThan(lastX)); Assert.That(x, Is.LessThan(reference.localPosition.x)); lastX = x;
                    var module = modules.OrderBy(t => Vector3.Distance(t.position + offset, port.CurrentAnchorPosition)).First();
                    Assert.That(Vector3.Distance(module.position + offset, port.CurrentAnchorPosition), Is.LessThan(.0005f));
                    Assert.That(controller.Graph.Solve(0).SameNet(port.QualifiedPort, d.Node), Is.True);
                    Assert.That(view.Ports.Where(p => p != port).All(p => Vector3.Distance(p.CurrentAnchorPosition, port.CurrentAnchorPosition) > .001f), Is.True);
                }
                var oldPort = view.Ports.Single(p => p.PortName == (upper ? "G120_L1" : "G120_U2"));
                Assert.That(Vector3.Distance(oldPort.CurrentAnchorPosition, reference.position), Is.LessThan(.00001f));
            }
        }
        [UnityTest] public IEnumerator ActualBoardWiringDrivesMacroOneAndPropertiesDoNotAlterWires()
        {
            controller.PanelPower.StartForAssessment();
            foreach (var p in new[] { "L1", "L2", "L3" }) controller.Graph.AddWire("POWER." + p, "DuanZiPai_3.G120_" + p, Color.red);
            controller.Graph.AddWire("DuanZiPai_3.G120_T28", "DuanZiPai_3.G120_T69", Color.black);
            controller.Graph.AddWire("DuanZiPai_3.G120_T09", "DuanZiPai_3.G120_DI0", Color.red);
            controller.Graph.AddWire("DuanZiPai_3.G120_T09", "DuanZiPai_3.G120_DI4", Color.red);
            controller.InverterPanel.TrySetParameter("P1120", .01f); controller.SetMode(SimulationMode.Simulate);
            controller.AdvanceSimulation(.1f); yield return null;
            Assert.That(controller.InverterPanel.Macro, Is.EqualTo(1)); Assert.That(controller.InverterPanel.ActualSpeedRpm, Is.EqualTo(300).Within(.1));
            var count = controller.Graph.Wires.Count; controller.SelectInverter(true); yield return null;
            Assert.That(controller.InverterProperties.IsVisible, Is.True);
            Assert.That(controller.InverterProperties.DisplayedText, Does.Contain("正转启动").And.Contain("旧模拟量端子").And.Contain("69"));
            controller.SelectInverter(false); Assert.That(controller.Graph.Wires.Count, Is.EqualTo(count));
            controller.InverterControls.Inputs[0].Simulated = true; controller.InverterControls.PtcEnabled = true;
            controller.ResetTraining(); Assert.That(controller.InverterControls.Inputs[0].Simulated, Is.False); Assert.That(controller.InverterControls.PtcEnabled, Is.False);
        }
        [UnityTest] public IEnumerator NewPortsSupportNormalWireRoutingUndoAndRedo()
        {
            controller.SetMode(SimulationMode.Wiring); controller.SetWireStyle(Color.green, .01f, "ElectricalWire"); yield return null;
            var ports = Object.FindObjectsOfType<ElectricalPortView>();
            var start = ports.Single(p => p.QualifiedPort == "DuanZiPai_3.G120_T01");
            var end = ports.Single(p => p.QualifiedPort == "DuanZiPai_3.G120_T03");
            var flags = BindingFlags.Instance | BindingFlags.NonPublic;
            typeof(SimulationController).GetMethod("BeginWireRoute", flags).Invoke(controller, new object[] { start });
            typeof(SimulationController).GetMethod("CompleteWireRoute", flags).Invoke(controller, new object[] { end });
            yield return null;
            Assert.That(controller.Graph.Wires.Count, Is.EqualTo(1));
            Assert.That(controller.Graph.Wires[0].StartPort, Is.EqualTo(start.QualifiedPort));
            controller.UndoWiring(); Assert.That(controller.Graph.Wires.Count, Is.Zero);
            controller.RedoWiring(); Assert.That(controller.Graph.Wires.Count, Is.EqualTo(1));
            var path = Path.Combine(Application.dataPath, "../Build/Reports/g120-roundtrip.cc3d");
            controller.InverterControls.Inputs[0].Simulated = true; controller.InverterControls.Inputs[0].SimulatedValue = 7;
            controller.InverterControls.PtcEnabled = true;
            Assert.That(controller.SaveCc3dToPath(path), Is.EqualTo(WiringFileResult.Success), controller.LastFileError);
            controller.ResetTraining();
            Assert.That(controller.OpenCc3dFromPath(path), Is.EqualTo(WiringFileResult.Success), controller.LastFileError);
            Assert.That(controller.Graph.Wires.Single().StartPort, Is.EqualTo(start.QualifiedPort));
            Assert.That(controller.Graph.Wires.Single().EndPort, Is.EqualTo(end.QualifiedPort));
            Assert.That(controller.InverterControls.Inputs[0].Simulated, Is.False);
            Assert.That(controller.InverterControls.PtcEnabled, Is.False);
        }
        [UnityTest] public IEnumerator BodyAndBothControlRegionsOpenPropertiesOnlyInInspectionModes()
        {
            var camera = Camera.main; Object.FindObjectOfType<TrainingCameraController>().enabled = false;
            var environment = GameObject.Find("OriginalLabEnvironment");
            var outward = GameObject.Find("Front Independent Breakers").transform.forward;
            var body = controller.InverterModel.GetComponentsInChildren<Renderer>().OrderByDescending(r => r.bounds.size.sqrMagnitude).First().bounds.center;
            var targets = new System.Collections.Generic.List<Vector3> { body };
            foreach (var item in new[] { new { Id = "DuanZiPai_3", Port = "G120_T01" }, new { Id = "DuanZiPai_4", Port = "G120_T14" } })
            {
                var board = environment.GetComponentsInChildren<Transform>(true).Single(t => t.name == item.Id && t.Find("point") != null);
                var anchor = board.Find("point/" + item.Port);
                var renderer = board.Find("mesh").GetComponentsInChildren<Renderer>().OrderBy(r => Vector3.Distance(r.bounds.center, anchor.position)).First();
                targets.Add(renderer.bounds.center);
            }
            var method = typeof(SimulationController).GetMethod("HandleScenePointerDown", BindingFlags.Instance | BindingFlags.NonPublic);
            foreach (var mode in new[] { SimulationMode.View, SimulationMode.Simulate })
            {
                controller.SetMode(mode); yield return null;
                camera.fieldOfView = 32; camera.transform.position = body + outward * .85f; camera.transform.LookAt(body);
                Physics.SyncTransforms(); Canvas.ForceUpdateCanvases();
                foreach (var target in targets)
                {
                    controller.SelectInverter(false);
                    method.Invoke(controller, new object[] { camera, (Vector2)camera.WorldToScreenPoint(target) });
                    Assert.That(controller.InverterProperties.IsVisible, Is.True, mode + " inspection target " + target);
                }
            }
            controller.SetMode(SimulationMode.Wiring);
            method.Invoke(controller, new object[] { camera, (Vector2)camera.WorldToScreenPoint(body) });
            Assert.That(controller.InverterProperties.IsVisible, Is.False);
        }
        [UnityTest] public IEnumerator CaptureG120LayoutAndPropertiesAtBothResolutions()
        {
            var camera = Camera.main; Object.FindObjectOfType<TrainingCameraController>().enabled = false;
            var environment = GameObject.Find("OriginalLabEnvironment");
            var upper = environment.GetComponentsInChildren<Transform>(true).Single(t => t.name == "DuanZiPai_3" && t.Find("point") != null);
            var lower = environment.GetComponentsInChildren<Transform>(true).Single(t => t.name == "DuanZiPai_4" && t.Find("point") != null);
            var targets = upper.Find("point").Cast<Transform>().Concat(lower.Find("point").Cast<Transform>()).Where(t => t.name.StartsWith("G120_")).ToArray();
            var center = targets.Aggregate(Vector3.zero, (sum, t) => sum + t.position) / targets.Length;
            var outward = GameObject.Find("Front Independent Breakers").transform.forward;

            foreach (var width in new[] { 1920, 1280 })
            {
                var height = width * 9 / 16; Screen.SetResolution(width, height, false); yield return null; yield return null;
                controller.SetMode(SimulationMode.Wiring); controller.SetWireStyle(Color.red, .01f, "ElectricalWire"); yield return null;
                camera.fieldOfView = 32; camera.transform.position = center + outward * .85f; camera.transform.LookAt(center);
                Physics.SyncTransforms();
                foreach (var port in Object.FindObjectsOfType<ElectricalPortView>().Where(p => p.PortName.StartsWith("G120_T")))
                {
                    var screen = camera.WorldToScreenPoint(port.CurrentAnchorPosition);
                    Assert.That(Vector2.Distance(camera.WorldToScreenPoint(port.transform.position), screen), Is.LessThanOrEqualTo(2));
                    Assert.That(Physics.Raycast(camera.ScreenPointToRay(screen), out var hit, 10), Is.True, port.QualifiedPort);
                    Assert.That(hit.collider.GetComponent<ElectricalPortView>(), Is.SameAs(port), port.QualifiedPort + " must be pickable at its visible marker");
                }
                Capture(camera, width, height, "g120-terminals-" + width);
                controller.SetMode(SimulationMode.View); controller.SelectInverter(true); yield return null;
                var canvas = controller.InverterProperties.GetComponentInParent<Canvas>(); var oldMode = canvas.renderMode; var oldCamera = canvas.worldCamera; var oldDistance = canvas.planeDistance;
                canvas.renderMode = RenderMode.ScreenSpaceCamera; canvas.worldCamera = camera; canvas.planeDistance = .2f; Canvas.ForceUpdateCanvases();
                camera.fieldOfView = 32; camera.transform.position = center + outward * .85f; camera.transform.LookAt(center);
                Capture(camera, width, height, "g120-properties-" + width, controller.InverterProperties);
                controller.InverterProperties.GetComponentInChildren<ScrollRect>().verticalNormalizedPosition = 0;
                Canvas.ForceUpdateCanvases();
                Capture(camera, width, height, "g120-properties-bottom-" + width, controller.InverterProperties);
                canvas.renderMode = oldMode; canvas.worldCamera = oldCamera; canvas.planeDistance = oldDistance;
                controller.SelectInverter(false);
            }
        }
        private static void Capture(Camera camera, int width, int height, string name, G120PropertiesPresenter panel = null)
        {
            var target = new RenderTexture(width, height, 24); var texture = new Texture2D(width, height, TextureFormat.RGB24, false);
            var old = RenderTexture.active; var previous = camera.targetTexture;
            camera.targetTexture = target; Canvas.ForceUpdateCanvases();
            if (panel != null)
            {
                var scroll = panel.GetComponentInChildren<ScrollRect>();
                scroll.verticalNormalizedPosition = name.Contains("bottom") ? 0 : 1;
                Canvas.ForceUpdateCanvases();
                Assert.That(scroll.verticalNormalizedPosition, Is.EqualTo(name.Contains("bottom") ? 0 : 1).Within(.001));
                var corners = new Vector3[4]; ((RectTransform)panel.transform).GetWorldCorners(corners);
                foreach (var corner in corners) { var p = camera.WorldToViewportPoint(corner); Assert.That(p.x, Is.InRange(0f, 1.001f)); Assert.That(p.y, Is.InRange(0f, 1.001f)); }
            }
            camera.Render();
            if (panel != null && name.Contains("bottom"))
            {
                var d = panel.GetComponentsInChildren<Text>().OrderByDescending(t => t.text.Length).First();
                var sc = panel.GetComponentInChildren<ScrollRect>();
                Assert.That(d.rectTransform.rect.height, Is.GreaterThanOrEqualTo(d.preferredHeight));
                var bottom = sc.viewport.InverseTransformPoint(d.transform.TransformPoint(new Vector3(0, -d.preferredHeight, 0)));
                Assert.That(bottom.y, Is.GreaterThanOrEqualTo(sc.viewport.rect.yMin - .5f), "The final terminal and compatibility notes must be reachable by scrolling");
            }
            RenderTexture.active = target;
            texture.ReadPixels(new Rect(0, 0, width, height), 0, 0); texture.Apply();
            var directory = Path.Combine(Application.dataPath, "../Build/Reports"); Directory.CreateDirectory(directory); File.WriteAllBytes(Path.Combine(directory, name + ".png"), texture.EncodeToPNG());
            camera.targetTexture = previous; RenderTexture.active = old; Object.Destroy(texture); Object.Destroy(target);
        }
    }
}
