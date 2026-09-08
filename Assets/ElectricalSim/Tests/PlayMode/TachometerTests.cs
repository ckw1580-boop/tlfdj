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
    public sealed class TachometerTests
    {
        private SimulationController controller;
        private TachometerController meter;
        [UnitySetUp]
        public IEnumerator SetUp()
        {
            SceneManager.LoadScene("ElectricalTraining");
            yield return null;
            yield return null;
            controller = Object.FindObjectOfType<SimulationController>();
            meter = controller.Tachometer;
            Assert.That(meter, Is.Not.Null);
        }

        [UnityTest]
        public IEnumerator FaultToolbarShowsShaftTargetsBeforeSelectingOrHoveringAnInstrument()
        {
            var faultButton = Object.FindObjectsOfType<Button>(true).Single(b => b.name == "btn_paigu");
            AssertTargetsVisible(false);
            faultButton.onClick.Invoke();
            yield return null;
            Assert.That(controller.Mode, Is.EqualTo(SimulationMode.Fault));
            Assert.That(meter.IsSelected, Is.False);
            Assert.That(meter.View.gameObject.activeSelf, Is.False);
            AssertTargetsVisible(true);
            Assert.That(meter.Targets.All(t => !t.GetComponent<Collider>().enabled), Is.True,
                "Visible shaft markers must not block other instruments before selecting the tachometer");

            if (SystemInfo.graphicsDeviceType != UnityEngine.Rendering.GraphicsDeviceType.Null)
            {
                Capture(Camera.main, "tachometer-fault-mode");
                var target = meter.Targets.Single(t => t.MotorId == "M1" && t.transform.parent.parent.name == "107");
                var camera = Camera.main;
                camera.transform.position = target.transform.position + target.Outward * 0.5f + Vector3.up * 0.16f;
                camera.transform.LookAt(target.transform.position);
                Capture(camera, "tachometer-fault-target-visible");
            }

            controller.SelectInstrument(InstrumentKind.Tachometer);
            var shaft = meter.Targets[0];
            shaft.Highlight(true);
            shaft.Highlight(false);
            meter.HandlePointer(Camera.main, Vector2.zero, false, true);
            AssertTargetsVisible(true);
            meter.TryAttach(shaft);
            AssertTargetsVisible(true);
            meter.PickUp();
            AssertTargetsVisible(true);
            controller.SelectInstrument(InstrumentKind.Multimeter);
            AssertTargetsVisible(true);
            Assert.That(meter.Targets.All(t => !t.GetComponent<Collider>().enabled), Is.True);

            foreach (var mode in new[] { SimulationMode.View, SimulationMode.Drag, SimulationMode.Wiring, SimulationMode.Simulate })
            {
                controller.SetMode(mode);
                AssertTargetsVisible(false);
                controller.SetMode(SimulationMode.Fault);
                AssertTargetsVisible(true);
            }
            faultButton.onClick.Invoke();
            Assert.That(controller.Mode, Is.EqualTo(SimulationMode.View));
            AssertTargetsVisible(false);
        }

        private void AssertTargetsVisible(bool visible)
        {
            foreach (var target in meter.Targets)
            {
                Assert.That(target.gameObject.activeInHierarchy, Is.True);
                Assert.That(target.GetComponent<Renderer>().enabled, Is.EqualTo(visible),
                    target.MotorId + " / " + target.transform.parent.parent.name);
                if (!visible) Assert.That(target.GetComponent<Collider>().enabled, Is.False);
            }
        }

        [UnityTest]
        public IEnumerator ModelAndTargetsSupportAttachPickupAndModeCleanup()
        {
            Assert.That(meter.Targets.Count, Is.EqualTo(4));
            Assert.That(meter.View.GetComponentsInChildren<MeshRenderer>(true).Length, Is.EqualTo(3));
            controller.SelectInstrument(InstrumentKind.Tachometer);
            Assert.That(meter.View.DisplayText, Is.EqualTo("—"));
            foreach (var target in meter.Targets)
            {
                Assert.That(target.GetComponent<ElectricalPortView>(), Is.Null);
                Physics.SyncTransforms();
                var ray = new Ray(target.transform.position + target.Outward * 0.10f, -target.Outward);
                Assert.That(Physics.Raycast(ray, out var targetHit, 0.2f), Is.True);
                Assert.That(targetHit.collider.GetComponent<MotorSpeedTarget>(), Is.EqualTo(target), "The shaft must be reachable by the actual picking ray");
                Assert.That(meter.TryAttach(target), Is.True);
                meter.Refresh(controller.Graph.Solve(0));
                Assert.That(meter.View.DisplayText, Is.EqualTo("0"));
                Assert.That(Vector3.Distance(meter.View.ProbeTip.position, target.transform.position), Is.LessThan(0.0001f));
                var before = meter.View.transform.position;
                Camera.main.transform.position += Vector3.up * 0.1f;
                meter.Refresh(controller.Graph.Solve(0));
                Assert.That(Vector3.Distance(before, meter.View.transform.position), Is.LessThan(0.0001f));
                meter.PickUp();
                Assert.That(meter.AttachedTarget, Is.Null);
            }
            controller.SelectInstrument(InstrumentKind.Multimeter);
            Assert.That(meter.IsSelected, Is.False);
            Assert.That(meter.View.gameObject.activeSelf, Is.False);
            controller.SelectInstrument(InstrumentKind.Tachometer);
            controller.SetMode(SimulationMode.View);
            Assert.That(meter.Targets.All(t => !t.GetComponent<Collider>().enabled), Is.True);
            yield return null;
        }

        [UnityTest]
        public IEnumerator PointerPickingRespectsUiOcclusionAndCanRetrieveTheMeter()
        {
            controller.SelectInstrument(InstrumentKind.Tachometer);
            controller.enabled = false;
            var camera = Camera.main;
            camera.GetComponent<TrainingCameraController>().enabled = false;
            var target = meter.Targets.Single(t => t.MotorId == "M1" && t.transform.parent.parent.name == "107");
            camera.transform.position = target.transform.position + target.Outward * 0.5f;
            camera.transform.LookAt(target.transform.position);
            camera.nearClipPlane = 0.01f;
            var pointer = (Vector2)camera.WorldToScreenPoint(target.transform.position);
            meter.HandlePointer(camera, pointer, true, true);
            Assert.That(meter.AttachedTarget, Is.Null, "UI clicks must not place a meter behind the UI");
            var obstruction = GameObject.CreatePrimitive(PrimitiveType.Cube);
            obstruction.transform.position = target.transform.position + target.Outward * 0.08f;
            obstruction.transform.localScale = Vector3.one * 0.06f;
            Physics.SyncTransforms();
            meter.HandlePointer(camera, pointer, true, false);
            Assert.That(meter.AttachedTarget, Is.Null, "A cabinet or another solid object must occlude the shaft");
            Object.Destroy(obstruction);
            yield return null;
            Physics.SyncTransforms();
            meter.HandlePointer(camera, pointer, false, false);
            Assert.That(Vector3.Distance(meter.View.ProbeTip.position, target.transform.position), Is.LessThan(0.0001f));
            meter.HandlePointer(camera, pointer, true, false);
            Assert.That(meter.AttachedTarget, Is.EqualTo(target));
            Physics.SyncTransforms();
            pointer = camera.WorldToScreenPoint(meter.View.transform.TransformPoint(meter.View.GetComponent<BoxCollider>().center));
            meter.HandlePointer(camera, pointer, true, false);
            Assert.That(meter.AttachedTarget, Is.Null, "Clicking the actual table body must retrieve it");
        }

        [UnityTest]
        public IEnumerator ActualInverterRampsReachOnlyTheWiredMotorAndReverseThroughZero()
        {
            ConnectInverter();
            var inverter = controller.InverterPanel;
            inverter.TrySetParameter("P1082", 1000f);
            inverter.TrySetParameter("P1120", 0.5f);
            inverter.TrySetParameter("P1121", 1f);
            inverter.TrySetParameter("SP", 600f);
            inverter.ToggleHandAuto();
            inverter.PressRun();
            controller.SelectInstrument(InstrumentKind.Tachometer);
            meter.TryAttach(meter.Targets.First(t => t.MotorId == "M1"));
            yield return new WaitForSecondsRealtime(0.15f);
            var motor = (ElectricalDeviceRuntime)controller.Graph.Devices["M1"];
            Assert.That(motor.ActualSpeedRpm, Is.GreaterThan(0).And.LessThan(600));
            yield return new WaitForSecondsRealtime(0.6f);
            Assert.That(motor.ActualSpeedRpm, Is.EqualTo(600).Within(2));
            Assert.That(meter.View.DisplayText, Is.EqualTo("600"));
            Assert.That(((ElectricalDeviceRuntime)controller.Graph.Devices["M2"]).ActualSpeedRpm, Is.Zero);
            inverter.SetControlOptions(false, true);
            var observedZero = false;
            var deadline = Time.realtimeSinceStartup + 2f;
            while (Time.realtimeSinceStartup < deadline)
            {
                yield return null;
                if (Mathf.Abs(inverter.ActualSpeedRpm) < 0.01f) observedZero = true;
            }
            Assert.That(observedZero, Is.True);
            Assert.That(motor.ActualSpeedRpm, Is.EqualTo(-600).Within(2));
            Assert.That(meter.View.DisplayText, Is.EqualTo("600"));
            inverter.PressStop();
            yield return new WaitForSecondsRealtime(0.8f);
            Assert.That(motor.ActualSpeedRpm, Is.Zero.Within(0.1));
            controller.ResetTraining();
            Assert.That(meter.IsSelected, Is.False);
        }

        [UnityTest]
        public IEnumerator CaptureTachometerVisualEvidence()
        {
            if (SystemInfo.graphicsDeviceType == UnityEngine.Rendering.GraphicsDeviceType.Null)
                Assert.Ignore("Visual evidence requires a graphics device.");
            var camera = Camera.main;
            camera.GetComponent<TrainingCameraController>().enabled = false;
            foreach (var canvas in Object.FindObjectsOfType<Canvas>())
                if (canvas.renderMode != RenderMode.WorldSpace) canvas.enabled = false;
            controller.SelectInstrument(InstrumentKind.Tachometer);
            var target = meter.Targets.Single(t => t.MotorId == "M1" && t.transform.parent.parent.name == "107");
            meter.TryAttach(target);
            controller.enabled = false;
            meter.Refresh(controller.Graph.Solve(0));
            camera.transform.position = target.transform.position + target.Outward * 0.5f + Vector3.up * 0.16f + target.transform.right * 0.08f;
            camera.transform.LookAt(target.transform.position - Vector3.up * 0.045f);
            camera.fieldOfView = 38;
            camera.nearClipPlane = 0.01f;
            yield return null;
            Capture(camera, "tachometer-attached");
            ConnectInverter();
            var inverter = controller.InverterPanel;
            inverter.TrySetParameter("SP", 600f);
            inverter.TrySetParameter("P1120", 0.1f);
            inverter.ToggleHandAuto();
            inverter.PressRun();
            yield return new WaitForSecondsRealtime(0.3f);
            meter.Refresh(controller.Graph.Solve(0.02f));
            Capture(camera, "tachometer-running");
            // Standalone close-up uses the same mesh and display, isolated on a temporary render layer.
            foreach (var transform in meter.View.GetComponentsInChildren<Transform>()) transform.gameObject.layer = 30;
            meter.View.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            camera.cullingMask = 1 << 30;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.19f, 0.5f, 0.44f);
            camera.transform.position = new Vector3(0.06f, 0.025f, -0.28f);
            camera.transform.LookAt(Vector3.zero);
            meter.View.SetReading(0);
            Capture(camera, "tachometer-model");
            File.WriteAllText(Path.Combine(Application.dataPath, "../Build/Reports/tachometer-geometry.txt"),
                string.Join("\n", meter.Targets.Select(t => t.MotorId + " " + t.transform.parent.parent.name + " " + t.transform.position + " normal=" + t.Outward)));
        }

        private void ConnectInverter()
        {
            foreach (var phase in new[] { "L1", "L2", "L3" }) controller.Graph.AddWire("POWER." + phase, "G120." + phase, Color.red);
            foreach (var phase in new[] { "U", "V", "W" }) controller.Graph.AddWire("G120." + phase + "2", "M1." + phase, Color.red);
        }
        private static void Capture(Camera camera, string name)
        {
            var previous = RenderTexture.active;
            var target = new RenderTexture(1000, 1000, 24);
            var texture = new Texture2D(1000, 1000, TextureFormat.RGB24, false);
            camera.targetTexture = target;
            camera.Render();
            RenderTexture.active = target;
            texture.ReadPixels(new Rect(0, 0, 1000, 1000), 0, 0);
            texture.Apply();
            Directory.CreateDirectory(Path.Combine(Application.dataPath, "../Build/Reports"));
            File.WriteAllBytes(Path.Combine(Application.dataPath, "../Build/Reports/" + name + ".png"), texture.EncodeToPNG());
            camera.targetTexture = null;
            RenderTexture.active = previous;
            Object.Destroy(texture);
            Object.Destroy(target);
        }
    }
}
