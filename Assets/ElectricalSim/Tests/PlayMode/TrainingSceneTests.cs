using System.Collections;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UI;

namespace ElectricalSim.Tests
{
    public sealed class TrainingSceneTests
    {
        [UnitySetUp]
        public IEnumerator LoadTrainingScene()
        {
            SceneManager.LoadScene("ElectricalTraining", LoadSceneMode.Single);
            yield return null;
            yield return null;
        }

        [UnityTest]
        public IEnumerator SceneBootstrapsControllerCameraDevicesAndHud()
        {
            Assert.That(Object.FindObjectOfType<SimulationController>(), Is.Not.Null);
            Assert.That(Object.FindObjectOfType<TrainingCameraController>(), Is.Not.Null);
            Assert.That(Object.FindObjectsOfType<ElectricalDeviceView>().Length, Is.GreaterThanOrEqualTo(20));
            Assert.That(GameObject.Find("Simulation HUD"), Is.Not.Null);
            yield return null;
        }

        [UnityTest]
        public IEnumerator EveryModeCanBeSelected()
        {
            var controller = Object.FindObjectOfType<SimulationController>();
            foreach (SimulationMode mode in System.Enum.GetValues(typeof(SimulationMode)))
            {
                controller.SetMode(mode);
                Assert.That(controller.Mode, Is.EqualTo(mode));
                yield return null;
            }
        }

        [UnityTest]
        public IEnumerator ReferenceWiringPassesCurrentTaskTopology()
        {
            var controller = Object.FindObjectOfType<SimulationController>();
            controller.LoadReferenceWiring();
            yield return null;
            var result = CircuitTaskEvaluator.EvaluateTopology(controller.Graph, controller.CurrentTask);
            Assert.That(result.Passed, Is.True, result.Summary());
            Assert.That(controller.Graph.Wires.Count, Is.GreaterThan(10));
        }

        [UnityTest]
        public IEnumerator OriginalModelConnectionPointsUseTerminalTransforms()
        {
            AssertPortMatchesMappedTerminal("QF", "L1", "L1", "123");
            AssertPortMatchesMappedTerminal("QF", "T3", "L6", "123");
            AssertPortMatchesTerminal("KM1", "L1", "1L1");
            AssertPortMatchesTerminal("KM1", "T2", "4T2");
            AssertPortMatchesTerminal("KM1", "A1", "A1");
            AssertPortMatchesTerminal("KM1", "13", "13NO");
            AssertPortMatchesTerminal("FR", "95", "95NC");
            AssertPortMatchesMappedTerminal("M1", "U", "U1", "38");
            yield return null;
        }

        [UnityTest]
        public IEnumerator TopPanelControlsExposeOnlyTerminalBoardConnectionPoints()
        {
            var views = Object.FindObjectsOfType<ElectricalDeviceView>();
            foreach (var view in views.Where(item =>
                         item.Runtime.Kind == ElectricalDeviceKind.PowerSource ||
                         item.Runtime.Kind == ElectricalDeviceKind.PushButton ||
                         item.Runtime.Kind == ElectricalDeviceKind.Indicator ||
                         item.Runtime.Kind == ElectricalDeviceKind.SelectorSwitch))
                Assert.That(view.Ports, Is.Empty, view.Runtime.DeviceId + " must be routed through DuanZiPai_0");

            var board = views.Single(view => view.Runtime.DeviceId == OriginalTerminalBoardMap.DeviceId);
            Assert.That(board.Ports.Count, Is.EqualTo(76));
            Assert.That(board.Runtime.GetConductiveLinks().Count(), Is.EqualTo(76));
            yield return null;
        }

        [UnityTest]
        public IEnumerator PlcAndRelayTerminalBoardsUseOriginalNamedConnectionPoints()
        {
            var environment = GameObject.Find("OriginalLabEnvironment");
            var views = Object.FindObjectsOfType<ElectricalDeviceView>();
            AssertNamedBoardPort(environment, views, "DuanZiPai_1", "PLC_1_M0.0");
            AssertNamedBoardPort(environment, views, "DuanZiPai_1", "KA6_8");
            AssertNamedBoardPort(environment, views, "DuanZiPai_2", "PLC_1_Q0.0");
            AssertNamedBoardPort(environment, views, "DuanZiPai_2", "KA6_14");

            foreach (var definition in OriginalCabinetTerminalBoardMap.Boards)
            {
                var board = environment.GetComponentsInChildren<Transform>(true)
                    .Single(item => item.name == definition.DeviceId && item.Find("point") != null);
                var expectedCount = board.Find("point").Cast<Transform>()
                    .Count(item => OriginalCabinetTerminalBoardMap.IsTerminalName(item.name));
                var view = views.Single(item => item.Runtime.DeviceId == definition.DeviceId);
                Assert.That(view.Ports.Count, Is.EqualTo(expectedCount));
                Assert.That(view.Runtime.GetConductiveLinks().Count(), Is.EqualTo(expectedCount));
            }
            yield return null;
        }

        [UnityTest]
        public IEnumerator PlcAndRelayTerminalHoverShowsOriginalName()
        {
            var controller = Object.FindObjectOfType<SimulationController>();
            var presenter = Object.FindObjectsOfType<PortHoverPresenter>(true).Single();
            var port = Object.FindObjectsOfType<ElectricalDeviceView>()
                .Single(view => view.Runtime.DeviceId == "DuanZiPai_1")
                .Ports.Single(item => item.PortName == "KA6_8");

            controller.SetMode(SimulationMode.Wiring);
            yield return null;
            Assert.That(port.GetComponent<MeshRenderer>(), Is.Not.Null);
            Assert.That(port.GetComponent<MeshRenderer>().enabled, Is.True);
            var screenPoint = (Vector2)Camera.main.WorldToScreenPoint(port.CurrentAnchorPosition);
            presenter.Present(port, Camera.main, screenPoint + new Vector2(-60f, 48f));
            Assert.That(presenter.IsVisible, Is.True);
            Assert.That(presenter.CurrentText, Is.EqualTo("KA6_8"));
        }

        [UnityTest]
        public IEnumerator CabinetBrandingCoversBothOriginalHeaderLogos()
        {
            var environment = GameObject.Find("OriginalLabEnvironment");
            var cameraController = Object.FindObjectOfType<TrainingCameraController>();
            Assert.That(environment, Is.Not.Null);
            var cabinet = environment.GetComponentsInChildren<Transform>(true)
                .Single(item => item.name == "wanggui");
            var mesh = cabinet.GetComponent<MeshFilter>().sharedMesh;
            var front = cabinet.Find("Cabinet WCK Logo Front");
            var back = cabinet.Find("Cabinet WCK Logo Back");

            Assert.That(front, Is.Not.Null);
            Assert.That(back, Is.Not.Null);
            Assert.That(front.localPosition.y, Is.GreaterThan(mesh.bounds.center.y + mesh.bounds.extents.y * 0.8f));
            Assert.That(back.localPosition.y, Is.EqualTo(front.localPosition.y).Within(0.0001f));
            Assert.That(front.localPosition.z, Is.GreaterThan(mesh.bounds.max.z));
            Assert.That(back.localPosition.z, Is.LessThan(mesh.bounds.min.z));
            Assert.That(front.GetComponentInChildren<MeshRenderer>().enabled, Is.True);
            Assert.That(back.GetComponentInChildren<MeshRenderer>().enabled, Is.True);

            cameraController.SetWiringView();
            yield return null;
            var frontScreen = Camera.main.WorldToScreenPoint(front.position);
            Debug.Log($"[CabinetBranding] front local={front.localPosition}, world={front.position}, screen={frontScreen}");
            foreach (var renderer in environment.GetComponentsInChildren<Renderer>(true)
                         .Where(item => item.enabled && item.bounds.max.y > 2.6f)
                         .OrderByDescending(item => item.bounds.max.y)
                         .Take(30))
            {
                var screen = Camera.main.WorldToScreenPoint(renderer.bounds.center);
                Debug.Log($"[CabinetHeaderCandidate] {HierarchyPath(renderer.transform)} bounds={renderer.bounds} screen={screen}");
            }
            Assert.That(frontScreen.z, Is.GreaterThan(0f));

            cameraController.SetFaultView();
            yield return null;
            var backScreen = Camera.main.WorldToScreenPoint(back.position);
            Debug.Log($"[CabinetBranding] back local={back.localPosition}, world={back.position}, screen={backScreen}");
            Assert.That(backScreen.z, Is.GreaterThan(0f));
        }

        [UnityTest]
        public IEnumerator OriginalViewMenuSwitchesBetweenFrontAndBack()
        {
            var cameraController = Object.FindObjectOfType<TrainingCameraController>();
            var toolbar = GameObject.Find("OriginalUI_ExperimentToolbar");
            Assert.That(toolbar, Is.Not.Null);
            var menu = toolbar.GetComponentsInChildren<Transform>(true).Single(item => item.name == "twoChange");
            var viewButton = toolbar.GetComponentsInChildren<Button>(true).Single(item => item.name == "btn_viewChange");

            viewButton.onClick.Invoke();
            Assert.That(menu.gameObject.activeSelf, Is.True);
            ButtonWithText(menu, "排故视角").onClick.Invoke();
            yield return null;
            Assert.That(cameraController.CurrentPreset, Is.EqualTo(TrainingViewPreset.FaultBack));
            Assert.That(cameraController.transform.position.z, Is.LessThan(-4f));
            Assert.That(Vector3.Dot(cameraController.transform.forward, Vector3.forward), Is.GreaterThan(0.95f));

            viewButton.onClick.Invoke();
            ButtonWithText(menu, "接线视角").onClick.Invoke();
            yield return null;
            Assert.That(cameraController.CurrentPreset, Is.EqualTo(TrainingViewPreset.WiringFront));
            Assert.That(Vector3.Dot(cameraController.transform.forward, Vector3.back), Is.GreaterThan(0.95f));
        }

        [UnityTest]
        public IEnumerator FrontLineTypeAndBackViewUseDistinctOriginalAnchors()
        {
            var controller = Object.FindObjectOfType<SimulationController>();
            var cameraController = Object.FindObjectOfType<TrainingCameraController>();
            var environment = GameObject.Find("OriginalLabEnvironment");
            var port = Object.FindObjectsOfType<ElectricalDeviceView>()
                .Single(view => view.Runtime.DeviceId == "KM1").Ports.Single(view => view.PortName == "A1");

            cameraController.SetWiringView();
            controller.SetWireStyle(Color.red, 0.01f, "ElectricalWire");
            yield return null;
            var electrical = environment.GetComponentsInChildren<Transform>(true)
                .First(item => item.name == "KM1_a1" && HasAncestor(item, "point"));
            Assert.That(Vector3.Distance(port.transform.position, electrical.position), Is.LessThan(0.0005f));

            controller.SetWireStyle(Color.red, 0.01f, "JumperLine");
            yield return null;
            var jumper = environment.GetComponentsInChildren<Transform>(true)
                .First(item => item.name == "KM1_A1" && HasAncestor(item, "point"));
            Assert.That(Vector3.Distance(port.transform.position, jumper.position), Is.LessThan(0.0005f));
            Assert.That(Vector3.Distance(electrical.position, jumper.position), Is.GreaterThan(0.01f));

            cameraController.SetFaultView();
            yield return null;
            var back = environment.GetComponentsInChildren<Transform>(true)
                .First(item => item.name == "A1" && HasAncestor(item, "112") && HasAncestor(item, "point"));
            Assert.That(Vector3.Distance(port.transform.position, back.position), Is.LessThan(0.0005f));
        }

        [UnityTest]
        public IEnumerator TopTerminalBoardUsesElectricalAnchorsAndHidesDuplicateJumperPoints()
        {
            var controller = Object.FindObjectOfType<SimulationController>();
            var cameraController = Object.FindObjectOfType<TrainingCameraController>();
            var environment = GameObject.Find("OriginalLabEnvironment");
            var board = Object.FindObjectsOfType<ElectricalDeviceView>()
                .Single(view => view.Runtime.DeviceId == OriginalTerminalBoardMap.DeviceId);
            Assert.That(board.Ports.Count, Is.EqualTo(76));

            var port = board.Ports.Single(item => item.PortName == "a75");
            Assert.That(port.HoverLabel, Is.EqualTo("SB8_COM2"));
            Assert.That(port.SupportsJumperAnchor, Is.False);
            cameraController.SetWiringView();
            controller.SetMode(SimulationMode.Wiring);
            controller.SetWireStyle(Color.red, 0.01f, "ElectricalWire");
            yield return null;
            var electrical = environment.GetComponentsInChildren<Transform>(true)
                .Single(item => item.name == "a75" && HasAncestor(item, "DuanZiPai_0") && HasAncestor(item, "13"));
            Assert.That(Vector3.Distance(port.transform.position, electrical.position), Is.LessThan(0.0005f));
            Assert.That(port.UsesJumperAnchor, Is.False);

            controller.SetWireStyle(Color.red, 0.01f, "JumperLine");
            yield return null;
            Assert.That(port.CurrentAnchor, Is.Null);
            Assert.That(port.UsesJumperAnchor, Is.False);
            Assert.That(port.IsVisible, Is.False);
            Assert.That(port.GetComponent<Collider>().enabled, Is.False);
        }

        [UnityTest]
        public IEnumerator PortHoverMatchesOriginalTooltipInWiringAndFaultModes()
        {
            var controller = Object.FindObjectOfType<SimulationController>();
            var cameraController = Object.FindObjectOfType<TrainingCameraController>();
            var presenter = Object.FindObjectsOfType<PortHoverPresenter>(true).Single();
            var port = Object.FindObjectsOfType<ElectricalDeviceView>()
                .Single(view => view.Runtime.DeviceId == OriginalTerminalBoardMap.DeviceId)
                .Ports.Single(item => item.PortName == "a75");

            controller.SetMode(SimulationMode.Wiring);
            yield return null;
            var screenPoint = (Vector2)Camera.main.WorldToScreenPoint(port.CurrentAnchorPosition);
            presenter.Present(port, Camera.main, screenPoint + new Vector2(-70f, 55f));
            Assert.That(presenter.IsVisible, Is.True);
            Assert.That(presenter.CurrentText, Is.EqualTo("SB8_COM2"));
            var canvas = GameObject.Find("Simulation HUD").GetComponent<RectTransform>();
            RectTransformUtility.ScreenPointToLocalPointInRectangle(canvas, screenPoint, null, out var expectedEnd);
            Assert.That(Vector2.Distance(presenter.LeaderEndCanvasPosition, expectedEnd), Is.LessThanOrEqualTo(2f));

            controller.SetMode(SimulationMode.Fault);
            yield return null;
            screenPoint = Camera.main.WorldToScreenPoint(port.CurrentAnchorPosition);
            presenter.Present(port, Camera.main, screenPoint + new Vector2(-70f, 55f));
            Assert.That(presenter.IsVisible, Is.True);
            Assert.That(presenter.CurrentText, Is.EqualTo("SB8_COM2"));

            controller.SetMode(SimulationMode.View);
            Assert.That(presenter.IsVisible, Is.False);
        }

        [UnityTest]
        public IEnumerator WiringPortsStayOnTheOriginalCabinetInsteadOfNearTheCamera()
        {
            var controller = Object.FindObjectOfType<SimulationController>();
            var cameraController = Object.FindObjectOfType<TrainingCameraController>();
            cameraController.SetWiringView();
            controller.SetWireStyle(Color.red, 0.01f, "ElectricalWire");
            controller.SetMode(SimulationMode.Wiring);
            yield return null;

            foreach (var port in Object.FindObjectsOfType<ElectricalPortView>())
            {
                var position = port.transform.position;
                Assert.That(position.x, Is.InRange(-0.7f, 0.7f), $"{port.QualifiedPort} x={position.x}");
                Assert.That(position.y, Is.InRange(0.15f, 1.95f), $"{port.QualifiedPort} y={position.y}");
                Assert.That(position.z, Is.InRange(-2.0f, -1.1f), $"{port.QualifiedPort} z={position.z}");
            }
        }

        private static Button ButtonWithText(Transform root, string label)
            => root.GetComponentsInChildren<Button>(true).Single(button =>
                button.GetComponentsInChildren<Text>(true).Any(text => text.text == label));

        private static bool HasAncestor(Transform item, string name)
        {
            for (var current = item.parent; current != null; current = current.parent)
                if (current.name == name) return true;
            return false;
        }

        private static void AssertNamedBoardPort(
            GameObject environment,
            ElectricalDeviceView[] views,
            string boardId,
            string terminalName)
        {
            var board = environment.GetComponentsInChildren<Transform>(true)
                .Single(item => item.name == boardId && item.Find("point") != null);
            var anchor = board.Find("point/" + terminalName);
            Assert.That(anchor, Is.Not.Null, boardId + "/" + terminalName + " original anchor");
            var port = views.Single(item => item.Runtime.DeviceId == boardId)
                .Ports.Single(item => item.PortName == terminalName);
            Assert.That(port.HoverLabel, Is.EqualTo(terminalName));
            Assert.That(port.PhysicalAnchorId, Is.EqualTo(terminalName));
            Assert.That(Vector3.Distance(port.CurrentAnchorPosition, anchor.position), Is.LessThan(0.0005f));
            Assert.That(port.GetComponent<SphereCollider>(), Is.Not.Null);
            Assert.That(port.GetComponent<MeshRenderer>(), Is.Not.Null);
        }

        private static string HierarchyPath(Transform item)
        {
            var names = new System.Collections.Generic.List<string>();
            for (var current = item; current != null; current = current.parent) names.Add(current.name);
            names.Reverse();
            return string.Join("/", names);
        }

        private static void AssertPortMatchesTerminal(string deviceId, string portName, string terminalName)
        {
            var device = Object.FindObjectsOfType<ElectricalDeviceView>()
                .Single(view => view.Runtime.DeviceId == deviceId);
            var port = device.Ports.Single(view => view.PortName == portName);
            var environment = GameObject.Find("OriginalLabEnvironment");
            var environmentNames = deviceId == "FR"
                ? new[] { "FR1_" + terminalName, "FR_" + terminalName }
                : new[] { deviceId + "_" + terminalName };
            var terminal = environment == null ? null : environment.GetComponentsInChildren<Transform>(true)
                .FirstOrDefault(transform => environmentNames.Any(name =>
                    string.Equals(transform.name, name, System.StringComparison.OrdinalIgnoreCase)));
            if (terminal == null)
                terminal = device.GetComponentsInChildren<Transform>(true)
                    .First(transform => string.Equals(transform.name, terminalName, System.StringComparison.OrdinalIgnoreCase));
            Assert.That(Vector3.Distance(port.transform.position, terminal.position), Is.LessThan(0.0005f),
                $"{deviceId}.{portName} must be located on original terminal {terminalName}");
        }

        private static void AssertPortMatchesMappedTerminal(string deviceId, string portName, string terminalName, string nut)
        {
            var port = Object.FindObjectsOfType<ElectricalDeviceView>()
                .Single(view => view.Runtime.DeviceId == deviceId).Ports.Single(view => view.PortName == portName);
            var environment = GameObject.Find("OriginalLabEnvironment");
            var terminal = environment.GetComponentsInChildren<Transform>(true).First(item =>
                string.Equals(item.name, terminalName, System.StringComparison.OrdinalIgnoreCase) &&
                HasAncestor(item, nut) && HasAncestor(item, "point"));
            Assert.That(Vector3.Distance(port.transform.position, terminal.position), Is.LessThan(0.0005f));
        }
    }
}
