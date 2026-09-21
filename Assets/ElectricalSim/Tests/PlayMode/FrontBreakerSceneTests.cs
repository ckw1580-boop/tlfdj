using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UI;

namespace ElectricalSim.Tests
{
    public sealed class FrontBreakerSceneTests
    {
        private SimulationController controller;
        private FrontBreakerView[] breakers;
        private Vector3 outward;
        private static readonly string[] Terminals = { "N1", "L1", "L3", "L5", "N2", "L2", "L4", "L6" };

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            SceneManager.LoadScene("ElectricalTraining");
            yield return null; yield return null;
            controller = Object.FindObjectOfType<SimulationController>();
            breakers = controller.FrontBreakers.ToArray();
            Assert.That(breakers.Length, Is.EqualTo(3));
            outward = GameObject.Find("Front Independent Breakers").transform.forward;
        }

        [UnityTest]
        public IEnumerator LayoutKeepsRightTerminalsAndAll24PortsArePickable()
        {
            var environment = GameObject.Find("OriginalLabEnvironment").transform;
            var strip = environment.Find(TrainingSceneBootstrap.FrontBreakerStripPath);
            var modules = strip.Cast<Transform>().OrderByDescending(t => t.GetComponentInChildren<Renderer>(true).bounds.center.x).ToArray();
            var kept = false;
            foreach (var module in modules)
            {
                if (module.gameObject.activeSelf) kept = true;
                else Assert.That(kept, Is.False, "Only a continuous left section may be replaced");
            }
            Assert.That(modules.Any(t => !t.gameObject.activeSelf), Is.True);
            Assert.That(modules.Any(t => t.gameObject.activeSelf), Is.True);
            Assert.That(environment.Find("Bench/ElectricBench/mesh/model/H3C").gameObject.activeInHierarchy, Is.True);
            Assert.That(controller.CabinetBreakers.Count, Is.EqualTo(2));
            Assert.That(breakers.Select(v => v.Runtime.DeviceId), Is.EqualTo(new[] { "QFFRONT1", "QFFRONT2", "QFFRONT3" }));
            var ports = breakers.SelectMany(v => v.Device.Ports).ToArray();
            Assert.That(ports.Select(p => p.QualifiedPort).Distinct().Count(), Is.EqualTo(24));
            controller.SetWireStyle(Color.red, .01f, "ElectricalWire");
            controller.SetMode(SimulationMode.Wiring);
            yield return null;
            foreach (var breaker in breakers)
            {
                Assert.That(breaker.Device.Ports.Select(p => p.PortName), Is.EquivalentTo(Terminals));
                Assert.That(breaker.Runtime.FixedLinks, Is.Empty);
                foreach (var port in breaker.Device.Ports)
                {
                    Assert.That(port.IsVisible, Is.True);
                    Assert.That(port.CurrentAnchor.IsChildOf(breaker.transform), Is.True);
                    Aim(port.CurrentAnchorPosition);
                    Physics.SyncTransforms();
                    Assert.That(Physics.Raycast(Camera.main.ScreenPointToRay(Camera.main.WorldToScreenPoint(port.CurrentAnchorPosition)), out var hit, 10f), Is.True);
                    Assert.That(hit.collider.GetComponent<ElectricalPortView>(), Is.SameAs(port),
                        port.QualifiedPort + " blocked by " + hit.collider.name);
                }
            }
            controller.SetWireStyle(Color.red, .01f, "JumperLine");
            yield return null;
            Assert.That(ports.All(p => !p.IsVisible), Is.True);
            controller.SetMode(SimulationMode.Simulate);
            yield return null;
            Assert.That(ports.All(p => !p.IsVisible), Is.True);
        }

        [UnityTest]
        public IEnumerator BodyAndHandleClicksKeepAnimationPropertiesAndIndependentStateInSync()
        {
            var main = Object.FindObjectsOfType<ElectricalDeviceView>().Single(v => v.Runtime.DeviceId == "QF").Runtime;
            foreach (var breaker in breakers)
            {
                controller.SetMode(SimulationMode.View);
                yield return null;
                Click(FindPointer(breaker, false));
                Assert.That(controller.SelectedFrontBreaker, Is.SameAs(breaker));
                Assert.That(breaker.Runtime.IsClosed, Is.True);
                Assert.That(controller.FrontBreakerProperties.DisplayedText, Does.Contain(breaker.Runtime.DeviceId).And.Contain("四极"));
                controller.SelectFrontBreaker(null);
                controller.SetMode(SimulationMode.Simulate);
                yield return null;
                var closedRotation = breaker.Animation.Handle.localRotation;
                Click(FindPointer(breaker, true));
                Assert.That(breaker.Runtime.IsClosed, Is.False);
                Assert.That(main.IsClosed, Is.True);
                Assert.That(breakers.Where(v => v != breaker).All(v => v.Runtime.IsClosed), Is.True);
                yield return new WaitForSecondsRealtime(breaker.Animation.AnimationDuration + .05f);
                Assert.That(Quaternion.Angle(closedRotation, breaker.Animation.Handle.localRotation), Is.GreaterThan(40));
                Click(FindPointer(breaker, false));
                yield return null;
                var panel = controller.FrontBreakerProperties;
                Assert.That(panel.DisplayedText, Does.Contain("断开"));
                var close = panel.transform.Find("CloseBreaker").GetComponent<Button>();
                Assert.That(close.interactable, Is.True);
                close.onClick.Invoke();
                Assert.That(breaker.Runtime.IsClosed, Is.True);
                yield return new WaitForSecondsRealtime(breaker.Animation.AnimationDuration + .05f);
                Assert.That(Quaternion.Angle(closedRotation, breaker.Animation.Handle.localRotation), Is.LessThan(.01f));
                yield return null;
                Assert.That(panel.DisplayedText, Does.Contain("接通"));
                Assert.That(panel.transform.Find("Open").GetComponent<Button>().interactable, Is.True);
                controller.SelectFrontBreaker(null);
            }
        }

        [UnityTest]
        public IEnumerator ReverseResetModeAndModalGuardsRemainConsistent()
        {
            var breaker = breakers[0];
            var rotation = breaker.Animation.Handle.localRotation;
            Assert.That(controller.SetFrontBreakerClosed(breaker, false), Is.False);
            controller.SetMode(SimulationMode.Simulate);
            Assert.That(controller.SetFrontBreakerClosed(breaker, false), Is.True);
            yield return new WaitForSecondsRealtime(.05f);
            Assert.That(controller.SetFrontBreakerClosed(breaker, true), Is.True);
            yield return new WaitForSecondsRealtime(.25f);
            Assert.That(Quaternion.Angle(rotation, breaker.Animation.Handle.localRotation), Is.LessThan(.01f));
            controller.SetFrontBreakerClosed(breaker, false);
            controller.SetMode(SimulationMode.Wiring);
            Assert.That(breaker.Runtime.IsClosed, Is.False);
            Assert.That(controller.SetFrontBreakerClosed(breaker, true), Is.False);
            controller.SetMode(SimulationMode.Simulate);
            controller.SchematicGallery.OpenViewer();
            Assert.That(controller.SetFrontBreakerClosed(breaker, true), Is.False);
            controller.SchematicGallery.CloseViewer();
            yield return null; yield return null;
            Assert.That(controller.SetFrontBreakerClosed(breaker, true), Is.True);
            foreach (var mode in new[] { SimulationMode.Drag, SimulationMode.Fault })
            {
                controller.SetMode(mode);
                Assert.That(controller.SetFrontBreakerClosed(breaker, false), Is.True);
                Assert.That(controller.SetFrontBreakerClosed(breaker, true), Is.True);
            }
            controller.ResetTraining();
            Assert.That(breakers.All(v => v.Runtime.IsClosed && v.Animation.IsClosed), Is.True);
            Assert.That(Quaternion.Angle(rotation, breaker.Animation.Handle.localRotation), Is.LessThan(.01f));
        }

        [UnityTest]
        public IEnumerator AllNewTerminalsSaveAndLoadInAFreshScene()
        {
            var path = Path.GetFullPath("Build/Reports/front-breaker-roundtrip.cc3d");
            controller.SetWireStyle(Color.red, .01f, "ElectricalWire");
            foreach (var terminal in Terminals)
            {
                controller.Graph.AddWire("QFFRONT1." + terminal, "QFFRONT2." + terminal, Color.red, "ElectricalWire").FaultSide = false;
                controller.Graph.AddWire("QFFRONT2." + terminal, "QFFRONT3." + terminal, Color.blue, "ElectricalWire").FaultSide = false;
            }
            Assert.That(controller.SaveCc3dToPath(path), Is.EqualTo(WiringFileResult.Success), controller.LastFileError);
            SceneManager.LoadScene("ElectricalTraining");
            yield return null; yield return null;
            controller = Object.FindObjectOfType<SimulationController>();
            Assert.That(controller.OpenCc3dFromPath(path), Is.EqualTo(WiringFileResult.Success), controller.LastFileError);
            Assert.That(controller.Graph.Wires.Count, Is.EqualTo(16));
            Assert.That(controller.Graph.Wires.SelectMany(w => new[] { w.StartPort, w.EndPort }).Distinct().Count(), Is.EqualTo(24));
            Assert.That(controller.FrontBreakers.All(v => v.Runtime.IsClosed), Is.True);
        }

        [UnityTest]
        public IEnumerator CaptureFrontBreakerLayout()
        {
            if (SystemInfo.graphicsDeviceType == UnityEngine.Rendering.GraphicsDeviceType.Null)
                Assert.Ignore("Graphics device required for layout capture.");
            var target = new Vector3(0, 1.57f, -1.40f);
            Camera.main.transform.position = target + outward * .65f;
            Camera.main.transform.LookAt(target);
            yield return null;
            Capture("Build/Reports/front-breakers-layout.png");
            controller.SetWireStyle(Color.red, .01f, "ElectricalWire");
            controller.SetMode(SimulationMode.Wiring);
            yield return null;
            Capture("Build/Reports/front-breakers-terminals.png");
            controller.SetMode(SimulationMode.View);
            Camera.main.orthographic = true;
            Camera.main.orthographicSize = .11f;
            Capture("Build/Reports/front-breakers-closeup.png", 1600, 450);
            Camera.main.orthographic = false;
            controller.SetMode(SimulationMode.Simulate);
            controller.SelectFrontBreaker(breakers[0]);
            yield return null;
            var canvas = controller.FrontBreakerProperties.GetComponentInParent<Canvas>();
            var mode = canvas.renderMode;
            var camera = canvas.worldCamera;
            var distance = canvas.planeDistance;
            try
            {
                canvas.renderMode = RenderMode.ScreenSpaceCamera;
                canvas.worldCamera = Camera.main;
                canvas.planeDistance = Camera.main.nearClipPlane + .01f;
                Capture("Build/Reports/front-breakers-properties.png");
            }
            finally
            {
                canvas.renderMode = mode;
                canvas.worldCamera = camera;
                canvas.planeDistance = distance;
            }
        }

        private void Aim(Vector3 target)
        {
            Camera.main.transform.position = target + outward * .3f;
            Camera.main.transform.LookAt(target);
        }

        private Vector2 FindPointer(FrontBreakerView view, bool handle)
        {
            Aim(view.Animation.Handle.GetComponentInChildren<Renderer>().bounds.center);
            Physics.SyncTransforms(); Canvas.ForceUpdateCanvases();
            var cam = Camera.main;
            for (var y = .25f; y <= .75f; y += .0125f)
                for (var x = .3f; x <= .7f; x += .0125f)
                {
                    var pointer = (Vector2)cam.ViewportToScreenPoint(new Vector3(x, y));
                    if (!Physics.Raycast(cam.ScreenPointToRay(pointer), out var hit, 10f) ||
                        hit.collider.GetComponentInParent<FrontBreakerView>() != view || view.IsHandle(hit.collider) != handle) continue;
                    var ui = new List<RaycastResult>();
                    EventSystem.current.RaycastAll(new PointerEventData(EventSystem.current) { position = pointer }, ui);
                    if (ui.Count == 0) return pointer;
                }
            Assert.Fail(view.Runtime.DeviceId + (handle ? " handle" : " body") + " has no unobstructed clickable surface");
            return default;
        }

        private void Click(Vector2 point) => typeof(SimulationController).GetMethod("HandleScenePointerDown",
            BindingFlags.Instance | BindingFlags.NonPublic).Invoke(controller, new object[] { Camera.main, point });

        private static void Capture(string path, int width = 1920, int height = 1080)
        {
            var cam = Camera.main;
            var rt = new RenderTexture(width, height, 24);
            var previousTarget = cam.targetTexture; var previousActive = RenderTexture.active;
            var image = new Texture2D(width, height, TextureFormat.RGB24, false);
            try
            {
                cam.targetTexture = rt; Canvas.ForceUpdateCanvases(); cam.Render(); RenderTexture.active = rt;
                image.ReadPixels(new Rect(0, 0, width, height), 0, 0); image.Apply();
                File.WriteAllBytes(path, image.EncodeToPNG());
            }
            finally
            {
                cam.targetTexture = previousTarget; RenderTexture.active = previousActive;
                Object.Destroy(rt); Object.Destroy(image);
            }
        }
    }
}
