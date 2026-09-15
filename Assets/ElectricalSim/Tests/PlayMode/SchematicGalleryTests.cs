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
    public sealed class SchematicGalleryTests
    {
        private SimulationController controller;
        private SchematicGalleryPresenter gallery;
        private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            SceneManager.LoadScene("ElectricalTraining");
            yield return null;
            yield return null;
            controller = Object.FindObjectOfType<SimulationController>();
            gallery = controller.SchematicGallery;
        }

        [UnityTest]
        public IEnumerator PagesCycleInBothDirectionsAndSurviveModesAndCollapse()
        {
            Assert.That(gallery.CurrentIndex, Is.Zero);
            Assert.That(gallery.Caption, Does.StartWith("01/06"));
            Click("PreviousSchematic");
            Assert.That(gallery.Caption, Does.StartWith("06/06"));
            Click("NextSchematic");
            for (var i = 0; i < 6; i++)
            {
                Assert.That(gallery.CurrentIndex, Is.EqualTo(i));
                Assert.That(gallery.Preview.sprite, Is.SameAs(SchematicCatalog.Load().Pages[i].Sprite));
                Click("NextSchematic");
            }
            Click("NextSchematic");
            foreach (SimulationMode mode in System.Enum.GetValues(typeof(SimulationMode)))
            {
                controller.SetMode(mode);
                Assert.That(gallery.CurrentIndex, Is.EqualTo(1));
            }
            Click("TaskPanelSlideHandle");
            yield return new WaitForSecondsRealtime(0.25f);
            Click("TaskPanelSlideHandle");
            yield return new WaitForSecondsRealtime(0.25f);
            Assert.That(gallery.CurrentIndex, Is.EqualTo(1));
            SceneManager.LoadScene("ElectricalTraining");
            yield return null;
            yield return null;
            Assert.That(Object.FindObjectOfType<SchematicGalleryPresenter>().CurrentIndex, Is.Zero);
        }

        [UnityTest]
        public IEnumerator ModalButtonsAndCancelKeepPendingWiringAndCameraIntact()
        {
            controller.SetMode(SimulationMode.Wiring);
            var port = Object.FindObjectsOfType<ElectricalPortView>().First(p => p.QualifiedPort == "FR.T1");
            Invoke("BeginWireRoute", port);
            var points = (List<Vector3>)typeof(SimulationController).GetField("pendingWirePoints", Private).GetValue(controller);
            points.Add(port.transform.position + Vector3.up * 0.1f);
            var savedPoints = points.ToArray();
            var camera = Camera.main;
            var position = camera.transform.position;
            var rotation = camera.transform.rotation;
            var wireCount = controller.Graph.Wires.Count;
            Click("SchematicFrame");
            yield return null;
            Assert.That(gallery.IsViewerOpen, Is.True);
            Assert.That(controller.IsInteractionBlocked, Is.True);
            Assert.That(camera.GetComponent<TrainingCameraController>().SchematicInputBlocked, Is.True);
            Click("ZoomIn");
            Assert.That(gallery.ZoomViewport.Zoom, Is.GreaterThan(1));
            Click("ViewerNext");
            Assert.That(gallery.CurrentIndex, Is.EqualTo(1));
            Assert.That(gallery.ZoomViewport.Zoom, Is.EqualTo(1));
            Assert.That(gallery.ZoomViewport.Diagram.sprite, Is.SameAs(gallery.Preview.sprite));
            Invoke("HandleWiringPointerDown", camera, new Vector2(Screen.width * 0.8f, Screen.height * 0.5f));
            Assert.That(points, Is.EqualTo(savedPoints));
            Assert.That(controller.Graph.Wires.Count, Is.EqualTo(wireCount));
            ExecuteEvents.Execute(gallery.gameObject, new BaseEventData(EventSystem.current), ExecuteEvents.cancelHandler);
            Assert.That(gallery.IsViewerOpen, Is.False);
            Assert.That(controller.IsInteractionBlocked, Is.True, "关闭的同一帧必须消费 Esc/鼠标输入");
            yield return null;
            Assert.That(controller.IsInteractionBlocked, Is.False);
            Assert.That(controller.Mode, Is.EqualTo(SimulationMode.Wiring));
            Assert.That(controller.IsRoutingWire, Is.True);
            Assert.That(points, Is.EqualTo(savedPoints));
            Assert.That(camera.transform.position, Is.EqualTo(position));
            Assert.That(camera.transform.rotation, Is.EqualTo(rotation));
            Click("EnlargeSchematic");
            yield return null;
            Click("CloseSchematicViewer");
            yield return null;
            Assert.That(gallery.IsViewerOpen, Is.False);
            Assert.That(controller.IsRoutingWire, Is.True);
        }

        [UnityTest]
        public IEnumerator WheelZoomUsesPointerAndDraggingClampsAtImageEdges()
        {
            gallery.OpenViewer();
            yield return null;
            var zoom = gallery.ZoomViewport;
            var focus = new Vector2(35, 20);
            var screen = RectTransformUtility.WorldToScreenPoint(null, zoom.Viewport.TransformPoint(focus));
            zoom.OnScroll(new PointerEventData(EventSystem.current) { position = screen, scrollDelta = new Vector2(0, 3) });
            var expected = focus * (1 - zoom.Zoom);
            Assert.That(Vector2.Distance(zoom.Diagram.rectTransform.anchoredPosition, expected), Is.LessThan(0.1f));
            zoom.SetZoom(20, Vector2.zero);
            Assert.That(zoom.Zoom, Is.EqualTo(4));
            var drag = new PointerEventData(EventSystem.current) { position = screen, button = PointerEventData.InputButton.Left };
            zoom.OnBeginDrag(drag);
            drag.position += new Vector2(100000, -100000);
            zoom.OnDrag(drag);
            var limit = Vector2.Max(Vector2.zero, (zoom.Diagram.rectTransform.sizeDelta - zoom.Viewport.rect.size) * 0.5f);
            Assert.That(zoom.Diagram.rectTransform.anchoredPosition.x, Is.EqualTo(limit.x).Within(0.1f));
            Assert.That(zoom.Diagram.rectTransform.anchoredPosition.y, Is.EqualTo(-limit.y).Within(0.1f));
            zoom.SetZoom(-5, Vector2.zero);
            Assert.That(zoom.Zoom, Is.EqualTo(1));
            Assert.That(zoom.Diagram.rectTransform.anchoredPosition, Is.EqualTo(Vector2.zero));
            zoom.ZoomIn();
            Click("FitSchematic");
            Assert.That(zoom.Zoom, Is.EqualTo(1));
            gallery.CloseViewer();
            gallery.OpenViewer();
            Assert.That(zoom.Zoom, Is.EqualTo(1));
        }

        [UnityTest]
        public IEnumerator ViewerFitsResolutionsAndResetsZoomOnResize()
        {
            var camera = Camera.main;
            var canvas = gallery.GetComponentInParent<Canvas>();
            var mode = canvas.renderMode;
            var previousCamera = canvas.worldCamera;
            var distance = canvas.planeDistance;
            var oldTarget = camera.targetTexture;
            var oldActive = RenderTexture.active;
            try
            {
                canvas.renderMode = RenderMode.ScreenSpaceCamera;
                canvas.worldCamera = camera;
                canvas.planeDistance = camera.nearClipPlane + 0.05f;
                foreach (var size in new[] { new Vector2Int(1920, 1080), new Vector2Int(1280, 720), new Vector2Int(1600, 1000) })
                {
                    var target = RenderTexture.GetTemporary(size.x, size.y, 24);
                    try
                    {
                        camera.targetTexture = target;
                        Canvas.ForceUpdateCanvases();
                        gallery.OpenViewer();
                        Canvas.ForceUpdateCanvases();
                        yield return null;
                        Assert.That(gallery.ZoomViewport.Zoom, Is.EqualTo(1), "窗口尺寸改变后恢复完整适配");
                        var dialog = gallery.ZoomViewport.transform.parent.GetComponent<RectTransform>();
                        var box = ScreenRect(dialog, camera);
                        Assert.That(box.xMin, Is.EqualTo(size.x * 0.05f).Within(2));
                        Assert.That(box.yMin, Is.EqualTo(size.y * 0.05f).Within(2));
                        Assert.That(box.width, Is.EqualTo(size.x * 0.9f).Within(2));
                        Assert.That(box.height, Is.EqualTo(size.y * 0.9f).Within(2));
                        for (var page = 0; page < 6; page++)
                        {
                            var image = ScreenRect(gallery.ZoomViewport.Diagram.rectTransform, camera);
                            var view = ScreenRect(gallery.ZoomViewport.Viewport, camera);
                            Assert.That(image.xMin, Is.GreaterThanOrEqualTo(view.xMin - 1));
                            Assert.That(image.yMin, Is.GreaterThanOrEqualTo(view.yMin - 1));
                            Assert.That(image.xMax, Is.LessThanOrEqualTo(view.xMax + 1));
                            Assert.That(image.yMax, Is.LessThanOrEqualTo(view.yMax + 1));
                            Assert.That(image.width / image.height, Is.EqualTo(gallery.CurrentPage.Sprite.rect.width / gallery.CurrentPage.Sprite.rect.height).Within(0.001f));
                            Capture(camera, target, $"schematic-viewer-{size.x}-{page + 1:00}.png");
                            if (page == 3)
                            {
                                gallery.ZoomViewport.SetZoom(2, new Vector2(0, -100));
                                Capture(camera, target, $"schematic-zoom-{size.x}.png");
                            }
                            gallery.Next();
                        }
                        gallery.ZoomViewport.SetZoom(3, new Vector2(30, 20));
                    }
                    finally
                    {
                        camera.targetTexture = oldTarget;
                        RenderTexture.active = oldActive;
                        RenderTexture.ReleaseTemporary(target);
                    }
                }
            }
            finally
            {
                gallery.CloseViewer();
                canvas.renderMode = mode;
                canvas.worldCamera = previousCamera;
                canvas.planeDistance = distance;
            }
        }

        [UnityTest]
        public IEnumerator RemovedTaskAndExamControlsAreUnavailable()
        {
            var navigation = GameObject.Find("OriginalUI_TopNavigation");
            Assert.That(navigation.GetComponentsInChildren<Button>().Any(b => b.name == "submitBtn"), Is.False);
            Assert.That(navigation.GetComponentsInChildren<Text>().Any(t => t.name == "countdownText"), Is.False);
            var query = navigation.GetComponentsInChildren<Button>().Single(b => b.name == "scheduleBtn");
            Assert.That(query.GetComponentInChildren<Text>().text, Is.EqualTo("原理图"));
            var buttons = Object.FindObjectsOfType<Button>(true);
            Assert.That(buttons.Any(b => new[] { "Reference", "Submit", "PreviousTask", "NextTask" }.Contains(b.name)), Is.False);
            Assert.That(typeof(SimulationController).GetMethod("SubmitTask"), Is.Null);
            Assert.That(typeof(SimulationController).GetMethod("LoadReferenceWiring"), Is.Null);
            Assert.That(typeof(TrainingSceneBootstrap).GetMethod("BeginExam", Private), Is.Null);
            Assert.That(typeof(SimulationController).Assembly.GetType("ElectricalSim.OfflineExamController"), Is.Null);
            yield return null;
        }

        private void Invoke(string name, params object[] args)
            => typeof(SimulationController).GetMethod(name, Private).Invoke(controller, args);

        private static void Click(string name)
        {
            var target = Object.FindObjectsOfType<RectTransform>().Single(t => t.name == name);
            var canvas = target.GetComponentInParent<Canvas>();
            var camera = canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : canvas.worldCamera;
            var position = RectTransformUtility.WorldToScreenPoint(camera, target.position);
            var data = new PointerEventData(EventSystem.current) { position = position, button = PointerEventData.InputButton.Left };
            var hits = new List<RaycastResult>();
            EventSystem.current.RaycastAll(data, hits);
            Assert.That(hits, Is.Not.Empty, name);
            var clicked = ExecuteEvents.ExecuteHierarchy(hits[0].gameObject, data, ExecuteEvents.pointerClickHandler);
            Assert.That(clicked, Is.EqualTo(target.gameObject), name + " must receive the topmost click");
        }

        private static Rect ScreenRect(RectTransform rect, Camera camera)
        {
            var corners = new Vector3[4];
            rect.GetWorldCorners(corners);
            var min = camera.WorldToScreenPoint(corners[0]);
            var max = camera.WorldToScreenPoint(corners[2]);
            return Rect.MinMaxRect(min.x, min.y, max.x, max.y);
        }

        private static void Capture(Camera camera, RenderTexture target, string name)
        {
            camera.Render();
            RenderTexture.active = target;
            var texture = new Texture2D(target.width, target.height, TextureFormat.RGB24, false);
            try
            {
                texture.ReadPixels(new Rect(0, 0, target.width, target.height), 0, 0);
                texture.Apply();
                Directory.CreateDirectory("Build/Reports");
                File.WriteAllBytes("Build/Reports/" + name, texture.EncodeToPNG());
            }
            finally { Object.Destroy(texture); }
        }
    }
}
