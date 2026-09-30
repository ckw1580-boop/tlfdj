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
    public sealed class VoltageProbeSceneTests
    {
        private SimulationController controller;
        private VoltageProbeController probe;
        private Camera camera;
        private GameObject fixture;
        [UnitySetUp]
        public IEnumerator SetUp()
        {
            SceneManager.LoadScene("ElectricalTraining"); yield return null; yield return null;
            controller = Object.FindObjectOfType<SimulationController>(); probe = controller.VoltageProbe; camera = Camera.main;
            Assert.That(probe, Is.Not.Null);
            Assert.That(probe.View.gameObject.activeSelf, Is.False);
        }
        [UnityTearDown]
        public IEnumerator TearDown() { if (fixture != null) Object.Destroy(fixture); yield return null; }
        private void Isolate()
        {
            controller.enabled = false; camera.GetComponent<TrainingCameraController>().enabled = false;
            controller.SelectInstrument(InstrumentKind.VoltageProbe);
            camera.transform.SetPositionAndRotation(new Vector3(0, 10, -0.8f), Quaternion.identity);
            camera.fieldOfView = 45; camera.nearClipPlane = 0.01f;
            fixture = new GameObject("Voltage probe test contacts");
            controller.Graph.RegisterDevice(new ElectricalDeviceRuntime("PROBE_TEST", ElectricalDeviceKind.Terminal, new[] { "A", "B" }));
            controller.PanelPower.StartForAssessment();
            probe.Refresh(controller.Graph.Solve(0));
        }
        private ElectricalPortView Port(string name, Vector3 point)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Sphere); go.transform.SetParent(fixture.transform);
            go.transform.position = point; go.transform.localScale = Vector3.one * 0.024f;
            var port = go.AddComponent<ElectricalPortView>(); port.Initialize("PROBE_TEST", name, Color.green);
            port.SetVisibleForMode(SimulationMode.Fault); return port;
        }
        private void Attach(ElectricalPortView port)
        {
            probe.PickUp(); Physics.SyncTransforms();
            Assert.That(probe.HandlePointer(camera, camera.WorldToScreenPoint(port.CurrentAnchorPosition), true, false), Is.True);
            Assert.That(probe.ContactPort, Is.EqualTo(port.QualifiedPort));
        }
        [UnityTest]
        public IEnumerator PhysicalButtonHudAndReadoutStayInSyncWithoutChangingWiring()
        {
            Isolate();
            var a = Port("A", new Vector3(0, 10, 0));
            controller.Graph.AddWire("TERMINAL_BUS.DC_POSITIVE", a.QualifiedPort, Color.red);
            var count = controller.Graph.Wires.Count; var dirty = controller.HasUnsavedWiring;
            Attach(a); probe.Refresh(controller.Graph.Solve(0));
            Assert.That(probe.Reading.State, Is.EqualTo(VoltageProbeState.ModeMismatch));
            var button = probe.View.GetComponentsInChildren<VoltageProbeInteractable>().Single(c => c.Action == VoltageProbeAction.ToggleMode);
            Physics.SyncTransforms();
            probe.HandlePointer(camera, camera.WorldToScreenPoint(button.transform.position), true, false);
            Assert.That(probe.MeasurementMode, Is.EqualTo(VoltageProbeMode.DC));
            Assert.That(probe.ContactPort, Is.EqualTo(a.QualifiedPort));
            Assert.That(probe.View.DisplayText, Is.EqualTo("24.0 V"));
            var hud = Object.FindObjectOfType<VoltageProbeHudPresenter>(); hud.Refresh();
            Assert.That(hud.DisplayText, Does.Contain("24.0 V").And.Contain("24V−"));
            var ac = hud.Panel.GetComponentsInChildren<Button>().Single(b => b.name == "VoltageProbeAC");
            ac.onClick.Invoke(); hud.Refresh();
            Assert.That(probe.View.ModeText, Does.StartWith("AC")); Assert.That(ac.interactable, Is.False);
            var picker = probe.View.GetComponentsInChildren<VoltageProbeInteractable>().Single(c => c.Action == VoltageProbeAction.PickUp);
            probe.HandlePointer(camera, camera.WorldToScreenPoint(picker.transform.TransformPoint(new Vector3(0, 0.025f, -0.02f))), true, false);
            Assert.That(probe.ContactPort, Is.Null); Assert.That(probe.IsBusy, Is.True);
            Attach(a); hud.Panel.GetComponentsInChildren<Button>().Single(b => b.name == "PickUpVoltageProbe").onClick.Invoke();
            Assert.That(probe.ContactPort, Is.Null);
            Assert.That(controller.Graph.Wires.Count, Is.EqualTo(count)); Assert.That(controller.HasUnsavedWiring, Is.EqualTo(dirty));
            yield return null;
        }
        [UnityTest]
        public IEnumerator AnchorsRemainPhysicalAndVoltageTracksSupplyChanges()
        {
            Isolate(); var a = Port("A", new Vector3(-0.1f, 10, 0));
            var front = new GameObject("Front").transform; front.SetParent(fixture.transform); front.position = a.transform.position;
            var back = new GameObject("Back").transform; back.SetParent(fixture.transform); back.position = front.position + Vector3.forward * 0.3f;
            a.ConfigureOriginalAnchors(front, front, back, back); a.ApplyOriginalAnchor(TrainingViewPreset.WiringFront, false);
            controller.Graph.AddWire("POWER.L1", a.QualifiedPort, Color.red);
            Attach(a); probe.Refresh(controller.Graph.Solve(0));
            Assert.That(probe.View.DisplayText, Is.EqualTo("220.0 V"));
            Assert.That(controller.CanOperateFaultControls, Is.True);
            var point = probe.View.Tip.position;
            a.ApplyOriginalAnchor(TrainingViewPreset.FaultBack, false); camera.transform.position += Vector3.up * 0.1f;
            probe.Refresh(controller.Graph.Solve(0));
            Assert.That(Vector3.Distance(probe.View.Tip.position, point), Is.LessThan(0.0001f));
            front.position += Vector3.right * 0.02f; probe.Refresh(controller.Graph.Solve(0));
            Assert.That(Vector3.Distance(probe.View.Tip.position, front.position), Is.LessThan(0.0001f));
            controller.PanelPower.Reset(); probe.Refresh(controller.Graph.Solve(0));
            Assert.That(probe.Reading.State, Is.EqualTo(VoltageProbeState.UndefinedReference));
            Object.DestroyImmediate(front.gameObject); probe.Refresh(controller.Graph.Solve(0));
            Assert.That(probe.ContactPort, Is.Null); Assert.That(controller.CanOperateFaultControls, Is.False);
            yield return null;
        }
        [UnityTest]
        public IEnumerator UiAndGeometryBlockPickingAndToolLifecycleCleansUp()
        {
            Isolate(); var a = Port("A", new Vector3(0, 10, 0)); var pointer = camera.WorldToScreenPoint(a.transform.position);
            probe.HandlePointer(camera, pointer, true, true); Assert.That(probe.ContactPort, Is.Null);
            Assert.That(probe.View.gameObject.activeSelf, Is.False);
            var blocker = GameObject.CreatePrimitive(PrimitiveType.Cube); blocker.transform.SetParent(fixture.transform);
            blocker.transform.position = new Vector3(0, 10, -0.2f); blocker.transform.localScale = Vector3.one * 0.1f;
            Physics.SyncTransforms(); probe.HandlePointer(camera, pointer, true, false); Assert.That(probe.ContactPort, Is.Null);
            Object.DestroyImmediate(blocker); Attach(a);
            controller.HomePage.Open();
            var hud = Object.FindObjectOfType<VoltageProbeHudPresenter>(); hud.Refresh(); Assert.That(hud.Panel.activeSelf, Is.False);
            controller.HomePage.Close(); yield return null; yield return null;
            hud.Refresh(); Assert.That(hud.Panel.activeSelf, Is.True);
            controller.SelectInstrument(InstrumentKind.Multimeter);
            Assert.That(probe.IsSelected, Is.False); Assert.That(probe.ContactPort, Is.Null); Assert.That(probe.View.gameObject.activeSelf, Is.False);
            controller.SelectInstrument(InstrumentKind.VoltageProbe); Assert.That(controller.Multimeter.IsSelected, Is.False);
            probe.SetMode(VoltageProbeMode.DC); controller.SetMode(SimulationMode.View); Assert.That(probe.IsSelected, Is.False);
            controller.SelectInstrument(InstrumentKind.VoltageProbe); Assert.That(probe.MeasurementMode, Is.EqualTo(VoltageProbeMode.AC));
            controller.SelectInstrument(InstrumentKind.Tachometer); Assert.That(probe.IsSelected, Is.False);
            controller.SelectInstrument(InstrumentKind.VoltageProbe); controller.ResetTraining();
            Assert.That(probe.IsSelected, Is.False); hud.Refresh(); Assert.That(hud.Panel.activeSelf, Is.False);
        }
        [UnityTest]
        public IEnumerator BothTerminalStylesRemainAvailableAndRemovedPortsReleaseContact()
        {
            controller.SetWireStyle(Color.red, 0.01f, "Wire"); controller.SelectInstrument(InstrumentKind.VoltageProbe);
            var ports = Object.FindObjectsOfType<ElectricalPortView>();
            Assert.That(ports.Any(p => p.JumperOnly && p.IsVisible), Is.True);
            Assert.That(ports.Any(p => p.ElectricalOnly && p.IsVisible), Is.True);
            controller.SetWireStyle(Color.red, 0.01f, "JumperLine");
            Assert.That(ports.Any(p => p.ElectricalOnly && p.IsVisible), Is.True);
            Isolate(); var a = Port("A", new Vector3(0, 10, 0)); Attach(a);
            controller.Graph.RegisterDevice(new ElectricalDeviceRuntime("PROBE_TEST", ElectricalDeviceKind.Terminal, new[] { "B" }));
            probe.Refresh(controller.Graph.Solve(0)); Assert.That(probe.ContactPort, Is.Null);
            yield return null;
        }
        [UnityTest]
        public IEnumerator SupplyButtonsUpdateAttachedProbeAndRejectOperationWhileHeld()
        {
            Isolate(); controller.PanelPower.Reset();
            var a = Port("A", new Vector3(0, 10, 0));
            controller.Graph.AddWire("POWER.L1", a.QualifiedPort, Color.red);
            var key = controller.PanelControls.First(v => v.Definition.Id == "PANEL_KEY");
            var start = controller.PanelControls.First(v => v.Definition.Id == "PANEL_START");
            var stop = controller.PanelControls.First(v => v.Definition.Id == "PANEL_STOP");
            controller.PressPanelDevice(key); Assert.That(key.Runtime.IsPressed, Is.False);
            Attach(a);
            controller.PressPanelDevice(key); controller.PressPanelDevice(start); controller.ReleasePanelButton();
            probe.Refresh(controller.Graph.Solve(0));
            Assert.That(controller.PanelPower.Enabled, Is.True);
            Assert.That(probe.View.DisplayText, Is.EqualTo("220.0 V"));
            controller.PressPanelDevice(stop); controller.ReleasePanelButton(); probe.Refresh(controller.Graph.Solve(0));
            Assert.That(controller.PanelPower.Enabled, Is.False);
            Assert.That(probe.Reading.State, Is.EqualTo(VoltageProbeState.UndefinedReference));
            Assert.That(probe.ContactPort, Is.EqualTo(a.QualifiedPort));
            var breaker = controller.CabinetBreakers[0];
            Assert.That(controller.TryToggleCabinetBreaker(breaker), Is.True);
            probe.PickUp(); Assert.That(controller.TryToggleCabinetBreaker(breaker), Is.False);
            yield return null;
        }
        [UnityTest]
        public IEnumerator CaptureVoltageProbeEvidence()
        {
            if (SystemInfo.graphicsDeviceType == UnityEngine.Rendering.GraphicsDeviceType.Null) Assert.Ignore("Visual evidence requires graphics.");
            controller.enabled = false; camera.GetComponent<TrainingCameraController>().enabled = false;
            camera.GetComponent<TrainingCameraController>().SetFaultView(); controller.SelectInstrument(InstrumentKind.VoltageProbe);
            controller.PanelPower.StartForAssessment(); var snapshot = controller.Graph.Solve(0);
            var ports = Object.FindObjectsOfType<ElectricalPortView>().Where(p => p.IsVisible).ToArray();
            var ac = ports.First(p => snapshot.GetPotential(p.QualifiedPort) == ElectricalPotential.PhaseL1);
            Assert.That(probe.TryAttach(ac, camera), Is.True); probe.Refresh(snapshot); yield return null;
            Assert.That(probe.View.DisplayText, Is.EqualTo("220.0 V")); Capture("voltage-probe-ac-1920", 1920, 1080, true);
            probe.PickUp(); var dc = ports.First(p => snapshot.GetPotential(p.QualifiedPort) == ElectricalPotential.DcPositive24);
            Assert.That(probe.TryAttach(dc, camera), Is.True); probe.SetMode(VoltageProbeMode.DC); probe.Refresh(snapshot);
            Assert.That(probe.View.DisplayText, Is.EqualTo("24.0 V")); Capture("voltage-probe-dc-1280", 1280, 720, true);
            // Isolated close-up shows the actual prefab and LCD at a useful inspection scale.
            foreach (var t in probe.View.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = 30;
            camera.cullingMask = 1 << 30; probe.View.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            camera.transform.position = new Vector3(0.035f, 0.025f, -0.3f); camera.transform.LookAt(new Vector3(0, 0.03f, 0));
            camera.fieldOfView = 40; camera.nearClipPlane = 0.01f;
            camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = new Color(0.055f, 0.078f, 0.1f);
            Capture("voltage-probe-model", 1000, 1200, false);
        }
        private void Capture(string name, int width, int height, bool includeHud)
        {
            var previous = RenderTexture.active; var oldTarget = camera.targetTexture;
            var target = new RenderTexture(width, height, 24); var texture = new Texture2D(width, height, TextureFormat.RGB24, false);
            var hud = Object.FindObjectOfType<VoltageProbeHudPresenter>(); var canvas = hud.Panel.GetComponentInParent<Canvas>();
            var oldMode = canvas.renderMode; var oldCamera = canvas.worldCamera; var oldDistance = canvas.planeDistance;
            try
            {
                camera.targetTexture = target;
                if (includeHud)
                {
                    hud.Refresh(); canvas.renderMode = RenderMode.ScreenSpaceCamera; canvas.worldCamera = camera; canvas.planeDistance = 0.1f;
                    Canvas.ForceUpdateCanvases();
                    var text = hud.Panel.GetComponentsInChildren<Text>().Single(t => t.name == "VoltageProbeProperties");
                    Assert.That(text.cachedTextGenerator.characterCountVisible, Is.GreaterThanOrEqualTo(text.text.Length - 1), "All probe properties must fit.");
                    var corners = new Vector3[4]; hud.Panel.GetComponent<RectTransform>().GetWorldCorners(corners);
                    Assert.That(corners.Select(camera.WorldToViewportPoint).All(p => p.x >= 0 && p.x <= 1 && p.y >= 0 && p.y <= 1), Is.True);
                }
                camera.Render(); RenderTexture.active = target;
                texture.ReadPixels(new Rect(0, 0, width, height), 0, 0); texture.Apply();
                Directory.CreateDirectory(Path.Combine(Application.dataPath, "../Build/Reports"));
                File.WriteAllBytes(Path.Combine(Application.dataPath, "../Build/Reports/" + name + ".png"), texture.EncodeToPNG());
            }
            finally
            {
                camera.targetTexture = oldTarget; RenderTexture.active = previous;
                canvas.renderMode = oldMode; canvas.worldCamera = oldCamera; canvas.planeDistance = oldDistance;
                Canvas.ForceUpdateCanvases(); Object.Destroy(texture); Object.Destroy(target);
            }
        }
    }
}
