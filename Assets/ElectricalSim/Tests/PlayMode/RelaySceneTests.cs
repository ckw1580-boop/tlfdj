using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace ElectricalSim.Tests
{
    public sealed class RelaySceneTests
    {
        private SimulationController controller;
        private string path;
        [UnitySetUp]
        public IEnumerator Setup()
        {
            SceneManager.LoadScene("ElectricalTraining"); yield return null; yield return null;
            controller = Object.FindObjectOfType<SimulationController>();
            path = Path.Combine(Application.temporaryCachePath, "relay-test-" + Guid.NewGuid().ToString("N") + ".cc3d");
        }
        [TearDown] public void Cleanup() { if (File.Exists(path)) File.Delete(path); }
        private void Wire(string a, string b) => controller.Graph.AddWire(a, b, Color.red, "ElectricalWire");
        private string Physical(string id, string terminal) => controller.RelayViews.Single(v => v.Runtime.DeviceId == id).Bindings[terminal].QualifiedPort;

        [UnityTest]
        public IEnumerator SixBodiesAndAll84OriginalAnchorsAreBound()
        {
            var environment = GameObject.Find("OriginalLabEnvironment").transform;
            var boards = environment.GetComponentsInChildren<Transform>(true)
                .Where(t => (t.name == "DuanZiPai_1" || t.name == "DuanZiPai_2") && t.Find("point") != null)
                .ToDictionary(t => t.name, t => t.Find("point"));
            Assert.That(controller.RelayViews.Count, Is.EqualTo(6));
            Assert.That(Object.FindObjectsOfType<IntermediateRelayView>().Length, Is.EqualTo(6));
            var snapshot = controller.Graph.Solve();
            foreach (var view in controller.RelayViews)
            {
                Assert.That(view.transform, Is.SameAs(environment.Find(view.Definition.ModelPath)));
                Assert.That(view.gameObject.activeInHierarchy, Is.True);
                Assert.That(view.Picker.enabled, Is.True);
                Assert.That(view.Picker.transform, Is.SameAs(view.transform.Find("picker")), "应复用本体的原始点击区域");
                Assert.That(controller.Graph.Devices[view.Runtime.DeviceId], Is.SameAs(view.Runtime));
                Assert.That(view.Bindings.Count, Is.EqualTo(14));
                foreach (var binding in view.Bindings)
                {
                    var port = binding.Value;
                    var expectedBoard = new[] { "1", "2", "3", "5", "6", "7", "8" }.Contains(binding.Key) ? "DuanZiPai_1" : "DuanZiPai_2";
                    Assert.That(port.DeviceId, Is.EqualTo(expectedBoard));
                    Assert.That(port.HoverLabel, Is.EqualTo(view.Runtime.DeviceId + "_" + binding.Key));
                    var anchor = boards[expectedBoard].Find(port.PortName);
                    Assert.That(port.GetOriginalAnchor(TrainingViewPreset.WiringFront, false), Is.SameAs(anchor));
                    Assert.That(Vector3.Distance(port.CurrentAnchorPosition, anchor.position), Is.LessThan(0.0005f));
                    Assert.That(snapshot.SameNet(port.QualifiedPort, view.Runtime.DeviceId + "." + binding.Key), Is.True);
                }
                var ray = new Ray(view.Picker.bounds.center + Vector3.forward, Vector3.back);
                Assert.That(view.Picker.Raycast(ray, out _, 3f), Is.True);
                Physics.SyncTransforms();
                // Aim at the visible front face, not at the occluded interior of the
                // housing when the cabinet is viewed obliquely.
                var box = (BoxCollider)view.Picker;
                var face = box.transform.TransformPoint(box.center + Vector3.forward * box.size.z * 0.5f);
                var pointerRay = new Ray(Camera.main.transform.position, face - Camera.main.transform.position);
                Assert.That(Physics.Raycast(pointerRay, out var hit, 100f), Is.True);
                Assert.That(hit.collider.GetComponentInParent<IntermediateRelayView>(), Is.SameAs(view),
                    view.Runtime.DeviceId + " 本体必须能从正常视角选中，实际命中：" + hit.collider.GetComponentInParent<IntermediateRelayView>()?.Runtime.DeviceId);
                controller.SelectRelay(view);
                Assert.That(controller.RelayProperties.gameObject.activeInHierarchy, Is.True);
                Assert.That(controller.RelayProperties.DisplayedText, Does.Contain(view.Runtime.DeviceId).And.Contain("DC 24V"));
            }
            yield return null;
        }

        [UnityTest]
        public IEnumerator PhysicalWiringDrivesLampAndPropertiesAndSurvivesSaveLoad()
        {
            var view = controller.RelayViews[0];
            Wire("TERMINAL_BUS.DC_POSITIVE", Physical("KA1", "13"));
            Wire("TERMINAL_BUS.DC_NEGATIVE", Physical("KA1", "14"));
            Wire("TERMINAL_BUS.DC_POSITIVE", Physical("KA1", "9"));
            Wire(Physical("KA1", "5"), "HL1.L");
            Wire("HL1.N", "TERMINAL_BUS.DC_NEGATIVE");
            controller.PanelPower.StartForAssessment();
            controller.SetMode(SimulationMode.Simulate);
            controller.SelectRelay(view);
            yield return null; yield return null;
            Assert.That(view.Runtime.IsActive, Is.True);
            Assert.That(controller.Graph.Devices["HL1"].IsActive, Is.True);
            Assert.That(controller.RelayProperties.DisplayedText, Does.Contain("已吸合").And.Contain("24 V").And.Contain("9–5 常开：闭合"));
            Capture("relay-properties-top.png", 1);
            Capture("relay-properties-bottom.png", 0);
            Assert.That(controller.SaveCc3dToPath(path), Is.EqualTo(WiringFileResult.Success), controller.LastFileError);
            Assert.That(controller.OpenCc3dFromPath(path), Is.EqualTo(WiringFileResult.Success), controller.LastFileError);
            Assert.That(controller.Graph.Wires.Any(w => w.EndPort == Physical("KA1", "13")), Is.True);
            controller.PanelPower.StartForAssessment(); controller.SetMode(SimulationMode.Simulate);
            controller.SelectRelay(view);
            yield return null; yield return null;
            Assert.That(view.Runtime.IsActive, Is.True);
            Assert.That(controller.Graph.Devices["HL1"].IsActive, Is.True);
            var supply = controller.Graph.Wires.Single(w => w.EndPort == Physical("KA1", "13"));
            controller.Graph.RemoveWire(supply.Id);
            yield return null; yield return null;
            Assert.That(view.Runtime.IsActive, Is.False);
            Assert.That(controller.Graph.Devices["HL1"].IsActive, Is.False);
            Assert.That(controller.RelayProperties.DisplayedText, Does.Contain("已释放").And.Contain("9–1 常闭：闭合"));
        }

        [UnityTest]
        public IEnumerator RelayPropertiesAreReadOnlyAndMutuallyExclusiveWithOtherSelections()
        {
            var view = controller.RelayViews[0];
            controller.SelectRelay(view);
            Assert.That(controller.RelayProperties.GetComponentsInChildren<InputField>().Length, Is.Zero);
            Assert.That(view.Runtime.IsActive, Is.False);
            controller.SelectPlc(controller.PlcViews[0]);
            Assert.That(controller.SelectedRelay, Is.Null);
            Assert.That(controller.RelayProperties.gameObject.activeSelf, Is.False);
            controller.SelectRelay(view);
            Assert.That(controller.SelectedPlc, Is.Null);
            controller.SelectPanelDevice(controller.PanelControls.First());
            Assert.That(controller.SelectedRelay, Is.Null);
            controller.SelectRelay(view);
            Assert.That(controller.SelectedPanelDevice, Is.Null);
            Wire(Physical("KA1", "1"), Physical("KA2", "1"));
            var flags = BindingFlags.Instance | BindingFlags.NonPublic;
            typeof(SimulationController).GetMethod("RefreshWireViews", flags).Invoke(controller, null);
            var wireView = Object.FindObjectOfType<ElectricalWireView>();
            Assert.That(wireView, Is.Not.Null);
            controller.SelectRelay(view);
            typeof(SimulationController).GetMethod("SelectWire", flags).Invoke(controller, new object[] { wireView });
            Assert.That(controller.SelectedRelay, Is.Null);
            Assert.That(controller.SelectedWire, Is.Not.Null);
            controller.SelectRelay(view);
            Assert.That(controller.SelectedWire, Is.Null);
            controller.RelayProperties.GetComponentInChildren<Button>().onClick.Invoke();
            Assert.That(controller.SelectedRelay, Is.Null);
            controller.SelectRelay(view);
            controller.SetMode(SimulationMode.Wiring);
            Assert.That(controller.SelectedRelay, Is.Null);
            controller.SelectRelay(view);
            Assert.That(controller.SelectedRelay, Is.Null, "接线模式不能被本体属性截获");
            controller.SetMode(SimulationMode.Simulate);
            controller.SelectRelay(view); yield return null;
            Assert.That(view.Runtime.IsActive, Is.False, "点击本体不能手动吸合");
        }

        private sealed class OutputTransport : IPlcTransport
        {
            public volatile bool Output;
            public Task ConnectAsync(PlcConfiguration c, CancellationToken ct) => Task.CompletedTask;
            public Task<bool[]> ReadBitsAsync(IReadOnlyList<PlcBitAddress> addresses, CancellationToken ct)
                => Task.FromResult(addresses.Select(a => a.ToString() == "Q0.0" && Output).ToArray());
            public Task WriteBitAsync(PlcBitAddress a, bool value, CancellationToken ct) => Task.CompletedTask;
            public void Dispose() { }
        }
        [UnityTest]
        public IEnumerator SimulatedPlcOutputSwitchesWiredRelayAndDisconnectReleasesIt()
        {
            var transport = new OutputTransport();
            controller.PlcTransportFactory = () => transport;
            var config = controller.PlcSessions["PLC_1"].Configuration;
            config.Ip = "127.0.0.1"; config.PollIntervalMs = 20;
            controller.PlcSessions["PLC_1"].Configure(config);
            foreach (var terminal in new[] { "L+", "3L+" })
                Wire("TERMINAL_BUS.DC_POSITIVE", controller.ResolvePlcTerminal("PLC_1", terminal).QualifiedPort);
            foreach (var terminal in new[] { "M", "3M-" })
                Wire("TERMINAL_BUS.DC_NEGATIVE", controller.ResolvePlcTerminal("PLC_1", terminal).QualifiedPort);
            Wire(controller.ResolvePlcTerminal("PLC_1", "Q0.0").QualifiedPort, Physical("KA1", "13"));
            Wire("TERMINAL_BUS.DC_NEGATIVE", Physical("KA1", "14"));
            controller.PanelPower.StartForAssessment(); controller.SetMode(SimulationMode.Simulate);
            controller.ConnectPlc("PLC_1");
            var relay = controller.RelayViews[0].Runtime;
            foreach (var active in new[] { true, false, true })
            {
                transport.Output = active;
                var deadline = Time.realtimeSinceStartup + 4;
                while (relay.IsActive != active && Time.realtimeSinceStartup < deadline) yield return null;
                Assert.That(relay.IsActive, Is.EqualTo(active));
            }
            var disconnect = controller.PlcSessions["PLC_1"].DisconnectAsync();
            while (!disconnect.IsCompleted) yield return null;
            yield return null; yield return null;
            Assert.That(relay.IsActive, Is.False);
        }

        private void Capture(string name, float position)
            => CapturePanel(controller.RelayProperties, name, position);

        internal static void CapturePanel(Component panel, string name, float position)
        {
            var camera = Camera.main;
            var canvas = panel.GetComponentInParent<Canvas>();
            var previousMode = canvas.renderMode; var previousCamera = canvas.worldCamera; var previousDistance = canvas.planeDistance;
            var previousTarget = camera.targetTexture; var previousActive = RenderTexture.active;
            var target = new RenderTexture(1920, 1080, 24); var texture = new Texture2D(1920, 1080, TextureFormat.RGB24, false);
            try
            {
                camera.targetTexture = target;
                canvas.renderMode = RenderMode.ScreenSpaceCamera; canvas.worldCamera = camera; canvas.planeDistance = camera.nearClipPlane + 0.05f;
                Canvas.ForceUpdateCanvases();
                var scroll = panel.GetComponentInChildren<ScrollRect>();
                Assert.That(scroll.content.rect.height, Is.GreaterThan(scroll.viewport.rect.height));
                scroll.StopMovement(); scroll.verticalNormalizedPosition = position; Canvas.ForceUpdateCanvases();
                if (position == 0)
                {
                    var corners = new Vector3[4]; scroll.content.GetWorldCorners(corners);
                    var bottom = scroll.viewport.InverseTransformPoint(corners[0]).y;
                    Assert.That(bottom, Is.EqualTo(scroll.viewport.rect.yMin).Within(2f), "滚动到底必须能看到最后一个端子");
                }
                camera.Render(); RenderTexture.active = target;
                texture.ReadPixels(new Rect(0, 0, 1920, 1080), 0, 0); texture.Apply();
                Directory.CreateDirectory("Build/Reports"); File.WriteAllBytes("Build/Reports/" + name, texture.EncodeToPNG());
            }
            finally
            {
                camera.targetTexture = previousTarget; RenderTexture.active = previousActive;
                canvas.renderMode = previousMode; canvas.worldCamera = previousCamera; canvas.planeDistance = previousDistance;
                Object.Destroy(texture); Object.Destroy(target);
            }
        }
    }
}
