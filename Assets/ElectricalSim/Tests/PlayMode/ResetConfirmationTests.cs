using System.Collections;
using System.Collections.Generic;
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
    public sealed class ResetConfirmationTests
    {
        private const BindingFlags Private = BindingFlags.NonPublic | BindingFlags.Instance;
        private SimulationController controller;
        private TrainingCameraController camera;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            SceneManager.LoadScene("ElectricalTraining");
            yield return null;
            yield return null;
            controller = Object.FindObjectOfType<SimulationController>();
            camera = Camera.main.GetComponent<TrainingCameraController>();
        }

        [UnityTest]
        public IEnumerator AllResetButtonsAskFirstAndDialogCapturesPointerAndKeyboard()
        {
            var wire = controller.Graph.AddWire("QF.T1", "KM1.L1", Color.blue);
            foreach (var name in new[] { "resetBtn", "btn_resume", "Reset" })
            {
                // The fallback top bar is intentionally inactive when imported UI is available.
                var button = Object.FindObjectsOfType<Button>(true).Single(b => b.name == name &&
                    (name != "Reset" || b.transform.parent.name == "TopBar"));
                if (button.gameObject.activeInHierarchy) Click(button.gameObject);
                else button.onClick.Invoke();
                Assert.That(controller.IsResetConfirmationOpen, Is.True, name);
                Assert.That(controller.Graph.Wires.Single(), Is.SameAs(wire));
                // Newly enabled graphics enter Unity's raycast registry on the next frame.
                yield return null;
                Assert.That(GameObject.Find("ResetConfirmationMessage").GetComponent<Text>().text,
                    Is.EqualTo("是否确认重置"));
                var overlay = GameObject.Find("ResetConfirmationDialog");
                var confirm = GameObject.Find("ConfirmReset").GetComponent<Button>();
                var cancel = GameObject.Find("CancelReset").GetComponent<Button>();
                Assert.That(EventSystem.current.currentSelectedGameObject, Is.EqualTo(cancel.gameObject));
                Assert.That(confirm.GetComponentInChildren<Text>().text, Is.EqualTo("确认"));
                Assert.That(cancel.GetComponentInChildren<Text>().text, Is.EqualTo("取消"));
                var move = new AxisEventData(EventSystem.current) { moveDir = MoveDirection.Left };
                ExecuteEvents.Execute(cancel.gameObject, move, ExecuteEvents.moveHandler);
                Assert.That(EventSystem.current.currentSelectedGameObject, Is.EqualTo(confirm.gameObject));
                move.moveDir = MoveDirection.Right;
                ExecuteEvents.Execute(confirm.gameObject, move, ExecuteEvents.moveHandler);
                Assert.That(EventSystem.current.currentSelectedGameObject, Is.EqualTo(cancel.gameObject));
                button.onClick.Invoke();
                Assert.That(Object.FindObjectsOfType<RectTransform>().Count(r => r.name == overlay.name), Is.EqualTo(1));

                Canvas.ForceUpdateCanvases();
                var outside = new Vector2(Screen.width * 0.9f, Screen.height * 0.5f);
                var hit = Hit(outside);
                Assert.That(hit.gameObject, Is.EqualTo(overlay));
                ExecuteEvents.ExecuteHierarchy(hit.gameObject, Pointer(outside), ExecuteEvents.pointerClickHandler);
                yield return null;
                Assert.That(controller.IsResetConfirmationOpen, Is.True, "点击遮罩不能重置或关闭弹窗");
                Assert.That(controller.Graph.Wires.Single(), Is.SameAs(wire));
                Click(cancel.gameObject);
                yield return null;
            }
        }

        [UnityTest]
        public IEnumerator CancelAndEscapePreserveWiringDraftModeAndCameraAndConsumeClosingFrame()
        {
            controller.SetMode(SimulationMode.Wiring);
            camera.SetWiringView();
            var position = camera.transform.position;
            var rotation = camera.transform.rotation;
            var wire = controller.Graph.AddWire("QF.T1", "KM1.L1", Color.blue);
            var port = Object.FindObjectsOfType<ElectricalPortView>().First(p => p.QualifiedPort == "FR.T1");
            Invoke("BeginWireRoute", port);
            var points = (List<Vector3>)typeof(SimulationController).GetField("pendingWirePoints", Private).GetValue(controller);
            points.Add(port.transform.position + Vector3.up * 0.1f);
            var expectedPoints = points.ToArray();
            foreach (var useEscape in new[] { false, true })
            {
                controller.RequestResetTraining();
                yield return null;
                Assert.That(controller.IsInteractionBlocked, Is.True);
                Assert.That(camera.ResetInputBlocked, Is.True);
                var pointer = new Vector2(Screen.width * 0.9f, Screen.height * 0.5f);
                Invoke("HandleWiringPointerDown", Camera.main, pointer);
                Invoke("HandleScenePointerDown", Camera.main, pointer);
                Assert.That(points, Is.EqualTo(expectedPoints));
                if (useEscape)
                    ExecuteEvents.Execute(GameObject.Find("ConfirmReset"), new BaseEventData(EventSystem.current), ExecuteEvents.cancelHandler);
                else Click(GameObject.Find("CancelReset"));
                Assert.That(controller.IsResetConfirmationOpen, Is.False);
                Assert.That(controller.IsInteractionBlocked, Is.True, "关闭当帧不能把点击或 Esc 传给场景");
                Assert.That((int)typeof(TrainingCameraController).GetField("resetInputResumeFrame", Private).GetValue(camera),
                    Is.EqualTo(Time.frameCount));
                Invoke("HandleWiringPointerDown", Camera.main, pointer);
                yield return null;
                Assert.That(controller.IsInteractionBlocked, Is.False);
                Assert.That(camera.ResetInputBlocked, Is.False);
                Assert.That(controller.Mode, Is.EqualTo(SimulationMode.Wiring));
                Assert.That(controller.IsRoutingWire, Is.True);
                Assert.That(points, Is.EqualTo(expectedPoints));
                Assert.That(controller.Graph.Wires.Single(), Is.SameAs(wire));
                Assert.That(camera.transform.position, Is.EqualTo(position));
                Assert.That(camera.transform.rotation, Is.EqualTo(rotation));
            }
        }

        [UnityTest]
        public IEnumerator ConfirmRunsExistingResetExactlyOnce()
        {
            controller.SetMode(SimulationMode.Drag);
            camera.SetFaultView();
            controller.Graph.AddWire("QF.T1", "KM1.L1", Color.blue);
            Assert.That(controller.TryToggleCabinetBreaker(controller.CabinetBreakers.First()), Is.True);
            var resetCount = 0;
            controller.StatusChanged += (message, error) => { if (message == "训练场景已重置。") resetCount++; };
            controller.RequestResetTraining();
            var confirm = GameObject.Find("ConfirmReset").GetComponent<Button>();
            yield return null;
            Click(confirm.gameObject);
            confirm.onClick.Invoke();
            Assert.That(resetCount, Is.EqualTo(1));
            Assert.That(controller.IsResetConfirmationOpen, Is.False);
            Assert.That(controller.Graph.Wires, Is.Empty);
            Assert.That(controller.AreCabinetBreakersClosed, Is.True);
            Assert.That(controller.Mode, Is.EqualTo(SimulationMode.View));
            Assert.That(camera.transform.position, Is.EqualTo(camera.DefaultPosition));
            Assert.That(controller.IsInteractionBlocked, Is.True);
            yield return null;
            Assert.That(controller.IsInteractionBlocked, Is.False);
        }

        [UnityTest]
        public IEnumerator SimulationContinuesAndOtherInputBlocksArePreserved()
        {
            controller.SetMode(SimulationMode.Simulate);
            var snapshotField = typeof(SimulationController).GetField("lastSnapshot", Private);
            var snapshot = snapshotField.GetValue(controller);
            var scale = Time.timeScale;
            camera.InputBlocked = true;
            controller.RequestResetTraining();
            yield return null;
            yield return null;
            Assert.That(snapshotField.GetValue(controller), Is.Not.SameAs(snapshot));
            Assert.That(controller.Mode, Is.EqualTo(SimulationMode.Simulate));
            Assert.That(Time.timeScale, Is.EqualTo(scale));
            Assert.That(controller.TryToggleCabinetBreaker(controller.CabinetBreakers.First()), Is.False);
            controller.ResetConfirmation.OnCancel(null);
            yield return null;
            Assert.That(camera.InputBlocked, Is.True);
            Assert.That(camera.ResetInputBlocked, Is.False);
            camera.InputBlocked = false;

            controller.HomePage.Open();
            controller.RequestResetTraining();
            Assert.That(controller.IsResetConfirmationOpen, Is.False, "不能在已有模态页面上叠加重置弹窗");
            controller.HomePage.Close();
            yield return null;
            controller.RequestResetTraining();
            Assert.That(controller.IsResetConfirmationOpen, Is.True);
            controller.ResetConfirmation.enabled = false;
            yield return null;
            Assert.That(controller.IsResetConfirmationOpen, Is.False);
            Assert.That(controller.IsInteractionBlocked, Is.False);
            Assert.That(camera.ResetInputBlocked, Is.False);
        }

        private void Invoke(string name, params object[] args)
            => typeof(SimulationController).GetMethod(name, Private).Invoke(controller, args);

        private static PointerEventData Pointer(Vector2 point)
            => new PointerEventData(EventSystem.current) { position = point, button = PointerEventData.InputButton.Left };

        private static RaycastResult Hit(Vector2 point)
        {
            var hits = new List<RaycastResult>();
            EventSystem.current.RaycastAll(Pointer(point), hits);
            Assert.That(hits, Is.Not.Empty);
            return hits[0];
        }

        private static void Click(GameObject button)
        {
            Canvas.ForceUpdateCanvases();
            var rect = button.GetComponent<RectTransform>();
            var point = RectTransformUtility.WorldToScreenPoint(null, rect.TransformPoint(rect.rect.center));
            var clicked = ExecuteEvents.ExecuteHierarchy(Hit(point).gameObject, Pointer(point), ExecuteEvents.pointerClickHandler);
            Assert.That(clicked, Is.EqualTo(button));
        }
    }
}
