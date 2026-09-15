using System.Collections;
using System.IO;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace ElectricalSim.Tests
{
    public sealed class FaultPowerTerminalSceneTests
    {
        private SimulationController controller;
        private TrainingCameraController cameraController;
        private PowerTerminalBlockView block;
        private ElectricalPortView[] ports;
        private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            SceneManager.LoadScene("ElectricalTraining");
            yield return null;
            yield return null;
            controller = Object.FindObjectOfType<SimulationController>();
            cameraController = Object.FindObjectOfType<TrainingCameraController>();
            block = Object.FindObjectsOfType<PowerTerminalBlockView>().Single(b => b.Runtime.DeviceId == FaultPowerTerminalBlock.DeviceId);
            ports = Object.FindObjectsOfType<ElectricalPortView>().Where(p => p.DeviceId == FaultPowerTerminalBlock.DeviceId).ToArray();
        }

        [UnityTest]
        public IEnumerator LayoutAndPickingExposeExactlyFourteenRearElectricalPoints()
        {
            Assert.That(ports.Select(p => p.PortName), Is.EquivalentTo(new[]
            {
                "U1", "V1", "W1", "N1", "U2", "V2", "W2", "N2",
                "24V+_1", "24V-_1", "24V+_2", "24V-_2", "24V+_3", "24V-_3"
            }));
            Assert.That(block.transform.Find("mesh").childCount, Is.EqualTo(14));
            Assert.That(ports.All(p => !p.IsVisible && !p.GetComponent<Collider>().enabled), Is.True);
            Assert.That(block.Picker.enabled, Is.False);
            cameraController.SetFaultView();
            controller.SetMode(SimulationMode.Wiring);
            controller.SetWireStyle(Color.red, 0.01f, "ElectricalWire");
            yield return null;
            var camera = Camera.main;
            var ordered = ports.OrderBy(p => camera.WorldToScreenPoint(p.transform.position).x).ToArray();
            Assert.That(ordered.Select(p => p.PortName), Is.EqualTo(FaultPowerTerminalBlock.PortNames));
            var sb = GameObject.Find("OriginalLabEnvironment").GetComponentsInChildren<Transform>(true)
                .Single(t => t.name == "DuanZiPai_5" && t.Find("point") != null);
            var sbPoints = Enumerable.Range(1, 12).Select(i => sb.Find("point/a" + i)).ToArray();
            Assert.That(ordered.Max(p => camera.WorldToScreenPoint(p.transform.position).x),
                Is.LessThan(sbPoints.Min(p => camera.WorldToScreenPoint(p.position).x)));
            var meanY = sbPoints.Average(p => p.position.y);
            Assert.That(ports.All(p => Mathf.Abs(p.transform.position.y - meanY) < 0.001f), Is.True);
            var label = GameObject.Find("Terminal Annotation - Fault Power").GetComponent<TextMesh>();
            var sbLabel = GameObject.Find("Terminal Annotation - Fault Buttons SB").GetComponent<TextMesh>();
            Assert.That(label.text, Is.EqualTo("电源端子区"));
            Assert.That(label.characterSize, Is.EqualTo(sbLabel.characterSize));
            Assert.That(label.transform.localScale, Is.EqualTo(sbLabel.transform.localScale));
            Assert.That(label.GetComponent<Renderer>().enabled, Is.True);
            Capture("fault-power-overview.png");

            cameraController.enabled = false;
            var center = (ports[0].transform.position + sbPoints[5].position) * 0.5f;
            camera.transform.position = center - camera.transform.forward * 0.45f;
            yield return null;
            Physics.SyncTransforms();
            Capture("fault-power-close.png");
            foreach (var port in ports)
            {
                Assert.That(port.IsVisible, Is.True, port.PortName);
                Assert.That(port.CurrentAnchor, Is.SameAs(block.transform.Find("point/" + port.PortName)));
                Assert.That(port.HoverLabel, Is.EqualTo(port.PortName));
                Assert.That(controller.ResolveWireTerminalName(port.QualifiedPort), Does.Contain(port.PortName));
                Assert.That(Physics.Raycast(camera.ScreenPointToRay(camera.WorldToScreenPoint(port.transform.position)), out var hit, 10f), Is.True);
                Assert.That(hit.collider.GetComponent<ElectricalPortView>(), Is.SameAs(port), port.PortName + ": hit " + hit.collider.name);
            }
            var screen = (Vector2)camera.WorldToScreenPoint(block.Picker.bounds.center);
            typeof(SimulationController).GetMethod("HandleWiringPointerDown", Private).Invoke(controller, new object[] { camera, screen });
            Assert.That(controller.SelectedPowerTerminalBlock, Is.SameAs(block));
            Assert.That(controller.DescribePowerTerminalBlock(), Does.Contain("380V").And.Contain("220V").And.Contain("24V-_3").And.Contain("未供电"));
            yield return new WaitForSeconds(0.3f);
            var presenter = Object.FindObjectOfType<PanelPropertiesPresenter>();
            var propertyText = presenter.transform.Find("PanelDeviceProperties").GetComponent<UnityEngine.UI.Text>();
            Canvas.ForceUpdateCanvases();
            Assert.That(propertyText.cachedTextGenerator.characterCountVisible, Is.GreaterThanOrEqualTo(propertyText.text.Length - 1),
                "The complete power terminal description must fit in the properties panel.");
            Capture("fault-power-properties.png", true);
            controller.SetWireStyle(Color.red, 0.01f, "JumperLine");
            yield return null;
            Assert.That(ports.All(p => !p.IsVisible), Is.True);
            controller.SetWireStyle(Color.red, 0.01f, "ElectricalWire");
            cameraController.SetWiringView();
            yield return null;
            Assert.That(ports.All(p => !p.IsVisible && !p.GetComponent<Collider>().enabled), Is.True);
            Assert.That(block.Picker.enabled, Is.False);
            Assert.That(label.GetComponent<Renderer>().enabled, Is.False);
        }

        [UnityTest]
        public IEnumerator SupplyAndPropertiesFollowExistingAcAndDcBuses()
        {
            controller.SelectPowerTerminalBlock(block);
            var off = controller.Graph.Solve();
            Assert.That(off.GetAcVoltage(Node("U1"), Node("V1")), Is.Zero);
            Assert.That(off.GetDcVoltage(Node("24V+_1"), Node("24V-_1")), Is.Zero);
            controller.PanelPower.StartForAssessment();
            var on = controller.Graph.Solve();
            foreach (var phase in new[] { "U", "V", "W", "N" })
                Assert.That(on.SameNet(Node(phase + "1"), Node(phase + "2")), Is.True);
            Assert.That(on.GetAcVoltage(Node("U1"), Node("V2")), Is.EqualTo(380d));
            Assert.That(on.GetAcVoltage(Node("W2"), Node("N1")), Is.EqualTo(220d));
            for (var group = 1; group <= 3; group++)
            {
                Assert.That(on.GetDcVoltage(Node("24V+_" + group), Node("24V-_" + group)), Is.EqualTo(24d));
                Assert.That(on.SameNet(Node("24V+_" + group), "DuanZiPai_6.V_1"), Is.True);
                Assert.That(on.SameNet(Node("24V-_" + group), "DuanZiPai_6.N_1"), Is.True);
            }
            Assert.That(on.SameNet(Node("U1"), "DuanZiPai_0.a1"), Is.True);
            yield return null;
            Assert.That(controller.DescribePowerTerminalBlock(), Does.Contain("供电中"));
            controller.PanelPower.Reset();
            off = controller.Graph.Solve();
            Assert.That(off.GetAcVoltage(Node("U1"), Node("V2")), Is.Zero);
            Assert.That(off.GetDcVoltage(Node("24V+_3"), Node("24V-_2")), Is.Zero);
            Assert.That(controller.DescribePowerTerminalBlock(), Does.Contain("未供电"));
        }

        [UnityTest]
        public IEnumerator WiringDeleteUndoAndSaveReloadKeepDistinctRearEndpoints()
        {
            cameraController.SetFaultView();
            controller.SetMode(SimulationMode.Wiring);
            controller.SetWireStyle(Color.red, 0.01f, "ElectricalWire");
            yield return null;
            var allPorts = Object.FindObjectsOfType<ElectricalPortView>();
            var start = ports.Single(p => p.PortName == "24V+_3");
            var end = allPorts.Single(p => p.QualifiedPort == "DuanZiPai_0.a1");
            typeof(SimulationController).GetMethod("BeginWireRoute", Private).Invoke(controller, new object[] { start });
            typeof(SimulationController).GetMethod("CompleteWireRoute", Private).Invoke(controller, new object[] { end });
            Assert.That(controller.Graph.Wires.Count, Is.EqualTo(1));
            var wire = controller.Graph.Wires.Single();
            Assert.That(wire.StartPort, Is.EqualTo(Node("24V+_3")));
            typeof(SimulationController).GetMethod("DeleteWireSelectionOrLast", Private).Invoke(controller, null);
            Assert.That(controller.Graph.Wires, Is.Empty);
            controller.UndoWiring();
            Assert.That(controller.Graph.Wires.Count, Is.EqualTo(1));
            var path = Path.GetFullPath("Build/Reports/fault-power-roundtrip.cc3d");
            Assert.That(controller.SaveCc3dToPath(path), Is.EqualTo(WiringFileResult.Success));
            SceneManager.LoadScene("ElectricalTraining");
            yield return null;
            yield return null;
            controller = Object.FindObjectOfType<SimulationController>();
            Assert.That(controller.OpenCc3dFromPath(path), Is.EqualTo(WiringFileResult.Success));
            Object.FindObjectOfType<TrainingCameraController>().SetFaultView();
            yield return null;
            Assert.That(controller.Graph.Wires.Single().StartPort, Is.EqualTo(Node("24V+_3")));
            Assert.That(controller.Graph.Wires.Single().EndPort, Is.EqualTo("DuanZiPai_0.a1"));
            var restored = Object.FindObjectOfType<ElectricalWireView>();
            var restoredStart = Object.FindObjectsOfType<ElectricalPortView>().Single(p => p.QualifiedPort == Node("24V+_3"));
            Assert.That(Vector3.Distance(restored.RenderedPoints.First(), restoredStart.CurrentAnchorPosition), Is.LessThan(0.001f));
        }

        private static string Node(string port) => FaultPowerTerminalBlock.DeviceId + "." + port;

        private static void Capture(string name, bool includeHud = false)
        {
            var camera = Camera.main;
            var canvas = Object.FindObjectOfType<PanelPropertiesPresenter>().GetComponentInParent<Canvas>();
            var oldMode = canvas.renderMode;
            var oldCamera = canvas.worldCamera;
            var oldDistance = canvas.planeDistance;
            var target = RenderTexture.GetTemporary(1600, 900, 24);
            var oldTarget = camera.targetTexture;
            var oldActive = RenderTexture.active;
            var pixels = new Texture2D(1600, 900, TextureFormat.RGB24, false);
            try
            {
                if (includeHud)
                {
                    canvas.renderMode = RenderMode.ScreenSpaceCamera;
                    canvas.worldCamera = camera;
                    canvas.planeDistance = 0.1f;
                    Canvas.ForceUpdateCanvases();
                }
                camera.targetTexture = target;
                camera.Render();
                RenderTexture.active = target;
                pixels.ReadPixels(new Rect(0, 0, 1600, 900), 0, 0);
                pixels.Apply();
                Directory.CreateDirectory("Build/Reports");
                File.WriteAllBytes("Build/Reports/" + name, pixels.EncodeToPNG());
            }
            finally
            {
                canvas.renderMode = oldMode;
                canvas.worldCamera = oldCamera;
                canvas.planeDistance = oldDistance;
                camera.targetTexture = oldTarget;
                RenderTexture.active = oldActive;
                RenderTexture.ReleaseTemporary(target);
                Object.Destroy(pixels);
            }
        }
    }
}
