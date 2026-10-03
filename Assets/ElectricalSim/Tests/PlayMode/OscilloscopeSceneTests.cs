using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UI;

namespace ElectricalSim.Tests
{
    public sealed class OscilloscopeSceneTests
    {
        private SimulationController simulation;
        private OscilloscopeController scope;
        private OscilloscopePropertiesPresenter properties;
        private Camera camera;
        private GameObject fixture;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            SceneManager.LoadScene("ElectricalTraining");
            yield return null;
            yield return null;
            simulation = Object.FindObjectOfType<SimulationController>();
            Assert.That(simulation, Is.Not.Null);
            scope = simulation.Oscilloscope;
            properties = Object.FindObjectOfType<OscilloscopePropertiesPresenter>();
            camera = Camera.main;
            Assert.That(scope, Is.Not.Null, "Build Oscilloscope Model must supply the scene's resource prefab.");
            Assert.That(properties, Is.Not.Null);
            Assert.That(scope.View.gameObject.activeSelf, Is.False);
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            if (fixture != null) Object.Destroy(fixture);
            yield return null;
        }

        private void Isolate()
        {
            simulation.enabled = false;
            camera.GetComponent<TrainingCameraController>().enabled = false;
            camera.transform.SetPositionAndRotation(new Vector3(0, 10, -1.5f), Quaternion.identity);
            camera.fieldOfView = 45;
            camera.nearClipPlane = .01f;
            simulation.SelectInstrument(InstrumentKind.Oscilloscope);
            scope.View.Body.SetPositionAndRotation(new Vector3(.27f, 10, 0), Quaternion.identity);
            fixture = new GameObject("Oscilloscope test contacts");
            simulation.Graph.RegisterDevice(new ElectricalDeviceRuntime("SCOPE_TEST", ElectricalDeviceKind.Terminal, new[] { "A", "B", "C", "D" }));
            simulation.PanelPower.StartForAssessment();
            Refresh();
        }

        private ElectricalPortView Port(string name, float y = 10)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            go.transform.SetParent(fixture.transform);
            go.transform.position = new Vector3(-.28f, y, 0);
            go.transform.localScale = Vector3.one * .024f;
            var port = go.AddComponent<ElectricalPortView>();
            port.Initialize("SCOPE_TEST", name, Color.green);
            port.SetVisibleForMode(SimulationMode.Fault);
            return port;
        }

        private void Refresh()
        {
            scope.Refresh(simulation.Graph.Solve(0), true);
            properties.Refresh();
        }

        private void Attach(int index, ElectricalPortView port)
        {
            scope.PickUpProbe(index);
            Physics.SyncTransforms();
            scope.HandlePointer(camera, camera.WorldToScreenPoint(port.CurrentAnchorPosition), true, true, false);
            Assert.That(scope.ProbePort(index), Is.EqualTo(port.QualifiedPort), "Probe " + index + " must reach the terminal through its actual picking ray.");
        }

        private void ClickModel(OscilloscopeAction action)
        {
            var control = scope.View.GetComponentsInChildren<OscilloscopeInteractable>().Single(c => c.Action == action);
            Physics.SyncTransforms();
            scope.HandlePointer(camera, camera.WorldToScreenPoint(control.transform.position), true, true, false);
        }

        private Button PropertyButton(string name) => properties.Panel.GetComponentsInChildren<Button>(true).Single(b => b.name == name);

        [UnityTest]
        public IEnumerator FourPhysicalProbesShareOneTerminalWithoutChangingTheCircuit()
        {
            Isolate();
            var contact = Port("A");
            var wireCount = simulation.Graph.Wires.Count;
            var dirty = simulation.HasUnsavedWiring;
            for (var i = 0; i < 4; i++) Attach(i, contact);
            Refresh();
            foreach (var channel in scope.Frame.Channels)
            {
                Assert.That(channel.Signal.Valid, Is.True);
                Assert.That(channel.Signal.Rms(channel.Coupling), Is.Zero);
            }
            for (var i = 0; i < 4; i++)
            {
                Physics.SyncTransforms();
                scope.HandlePointer(camera, camera.WorldToScreenPoint(scope.View.Probe(i).position), true, true, false);
                Assert.That(scope.HeldProbe, Is.EqualTo(i), "Coincident tips must still have individually pickable handles.");
                Assert.That(scope.TryAttach(contact, camera), Is.True);
                Assert.That(scope.View.Lead(i).useWorldSpace, Is.True);
                Assert.That(scope.View.Lead(i).GetPosition(scope.View.Lead(i).positionCount - 1),
                    Is.EqualTo(scope.View.Probe(i).position - scope.View.Probe(i).up * .03f));
            }
            Assert.That(simulation.Graph.Wires.Count, Is.EqualTo(wireCount));
            Assert.That(simulation.HasUnsavedWiring, Is.EqualTo(dirty));
            Assert.That(scope.View.Plot.Frame, Is.SameAs(properties.Plot.Frame));
            Assert.That(scope.View.Plot.Frame, Is.SameAs(scope.Frame));
            Assert.That(properties.Panel.GetComponentInChildren<ScrollRect>(), Is.Not.Null);
            Assert.That(scope.View.Body.GetComponentsInChildren<Transform>().Count(t => t.name == "SupportFoot"), Is.EqualTo(2));
            PropertyButton("Retract").onClick.Invoke();
            for (var i = 0; i < 4; i++) Assert.That(scope.ProbePort(i), Is.Null);
            Assert.That(scope.View.DisplayText, Does.Contain("请连接正负探头"));
            yield return null;
        }

        [UnityTest]
        public IEnumerator AnchorsFollowPhysicalContactsWhileDraggingAndOcclusionBlocksPlacement()
        {
            Isolate();
            var contact = Port("A");
            var front = new GameObject("Original front contact").transform;
            front.SetParent(fixture.transform); front.position = contact.transform.position;
            var back = new GameObject("Original back contact").transform;
            back.SetParent(fixture.transform); back.position = front.position + Vector3.forward * .3f;
            contact.ConfigureOriginalAnchors(front, front, back, back);
            contact.ApplyOriginalAnchor(TrainingViewPreset.WiringFront, false);
            Attach(0, contact);
            var originalPoint = scope.View.ProbeTip(0).position;
            contact.ApplyOriginalAnchor(TrainingViewPreset.FaultBack, false);
            Refresh();
            Assert.That(Vector3.Distance(scope.View.ProbeTip(0).position, originalPoint), Is.LessThan(.0001f));
            var start = scope.View.Body.position;
            var pointer = (Vector2)camera.WorldToScreenPoint(scope.View.Body.TransformPoint(new Vector3(-.2f, .125f, -.08f)));
            Physics.SyncTransforms();
            scope.HandlePointer(camera, pointer, true, true, false);
            Assert.That(scope.IsDragging, Is.True);
            Assert.That(camera.GetComponent<TrainingCameraController>().InstrumentInputBlocked, Is.True);
            scope.HandlePointer(camera, pointer + Vector2.right * 80, false, true, false);
            scope.HandlePointer(camera, pointer + Vector2.right * 80, false, false, false);
            Assert.That(Vector3.Distance(scope.View.Body.position, start), Is.GreaterThan(.01f));
            Assert.That(camera.GetComponent<TrainingCameraController>().InstrumentInputBlocked, Is.False);
            Assert.That(Vector3.Distance(scope.View.ProbeTip(0).position, originalPoint), Is.LessThan(.0001f));
            front.position += Vector3.up * .035f;
            Refresh();
            Assert.That(Vector3.Distance(scope.View.ProbeTip(0).position, front.position), Is.LessThan(.0001f));

            var other = Port("B", 9.8f);
            scope.PickUpProbe(1);
            var otherPointer = (Vector2)camera.WorldToScreenPoint(other.CurrentAnchorPosition);
            scope.HandlePointer(camera, otherPointer, true, true, true);
            Assert.That(scope.ProbePort(1), Is.Null);
            var obstacle = GameObject.CreatePrimitive(PrimitiveType.Cube);
            obstacle.transform.SetParent(fixture.transform);
            obstacle.transform.position = Vector3.Lerp(camera.transform.position, other.CurrentAnchorPosition, .8f);
            obstacle.transform.localScale = Vector3.one * .08f;
            Physics.SyncTransforms();
            scope.HandlePointer(camera, otherPointer, true, true, false);
            Assert.That(scope.ProbePort(1), Is.Null);
            Object.Destroy(obstacle);
            yield return null;
            Attach(1, other);
            Object.Destroy(front.gameObject);
            yield return null;
            Refresh();
            Assert.That(scope.ProbePort(0), Is.Null, "Removed physical contacts must release the probe.");
        }

        [UnityTest]
        public IEnumerator FreezeKeepsBothWaveformsReadingsAndLabelsWhileContactsCanChange()
        {
            Isolate();
            var a = Port("A", 10.18f); var b = Port("B", 10.06f);
            var c = Port("C", 9.94f); var d = Port("D", 9.82f);
            simulation.Graph.AddWire("POWER.L1", a.QualifiedPort, Color.red);
            simulation.Graph.AddWire("POWER.N", b.QualifiedPort, Color.blue);
            simulation.Graph.AddWire("TERMINAL_BUS.DC_POSITIVE", c.QualifiedPort, Color.red);
            simulation.Graph.AddWire("TERMINAL_BUS.DC_NEGATIVE", d.QualifiedPort, Color.blue);
            Refresh();
            Attach(0, a); Attach(1, b); Attach(2, c); Attach(3, d);
            Refresh();
            Assert.That(scope.Frame.Channels[0].Signal.AcRms, Is.EqualTo(220));
            Assert.That(scope.Frame.Channels[1].Signal.Dc, Is.EqualTo(24));
            Assert.That(simulation.Graph.Solve(0).SameNet(b.QualifiedPort, d.QualifiedPort), Is.False, "Independent channel negatives must not become common ground.");
            var firstSamples = scope.Frame.Channels[0].Samples.ToArray();
            var secondSamples = scope.Frame.Channels[1].Samples.ToArray();
            var frozenReading = scope.Frame.DescribeChannel(0);
            ClickModel(OscilloscopeAction.RunStop);
            Assert.That(scope.Frame.Frozen, Is.True);
            Attach(0, c);
            Refresh();
            Assert.That(scope.Frame.DescribeChannel(0), Is.EqualTo(frozenReading));
            CollectionAssert.AreEqual(firstSamples, scope.Frame.Channels[0].Samples);
            CollectionAssert.AreEqual(secondSamples, scope.Frame.Channels[1].Samples);
            Assert.That(scope.View.DisplayText, Does.Contain("已冻结"));
            Assert.That(properties.Panel.GetComponentsInChildren<Text>().Any(t => t.text.Contains("已冻结")), Is.True);
            PropertyButton("RunStop").onClick.Invoke();
            Refresh();
            Assert.That(scope.Frame.Channels[0].Signal.State, Is.EqualTo(OscilloscopeSignalState.UndefinedReference));
            Assert.That(scope.Frame.Channels[0].Samples, Is.All.Zero);
            Assert.That(scope.Frame.Channels[1].Signal.Dc, Is.EqualTo(24));
            var coupling = properties.Panel.GetComponentsInChildren<Dropdown>(true).Single(v => v.name == "Coupling1");
            coupling.value = (int)OscilloscopeCoupling.AC;
            Assert.That(scope.Frame.Channels[1].Samples, Is.All.Zero);
            Assert.That(scope.Frame.DescribeChannel(1), Does.Contain("平均值 0 V").And.Contain("频率 —"));
            yield return null;
        }

        [UnityTest]
        public IEnumerator PhysicalControlsAndPropertiesPersistAcrossRaycastToolSwitchUntilTrainingReset()
        {
            Isolate();
            ClickModel(OscilloscopeAction.Timebase);
            Assert.That(scope.Frame.MillisecondsPerDivision, Is.EqualTo(10));
            ClickModel(OscilloscopeAction.Voltage1);
            Assert.That(scope.Frame.Channels[0].VoltsPerDivision, Is.EqualTo(200));
            ClickModel(OscilloscopeAction.Voltage2);
            Assert.That(scope.Frame.Channels[1].VoltsPerDivision, Is.EqualTo(200));
            ClickModel(OscilloscopeAction.AutoScale);
            Assert.That(scope.Frame.MillisecondsPerDivision, Is.EqualTo(10), "No signal must leave the selected range unchanged.");
            PropertyButton("Channel1").onClick.Invoke();
            PropertyButton("Up0").onClick.Invoke();
            properties.Panel.GetComponentsInChildren<Dropdown>(true).Single(v => v.name == "Coupling0").value = (int)OscilloscopeCoupling.AC;
            Assert.That(scope.Frame.Channels[0].Position, Is.EqualTo(.5f));
            Assert.That(scope.Frame.Channels[1].Enabled, Is.False);

            // Open the real fault picker, then require a UI ray to reach the tool button above the property panel.
            Object.FindObjectsOfType<Button>(true).Single(b => b.name == "btn_paigu").onClick.Invoke();
            properties.Refresh();
            // Enabled graphics join Unity's raycast registry on the following frame.
            yield return null;
            Canvas.ForceUpdateCanvases();
            var multimeterButton = Object.FindObjectsOfType<Button>(true).Single(b => b.name == "Instrument_Multimeter");
            Assert.That(multimeterButton.gameObject.activeInHierarchy, Is.True);
            var rect = (RectTransform)multimeterButton.transform;
            var canvas = rect.GetComponentInParent<Canvas>();
            var point = RectTransformUtility.WorldToScreenPoint(canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : canvas.worldCamera, rect.TransformPoint(rect.rect.center));
            var pointer = new PointerEventData(EventSystem.current) { position = point };
            var hits = new List<RaycastResult>();
            EventSystem.current.RaycastAll(pointer, hits);
            Assert.That(hits, Is.Not.Empty);
            Assert.That(hits[0].gameObject.GetComponentInParent<Button>(), Is.SameAs(multimeterButton), "The open scope property panel must not cover the tool picker.");
            ExecuteEvents.ExecuteHierarchy(hits[0].gameObject, pointer, ExecuteEvents.pointerClickHandler);
            Assert.That(simulation.Multimeter.IsSelected, Is.True);
            Assert.That(scope.IsSelected, Is.False);
            simulation.SelectInstrument(InstrumentKind.Oscilloscope);
            Assert.That(scope.Frame.MillisecondsPerDivision, Is.EqualTo(10));
            Assert.That(scope.Frame.Channels[0].Coupling, Is.EqualTo(OscilloscopeCoupling.AC));
            Assert.That(scope.Frame.Channels[1].Enabled, Is.False);
            Assert.That(scope.Frame.Channels[0].Position, Is.EqualTo(.5f));
            scope.PickUpProbe(3);
            simulation.SetMode(SimulationMode.View);
            Assert.That(scope.IsBusy, Is.False);
            Assert.That(scope.View.gameObject.activeSelf, Is.False);
            simulation.SelectInstrument(InstrumentKind.Oscilloscope);
            simulation.ResetTraining();
            Assert.That(scope.View.gameObject.activeSelf, Is.False);
            Assert.That(scope.Frame.MillisecondsPerDivision, Is.EqualTo(5));
            foreach (var channel in scope.Frame.Channels)
            {
                Assert.That(channel.Enabled, Is.True);
                Assert.That(channel.Coupling, Is.EqualTo(OscilloscopeCoupling.DC));
                Assert.That(channel.VoltsPerDivision, Is.EqualTo(100));
                Assert.That(channel.Position, Is.Zero);
            }
            yield return null;
        }

        [UnityTest]
        public IEnumerator SchematicHomeAndResetModalsSuspendDraggingWithoutLosingAttachedContacts()
        {
            Isolate();
            var a = Port("A"); Attach(0, a);
            simulation.enabled = true;
            var cameraControl = camera.GetComponent<TrainingCameraController>();
            for (var modal = 0; modal < 3; modal++)
            {
                var pointer = (Vector2)camera.WorldToScreenPoint(scope.View.Body.TransformPoint(new Vector3(-.2f, .125f, -.08f)));
                Physics.SyncTransforms();
                scope.HandlePointer(camera, pointer, true, true, false);
                Assert.That(scope.IsDragging, Is.True);
                if (modal == 0) simulation.SchematicGallery.OpenViewer();
                else if (modal == 1) simulation.HomePage.Open();
                else simulation.RequestResetTraining();
                yield return null;
                properties.Refresh();
                Assert.That(simulation.IsInteractionBlocked, Is.True);
                Assert.That(scope.InteractionBlocked, Is.True);
                Assert.That(scope.IsDragging, Is.False);
                Assert.That(cameraControl.InstrumentInputBlocked, Is.False);
                Assert.That(properties.Panel.activeSelf, Is.False);
                Assert.That(scope.ProbePort(0), Is.EqualTo(a.QualifiedPort));
                scope.PickUpProbe(1);
                Assert.That(scope.HeldProbe, Is.EqualTo(-1));
                if (modal == 0) simulation.SchematicGallery.CloseViewer();
                else if (modal == 1) simulation.HomePage.Close();
                else simulation.ResetConfirmation.OnCancel(null);
                yield return null;
                yield return null;
                properties.Refresh();
                Assert.That(simulation.IsInteractionBlocked, Is.False);
                Assert.That(properties.Panel.activeSelf, Is.True);
            }
        }

        [UnityTest]
        public IEnumerator InverterModesFreezeAndCaptureEvidence()
        {
            Isolate();
            var graph = simulation.Graph;
            graph.RegisterDevice(new InverterDriveRuntime("PWM_TEST", () => 725, () => false));
            foreach (var phase in new[] { "L1", "L2", "L3" }) graph.AddWire("POWER." + phase, "PWM_TEST." + phase, Color.red);
            var names = new[] { "A", "B", "C", "D" }; var phases = new[] { "U2", "V2", "V2", "W2" };
            for (var i = 0; i < 4; i++)
            {
                var port = Port(names[i], 10.18f - i * .12f);
                graph.AddWire("PWM_TEST." + phases[i], port.QualifiedPort, Color.red); Attach(i, port);
            }
            Refresh(); scope.AutoScale(); properties.Refresh();
            foreach (var c in scope.Frame.Channels) { Assert.That(c.Signal.AcRms, Is.EqualTo(190).Within(.001)); Assert.That(c.PwmIntervals.Count, Is.GreaterThan(0)); }
            Assert.That(scope.View.Plot.Frame, Is.SameAs(properties.Plot.Frame));
            yield return null;
            SaveWaveformEvidence("overlay.png");
            var dropdown = properties.Panel.GetComponentsInChildren<Dropdown>(true).Single(v => v.name == "Waveform0");
            dropdown.value = (int)OscilloscopeWaveform.Fundamental; Assert.That(scope.Frame.Channels[0].PwmIntervals.Count, Is.Zero);
            dropdown.value = (int)OscilloscopeWaveform.PWM; scope.SetWaveform(1, OscilloscopeWaveform.PWM); scope.SetTimeIndex(0);
            properties.Refresh(); yield return null;
            SaveWaveformEvidence("pwm-zoom.png");
            Assert.That(scope.Frame.Channels[0].DrawFundamental, Is.False);
            var frozen = scope.Frame.Channels[0].PwmIntervals.ToArray(); var labels = scope.Frame.DescribeChannel(0);
            scope.ToggleRunning(); scope.SetWaveform(0, OscilloscopeWaveform.Overlay);
            scope.Refresh(graph.Solve(.017f), true); properties.Refresh();
            CollectionAssert.AreEqual(frozen, scope.Frame.Channels[0].PwmIntervals);
            Assert.That(scope.Frame.DescribeChannel(0), Is.EqualTo(labels)); Assert.That(dropdown.interactable, Is.False);
            scope.ToggleRunning(); scope.SetCoupling(0, OscilloscopeCoupling.AC); Assert.That(scope.Frame.Channels[0].Signal.Dc, Is.Zero);
            simulation.ResetTraining(); Assert.That(scope.Frame.Channels[0].Waveform, Is.EqualTo(OscilloscopeWaveform.Overlay));
        }

        private void SaveWaveformEvidence(string name)
        {
            if (SystemInfo.graphicsDeviceType == UnityEngine.Rendering.GraphicsDeviceType.Null) return;
            var target = new RenderTexture(1920, 1080, 24); var texture = new Texture2D(1920, 1080, TextureFormat.RGB24, false);
            var canvas = properties.Panel.GetComponentInParent<Canvas>(); var oldTarget = camera.targetTexture; var oldActive = RenderTexture.active;
            var oldMode = canvas.renderMode; var oldCamera = canvas.worldCamera; var oldDistance = canvas.planeDistance;
            try
            {
                camera.targetTexture = target; canvas.renderMode = RenderMode.ScreenSpaceCamera; canvas.worldCamera = camera; canvas.planeDistance = .1f;
                Canvas.ForceUpdateCanvases(); camera.Render(); RenderTexture.active = target;
                texture.ReadPixels(new Rect(0, 0, 1920, 1080), 0, 0); texture.Apply();
                var directory = Path.GetFullPath(Path.Combine(Application.dataPath, "../Logs/InverterWaveform")); Directory.CreateDirectory(directory);
                File.WriteAllBytes(Path.Combine(directory, name), texture.EncodeToPNG());
            }
            finally { camera.targetTexture = oldTarget; RenderTexture.active = oldActive; canvas.renderMode = oldMode; canvas.worldCamera = oldCamera; canvas.planeDistance = oldDistance; Object.Destroy(texture); Object.Destroy(target); }
        }

        [UnityTest]
        public IEnumerator CaptureOscilloscopeEvidence()
        {
            if (SystemInfo.graphicsDeviceType == UnityEngine.Rendering.GraphicsDeviceType.Null)
                Assert.Ignore("Visual evidence requires a graphics device.");
            simulation.enabled = false;
            camera.GetComponent<TrainingCameraController>().enabled = false;
            camera.GetComponent<TrainingCameraController>().SetFaultView();
            simulation.SelectInstrument(InstrumentKind.Oscilloscope);
            simulation.PanelPower.StartForAssessment();
            var snapshot = simulation.Graph.Solve(0);
            var ports = Object.FindObjectsOfType<ElectricalPortView>().Where(p => p.IsVisible).ToArray();
            var potentials = new[] { ElectricalPotential.PhaseL1, ElectricalPotential.Neutral, ElectricalPotential.DcPositive24, ElectricalPotential.DcNegative };
            for (var i = 0; i < 4; i++)
            {
                scope.PickUpProbe(i);
                Assert.That(scope.TryAttach(ports.First(p => snapshot.GetPotential(p.QualifiedPort) == potentials[i]), camera), Is.True);
            }
            scope.Refresh(snapshot, true);
            properties.Refresh();
            yield return null;
            var target = new RenderTexture(1920, 1080, 24);
            var texture = new Texture2D(1920, 1080, TextureFormat.RGB24, false);
            var canvas = properties.Panel.GetComponentInParent<Canvas>();
            var oldTarget = camera.targetTexture; var oldActive = RenderTexture.active;
            var oldMode = canvas.renderMode; var oldCamera = canvas.worldCamera; var oldDistance = canvas.planeDistance;
            try
            {
                camera.targetTexture = target;
                canvas.renderMode = RenderMode.ScreenSpaceCamera; canvas.worldCamera = camera; canvas.planeDistance = .1f;
                Canvas.ForceUpdateCanvases();
                camera.Render(); RenderTexture.active = target;
                texture.ReadPixels(new Rect(0, 0, 1920, 1080), 0, 0); texture.Apply();
                var directory = Path.GetFullPath(Path.Combine(Application.dataPath, "../Logs/Oscilloscope"));
                Directory.CreateDirectory(directory);
                File.WriteAllBytes(Path.Combine(directory, "preview.png"), texture.EncodeToPNG());
            }
            finally
            {
                camera.targetTexture = oldTarget; RenderTexture.active = oldActive;
                canvas.renderMode = oldMode; canvas.worldCamera = oldCamera; canvas.planeDistance = oldDistance;
                Canvas.ForceUpdateCanvases();
                Object.Destroy(texture); Object.Destroy(target);
            }
        }
    }
}
