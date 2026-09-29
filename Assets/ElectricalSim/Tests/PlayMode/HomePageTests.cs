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
    public sealed class HomePageTests
    {
        private const BindingFlags Private = BindingFlags.NonPublic | BindingFlags.Instance;
        private SimulationController controller;
        private HomePagePresenter home;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            SceneManager.LoadScene("ElectricalTraining");
            yield return null;
            yield return null;
            controller = Object.FindObjectOfType<SimulationController>();
            home = controller.HomePage;
        }

        [UnityTest]
        public IEnumerator HomeNavigationShowsCompleteContentAndReturnsWithoutTogglingGallery()
        {
            Assert.That(home.IsOpen, Is.False);
            var gallery = controller.SchematicGallery.GetComponent<RectTransform>();
            var originalPosition = gallery.anchoredPosition;
            Click("homeBtn");
            yield return null;
            Assert.That(home.IsOpen, Is.True);
            Assert.That(gallery.anchoredPosition, Is.EqualTo(originalPosition));
            Assert.That(Text("AuthorCardName"), Is.EqualTo("作者：汪成康"));
            Assert.That(Text("AuthorCardEmail"), Is.EqualTo("邮箱：1873408329@qq.com"));
            Assert.That(Text("TeacherCardName"), Is.EqualTo("指导老师：李宁"));
            Assert.That(Text("TeacherCardEmail"), Is.EqualTo("邮箱：nli1161@163.com"));
            Assert.That(Text("AnnouncementText"), Does.StartWith("一个无聊的人所作的作品；"));
            Assert.That(Text("AnnouncementText"), Does.Contain("内测期间都有40多个"));
            Assert.That(Text("AnnouncementText"), Does.EndWith("祝你生活愉快"));
            var page = GameObject.Find("HomePage");
            home.Open();
            Assert.That(Object.FindObjectsOfType<RectTransform>().Count(r => r.name == "HomePage"), Is.EqualTo(1));
            Assert.That(GameObject.Find("HomePage"), Is.SameAs(page));
            Click("ReturnToTraining");
            yield return null;
            Assert.That(home.IsOpen, Is.False);
            Assert.That(gallery.anchoredPosition, Is.EqualTo(originalPosition));
            Click("scheduleBtn");
            yield return new WaitForSecondsRealtime(0.3f);
            Assert.That(gallery.anchoredPosition, Is.Not.EqualTo(originalPosition));
            Assert.That(home.IsOpen, Is.False);
            Click("scheduleBtn");
            yield return new WaitForSecondsRealtime(0.3f);
            Assert.That(Vector2.Distance(gallery.anchoredPosition, originalPosition), Is.LessThan(0.1f));
        }

        [UnityTest]
        public IEnumerator CancelPreservesWireDraftCameraAndModeAndConsumesClosingFrame()
        {
            controller.SetMode(SimulationMode.Wiring);
            var wire = controller.Graph.AddWire("QF.T1", "KM1.L1", Color.blue);
            var port = Object.FindObjectsOfType<ElectricalPortView>().First(p => p.QualifiedPort == "FR.T1");
            Invoke("BeginWireRoute", port);
            var points = (List<Vector3>)typeof(SimulationController).GetField("pendingWirePoints", Private).GetValue(controller);
            points.Add(port.transform.position + Vector3.up * 0.1f);
            var expectedPoints = points.ToArray();
            var camera = Camera.main;
            var position = camera.transform.position;
            var rotation = camera.transform.rotation;
            home.Open();
            yield return null;
            Assert.That(controller.IsInteractionBlocked, Is.True);
            Assert.That(camera.GetComponent<TrainingCameraController>().HomeInputBlocked, Is.True);
            var hits = Hits(new Vector2(Screen.width * 0.9f, Screen.height * 0.5f));
            Assert.That(hits[0].gameObject.transform.IsChildOf(GameObject.Find("HomePage").transform), Is.True);
            Invoke("HandleWiringPointerDown", camera, new Vector2(Screen.width * 0.9f, Screen.height * 0.5f));
            Invoke("HandleScenePointerDown", camera, new Vector2(Screen.width * 0.9f, Screen.height * 0.5f));
            Assert.That(points, Is.EqualTo(expectedPoints));
            home.OnCancel(new BaseEventData(EventSystem.current));
            Assert.That(home.IsOpen, Is.False);
            Assert.That(controller.IsInteractionBlocked, Is.True, "Esc/点击返回的同帧不能传递到实训");
            yield return null;
            Assert.That(controller.IsInteractionBlocked, Is.False);
            Assert.That(camera.GetComponent<TrainingCameraController>().HomeInputBlocked, Is.False);
            Assert.That(controller.Mode, Is.EqualTo(SimulationMode.Wiring));
            Assert.That(controller.IsRoutingWire, Is.True);
            Assert.That(points, Is.EqualTo(expectedPoints));
            Assert.That(controller.Graph.Wires.Single(), Is.SameAs(wire));
            Assert.That(camera.transform.position, Is.EqualTo(position));
            Assert.That(camera.transform.rotation, Is.EqualTo(rotation));
        }

        [UnityTest]
        public IEnumerator SimulationContinuesAndOtherInputBlocksRemainIndependent()
        {
            controller.SetMode(SimulationMode.Simulate);
            var snapshotField = typeof(SimulationController).GetField("lastSnapshot", Private);
            var snapshot = snapshotField.GetValue(controller);
            var scale = Time.timeScale;
            home.Open();
            yield return null;
            yield return null;
            Assert.That(snapshotField.GetValue(controller), Is.Not.SameAs(snapshot), "仿真应继续产生快照");
            Assert.That(controller.Mode, Is.EqualTo(SimulationMode.Simulate));
            Assert.That(Time.timeScale, Is.EqualTo(scale));
            var camera = Camera.main.GetComponent<TrainingCameraController>();
            controller.SchematicGallery.OpenViewer();
            home.Close();
            yield return null;
            Assert.That(controller.IsInteractionBlocked, Is.True);
            Assert.That(camera.HomeInputBlocked, Is.False);
            Assert.That(camera.SchematicInputBlocked, Is.True);
            home.Open();
            controller.SchematicGallery.CloseViewer();
            yield return null;
            Assert.That(controller.IsInteractionBlocked, Is.True);
            Assert.That(camera.HomeInputBlocked, Is.True);
            Assert.That(camera.SchematicInputBlocked, Is.False);
            Invoke("BeginFileOperation");
            home.Close();
            yield return null;
            Assert.That(controller.IsInteractionBlocked, Is.True);
            Assert.That(camera.InputBlocked, Is.True);
            home.Open();
            Invoke("EndFileOperation");
            yield return null;
            Assert.That(controller.IsInteractionBlocked, Is.True);
            Assert.That(camera.InputBlocked, Is.False);
            Assert.That(camera.HomeInputBlocked, Is.True);
            home.Close();
            yield return null;
            Assert.That(controller.IsInteractionBlocked, Is.False);
        }

        [UnityTest]
        public IEnumerator PageFitsTargetResolutionsAndScrollReachesFinalLine()
        {
            var camera = Camera.main;
            var canvas = home.GetComponent<Canvas>();
            var oldMode = canvas.renderMode;
            var oldCamera = canvas.worldCamera;
            var oldDistance = canvas.planeDistance;
            var oldTarget = camera.targetTexture;
            var oldActive = RenderTexture.active;
            var testedOverflow = false;
            try
            {
                canvas.renderMode = RenderMode.ScreenSpaceCamera;
                canvas.worldCamera = camera;
                canvas.planeDistance = camera.nearClipPlane + 0.05f;
                foreach (var size in new[] { new Vector2Int(1920, 1080), new Vector2Int(1600, 900), new Vector2Int(1280, 720), new Vector2Int(1280, 600) })
                {
                    var target = RenderTexture.GetTemporary(size.x, size.y, 24);
                    try
                    {
                        camera.targetTexture = target;
                        home.Open();
                        Canvas.ForceUpdateCanvases();
                        yield return null;
                        var scroll = GameObject.Find("AnnouncementScroll").GetComponent<ScrollRect>();
                        var text = GameObject.Find("AnnouncementText").GetComponent<Text>();
                        Assert.That(scroll.verticalNormalizedPosition, Is.EqualTo(1).Within(0.001f));
                        foreach (var name in new[] { "AuthorCard", "TeacherCard", "AnnouncementCard", "ReturnToTraining" })
                        {
                            var box = ScreenRect(GameObject.Find(name).GetComponent<RectTransform>(), camera);
                            Assert.That(box.xMin, Is.GreaterThanOrEqualTo(0), name);
                            Assert.That(box.yMin, Is.GreaterThanOrEqualTo(0), name);
                            Assert.That(box.xMax, Is.LessThanOrEqualTo(size.x), name);
                            Assert.That(box.yMax, Is.LessThanOrEqualTo(size.y), name);
                        }
                        foreach (var name in new[] { "AuthorCardName", "AuthorCardEmail", "TeacherCardName", "TeacherCardEmail" })
                        {
                            var label = GameObject.Find(name).GetComponent<Text>();
                            Assert.That(label.preferredHeight, Is.LessThanOrEqualTo(label.rectTransform.rect.height + 1), name);
                        }
                        Assert.That(text.rectTransform.rect.height, Is.EqualTo(text.preferredHeight).Within(1));
                        Capture(camera, target, $"home-{size.x}x{size.y}-top.png");
                        if (scroll.content.rect.height > scroll.viewport.rect.height)
                        {
                            testedOverflow = true;
                            Assert.That(scroll.verticalScrollbar.gameObject.activeSelf, Is.True);
                            var pointer = new PointerEventData(EventSystem.current) { scrollDelta = new Vector2(0, -3) };
                            scroll.OnScroll(pointer);
                            Assert.That(scroll.verticalNormalizedPosition, Is.LessThan(1));
                            scroll.verticalScrollbar.value = 0;
                            Canvas.ForceUpdateCanvases();
                            var contentBox = ScreenRect(scroll.content, camera);
                            var viewportBox = ScreenRect(scroll.viewport, camera);
                            Assert.That(contentBox.yMin, Is.EqualTo(viewportBox.yMin).Within(2), "末行必须可以完整滚入视口");
                            Assert.That(ScreenRect(text.rectTransform, camera).yMin,
                                Is.GreaterThan(viewportBox.yMin + 8), "末行下方留白，避免动态字体字形被遮罩裁切");
                            Capture(camera, target, $"home-{size.x}x{size.y}-bottom.png");
                        }
                        else
                        {
                            Assert.That(scroll.verticalScrollbar.gameObject.activeSelf, Is.False);
                            Assert.That(ScreenRect(scroll.content, camera).yMin,
                                Is.GreaterThanOrEqualTo(ScreenRect(scroll.viewport, camera).yMin - 1), "无需滚动时末行应直接可见");
                        }
                        home.Close();
                        home.Open();
                        Assert.That(scroll.verticalNormalizedPosition, Is.EqualTo(1).Within(0.001f));
                        home.Close();
                    }
                    finally
                    {
                        camera.targetTexture = oldTarget;
                        RenderTexture.active = oldActive;
                        RenderTexture.ReleaseTemporary(target);
                    }
                }
                Assert.That(testedOverflow, Is.True, "窄高比窗口应覆盖公告滚动路径");
            }
            finally
            {
                home.Close();
                canvas.renderMode = oldMode;
                canvas.worldCamera = oldCamera;
                canvas.planeDistance = oldDistance;
            }
        }

        private static string Text(string name) => GameObject.Find(name).GetComponent<Text>().text;

        private void Invoke(string name, params object[] args)
            => typeof(SimulationController).GetMethod(name, Private).Invoke(controller, args);

        private static List<RaycastResult> Hits(Vector2 point)
        {
            var hits = new List<RaycastResult>();
            EventSystem.current.RaycastAll(new PointerEventData(EventSystem.current) { position = point }, hits);
            Assert.That(hits, Is.Not.Empty);
            return hits;
        }

        private static void Click(string name)
        {
            var rect = Object.FindObjectsOfType<RectTransform>().Single(t => t.name == name);
            var canvas = rect.GetComponentInParent<Canvas>();
            var camera = canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : canvas.worldCamera;
            var position = RectTransformUtility.WorldToScreenPoint(camera, rect.position);
            var data = new PointerEventData(EventSystem.current) { position = position, button = PointerEventData.InputButton.Left };
            var clicked = ExecuteEvents.ExecuteHierarchy(Hits(position)[0].gameObject, data, ExecuteEvents.pointerClickHandler);
            Assert.That(clicked, Is.EqualTo(rect.gameObject), name);
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
                Directory.CreateDirectory("Build/Reports/home-page");
                File.WriteAllBytes("Build/Reports/home-page/" + name, texture.EncodeToPNG());
            }
            finally { Object.Destroy(texture); }
        }
    }
}
