using System.Collections;
using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UI;

namespace ElectricalSim.Tests
{
    public sealed class PanelSceneTests
    {
        private SimulationController controller;
        private PanelDeviceView Control(string id) => controller.PanelControls.Single(v => v.Runtime.DeviceId == id);
        [UnitySetUp]
        public IEnumerator SetUp()
        {
            SceneManager.LoadScene("ElectricalTraining");
            yield return null;
            yield return null;
            controller = Object.FindObjectOfType<SimulationController>();
            Assert.That(controller, Is.Not.Null);
        }

        [UnityTest]
        public IEnumerator AnimationMovesOnlyActuatorWhileMountsStayFixed()
        {
            var camera = Camera.main;
            camera.nearClipPlane = 0.001f;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.16f, 0.18f, 0.2f);
            camera.cullingMask = 1 << 30;
            camera.fieldOfView = 35f;
            var ids = Enumerable.Range(1, 8).Select(i => "SB" + i)
                .Concat(new[] { "PANEL_START", "PANEL_STOP", "PANEL_KEY", "PANEL_ESTOP" });
            foreach (var id in ids)
            {
                var view = Control(id);
                var key = id == "PANEL_KEY";
                // Independently identify the actual cap/key in the original asset, not the configured animation target.
                var actuator = view.transform.Find(key ? "mesh/box/YaoShi01" : "mesh/Box");
                Assert.That(actuator, Is.Not.Null, id);
                Assert.That(view.MovingParts, Is.EquivalentTo(new[] { actuator }), id);
                var all = view.GetComponentsInChildren<Transform>(true);
                var mounts = all.Where(t => t != actuator && !t.IsChildOf(actuator)).ToArray();
                var positions = mounts.Select(t => t.position).ToArray();
                var rotations = mounts.Select(t => t.rotation).ToArray();
                var layers = all.Select(t => t.gameObject.layer).ToArray();
                var rest = actuator.position;
                var rotation = actuator.rotation;
                foreach (var t in all) t.gameObject.layer = 30;
                camera.transform.position = view.transform.position + view.transform.forward * 0.13f + view.transform.right * 0.045f + view.transform.up * 0.025f;
                camera.transform.LookAt(view.transform.position, view.transform.up);
                var capture = id == "SB1" || key || id == "PANEL_ESTOP";
                if (capture) Capture("panel-actuator-" + id + "-rest");
                view.Runtime.SetControl(true);
                view.AdvanceAnimation(0.2f);
                if (key)
                    Assert.That(Quaternion.Angle(rotation, actuator.rotation), Is.EqualTo(90f).Within(0.1f));
                else
                {
                    Assert.That(Vector3.Distance(rest, actuator.position), Is.EqualTo(0.003f).Within(0.00001f), id);
                    Assert.That(Vector3.Dot(actuator.position - rest, view.transform.forward), Is.LessThan(0), id);
                }
                for (var i = 0; i < mounts.Length; i++)
                {
                    Assert.That(Vector3.Distance(positions[i], mounts[i].position), Is.LessThan(0.000001f), id + " 固定位置 " + mounts[i].name);
                    Assert.That(Quaternion.Angle(rotations[i], mounts[i].rotation), Is.LessThan(0.001f), id + " 固定旋转 " + mounts[i].name);
                }
                if (capture) Capture("panel-actuator-" + id + "-operated");
                view.Runtime.SetControl(false);
                view.AdvanceAnimation(0.2f);
                Assert.That(Vector3.Distance(rest, actuator.position), Is.LessThan(0.000001f), id + " 复位位置");
                Assert.That(Quaternion.Angle(rotation, actuator.rotation), Is.LessThan(0.001f), id + " 复位旋转");
                for (var i = 0; i < all.Length; i++) all[i].gameObject.layer = layers[i];
            }
            yield return null;
        }

        [UnityTest]
        public IEnumerator AllOriginalModelsAreBoundAndPropertiesAreReadOnly()
        {
            Assert.That(controller.PanelControls.Count, Is.EqualTo(20));
            FramePanel();
            Physics.SyncTransforms();
            foreach (var view in controller.PanelControls)
            {
                Assert.That(view.Picker.enabled && view.Picker.gameObject.activeInHierarchy, Is.True, view.name);
                Assert.That(view.MovingParts.All(p => p != null), Is.True);
                Assert.That(controller.Graph.Devices[view.Runtime.DeviceId], Is.SameAs(view.Runtime));
                var direction = view.Picker.bounds.center - Camera.main.transform.position;
                Assert.That(Physics.Raycast(Camera.main.transform.position, direction.normalized, out var hit, 100f), Is.True);
                Assert.That(hit.collider.GetComponentInParent<PanelDeviceView>(), Is.SameAs(view), "真实点击射线：" + view.Definition.Id + " hit " + hit.collider.name);
                controller.PressPanelDevice(view);
                Assert.That(controller.SelectedPanelDevice, Is.SameAs(view));
                Assert.That(view.Runtime.IsPressed, Is.False);
                yield return null;
                Assert.That(Object.FindObjectOfType<PanelPropertiesPresenter>().DisplayedText, Does.Contain(view.Definition.Label));
            }
            Assert.That(controller.PanelPower.Enabled, Is.False);
        }

        [UnityTest]
        public IEnumerator FocusAndFileDialogsReleaseButtonsAndPropertiesYieldToErrors()
        {
            controller.SetMode(SimulationMode.Simulate);
            var button = Control("SB1");
            controller.PressPanelDevice(button);
            controller.SendMessage("OnApplicationFocus", false);
            Assert.That(button.Runtime.IsPressed, Is.False);
            controller.PressPanelDevice(button);
            controller.FileDialogs = new CancelDialogs();
            controller.SaveCc3d();
            Assert.That(button.Runtime.IsPressed, Is.False);
            var presenter = Object.FindObjectOfType<PanelPropertiesPresenter>();
            controller.SelectPanelDevice(button);
            controller.ShowStatus("普通提示");
            Assert.That(presenter.transform.Find("PanelDeviceProperties").gameObject.activeSelf, Is.True);
            controller.ShowStatus("接线错误", true);
            Assert.That(presenter.transform.Find("PanelDeviceProperties").gameObject.activeSelf, Is.False);
            Assert.That(presenter.transform.Find("Status").gameObject.activeSelf, Is.True);
            controller.ShowStatus("故障已清除");
            Assert.That(presenter.transform.Find("PanelDeviceProperties").gameObject.activeSelf, Is.True);
            controller.SetMode(SimulationMode.View);
            controller.SelectPanelDevice(button);
            yield return new WaitForSecondsRealtime(0.3f);
            FramePanel();
            Capture("panel-properties", true);
        }

        private sealed class CancelDialogs : IWiringFileDialogs
        {
            public string ChooseOpen(string directory) => null;
            public string ChooseSave(string directory, string fileName) => null;
            public void ConfirmUnsaved(System.Action<UnsavedWiringChoice> completed) => completed(UnsavedWiringChoice.Cancel);
        }

        [UnityTest]
        public IEnumerator FrontAndFaultTerminalsAndLegacyAliasesShareState()
        {
            var board = (ElectricalDeviceRuntime)controller.Graph.Devices["DuanZiPai_0"];
            var com = board.FixedLinks.Single(l => l.B == "SB1.COM1").A;
            var no = board.FixedLinks.Single(l => l.B == "SB1.NO1").A;
            var ports = Object.FindObjectsOfType<ElectricalPortView>();
            var port = ports.Single(p => p.QualifiedPort == "DuanZiPai_0." + com);
            controller.SetMode(SimulationMode.Wiring);
            Object.FindObjectOfType<TrainingCameraController>().SetWiringView();
            yield return null;
            var front = port.CurrentAnchorPosition;
            controller.SetMode(SimulationMode.Fault);
            yield return null;
            Assert.That(Vector3.Distance(front, port.CurrentAnchorPosition), Is.GreaterThan(0.01f));
            Control("SB1").Runtime.SetControl(true);
            Assert.That(controller.Graph.Solve().SameNet("DuanZiPai_0." + com, "DuanZiPai_0." + no), Is.True);
            var legacy = (ElectricalDeviceRuntime)controller.Graph.Devices["SB0"];
            legacy.SetControl(true);
            Assert.That(Control("SB2").Runtime.IsPressed, Is.True);
            Assert.That(controller.Graph.Solve().SameNet("SB0.COM", "SB0.NC"), Is.False);
            var resolve = typeof(SimulationController).GetMethod("ResolveWireAnchor", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            foreach (var endpoint in new[] { "SB1.COM", "SB1.NO", "SB2.COM", "SB2.NO", "SB0.NC" })
                Assert.That(resolve.Invoke(controller, new object[] { endpoint, TrainingViewPreset.WiringFront, false }), Is.Not.Null, endpoint);
        }

        [UnityTest]
        public IEnumerator PhysicalTerminalCircuitAndAnimationStayInSync()
        {
            var board = (ElectricalDeviceRuntime)controller.Graph.Devices["DuanZiPai_0"];
            string Endpoint(string node) => "DuanZiPai_0." + board.FixedLinks.First(l => l.B == node).A;
            controller.Graph.AddWire(Endpoint("TERMINAL_BUS.DC_POSITIVE"), Endpoint("SB1.COM1"), Color.red);
            controller.Graph.AddWire(Endpoint("SB1.NO1"), Endpoint("HL1.L"), Color.red);
            controller.Graph.AddWire(Endpoint("HL1.N"), Endpoint("TERMINAL_BUS.DC_NEGATIVE"), Color.blue);
            controller.SetMode(SimulationMode.Simulate);
            controller.PanelPower.StartForAssessment();
            var button = Control("SB1");
            var lamp = Control("HL1");
            var rest = button.MovingParts[0].position;
            var anchor = Object.FindObjectsOfType<ElectricalPortView>().Single(p => p.QualifiedPort == Endpoint("SB1.COM1"));
            var originalAnchor = anchor.transform.position;
            controller.PressPanelDevice(button);
            controller.Graph.Solve();
            button.AdvanceAnimation(0.1f);
            lamp.AdvanceAnimation(0.1f);
            Assert.That(lamp.Runtime.IsActive, Is.True);
            Assert.That(Vector3.Distance(rest, button.MovingParts[0].position), Is.EqualTo(0.003f).Within(0.0001f));
            Assert.That(Vector3.Dot(button.MovingParts[0].position - rest, button.transform.forward), Is.LessThan(0));
            Assert.That(anchor.transform.position, Is.EqualTo(originalAnchor));
            FramePanel();
            Capture("panel-button-pressed-lamp-on");
            controller.ReleasePanelButton();
            controller.Graph.Solve();
            button.AdvanceAnimation(0.1f);
            lamp.AdvanceAnimation(0.1f);
            Assert.That(lamp.Runtime.IsActive, Is.False);
            Assert.That(Vector3.Distance(rest, button.MovingParts[0].position), Is.LessThan(0.0001f));
            Capture("panel-button-released-lamp-off");
            var selector = Control("SA1");
            var rotation = selector.MovingParts[0].rotation;
            controller.PressPanelDevice(selector);
            selector.AdvanceAnimation(0.15f);
            Assert.That(Quaternion.Angle(rotation, selector.MovingParts[0].rotation), Is.EqualTo(90f).Within(0.1f));
            Capture("panel-selector-operated");
            controller.PressPanelDevice(button);
            controller.SetMode(SimulationMode.View);
            Assert.That(button.Runtime.IsPressed, Is.False);
            Assert.That(selector.Runtime.IsPressed, Is.True);
            controller.ResetTraining();
            Assert.That(selector.Runtime.IsPressed, Is.False);
            Assert.That(controller.PanelPower.Enabled, Is.False);
            yield return null;
        }

        [UnityTest]
        public IEnumerator StopAndEmergencyRemoveBothSuppliesWithoutRestarting()
        {
            controller.SetMode(SimulationMode.Simulate);
            controller.PressPanelDevice(Control("PANEL_KEY"));
            controller.PressPanelDevice(Control("PANEL_START"));
            controller.ReleasePanelButton();
            Assert.That(controller.PanelPower.Enabled, Is.True);
            controller.PressPanelDevice(Control("PANEL_ESTOP"));
            Assert.That(controller.PanelPower.Enabled, Is.False);
            controller.PressPanelDevice(Control("PANEL_ESTOP"));
            Assert.That(controller.PanelPower.Enabled, Is.False);
            controller.PressPanelDevice(Control("PANEL_START"));
            controller.ReleasePanelButton();
            Assert.That(controller.PanelPower.Enabled, Is.True);
            controller.PressPanelDevice(Control("PANEL_STOP"));
            controller.ReleasePanelButton();
            Assert.That(controller.PanelPower.Enabled, Is.False);
            var s = controller.Graph.Solve();
            Assert.That(s.GetPotential("POWER.L1"), Is.EqualTo(ElectricalPotential.Floating));
            Assert.That(s.GetDcVoltage("TERMINAL_BUS.DC_POSITIVE", "TERMINAL_BUS.DC_NEGATIVE"), Is.Zero);
            yield return null;
        }

        private void FramePanel()
        {
            var camera = Camera.main;
            var first = Control("PANEL_KEY");
            var last = Control("SB8");
            var center = (first.transform.position + last.transform.position) * 0.5f;
            var normal = Control("SB1").transform.forward;
            camera.transform.position = center + normal * 0.7f;
            camera.transform.LookAt(center, Vector3.up);
            camera.fieldOfView = 30f;
        }
        private void Capture(string name, bool includeProperties = false)
        {
            var camera = Camera.main;
            var canvas = Object.FindObjectOfType<PanelPropertiesPresenter>().GetComponentInParent<Canvas>();
            var canvasMode = canvas.renderMode;
            var canvasCamera = canvas.worldCamera;
            var canvasDistance = canvas.planeDistance;
            var target = new RenderTexture(1920, 1080, 24);
            var previousTarget = camera.targetTexture;
            var previousActive = RenderTexture.active;
            var pixels = new Texture2D(1920, 1080, TextureFormat.RGB24, false);
            try
            {
                if (includeProperties)
                {
                    canvas.renderMode = RenderMode.ScreenSpaceCamera;
                    canvas.worldCamera = camera;
                    canvas.planeDistance = 0.1f;
                    Canvas.ForceUpdateCanvases();
                }
                camera.targetTexture = target;
                camera.Render();
                RenderTexture.active = target;
                pixels.ReadPixels(new Rect(0, 0, 1920, 1080), 0, 0);
                pixels.Apply();
                Directory.CreateDirectory("Build/Reports");
                File.WriteAllBytes("Build/Reports/" + name + ".png", pixels.EncodeToPNG());
            }
            finally
            {
                camera.targetTexture = previousTarget;
                RenderTexture.active = previousActive;
                canvas.renderMode = canvasMode;
                canvas.worldCamera = canvasCamera;
                canvas.planeDistance = canvasDistance;
                Object.Destroy(pixels);
                Object.Destroy(target);
            }
        }
    }
}
