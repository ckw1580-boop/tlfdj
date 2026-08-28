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
        public IEnumerator TerminalBoardAnnotationsUseIndependentVisibleTextMeshes()
        {
            var environment = GameObject.Find("OriginalLabEnvironment");
            Assert.That(environment, Is.Not.Null);

            var generatedRoot = environment.transform.Find("Terminal Board Annotations");
            Assert.That(generatedRoot, Is.Not.Null);
            Assert.That(generatedRoot.gameObject.activeInHierarchy, Is.True);

            var board = environment.transform.Find(OriginalTerminalBoardMap.BoardTransformPath);
            Assert.That(board, Is.Not.Null);
            var expectedTopLabels = new[]
            {
                "三相电源端子区",
                "指示灯（HL）端子区",
                "旋钮（SA）、按钮SB端子区"
            };
            var generatedLabels = generatedRoot.GetComponentsInChildren<TextMesh>(true)
                .Where(text => text.gameObject.activeInHierarchy && expectedTopLabels.Contains(text.text))
                .ToArray();
            Assert.That(generatedLabels.Select(text => text.text),
                Is.EquivalentTo(expectedTopLabels));
            var camera = Camera.main;
            Assert.That(camera, Is.Not.Null);
            var renderTarget = RenderTexture.GetTemporary(960, 540, 24, RenderTextureFormat.ARGB32);
            var previousTarget = camera.targetTexture;
            var previousActive = RenderTexture.active;
            var previousCameraPosition = camera.transform.position;
            var previousCameraRotation = camera.transform.rotation;
            var frame = new Texture2D(960, 540, TextureFormat.RGBA32, false);
            try
            {
                camera.targetTexture = renderTarget;
                camera.Render();
                RenderTexture.active = renderTarget;
                frame.ReadPixels(new Rect(0f, 0f, frame.width, frame.height), 0, 0);
                frame.Apply();
                foreach (var label in generatedLabels)
                {
                    var renderer = label.GetComponent<MeshRenderer>();
                    Assert.That(renderer, Is.Not.Null, label.text);
                    Assert.That(label.GetComponent("FrontFaceOnlyTextVisibility"), Is.Not.Null, label.text);
                    Assert.That(renderer.enabled, Is.True, label.text);
                    Assert.That(renderer.bounds.size.y, Is.InRange(0.012f, 0.04f), label.text);
                    Assert.That(Vector3.Dot(-label.transform.forward,
                        (camera.transform.position - label.transform.position).normalized),
                        Is.GreaterThan(0.65f), label.text);
                    var viewport = camera.WorldToViewportPoint(renderer.bounds.center);
                    Assert.That(viewport.z, Is.GreaterThan(0f), label.text);
                    Assert.That(viewport.x, Is.InRange(0f, 1f), label.text);
                    Assert.That(viewport.y, Is.InRange(0f, 1f), label.text);
                    var visibleYellowPixels = CountBrightYellowPixels(camera, frame, renderer.bounds);
                    renderer.enabled = false;
                    camera.Render();
                    RenderTexture.active = renderTarget;
                    frame.ReadPixels(new Rect(0f, 0f, frame.width, frame.height), 0, 0);
                    frame.Apply();
                    var hiddenYellowPixels = CountBrightYellowPixels(camera, frame, renderer.bounds);
                    renderer.enabled = true;
                    camera.Render();
                    RenderTexture.active = renderTarget;
                    frame.ReadPixels(new Rect(0f, 0f, frame.width, frame.height), 0, 0);
                    frame.Apply();
                    Assert.That(visibleYellowPixels - hiddenYellowPixels,
                        Is.GreaterThan(4), label.text + " must add visible yellow pixels to the rendered frame");
                }

                var labelRects = generatedLabels
                    .Select(label => GetViewportRect(camera, label.GetComponent<MeshRenderer>().bounds))
                    .OrderBy(rect => rect.center.x)
                    .ToArray();
                for (var index = 1; index < labelRects.Length; index++)
                    Assert.That(labelRects[index].xMin,
                        Is.GreaterThanOrEqualTo(labelRects[index - 1].xMax - 0.002f),
                        "terminal annotation zones must not overlap");

                var referenceLabel = generatedLabels[1].transform;
                camera.transform.position = referenceLabel.position + referenceLabel.forward * 2f;
                foreach (var label in generatedLabels)
                    label.gameObject.SendMessage("RefreshVisibility", SendMessageOptions.RequireReceiver);
                Assert.That(generatedLabels.All(label => !label.GetComponent<MeshRenderer>().enabled), Is.True,
                    "terminal annotations must be hidden from the cabinet rear/fault viewpoint");
            }
            finally
            {
                camera.transform.SetPositionAndRotation(previousCameraPosition, previousCameraRotation);
                foreach (var label in generatedLabels)
                    label.gameObject.SendMessage("RefreshVisibility", SendMessageOptions.DontRequireReceiver);
                camera.targetTexture = previousTarget;
                RenderTexture.active = previousActive;
                RenderTexture.ReleaseTemporary(renderTarget);
                Object.Destroy(frame);
            }
            var boardCanvases = environment.GetComponentsInChildren<Canvas>(true)
                .Where(canvas => canvas.GetComponentsInParent<Transform>(true)
                    .Any(item => item.name.StartsWith("DuanZiPai_")))
                .ToArray();
            Assert.That(boardCanvases.All(item => !item.gameObject.activeSelf), Is.True);

            var boardTextMeshes = environment.GetComponentsInChildren<TextMesh>(true)
                .Where(textMesh => textMesh.GetComponentsInParent<Transform>(true)
                    .Any(item => item.name.StartsWith("DuanZiPai_")))
                .ToArray();
            Assert.That(boardTextMeshes.All(textMesh => !textMesh.gameObject.activeSelf), Is.True);
            yield return null;
        }

        [UnityTest]
        public IEnumerator PlcAndRelayTerminalBoardAnnotationsAreVisibleFromTheFront()
        {
            var environment = GameObject.Find("OriginalLabEnvironment");
            Assert.That(environment, Is.Not.Null);
            var generatedRoot = environment.transform.Find("Terminal Board Annotations");
            Assert.That(generatedRoot, Is.Not.Null);

            var expectedRaisedLabels = new[]
            {
                "PLC_1端子区",
                "PLC_2端子区",
                "中间继电器（KA）4、9、10、11、12、13、14端子区"
            };
            var expectedLabels = new[]
            {
                "PLC_1DI端子区",
                "PLC_2DI端子区",
                "中间继电器（KA）1、2、3、5、6、7、8端子区",
            }.Concat(expectedRaisedLabels).ToArray();
            var labels = generatedRoot.GetComponentsInChildren<TextMesh>(true)
                .Where(item => expectedLabels.Contains(item.text))
                .ToArray();
            Assert.That(labels.Select(item => item.text), Is.EquivalentTo(expectedLabels));
            var orientationReference = generatedRoot.GetComponentsInChildren<TextMesh>(true)
                .Single(item => item.text == "三相电源端子区");
            var board2 = environment.GetComponentsInChildren<Transform>(true)
                .Single(item => item.name == "DuanZiPai_2" && item.Find("point") != null);
            var board2PointRoot = board2.Find("point");

            var camera = Camera.main;
            Assert.That(camera, Is.Not.Null);
            foreach (var label in labels)
            {
                var renderer = label.GetComponent<MeshRenderer>();
                Assert.That(renderer, Is.Not.Null, label.text);
                Assert.That(renderer.enabled, Is.True, label.text);
                Assert.That(label.GetComponent("FrontFaceOnlyTextVisibility"), Is.Not.Null, label.text);
                Assert.That(Quaternion.Angle(label.transform.rotation, orientationReference.transform.rotation),
                    Is.LessThan(0.01f), label.text + " must exactly match the three-phase label direction");
                Assert.That(Vector3.Distance(label.transform.localScale, orientationReference.transform.localScale),
                    Is.LessThan(0.0001f), label.text + " must exactly match the three-phase label size");
                Assert.That(Vector3.Dot(-label.transform.forward,
                    (camera.transform.position - label.transform.position).normalized),
                    Is.GreaterThan(0f), label.text);
                if (!expectedRaisedLabels.Contains(label.text)) continue;

                var prefix = label.text == "PLC_1端子区" ? "PLC_1_" :
                    label.text == "PLC_2端子区" ? "PLC_2_" : "KA";
                var anchors = board2PointRoot.Cast<Transform>()
                    .Where(item => item.name.StartsWith(prefix, System.StringComparison.Ordinal) &&
                                   OriginalCabinetTerminalBoardMap.IsTerminalName(item.name))
                    .ToArray();
                Assert.That(anchors, Is.Not.Empty, label.text);
                var center = anchors.Aggregate(Vector3.zero, (sum, anchor) => sum + anchor.position) / anchors.Length;
                var verticalOffset = Vector3.Dot(label.transform.position - center, label.transform.up);
                Assert.That(verticalOffset,
                    Is.EqualTo(renderer.bounds.size.y * 1.55f).Within(0.0005f),
                    label.text + " must be raised two glyph heights from its previous position");
            }
            yield return null;
        }

        [UnityTest]
        public IEnumerator AuxiliaryTerminalBoardAnnotationsUseWholeOriginalBoards()
        {
            var environment = GameObject.Find("OriginalLabEnvironment");
            Assert.That(environment, Is.Not.Null);
            var generatedRoot = environment.transform.Find("Terminal Board Annotations");
            Assert.That(generatedRoot, Is.Not.Null);
            var orientationReference = generatedRoot.GetComponentsInChildren<TextMesh>(true)
                .Single(item => item.text == "三相电源端子区");
            var camera = Camera.main;
            Assert.That(camera, Is.Not.Null);

            var expectedAnnotations = new[]
            {
                new { BoardName = "DuanZiPai_6", Text = "电源端子区" },
                new { BoardName = "DuanZiPai_7", Text = "电机端子区" },
                new { BoardName = "DuanZiPai_8", Text = "场景中传感器、电磁阀端子" }
            };
            foreach (var expected in expectedAnnotations)
            {
                var label = generatedRoot.GetComponentsInChildren<TextMesh>(true)
                    .Single(item => item.text == expected.Text);
                var renderer = label.GetComponent<MeshRenderer>();
                Assert.That(renderer, Is.Not.Null, label.text);
                Assert.That(renderer.enabled, Is.True, label.text);
                Assert.That(label.GetComponent("FrontFaceOnlyTextVisibility"), Is.Not.Null, label.text);
                Assert.That(Quaternion.Angle(label.transform.rotation, orientationReference.transform.rotation),
                    Is.LessThan(0.01f), label.text);
                Assert.That(Vector3.Distance(label.transform.localScale, orientationReference.transform.localScale),
                    Is.LessThan(0.0001f), label.text);

                var board = environment.GetComponentsInChildren<Transform>(true)
                    .Single(item => item.name == expected.BoardName && item.Find("point") != null);
                var pointRoot = board.Find("point");
                var boardDefinition = OriginalCabinetTerminalBoardMap.Boards
                    .Single(item => item.DeviceId == expected.BoardName);
                var anchors = pointRoot.Cast<Transform>()
                    .Where(item => OriginalCabinetTerminalBoardMap.IsTerminalName(boardDefinition, item.name))
                    .ToArray();
                Assert.That(anchors, Is.Not.Empty, label.text);
                var center = anchors.Aggregate(Vector3.zero, (sum, anchor) => sum + anchor.position) / anchors.Length;
                var verticalOffset = Vector3.Dot(label.transform.position - center, label.transform.up);
                Assert.That(verticalOffset,
                    Is.EqualTo(renderer.bounds.size.y * -0.45f).Within(0.0005f), label.text);
                Assert.That(Vector3.Dot(-label.transform.forward,
                    (camera.transform.position - label.transform.position).normalized),
                    Is.GreaterThan(0f), label.text);
            }
            yield return null;
        }

        [UnityTest]
        public IEnumerator InverterLowerTerminalBoardAnnotationsUseOriginalNumberedGroups()
        {
            var environment = GameObject.Find("OriginalLabEnvironment");
            Assert.That(environment, Is.Not.Null);
            var generatedRoot = environment.transform.Find("Terminal Board Annotations");
            Assert.That(generatedRoot, Is.Not.Null);
            var orientationReference = generatedRoot.Find("Terminal Annotation - Three Phase Power");
            Assert.That(orientationReference, Is.Not.Null);
            var board = environment.GetComponentsInChildren<Transform>(true)
                .Single(item => item.name == "DuanZiPai_5" && item.Find("point") != null);
            var pointRoot = board.Find("point");
            var camera = Camera.main;
            Assert.That(camera, Is.Not.Null);

            var expectedAnnotations = new[]
            {
                new
                {
                    ObjectName = "Terminal Annotation - G120 Inverter Below Inverter",
                    Text = "G120变频器端子区", First = 1, Last = 39, Count = 38
                },
                new
                {
                    ObjectName = "Terminal Annotation - Contactors KM Below Inverter",
                    Text = "交流接触器（KM）端子区", First = 40, Last = 76, Count = 37
                },
                new
                {
                    ObjectName = "Terminal Annotation - FR and KT Below Inverter",
                    Text = "FR端子区KT端子区", First = 77, Last = 85, Count = 9
                }
            };
            foreach (var expected in expectedAnnotations)
            {
                var labelTransform = generatedRoot.Find(expected.ObjectName);
                Assert.That(labelTransform, Is.Not.Null, expected.Text);
                var label = labelTransform.GetComponent<TextMesh>();
                Assert.That(label, Is.Not.Null, expected.Text);
                Assert.That(label.text, Is.EqualTo(expected.Text));
                var renderer = label.GetComponent<MeshRenderer>();
                Assert.That(renderer, Is.Not.Null, label.text);
                Assert.That(renderer.enabled, Is.True, label.text);
                Assert.That(label.GetComponent("FrontFaceOnlyTextVisibility"), Is.Not.Null, label.text);
                Assert.That(Quaternion.Angle(label.transform.rotation, orientationReference.rotation),
                    Is.LessThan(0.01f), label.text);
                Assert.That(Vector3.Distance(label.transform.localScale, orientationReference.localScale),
                    Is.LessThan(0.0001f), label.text);

                var anchors = pointRoot.Cast<Transform>()
                    .Where(item => item.name.Length > 1 && item.name[0] == 'a' &&
                                   int.TryParse(item.name.Substring(1), out var number) &&
                                   number >= expected.First && number <= expected.Last)
                    .ToArray();
                Assert.That(anchors.Length, Is.EqualTo(expected.Count), label.text);
                var center = anchors.Aggregate(Vector3.zero, (sum, anchor) => sum + anchor.position) / anchors.Length;
                var verticalOffset = Vector3.Dot(label.transform.position - center, label.transform.up);
                Assert.That(verticalOffset,
                    Is.EqualTo(renderer.bounds.size.y * 1.55f).Within(0.0005f), label.text);
                var horizontalOffset = Vector3.Dot(label.transform.position - center, label.transform.right);
                Assert.That(horizontalOffset, Is.EqualTo(0f).Within(0.0005f),
                    label.text + " must be centered over its lower terminal group");
                Assert.That(Vector3.Dot(-label.transform.forward,
                    (camera.transform.position - label.transform.position).normalized),
                    Is.GreaterThan(0f), label.text);
            }
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
        public IEnumerator CabinetTerminalBoardsUseOriginalNamedConnectionPoints()
        {
            var environment = GameObject.Find("OriginalLabEnvironment");
            var views = Object.FindObjectsOfType<ElectricalDeviceView>();
            AssertNamedBoardPort(environment, views, "DuanZiPai_1", "PLC_1_M0.0");
            AssertNamedBoardPort(environment, views, "DuanZiPai_1", "KA6_8");
            AssertNamedBoardPort(environment, views, "DuanZiPai_2", "PLC_1_Q0.0");
            AssertNamedBoardPort(environment, views, "DuanZiPai_2", "KA6_14");
            AssertNamedBoardPort(environment, views, "DuanZiPai_3", "G120_L1", "G120_l1");
            AssertNamedBoardPort(environment, views, "DuanZiPai_3", "KM1_53NO", "KM1_53no");
            AssertNamedBoardPort(environment, views, "DuanZiPai_3", "FR1_95NC", "FR1_95nc");
            AssertNamedBoardPort(environment, views, "DuanZiPai_3", "KT_A1", "a60");
            AssertNamedBoardPort(environment, views, "DuanZiPai_4", "G120_U2");
            AssertNamedBoardPort(environment, views, "DuanZiPai_4", "KM1_54NO");
            AssertNamedBoardPort(environment, views, "DuanZiPai_4", "FR1_96NC");
            AssertNamedBoardPort(environment, views, "DuanZiPai_6", "V_1", "v_1");
            AssertNamedBoardPort(environment, views, "DuanZiPai_6", "N_4", "n_4");
            AssertNamedBoardPort(environment, views, "DuanZiPai_7", "A_u1", "a_u1");
            AssertNamedBoardPort(environment, views, "DuanZiPai_7", "B_v2", "b_v2");
            AssertNamedBoardPort(environment, views, "DuanZiPai_7", "C_w1", "c_w1");
            AssertNamedBoardPort(environment, views, "DuanZiPai_8", "A_SIGNAL", "a_SIGNAL");
            AssertNamedBoardPort(environment, views, "DuanZiPai_8", "Diancifa3_GND", "diancifa3_GND");

            foreach (var definition in OriginalCabinetTerminalBoardMap.Boards)
            {
                var board = environment.GetComponentsInChildren<Transform>(true)
                    .Single(item => item.name == definition.DeviceId && item.Find("point") != null);
                var expectedCount = board.Find("point").Cast<Transform>()
                    .Count(item => OriginalCabinetTerminalBoardMap.IsTerminalName(definition, item.name));
                var view = views.Single(item => item.Runtime.DeviceId == definition.DeviceId);
                Assert.That(view.Ports.Count, Is.EqualTo(expectedCount));
                Assert.That(view.Ports.Count, Is.EqualTo(definition.ExpectedPortCount));
                Assert.That(view.Runtime.GetConductiveLinks().Count(), Is.EqualTo(expectedCount));
            }
            yield return null;
        }

        [UnityTest]
        public IEnumerator LowerCabinetDevicesExposeConnectionsOnlyOnTerminalBoards()
        {
            var routedDeviceIds = new[] { "KMF", "KM1", "KMR", "KM2", "KMB", "KB", "FR", "KT" };
            var views = Object.FindObjectsOfType<ElectricalDeviceView>();
            foreach (var deviceId in routedDeviceIds)
            {
                var view = views.Single(item => item.Runtime.DeviceId == deviceId);
                Assert.That(view.Ports, Is.Empty, deviceId + " must be routed through its original terminal board");
            }
            yield return null;
        }

        [UnityTest]
        public IEnumerator AllThreeMotorsExposeSixPhysicalConnectionPointsOnlyInJumperMode()
        {
            var controller = Object.FindObjectOfType<SimulationController>();
            var cameraController = Object.FindObjectOfType<TrainingCameraController>();
            var environment = GameObject.Find("OriginalLabEnvironment");
            var transforms = environment.GetComponentsInChildren<Transform>(true);
            var views = Object.FindObjectsOfType<ElectricalDeviceView>();
            var expectedPorts = new[] { "U", "V", "W", "U2", "V2", "W2" };

            cameraController.SetWiringView();
            controller.SetMode(SimulationMode.Wiring);
            controller.SetWireStyle(Color.red, 0.01f, "ElectricalWire");
            yield return null;

            var motors = new[]
            {
                new { Id = "M1", Nut = "38" },
                new { Id = "M_DOUBLE", Nut = "118" },
                new { Id = "M2", Nut = "49" }
            };

            foreach (var motor in motors)
            {
                var view = views.Single(item => item.Runtime.DeviceId == motor.Id);
                Assert.That(view.Ports.Select(item => item.PortName), Is.EquivalentTo(expectedPorts));
                Assert.That(view.Ports.Count, Is.EqualTo(6));

                foreach (var port in view.Ports)
                {
                    var terminalName = port.PortName.Length == 1 ? port.PortName + "1" : port.PortName;
                    var terminal = transforms.Single(item => item.name == terminalName &&
                        HasAncestor(item, motor.Nut) && HasAncestor(item, "point"));
                    Assert.That(Vector3.Distance(port.CurrentAnchorPosition, terminal.position), Is.LessThan(0.0005f),
                        motor.Id + "." + port.PortName + " must stay on the original motor terminal");
                    Assert.That(port.JumperOnly, Is.True);
                    Assert.That(port.IsVisible, Is.False);
                    Assert.That(port.GetComponent<MeshRenderer>().enabled, Is.False);
                    Assert.That(port.GetComponent<SphereCollider>().enabled, Is.False);
                }
            }

            controller.SetWireStyle(Color.red, 0.01f, "JumperLine");
            yield return null;

            foreach (var motor in motors)
            {
                var view = views.Single(item => item.Runtime.DeviceId == motor.Id);
                var positions = view.Ports.Select(item => item.transform.position).ToArray();
                for (var first = 0; first < positions.Length; first++)
                for (var second = first + 1; second < positions.Length; second++)
                    Assert.That(Vector3.Distance(positions[first], positions[second]), Is.GreaterThan(0.001f),
                        motor.Id + " connection markers must occupy six distinct terminals");
                foreach (var port in view.Ports)
                {
                    Assert.That(port.UsesJumperAnchor, Is.True);
                    Assert.That(port.IsVisible, Is.True);
                    Assert.That(port.GetComponent<MeshRenderer>().enabled, Is.True);
                    Assert.That(port.GetComponent<SphereCollider>().enabled, Is.True);
                }
            }
        }

        [UnityTest]
        public IEnumerator LineTypeShowsTheCorrectConnectionPointGroups()
        {
            var controller = Object.FindObjectOfType<SimulationController>();

            controller.SetMode(SimulationMode.Wiring);
            controller.SetWireStyle(Color.red, 0.01f, "ElectricalWire");
            yield return null;

            var ports = Object.FindObjectsOfType<ElectricalPortView>();
            var jumperPorts = ports.Where(port => port.JumperOnly).ToArray();
            var electricalPorts = ports.Where(port => port.ElectricalOnly).ToArray();
            var motorBoardPorts = ports.Where(port => port.DeviceId == "DuanZiPai_7").ToArray();
            Assert.That(jumperPorts.Length, Is.EqualTo(18));
            Assert.That(electricalPorts.Length, Is.GreaterThan(0));
            Assert.That(motorBoardPorts.Length, Is.EqualTo(18));
            Assert.That(motorBoardPorts.All(port => !port.JumperOnly && !port.ElectricalOnly), Is.True);
            Assert.That(jumperPorts.All(port => !port.IsVisible), Is.True);
            Assert.That(electricalPorts.All(port => port.IsVisible), Is.True);
            Assert.That(motorBoardPorts.All(port => port.IsVisible && !port.UsesJumperAnchor), Is.True);

            controller.SetWireStyle(Color.red, 0.01f, "JumperLine");
            yield return null;

            Assert.That(jumperPorts.All(port => port.IsVisible), Is.True);
            Assert.That(electricalPorts.All(port => !port.IsVisible), Is.True);
            Assert.That(motorBoardPorts.All(port => port.IsVisible && port.UsesJumperAnchor), Is.True);
        }

        [UnityTest]
        public IEnumerator ArrowedMotorTerminalStripAppearsInBothLineModes()
        {
            var controller = Object.FindObjectOfType<SimulationController>();
            var board = Object.FindObjectsOfType<ElectricalDeviceView>()
                .Single(view => view.Runtime.DeviceId == "DuanZiPai_7");

            Assert.That(board.Ports.Count, Is.EqualTo(18));
            Assert.That(board.Ports.All(port => !port.JumperOnly && !port.ElectricalOnly), Is.True);

            controller.SetMode(SimulationMode.Wiring);
            controller.SetWireStyle(Color.red, 0.01f, "ElectricalWire");
            yield return null;
            Assert.That(board.Ports.All(port => port.IsVisible && !port.UsesJumperAnchor), Is.True);

            controller.SetWireStyle(Color.red, 0.01f, "JumperLine");
            yield return null;
            Assert.That(board.Ports.All(port => port.IsVisible && port.UsesJumperAnchor), Is.True);
            Assert.That(board.Ports.All(port => port.GetComponent<MeshRenderer>().enabled), Is.True);
            Assert.That(board.Ports.All(port => port.GetComponent<SphereCollider>().enabled), Is.True);
        }

        [UnityTest]
        public IEnumerator MotorTerminalStripUsesLowerPointsInJumperMode()
        {
            var controller = Object.FindObjectOfType<SimulationController>();
            var cameraController = Object.FindObjectOfType<TrainingCameraController>();
            var environment = GameObject.Find("OriginalLabEnvironment");
            var port = Object.FindObjectsOfType<ElectricalDeviceView>()
                .Single(view => view.Runtime.DeviceId == "DuanZiPai_7").Ports
                .Single(view => view.PortName == "A_u1");
            var upper = environment.GetComponentsInChildren<Transform>(true)
                .Single(item => item.name == "a_u1" && HasAncestor(item, "DuanZiPai_7"));
            var lower = environment.GetComponentsInChildren<Transform>(true)
                .Single(item => item.name == "A_u1" && HasAncestor(item, "DuanZiPai_7"));

            cameraController.SetWiringView();
            controller.SetMode(SimulationMode.Wiring);
            controller.SetWireStyle(Color.red, 0.01f, "ElectricalWire");
            yield return null;
            Assert.That(Vector3.Distance(port.transform.position, upper.position), Is.LessThan(0.0005f));
            Assert.That(port.UsesJumperAnchor, Is.False);
            Assert.That(port.JumperOnly, Is.False);
            Assert.That(port.ElectricalOnly, Is.False);
            Assert.That(port.IsVisible, Is.True);
            Assert.That(port.GetComponent<MeshRenderer>().enabled, Is.True);
            Assert.That(port.GetComponent<SphereCollider>().enabled, Is.True);

            controller.SetWireStyle(Color.red, 0.01f, "JumperLine");
            yield return null;
            Assert.That(Vector3.Distance(port.transform.position, lower.position), Is.LessThan(0.0005f));
            Assert.That(lower.position.y, Is.LessThan(upper.position.y));
            Assert.That(port.UsesJumperAnchor, Is.True);
            Assert.That(port.IsVisible, Is.True);
            Assert.That(port.GetComponent<MeshRenderer>().enabled, Is.True);
            Assert.That(port.GetComponent<SphereCollider>().enabled, Is.True);
        }

        [UnityTest]
        public IEnumerator CabinetTerminalBoardsAppearOnlyInElectricalWireMode()
        {
            var controller = Object.FindObjectOfType<SimulationController>();
            var views = Object.FindObjectsOfType<ElectricalDeviceView>();
            var boardIds = OriginalCabinetTerminalBoardMap.Boards
                .Where(definition => definition.DeviceId != "DuanZiPai_7")
                .Select(definition => definition.DeviceId)
                .ToArray();

            controller.SetMode(SimulationMode.Wiring);
            controller.SetWireStyle(Color.red, 0.01f, "ElectricalWire");
            yield return null;
            Assert.That(views.Single(view => view.Runtime.DeviceId == "DuanZiPai_4").Ports.Count, Is.EqualTo(48));
            Assert.That(views.Single(view => view.Runtime.DeviceId == "DuanZiPai_6").Ports.Count, Is.EqualTo(8));
            Assert.That(views.Single(view => view.Runtime.DeviceId == "DuanZiPai_8").Ports.Count, Is.EqualTo(18));
            foreach (var boardId in boardIds)
            {
                var ports = views.Single(view => view.Runtime.DeviceId == boardId).Ports;
                Assert.That(ports.All(port => port.IsVisible), Is.True, boardId + " connection markers");
                Assert.That(ports.All(port => port.ElectricalOnly), Is.True, boardId + " electrical-only markers");
                Assert.That(ports.All(port => !port.UsesJumperAnchor), Is.True, boardId + " electrical anchors");
                Assert.That(ports.All(port => port.GetComponent<MeshRenderer>().enabled), Is.True);
                Assert.That(ports.All(port => port.GetComponent<SphereCollider>().enabled), Is.True);
            }

            controller.SetWireStyle(Color.red, 0.01f, "JumperLine");
            yield return null;
            foreach (var boardId in boardIds)
            {
                var ports = views.Single(view => view.Runtime.DeviceId == boardId).Ports;
                Assert.That(ports.All(port => !port.IsVisible), Is.True, boardId + " hidden in jumper mode");
                Assert.That(ports.All(port => !port.GetComponent<MeshRenderer>().enabled), Is.True);
                Assert.That(ports.All(port => !port.GetComponent<SphereCollider>().enabled), Is.True);
            }
        }

        [UnityTest]
        public IEnumerator SceneIoConnectionPointsUseTheUpperReferenceRow()
        {
            var controller = Object.FindObjectOfType<SimulationController>();
            var cameraController = Object.FindObjectOfType<TrainingCameraController>();
            var environment = GameObject.Find("OriginalLabEnvironment");
            var definition = OriginalCabinetTerminalBoardMap.Boards
                .Single(item => item.DeviceId == "DuanZiPai_8");
            var pointRoot = environment.GetComponentsInChildren<Transform>(true)
                .Single(item => item.name == definition.DeviceId && item.Find("point") != null)
                .Find("point");
            var ports = Object.FindObjectsOfType<ElectricalDeviceView>()
                .Single(view => view.Runtime.DeviceId == definition.DeviceId).Ports;

            cameraController.SetWiringView();
            controller.SetMode(SimulationMode.Wiring);
            controller.SetWireStyle(Color.red, 0.01f, "ElectricalWire");
            yield return null;

            foreach (var port in ports)
            {
                var upper = pointRoot.Find(port.PhysicalAnchorId);
                var lower = pointRoot.Find(OriginalCabinetTerminalBoardMap.GetJumperAnchorName(
                    definition, port.PhysicalAnchorId));
                Assert.That(upper, Is.Not.Null, port.PortName + " upper anchor");
                Assert.That(lower, Is.Not.Null, port.PortName + " lower anchor");
                Assert.That(Vector3.Distance(port.CurrentAnchorPosition, upper.position), Is.LessThan(0.0005f));
                Assert.That(port.IsVisible, Is.True);
                Assert.That(Camera.main.WorldToScreenPoint(upper.position).y,
                    Is.GreaterThan(Camera.main.WorldToScreenPoint(lower.position).y),
                    port.PortName + " must be displayed above the terminal strip");
            }
        }

        [UnityTest]
        public IEnumerator BrakeUnitDoesNotExposeGenericInOutConnectionPoints()
        {
            var brake = Object.FindObjectsOfType<ElectricalDeviceView>()
                .Single(item => item.Runtime.DeviceId == "BRAKE");
            Assert.That(brake.Ports, Is.Empty);
            Assert.That(Object.FindObjectsOfType<ElectricalPortView>()
                .Any(item => item.DeviceId == "BRAKE" || item.PortName == "IN" || item.PortName == "OUT"), Is.False);
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
        public IEnumerator OriginalViewMenuSwitchesBetweenOverviewAndFaultCloseUp()
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
            Assert.That(cameraController.transform.position, Is.EqualTo(cameraController.FaultPosition));
            Assert.That(cameraController.transform.position.z, Is.LessThan(-2.0f));
            Assert.That(Vector3.Dot(cameraController.transform.forward, Vector3.forward), Is.GreaterThan(0.95f));

            var cabinet = GameObject.Find("OriginalLabEnvironment")
                .GetComponentsInChildren<Transform>(true)
                .Single(item => item.name == "DQG01")
                .GetComponent<Renderer>();
            var cabinetCenter = Camera.main.WorldToViewportPoint(cabinet.bounds.center);
            var cabinetTop = Camera.main.WorldToViewportPoint(
                cabinet.bounds.center + Vector3.up * cabinet.bounds.extents.y);
            var cabinetBottom = Camera.main.WorldToViewportPoint(
                cabinet.bounds.center - Vector3.up * cabinet.bounds.extents.y);
            var directionToCabinet = (cabinet.bounds.center - cameraController.transform.position).normalized;
            Assert.That(cabinetCenter.z, Is.GreaterThan(0f));
            Assert.That(Vector3.Dot(cameraController.transform.forward, directionToCabinet), Is.GreaterThan(0.9999f));
            Assert.That(Vector3.Distance(cameraController.CurrentFaultTarget, cabinet.bounds.center), Is.LessThan(0.001f));
            Assert.That(cameraController.transform.position.z, Is.LessThan(cabinet.bounds.min.z - 0.5f));
            Assert.That(cabinetCenter.x, Is.EqualTo(0.5f).Within(0.001f));
            Assert.That(cabinetCenter.y, Is.EqualTo(0.5f).Within(0.001f));
            Assert.That(cabinetTop.y, Is.GreaterThan(0.88f));
            Assert.That(cabinetBottom.y, Is.LessThan(0.10f));

            viewButton.onClick.Invoke();
            ButtonWithText(menu, "接线视角").onClick.Invoke();
            yield return null;
            Assert.That(cameraController.CurrentPreset, Is.EqualTo(TrainingViewPreset.WiringFront));
            Assert.That(Vector3.Dot(cameraController.transform.forward, Vector3.back), Is.GreaterThan(0.95f));
        }

        [UnityTest]
        public IEnumerator LowerCabinetLineTypesStayOnLowerOriginalAnchors()
        {
            var controller = Object.FindObjectOfType<SimulationController>();
            var cameraController = Object.FindObjectOfType<TrainingCameraController>();
            var environment = GameObject.Find("OriginalLabEnvironment");
            var port = Object.FindObjectsOfType<ElectricalDeviceView>()
                .Single(view => view.Runtime.DeviceId == "DuanZiPai_4").Ports
                .Single(view => view.PortName == "FR2_6T3");

            cameraController.SetWiringView();
            controller.SetWireStyle(Color.red, 0.01f, "ElectricalWire");
            yield return null;
            var upper = environment.GetComponentsInChildren<Transform>(true)
                .First(item => item.name == "FR2_6t3" && HasAncestor(item, "DuanZiPai_4"));
            var lower = environment.GetComponentsInChildren<Transform>(true)
                .First(item => item.name == "FR2_6T3" && HasAncestor(item, "DuanZiPai_4"));
            Assert.That(Vector3.Distance(port.transform.position, lower.position), Is.LessThan(0.0005f));
            Assert.That(lower.position.y, Is.LessThan(upper.position.y));
            Assert.That(port.UsesJumperAnchor, Is.False);

            controller.SetWireStyle(Color.red, 0.01f, "JumperLine");
            yield return null;
            Assert.That(Vector3.Distance(port.transform.position, lower.position), Is.LessThan(0.0005f));
            Assert.That(Vector3.Distance(upper.position, lower.position), Is.GreaterThan(0.01f));
            Assert.That(port.UsesJumperAnchor, Is.True);

            cameraController.SetFaultView();
            yield return null;
            Assert.That(Vector3.Distance(port.transform.position, lower.position), Is.LessThan(0.0005f));
        }

        [UnityTest]
        public IEnumerator UpperCabinetLineTypesStayOnOriginalUpperAnchors()
        {
            var controller = Object.FindObjectOfType<SimulationController>();
            var cameraController = Object.FindObjectOfType<TrainingCameraController>();
            var environment = GameObject.Find("OriginalLabEnvironment");
            var port = Object.FindObjectsOfType<ElectricalDeviceView>()
                .Single(view => view.Runtime.DeviceId == "DuanZiPai_3").Ports
                .Single(view => view.PortName == "FR1_95NC");
            var upper = environment.GetComponentsInChildren<Transform>(true)
                .First(item => item.name == "FR1_95nc" && HasAncestor(item, "DuanZiPai_3"));
            var lower = environment.GetComponentsInChildren<Transform>(true)
                .First(item => item.name == "FR1_95NC" && HasAncestor(item, "DuanZiPai_3"));

            cameraController.SetWiringView();
            controller.SetWireStyle(Color.red, 0.01f, "ElectricalWire");
            yield return null;
            Assert.That(Vector3.Distance(port.transform.position, upper.position), Is.LessThan(0.0005f));
            Assert.That(upper.position.y, Is.GreaterThan(lower.position.y));
            Assert.That(port.UsesJumperAnchor, Is.False);

            controller.SetWireStyle(Color.red, 0.01f, "JumperLine");
            yield return null;
            Assert.That(Vector3.Distance(port.transform.position, upper.position), Is.LessThan(0.0005f));
            Assert.That(port.UsesJumperAnchor, Is.True);

            cameraController.SetFaultView();
            yield return null;
            Assert.That(Vector3.Distance(port.transform.position, upper.position), Is.LessThan(0.0005f));
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

        private static int CountBrightYellowPixels(Camera camera, Texture2D frame, Bounds bounds)
        {
            var min = new Vector2(float.PositiveInfinity, float.PositiveInfinity);
            var max = new Vector2(float.NegativeInfinity, float.NegativeInfinity);
            for (var corner = 0; corner < 8; corner++)
            {
                var world = bounds.center + Vector3.Scale(bounds.extents, new Vector3(
                    (corner & 1) == 0 ? -1f : 1f,
                    (corner & 2) == 0 ? -1f : 1f,
                    (corner & 4) == 0 ? -1f : 1f));
                var viewport = camera.WorldToViewportPoint(world);
                if (viewport.z <= 0f) continue;
                min = Vector2.Min(min, viewport);
                max = Vector2.Max(max, viewport);
            }

            var xMin = Mathf.Clamp(Mathf.FloorToInt(min.x * frame.width) - 3, 0, frame.width - 1);
            var xMax = Mathf.Clamp(Mathf.CeilToInt(max.x * frame.width) + 3, 0, frame.width - 1);
            var yMin = Mathf.Clamp(Mathf.FloorToInt(min.y * frame.height) - 3, 0, frame.height - 1);
            var yMax = Mathf.Clamp(Mathf.CeilToInt(max.y * frame.height) + 3, 0, frame.height - 1);
            var count = 0;
            for (var y = yMin; y <= yMax; y++)
            for (var x = xMin; x <= xMax; x++)
            {
                var pixel = frame.GetPixel(x, y);
                if (pixel.r > 0.72f && pixel.g > 0.58f && pixel.b < 0.28f && pixel.a > 0.5f) count++;
            }
            return count;
        }

        private static Rect GetViewportRect(Camera camera, Bounds bounds)
        {
            var min = new Vector2(float.PositiveInfinity, float.PositiveInfinity);
            var max = new Vector2(float.NegativeInfinity, float.NegativeInfinity);
            for (var corner = 0; corner < 8; corner++)
            {
                var world = bounds.center + Vector3.Scale(bounds.extents, new Vector3(
                    (corner & 1) == 0 ? -1f : 1f,
                    (corner & 2) == 0 ? -1f : 1f,
                    (corner & 4) == 0 ? -1f : 1f));
                var viewport = camera.WorldToViewportPoint(world);
                min = Vector2.Min(min, viewport);
                max = Vector2.Max(max, viewport);
            }
            return Rect.MinMaxRect(min.x, min.y, max.x, max.y);
        }

        private static void AssertNamedBoardPort(
            GameObject environment,
            ElectricalDeviceView[] views,
            string boardId,
            string terminalName,
            string physicalAnchorName = null)
        {
            var board = environment.GetComponentsInChildren<Transform>(true)
                .Single(item => item.name == boardId && item.Find("point") != null);
            physicalAnchorName = string.IsNullOrWhiteSpace(physicalAnchorName) ? terminalName : physicalAnchorName;
            var anchor = board.Find("point/" + physicalAnchorName);
            Assert.That(anchor, Is.Not.Null, boardId + "/" + physicalAnchorName + " original anchor");
            var port = views.Single(item => item.Runtime.DeviceId == boardId)
                .Ports.Single(item => item.PortName == terminalName);
            Assert.That(port.HoverLabel, Is.EqualTo(terminalName));
            Assert.That(port.PhysicalAnchorId, Is.EqualTo(physicalAnchorName));
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
