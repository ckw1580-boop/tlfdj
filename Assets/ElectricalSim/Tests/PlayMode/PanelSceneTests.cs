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
        private PanelDeviceView Control(string id) => controller.PanelControls.Single(v => v.Runtime.DeviceId == id && !v.IsRear);
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
            Assert.That(controller.PanelControls.Count, Is.EqualTo(23));
            FramePanel();
            Physics.SyncTransforms();
            foreach (var view in controller.PanelControls.Where(v => !v.IsRear))
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
        public IEnumerator RearButtonsShareFrontAnimationPropertiesAndPhysicalContacts()
        {
            var rears = controller.PanelControls.Where(v => v.IsRear).OrderBy(v => v.Definition.Id).ToArray();
            Assert.That(rears.Length, Is.EqualTo(3));
            var board = (ElectricalDeviceRuntime)controller.Graph.Devices["DuanZiPai_0"];
            var ports = Object.FindObjectsOfType<ElectricalPortView>();
            var terminalOrder = new[] { "NO1", "COM1", "NC2", "COM2" };
            for (var index = 0; index < rears.Length; index++)
            {
                var rear = rears[index];
                var id = "SB" + (index + 1);
                var front = Control(id);
                Assert.That(rear.transform.parent.name, Is.EqualTo((108 + index).ToString()));
                Assert.That(rear.Runtime, Is.SameAs(front.Runtime));
                Assert.That(rear.Definition, Is.SameAs(front.Definition));
                Assert.That(rear.MovingParts.Single(), Is.SameAs(rear.transform.Find("mesh/Box")));
                var rest = rear.MovingParts[0].position;
                var fixedParts = rear.GetComponentsInChildren<Transform>(true)
                    .Where(t => !t.IsChildOf(rear.MovingParts[0])).ToArray();
                var fixedPositions = fixedParts.Select(t => t.position).ToArray();
                var fixedRotations = fixedParts.Select(t => t.rotation).ToArray();
                var physical = terminalOrder.Select(port => "DuanZiPai_0." + board.FixedLinks.Single(l => l.B == id + "." + port).A).ToArray();
                var anchors = physical.Select(endpoint => ports.Single(p => p.QualifiedPort == endpoint)
                    .GetOriginalAnchor(TrainingViewPreset.FaultBack, false)).ToArray();
                var anchorPositions = anchors.Select(t => t.position).ToArray();
                for (var contact = 0; contact < 4; contact++)
                {
                    Assert.That(anchors[contact].parent.parent.name, Is.EqualTo("DuanZiPai_5"));
                    Assert.That(anchors[contact].name, Is.EqualTo("a" + (index * 4 + contact + 1)));
                }

                controller.SetMode(SimulationMode.View);
                Physics.SyncTransforms();
                var center = rear.Picker.bounds.center;
                Assert.That(Physics.Raycast(center + rear.transform.forward * 0.15f, -rear.transform.forward, out var hit, 0.2f), Is.True);
                Assert.That(hit.collider.GetComponentInParent<PanelDeviceView>(), Is.SameAs(rear), id + " 背面点击");
                controller.PressPanelDevice(rear);
                Assert.That(rear.Runtime.IsPressed, Is.False, "视角模式只读");
                yield return null;
                var text = Object.FindObjectOfType<PanelPropertiesPresenter>().DisplayedText;
                Assert.That(text, Does.Contain(id).And.Contain("柜体背面").And.Contain("释放"));
                for (var contact = 0; contact < 4; contact++)
                    Assert.That(text, Does.Contain(terminalOrder[contact] + " → DuanZiPai_5.a" + (index * 4 + contact + 1)));

                controller.SetMode(SimulationMode.Simulate);
                string Endpoint(string node) => "DuanZiPai_0." + board.FixedLinks.First(l => l.B == node).A;
                controller.Graph.AddWire(Endpoint("TERMINAL_BUS.DC_POSITIVE"), physical[1], Color.red);
                controller.Graph.AddWire(physical[0], Endpoint("HL" + (index + 1) + ".L"), Color.red);
                controller.Graph.AddWire(Endpoint("HL" + (index + 1) + ".N"), Endpoint("TERMINAL_BUS.DC_NEGATIVE"), Color.blue);
                controller.PanelPower.StartForAssessment();
                foreach (var source in new[] { rear, front })
                {
                    controller.PressPanelDevice(source);
                    rear.AdvanceAnimation(0.05f);
                    front.AdvanceAnimation(0.05f);
                    Assert.That(Vector3.Distance(rest, rear.MovingParts[0].position), Is.EqualTo(0.0015f).Within(0.00001f));
                    rear.AdvanceAnimation(0.05f);
                    front.AdvanceAnimation(0.05f);
                    Assert.That(rear.AnimationAmount, Is.EqualTo(front.AnimationAmount));
                    Assert.That(Vector3.Dot(rear.MovingParts[0].position - rest, rear.transform.forward), Is.EqualTo(-0.003f).Within(0.00001f));
                    var snapshot = controller.Graph.Solve();
                    Assert.That(snapshot.SameNet(physical[0], physical[1]), Is.True, id + " 常开闭合");
                    Assert.That(snapshot.SameNet(physical[2], physical[3]), Is.False, id + " 常闭断开");
                    Assert.That(snapshot.SameNet(physical[1], physical[3]), Is.False, "两组公共端独立");
                    Assert.That(Control("HL" + (index + 1)).Runtime.IsActive, Is.True);
                    for (var part = 0; part < fixedParts.Length; part++)
                    {
                        Assert.That(fixedParts[part].position, Is.EqualTo(fixedPositions[part]));
                        Assert.That(Quaternion.Angle(fixedParts[part].rotation, fixedRotations[part]), Is.LessThan(0.001f));
                    }
                    for (var contact = 0; contact < 4; contact++)
                        Assert.That(anchors[contact].position, Is.EqualTo(anchorPositions[contact]));
                    if (source == rear)
                    {
                        var target = (rears[0].Picker.bounds.center + rears[2].Picker.bounds.center) * 0.5f;
                        Camera.main.transform.position = target + rear.transform.forward * 0.38f;
                        Camera.main.transform.LookAt(target, rear.transform.up);
                        Camera.main.fieldOfView = 35f;
                        Capture("rear-button-" + id + "-pressed");
                    }
                    controller.ReleasePanelButton();
                    rear.AdvanceAnimation(0.1f);
                    front.AdvanceAnimation(0.1f);
                    snapshot = controller.Graph.Solve();
                    Assert.That(snapshot.SameNet(physical[0], physical[1]), Is.False);
                    Assert.That(snapshot.SameNet(physical[2], physical[3]), Is.True);
                    Assert.That(Control("HL" + (index + 1)).Runtime.IsActive, Is.False);
                    Assert.That(Vector3.Distance(rest, rear.MovingParts[0].position), Is.LessThan(0.000001f));
                }
            }
            controller.SetMode(SimulationMode.View);
            controller.SelectPanelDevice(rears[0]);
            yield return null;
            Capture("rear-button-properties", true);
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
            Object.FindObjectOfType<TrainingCameraController>().SetFaultView();
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
