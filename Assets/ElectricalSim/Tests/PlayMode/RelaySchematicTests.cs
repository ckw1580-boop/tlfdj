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
using Object = UnityEngine.Object;

namespace ElectricalSim.Tests
{
    public sealed class RelaySchematicTests
    {
        private SimulationController controller;
        private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
        [UnitySetUp]
        public IEnumerator Setup()
        {
            SceneManager.LoadScene("ElectricalTraining"); yield return null; yield return null;
            controller = Object.FindObjectOfType<SimulationController>();
            controller.SetMode(SimulationMode.Wiring);
            Canvas.ForceUpdateCanvases(); Physics.SyncTransforms();
        }
        private object Invoke(string name, params object[] args)
            => typeof(SimulationController).GetMethod(name, Private).Invoke(controller, args);
        private T Field<T>(string name) => (T)typeof(SimulationController).GetField(name, Private).GetValue(controller);
        private void Click(Vector2 screenPoint) => Invoke("HandleWiringPointerDown", Camera.main, screenPoint);
        private Vector2 RelayPoint(IntermediateRelayView view)
        {
            var box = (BoxCollider)view.Picker;
            return Camera.main.WorldToScreenPoint(box.transform.TransformPoint(box.center + Vector3.forward * box.size.z * 0.5f));
        }

        [UnityTest]
        public IEnumerator AllSixBodiesOpenOneWindowAndModeChangesHideIt()
        {
            var window = controller.RelaySchematic;
            Assert.That(window.gameObject.activeSelf, Is.False);
            foreach (var relay in controller.RelayViews)
            {
                Click(RelayPoint(relay));
                Assert.That(controller.SchematicRelay, Is.SameAs(relay));
                Assert.That(window.Title, Is.EqualTo(relay.Runtime.DeviceId + " · 中间继电器原理图"));
                Assert.That(window.gameObject.activeSelf, Is.True);
                Click(RelayPoint(relay));
                Assert.That(controller.RelaySchematic, Is.SameAs(window));
                Assert.That(relay.Runtime.IsActive, Is.False);
            }
            Assert.That(Object.FindObjectsOfType<RelaySchematicPresenter>(true).Length, Is.EqualTo(1));
            Assert.That(window.Diagram.texture, Is.SameAs(Resources.Load<Texture2D>("RelaySchematic")));
            Assert.That(controller.SelectedRelay, Is.Null);
            foreach (var mode in new[] { SimulationMode.View, SimulationMode.Simulate, SimulationMode.Drag, SimulationMode.Fault })
            {
                controller.SetMode(mode);
                Assert.That(window.gameObject.activeSelf, Is.False);
                Assert.That(controller.SchematicRelay, Is.Null);
                controller.ShowRelaySchematic(controller.RelayViews[0]);
                Assert.That(window.gameObject.activeSelf, Is.False);
                controller.SetMode(SimulationMode.Wiring);
                controller.ShowRelaySchematic(controller.RelayViews[0]);
            }
            yield return null;
        }

        [UnityTest]
        public IEnumerator InspectingAndClosingWindowPreservesPendingRouteAndDoesNotClickThrough()
        {
            var relay = controller.RelayViews[0];
            var start = relay.Bindings["13"];
            Invoke("BeginWireRoute", start);
            // Add a real route point through the existing surface projection handler.
            Invoke("HandleWiringClick", null, Camera.main.ScreenPointToRay(new Vector2(Screen.width * 0.55f, Screen.height * 0.4f)));
            var pending = Field<List<Vector3>>("pendingWirePoints");
            Assert.That(pending.Count, Is.GreaterThan(0));
            var before = pending.ToArray();
            var savedWires = controller.Graph.Wires.Count;
            Click(RelayPoint(relay));
            Assert.That(controller.SchematicRelay, Is.SameAs(relay));
            Assert.That(Field<ElectricalPortView>("selectedPort"), Is.SameAs(start));
            Assert.That(pending, Is.EqualTo(before));
            Assert.That(controller.Graph.Wires.Count, Is.EqualTo(savedWires));
            Canvas.ForceUpdateCanvases();
            var diagramPoint = RectTransformUtility.WorldToScreenPoint(null, controller.RelaySchematic.Diagram.rectTransform.position);
            Click(diagramPoint);
            Assert.That(pending, Is.EqualTo(before), "点击图纸不能穿透后添加路径点");
            var button = controller.RelaySchematic.GetComponentInChildren<Button>();
            var closePoint = RectTransformUtility.WorldToScreenPoint(null, button.transform.position);
            Click(closePoint);
            Assert.That(pending, Is.EqualTo(before));
            ExecuteEvents.Execute(button.gameObject, new PointerEventData(EventSystem.current) { button = PointerEventData.InputButton.Left }, ExecuteEvents.pointerClickHandler);
            Assert.That(controller.SchematicRelay, Is.Null);
            Assert.That(controller.IsRoutingWire, Is.True);
            Assert.That(pending, Is.EqualTo(before));
            Click(RelayPoint(relay));
            var end = relay.Bindings["14"];
            Click(Camera.main.WorldToScreenPoint(end.CurrentAnchorPosition));
            Assert.That(controller.IsRoutingWire, Is.False, "窗口外端子必须仍可完成接线");
            var wire = controller.Graph.Wires.Single();
            Assert.That(wire.StartPort, Is.EqualTo(start.QualifiedPort));
            Assert.That(wire.EndPort, Is.EqualTo(end.QualifiedPort));
            Assert.That(wire.Points, Is.EqualTo(before));
            Assert.That(controller.SchematicRelay, Is.SameAs(relay));
            yield return null;
        }

        [UnityTest]
        public IEnumerator BlankSpaceKeepsWindowOpenAndActiveNodeDragHasPriority()
        {
            var relay = controller.RelayViews[0];
            Click(RelayPoint(relay));
            Click(new Vector2(Screen.width * 0.6f, Screen.height * 0.3f));
            Assert.That(controller.SchematicRelay, Is.SameAs(relay));
            controller.HideRelaySchematic();
            var dragging = typeof(SimulationController).GetField("draggingWirePoint", Private);
            dragging.SetValue(controller, true);
            Click(RelayPoint(relay));
            Assert.That(controller.SchematicRelay, Is.Null);
            dragging.SetValue(controller, false);
            Click(RelayPoint(relay));
            Assert.That(controller.SchematicRelay, Is.SameAs(relay));
            yield return null;
        }

        [UnityTest]
        public IEnumerator WindowFitsBothResolutionsAndPreservesImageAspect()
        {
            controller.ShowRelaySchematic(controller.RelayViews[0]);
            yield return null;
            CaptureAndCheck(controller, 1920, 1080);
            CaptureAndCheck(controller, 1280, 720);
        }

        internal static void CaptureAndCheck(SimulationController controller, int width, int height, string prefix = "relay-schematic-")
        {
            var window = controller.RelaySchematic;
            var camera = Camera.main;
            var canvas = window.GetComponentInParent<Canvas>();
            var previousMode = canvas.renderMode; var previousCamera = canvas.worldCamera; var previousDistance = canvas.planeDistance;
            var previousTarget = camera.targetTexture; var previousActive = RenderTexture.active;
            var target = new RenderTexture(width, height, 24);
            var texture = new Texture2D(width, height, TextureFormat.RGB24, false);
            try
            {
                camera.targetTexture = target;
                canvas.renderMode = RenderMode.ScreenSpaceCamera; canvas.worldCamera = camera;
                // Keep the capture plane beyond the near clip plane. Coincident planes
                // can round to a negative ray distance when viewing the cabinet rear.
                canvas.planeDistance = camera.nearClipPlane + 0.05f;
                Canvas.ForceUpdateCanvases();
                var rect = (RectTransform)window.transform;
                var corners = new Vector3[4]; rect.GetWorldCorners(corners);
                var min = camera.WorldToScreenPoint(corners[0]); var max = camera.WorldToScreenPoint(corners[2]);
                var scale = width / 1920f;
                Assert.That(min.x, Is.EqualTo(16 * scale).Within(2f));
                Assert.That(max.y, Is.EqualTo(height - 140 * scale).Within(2f));
                Assert.That(max.x - min.x, Is.EqualTo(280 * scale).Within(2f));
                Assert.That(max.y - min.y, Is.EqualTo(240 * scale).Within(2f));
                Assert.That(min.y, Is.GreaterThan(0)); Assert.That(max.x, Is.LessThan(width));
                var title = window.transform.Find("Title").GetComponent<Text>();
                var close = window.GetComponentInChildren<Button>();
                Assert.That(title.fontSize, Is.EqualTo(14));
                Assert.That(close.GetComponentInChildren<Text>().fontSize, Is.EqualTo(12));
                Assert.That(title.preferredWidth, Is.LessThanOrEqualTo(title.rectTransform.rect.width), "标题不能被裁切");
                var frame = window.transform.Find("DiagramFrame").GetComponent<RectTransform>();
                Assert.That(frame.sizeDelta, Is.EqualTo(new Vector2(264, 196)));
                Assert.That(frame.GetComponent<Image>().color, Is.EqualTo(Color.white));
                var imageRect = window.Diagram.rectTransform.rect;
                Assert.That(imageRect.width / imageRect.height,
                    Is.EqualTo((float)window.Diagram.texture.width / window.Diagram.texture.height).Within(0.001f));
                // A newly opened window has no graphic depth until its first render.
                camera.Render();
                var uiHits = new List<RaycastResult>();
                EventSystem.current.RaycastAll(new PointerEventData(EventSystem.current) { position = (min + max) * 0.5f }, uiHits);
                Assert.That(uiHits.Any(h => h.gameObject.transform.IsChildOf(window.transform)), Is.True,
                    "Window raycast: active=" + window.gameObject.activeInHierarchy + ", depth=" + window.GetComponent<Image>().depth +
                    ", culled=" + window.GetComponent<Image>().canvasRenderer.cull + ", resolution=" + width + "x" + height);
                uiHits.Clear();
                EventSystem.current.RaycastAll(new PointerEventData(EventSystem.current) { position = new Vector2(max.x + 10, (min.y + max.y) / 2) }, uiHits);
                Assert.That(uiHits.Any(h => h.gameObject.transform.IsChildOf(window.transform)), Is.False);
                RenderTexture.active = target;
                texture.ReadPixels(new Rect(0, 0, width, height), 0, 0); texture.Apply();
                Directory.CreateDirectory("Build/Reports");
                File.WriteAllBytes("Build/Reports/" + prefix + width + ".png", texture.EncodeToPNG());
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
