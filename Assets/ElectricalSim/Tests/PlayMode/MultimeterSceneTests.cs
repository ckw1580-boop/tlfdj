using System.Collections;
using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace ElectricalSim.Tests
{
    public sealed class MultimeterSceneTests
    {
        private SimulationController controller;
        private MultimeterController meter;
        private Camera camera;
        private GameObject fixture;
        [UnitySetUp]
        public IEnumerator SetUp()
        {
            SceneManager.LoadScene("ElectricalTraining");
            yield return null; yield return null;
            controller = Object.FindObjectOfType<SimulationController>();
            meter = controller.Multimeter; camera = Camera.main;
            Assert.That(meter, Is.Not.Null);
        }
        [UnityTearDown]
        public IEnumerator TearDown()
        {
            if (fixture != null) Object.Destroy(fixture);
            AudioListener.pause = false;
            yield return null;
        }

        private void Isolate()
        {
            controller.enabled = false;
            camera.GetComponent<TrainingCameraController>().enabled = false;
            controller.SelectInstrument(InstrumentKind.Multimeter);
            camera.transform.SetPositionAndRotation(new Vector3(0, 10, -0.8f), Quaternion.identity);
            camera.fieldOfView = 45; camera.nearClipPlane = 0.01f;
            meter.View.Body.SetPositionAndRotation(new Vector3(0.22f, 10, 0), Quaternion.identity);
            fixture = new GameObject("Multimeter Test Contacts");
            controller.Graph.RegisterDevice(new ElectricalDeviceRuntime("METER_FIXTURE", ElectricalDeviceKind.Terminal, new[] { "A", "B", "C" }));
            meter.Refresh(controller.Graph.Solve(0));
        }
        private ElectricalPortView Port(string name, Vector3 position)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            go.transform.SetParent(fixture.transform); go.transform.position = position; go.transform.localScale = Vector3.one * 0.024f;
            var port = go.AddComponent<ElectricalPortView>(); port.Initialize("METER_FIXTURE", name, Color.green);
            port.SetVisibleForMode(SimulationMode.Fault);
            return port;
        }
        private void Attach(int index, ElectricalPortView port)
        {
            meter.PickUpProbe(index);
            Physics.SyncTransforms();
            meter.HandlePointer(camera, camera.WorldToScreenPoint(port.CurrentAnchorPosition), true, true, false);
            Assert.That(index == 0 ? meter.RedPort : meter.BlackPort, Is.EqualTo(port.QualifiedPort));
        }

        [UnityTest]
        public IEnumerator IndependentProbesCanShareATerminalAndNeverBecomeWires()
        {
            Assert.That(meter.View.gameObject.activeSelf, Is.False);
            Isolate();
            var a = Port("A", new Vector3(-0.10f, 10.02f, 0));
            var b = Port("B", new Vector3(-0.10f, 9.9f, 0));
            var count = controller.Graph.Wires.Count;
            var dirty = controller.HasUnsavedWiring;
            Attach(0, a); Attach(1, a);
            meter.SetMode(MultimeterMode.Continuity);
            meter.Refresh(controller.Graph.Solve(0));
            Assert.That(meter.Reading.ShouldBeep, Is.True);
            Assert.That(meter.View.DisplayText, Is.EqualTo("导通"));
            if (SystemInfo.graphicsDeviceType != UnityEngine.Rendering.GraphicsDeviceType.Null)
                Assert.That(meter.View.IsBeeping, Is.True);
            // Both handles must be independently pickable even when their tips share a contact.
            for (var index = 0; index < 2; index++)
            {
                Physics.SyncTransforms();
                meter.HandlePointer(camera, camera.WorldToScreenPoint(meter.View.Probe(index).position), true, true, false);
                Assert.That(meter.HeldProbe, Is.EqualTo(index), "The actual handle ray must distinguish red and black");
                Assert.That(meter.TryAttach(a, camera), Is.True);
            }
            Attach(0, b);
            Assert.That(meter.BlackPort, Is.EqualTo(a.QualifiedPort));
            Assert.That(meter.View.DisplayText, Is.EqualTo("OL"));
            Assert.That(meter.View.IsBeeping, Is.False);
            Assert.That(controller.Graph.Wires.Count, Is.EqualTo(count));
            Assert.That(controller.HasUnsavedWiring, Is.EqualTo(dirty));
            meter.RetractProbes();
            Assert.That(meter.RedPort, Is.Null); Assert.That(meter.BlackPort, Is.Null);
            Assert.That(meter.View.DisplayText, Is.EqualTo("—"));
            yield return null;
        }

        [UnityTest]
        public IEnumerator PhysicalAnchorsSurviveCameraSideChangesAndBodyDragging()
        {
            Isolate();
            var a = Port("A", new Vector3(-0.1f, 10, 0));
            var front = new GameObject("Original Front Anchor").transform; front.SetParent(fixture.transform); front.position = a.transform.position;
            var back = new GameObject("Original Back Anchor").transform; back.SetParent(fixture.transform); back.position = front.position + Vector3.forward * 0.4f;
            a.ConfigureOriginalAnchors(front, front, back, back);
            a.ApplyOriginalAnchor(TrainingViewPreset.WiringFront, false);
            Attach(0, a);
            var contactPoint = meter.View.ProbeTip(0).position;
            a.ApplyOriginalAnchor(TrainingViewPreset.FaultBack, false);
            camera.transform.position += Vector3.up * 0.02f;
            meter.Refresh(controller.Graph.Solve(0));
            Assert.That(Vector3.Distance(meter.View.ProbeTip(0).position, contactPoint), Is.LessThan(0.0001f));
            var bodyStart = meter.View.Body.position;
            var pointer = (Vector2)camera.WorldToScreenPoint(meter.View.Body.TransformPoint(new Vector3(0.052f, 0.068f, -0.022f)));
            Physics.SyncTransforms();
            meter.HandlePointer(camera, pointer, true, true, false);
            Assert.That(meter.IsDragging, Is.True);
            Assert.That(camera.GetComponent<TrainingCameraController>().InstrumentInputBlocked, Is.True);
            meter.HandlePointer(camera, pointer + Vector2.left * 60, false, true, false);
            meter.HandlePointer(camera, pointer + Vector2.left * 60, false, false, false);
            Assert.That(Vector3.Distance(meter.View.Body.position, bodyStart), Is.GreaterThan(0.01f));
            Assert.That(Vector3.Distance(meter.View.ProbeTip(0).position, contactPoint), Is.LessThan(0.0001f));
            Assert.That(camera.GetComponent<TrainingCameraController>().InstrumentInputBlocked, Is.False);
            front.position += Vector3.up * 0.025f;
            meter.Refresh(controller.Graph.Solve(0));
            Assert.That(Vector3.Distance(meter.View.ProbeTip(0).position, front.position), Is.LessThan(0.0001f));
            Assert.That(meter.View.Lead(0).GetPosition(24), Is.EqualTo(meter.View.Probe(0).position - meter.View.Probe(0).up * 0.036f));
            yield return null;
        }

        [UnityTest]
        public IEnumerator UiAndSolidObjectsOccludeProbePlacement()
        {
            Isolate();
            var a = Port("A", new Vector3(-0.1f, 10, 0));
            var pointer = (Vector2)camera.WorldToScreenPoint(a.transform.position);
            meter.PickUpProbe(0); Physics.SyncTransforms();
            meter.HandlePointer(camera, pointer, true, true, true);
            Assert.That(meter.RedPort, Is.Null);
            var obstruction = GameObject.CreatePrimitive(PrimitiveType.Cube);
            obstruction.transform.position = Vector3.Lerp(camera.transform.position, a.transform.position, 0.8f);
            obstruction.transform.localScale = Vector3.one * 0.07f;
            Physics.SyncTransforms();
            meter.HandlePointer(camera, pointer, true, true, false);
            Assert.That(meter.RedPort, Is.Null);
            Object.Destroy(obstruction); yield return null; Physics.SyncTransforms();
            meter.HandlePointer(camera, pointer, true, true, false);
            Assert.That(meter.RedPort, Is.EqualTo(a.QualifiedPort));
        }

        [UnityTest]
        public IEnumerator PhysicalDialAndCleanupWorkThroughActualPickingRays()
        {
            Isolate();
            foreach (var action in new[] { MultimeterAction.Off, MultimeterAction.DcVoltage, MultimeterAction.Continuity, MultimeterAction.AcVoltage })
            {
                var control = meter.View.GetComponentsInChildren<MultimeterInteractable>().Single(c => c.Action == action);
                Physics.SyncTransforms();
                meter.HandlePointer(camera, camera.WorldToScreenPoint(control.transform.position), true, true, false);
                Assert.That((int)meter.MeasurementMode, Is.EqualTo((int)action - (int)MultimeterAction.Off));
            }
            var a = Port("A", new Vector3(-0.1f, 10, 0)); Attach(0, a); Attach(1, a);
            meter.SetMode(MultimeterMode.Continuity);
            controller.SelectInstrument(InstrumentKind.Tachometer);
            Assert.That(meter.IsSelected, Is.False); Assert.That(meter.View.gameObject.activeSelf, Is.False);
            Assert.That(meter.View.IsBeeping, Is.False); Assert.That(meter.RedPort, Is.Null);
            controller.SelectInstrument(InstrumentKind.Multimeter);
            Assert.That(controller.Tachometer.IsSelected, Is.False);
            Assert.That(meter.MeasurementMode, Is.EqualTo(MultimeterMode.AcVoltage));
            controller.SetMode(SimulationMode.View);
            Assert.That(meter.IsSelected, Is.False);
            controller.SelectInstrument(InstrumentKind.Multimeter); controller.ResetTraining();
            Assert.That(meter.View.gameObject.activeSelf, Is.False);
            yield return null;
        }

        [UnityTest]
        public IEnumerator FaultPowerControlsPreserveProbesAndRejectClicksWhileHoldingOne()
        {
            Isolate();
            var a = Port("A", new Vector3(-0.1f, 10, 0));
            var b = Port("B", new Vector3(-0.1f, 9.9f, 0));
            controller.Graph.AddWire("POWER.L1", a.QualifiedPort, Color.red);
            controller.Graph.AddWire(a.QualifiedPort, b.QualifiedPort, Color.red);
            var key = controller.PanelControls.First(v => v.Definition.Id == "PANEL_KEY");
            var start = controller.PanelControls.First(v => v.Definition.Id == "PANEL_START");
            var stop = controller.PanelControls.First(v => v.Definition.Id == "PANEL_STOP");
            meter.PickUpProbe(0);
            controller.PressPanelDevice(key);
            Assert.That(key.Runtime.IsPressed, Is.False);
            meter.RetractProbes(); Attach(0, a); Attach(1, b);
            controller.PressPanelDevice(key); controller.PressPanelDevice(start); controller.ReleasePanelButton();
            Assert.That(controller.PanelPower.Enabled, Is.True);
            meter.SetMode(MultimeterMode.Continuity); meter.Refresh(controller.Graph.Solve(0));
            Assert.That(meter.View.HintText, Is.EqualTo("请先断电"));
            controller.PressPanelDevice(stop); controller.ReleasePanelButton();
            meter.Refresh(controller.Graph.Solve(0));
            Assert.That(controller.PanelPower.Enabled, Is.False);
            Assert.That(meter.View.DisplayText, Is.EqualTo("导通"));
            Assert.That(meter.RedPort, Is.EqualTo(a.QualifiedPort)); Assert.That(meter.BlackPort, Is.EqualTo(b.QualifiedPort));
            var breaker = controller.CabinetBreakers[0]; var initial = breaker.IsClosed;
            Assert.That(controller.TryToggleCabinetBreaker(breaker), Is.True);
            Assert.That(breaker.IsClosed, Is.Not.EqualTo(initial));
            meter.PickUpProbe(0);
            Assert.That(controller.TryToggleCabinetBreaker(breaker), Is.False);
            yield return null;
        }

        [UnityTest]
        public IEnumerator SchematicModalSuspendsInstrumentInputWithoutLosingContacts()
        {
            controller.SelectInstrument(InstrumentKind.Multimeter);
            var port = Object.FindObjectsOfType<ElectricalPortView>().First(p => p.IsVisible);
            meter.PickUpProbe(0); Assert.That(meter.TryAttach(port, camera), Is.True);
            var position = meter.View.ProbeTip(0).position;
            controller.SchematicGallery.OpenViewer();
            yield return null;
            Assert.That(controller.IsInteractionBlocked, Is.True);
            Assert.That(meter.RedPort, Is.EqualTo(port.QualifiedPort));
            Assert.That(Vector3.Distance(meter.View.ProbeTip(0).position, position), Is.LessThan(0.0001f));
            controller.SchematicGallery.CloseViewer();
            yield return null;
        }

        [UnityTest]
        public IEnumerator MeterExposesBothTerminalTypesAndVisibleHudRegardlessOfWiringTool()
        {
            camera.GetComponent<TrainingCameraController>().SetFaultView();
            foreach (var tool in new[] { "JumperLine", "ElectricalWire" })
            {
                controller.SetMode(SimulationMode.Wiring);
                controller.SetWireStyle(Color.red, 0.01f, tool);
                controller.SelectInstrument(InstrumentKind.Multimeter);
                yield return null;
                var ports = Object.FindObjectsOfType<ElectricalPortView>();
                var electrical = ports.Where(p => p.ElectricalOnly && !p.WiringModeOnly && p.GetOriginalAnchor(TrainingViewPreset.FaultBack, false) != null).ToArray();
                var jumpers = ports.Where(p => p.JumperOnly && !p.WiringModeOnly && p.GetOriginalAnchor(TrainingViewPreset.FaultBack, true) != null).ToArray();
                Assert.That(electrical, Is.Not.Empty); Assert.That(jumpers, Is.Not.Empty);
                Assert.That(electrical.All(p => p.IsVisible && !p.UsesJumperAnchor), Is.True);
                Assert.That(jumpers.All(p => p.IsVisible && p.UsesJumperAnchor), Is.True);
                var hud = Object.FindObjectOfType<MultimeterHudPresenter>();
                Assert.That(hud.Panel.activeInHierarchy, Is.True);
                Assert.That(hud.DisplayText, Does.Contain("交流"));
                var corners = new Vector3[4]; hud.Panel.GetComponent<RectTransform>().GetWorldCorners(corners);
                Assert.That(corners.All(c => c.x >= 0 && c.x <= Screen.width && c.y >= 0 && c.y <= Screen.height), Is.True);
                controller.SelectInstrument(InstrumentKind.Tachometer);
                yield return null;
                Assert.That(hud.Panel.activeInHierarchy, Is.False);
            }
        }

        [UnityTest]
        public IEnumerator CaptureMultimeterEvidence()
        {
            if (SystemInfo.graphicsDeviceType == UnityEngine.Rendering.GraphicsDeviceType.Null) Assert.Ignore("Visual evidence requires graphics.");
            controller.enabled = false;
            camera.GetComponent<TrainingCameraController>().enabled = false;
            camera.GetComponent<TrainingCameraController>().SetFaultView();
            controller.SelectInstrument(InstrumentKind.Multimeter);
            controller.PanelPower.StartForAssessment();
            var snapshot = controller.Graph.Solve(0);
            var ports = Object.FindObjectsOfType<ElectricalPortView>().Where(p => p.IsVisible).ToArray();
            var red = ports.First(p => snapshot.GetPotential(p.QualifiedPort) == ElectricalPotential.PhaseL1);
            var black = ports.First(p => snapshot.GetPotential(p.QualifiedPort) == ElectricalPotential.Neutral);
            meter.PickUpProbe(0); Assert.That(meter.TryAttach(red, camera), Is.True);
            meter.PickUpProbe(1); Assert.That(meter.TryAttach(black, camera), Is.True);
            meter.Refresh(snapshot);
            yield return null;
            Capture("multimeter-ac-cabinet", 1600, 1000);
            Capture("multimeter-ac-hud-1920", 1920, 1080, true);
            var dcRed = ports.First(p => snapshot.GetPotential(p.QualifiedPort) == ElectricalPotential.DcPositive24);
            var dcBlack = ports.First(p => snapshot.GetPotential(p.QualifiedPort) == ElectricalPotential.DcNegative);
            meter.PickUpProbe(0); meter.TryAttach(dcRed, camera); meter.PickUpProbe(1); meter.TryAttach(dcBlack, camera);
            meter.SetMode(MultimeterMode.DcVoltage); meter.Refresh(snapshot);
            Capture("multimeter-dc-cabinet", 1600, 1000);
            var peer = ports.First(p => p != dcRed && snapshot.SameNet(p.QualifiedPort, dcRed.QualifiedPort));
            meter.PickUpProbe(1); meter.TryAttach(peer, camera); meter.SetMode(MultimeterMode.Continuity);
            Capture("multimeter-live-warning", 1600, 1000);
            controller.PanelPower.Reset(); meter.Refresh(controller.Graph.Solve(0));
            Capture("multimeter-continuity-cabinet", 1600, 1000);
            Capture("multimeter-continuity-hud-1280", 1280, 720, true);
            meter.RetractProbes();
            foreach (var t in meter.View.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = 30;
            camera.cullingMask = 1 << 30;
            meter.View.Body.SetPositionAndRotation(Vector3.zero, Quaternion.identity); meter.Refresh(snapshot);
            camera.transform.position = new Vector3(0.10f, 0.035f, -0.47f); camera.transform.LookAt(new Vector3(0, -0.02f, 0));
            camera.fieldOfView = 42; camera.nearClipPlane = 0.01f;
            camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = new Color(0.055f, 0.078f, 0.10f);
            yield return null;
            Capture("multimeter-model", 1200, 1200);
        }

        private void Capture(string name, int width, int height, bool includeHud = false)
        {
            var previous = RenderTexture.active; var previousTarget = camera.targetTexture;
            var target = new RenderTexture(width, height, 24);
            var texture = new Texture2D(width, height, TextureFormat.RGB24, false);
            var hud = Object.FindObjectOfType<MultimeterHudPresenter>();
            var canvas = hud.Panel.GetComponentInParent<Canvas>();
            var canvasMode = canvas.renderMode; var canvasCamera = canvas.worldCamera; var canvasDistance = canvas.planeDistance;
            try
            {
                camera.targetTexture = target;
                if (includeHud)
                {
                    hud.Refresh();
                    canvas.renderMode = RenderMode.ScreenSpaceCamera; canvas.worldCamera = camera; canvas.planeDistance = 0.10f;
                    Canvas.ForceUpdateCanvases();
                }
                camera.Render(); RenderTexture.active = target;
                texture.ReadPixels(new Rect(0, 0, width, height), 0, 0); texture.Apply();
                var path = Path.Combine(Application.dataPath, "../Build/Reports/" + name + ".png");
                File.WriteAllBytes(path, texture.EncodeToPNG());
            }
            finally
            {
                camera.targetTexture = previousTarget; RenderTexture.active = previous;
                canvas.renderMode = canvasMode; canvas.worldCamera = canvasCamera; canvas.planeDistance = canvasDistance;
                Canvas.ForceUpdateCanvases();
                Object.Destroy(texture); Object.Destroy(target);
            }
        }
    }
}
