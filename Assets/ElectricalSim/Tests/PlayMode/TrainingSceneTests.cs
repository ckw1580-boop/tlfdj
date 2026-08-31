using System.Collections;
using System.Collections.Generic;
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
        public IEnumerator CabinetBreakersAnimateAndGateTheMainBreaker()
        {
            var controller = Object.FindObjectOfType<SimulationController>();
            var breakers = controller.CabinetBreakers.OrderBy(item => item.BreakerId).ToArray();
            Assert.That(breakers.Select(item => item.BreakerId), Is.EqualTo(new[] { "106", "122" }));
            Assert.That(breakers.All(item => item.IsClosed), Is.True);
            Assert.That(breakers.All(item => item.Handle != null && item.Pivot != null), Is.True);
            Assert.That(breakers.All(item => item.InteractionCollider != null &&
                                             item.InteractionCollider.enabled &&
                                             item.InteractionCollider.GetComponentInParent<CabinetBreakerInteractable>() == item),
                Is.True,
                "Both original picker colliders must resolve to their cabinet breaker interaction");

            var cameraController = Object.FindObjectOfType<TrainingCameraController>();
            cameraController.SetFaultView();
            yield return null;
            Physics.SyncTransforms();
            foreach (var breaker in breakers)
            {
                var direction = breaker.InteractionCollider.bounds.center - Camera.main.transform.position;
                Assert.That(Physics.Raycast(Camera.main.transform.position, direction.normalized, out var hit, 100f),
                    Is.True);
                Assert.That(hit.collider.GetComponentInParent<CabinetBreakerInteractable>(), Is.EqualTo(breaker),
                    $"The troubleshooting camera must have an unobstructed ray to breaker {breaker.BreakerId}");
            }

            var mainBreaker = Object.FindObjectsOfType<ElectricalDeviceView>()
                .Single(item => item.Runtime.DeviceId == "QF").Runtime;
            var closedRotations = breakers.ToDictionary(item => item.BreakerId, item => item.Handle.localRotation);
            var closedHandleHeights = breakers.ToDictionary(
                item => item.BreakerId,
                item => item.transform.InverseTransformPoint(item.Handle.position).y);
            var baseHandleColors = breakers.ToDictionary(
                item => item.BreakerId,
                item => item.Handle.GetComponent<Renderer>().material.color);
            Assert.That(breakers.All(item => !item.IsHighlighted), Is.True);
            Assert.That(breakers.All(item => item.HighlightRenderers.Count > 0 &&
                                             item.HighlightRenderers.All(renderer => !renderer.enabled)),
                Is.True,
                "Both breaker handles must have a hidden yellow outline outside drag mode");

            Assert.That(controller.TryToggleCabinetBreaker(breakers[0]), Is.False,
                "Cabinet breakers must ignore interactions outside drag mode");
            Assert.That(breakers[0].IsClosed, Is.True);

            controller.SetMode(SimulationMode.Drag);
            Assert.That(breakers.All(item => item.IsHighlighted), Is.True);
            foreach (var breaker in breakers)
            {
                Assert.That(breaker.HighlightRenderers.All(renderer => renderer.enabled), Is.True,
                    $"Breaker {breaker.BreakerId} yellow outline must be visible in drag mode");
                Assert.That(Vector4.Distance(
                        breaker.Handle.GetComponent<Renderer>().material.color,
                        baseHandleColors[breaker.BreakerId]),
                    Is.LessThan(0.001f),
                    $"Breaker {breaker.BreakerId} must retain its original blue material");
            }
            Assert.That(controller.TryToggleCabinetBreaker(breakers[0]), Is.True);
            yield return new WaitForSecondsRealtime(breakers[0].AnimationDuration * 0.25f);
            var midAnimationAngle = Quaternion.Angle(
                breakers[0].Handle.localRotation,
                closedRotations[breakers[0].BreakerId]);
            Assert.That(midAnimationAngle, Is.GreaterThan(0.1f).And.LessThan(40f));
            Assert.That(controller.TryToggleCabinetBreaker(breakers[0]), Is.True,
                "Clicking again during the animation must reverse from the current pose");
            yield return new WaitForSecondsRealtime(breakers[0].AnimationDuration + 0.05f);
            Assert.That(breakers[0].IsClosed, Is.True);
            Assert.That(mainBreaker.IsClosed, Is.True);
            Assert.That(Quaternion.Angle(
                    breakers[0].Handle.localRotation,
                    closedRotations[breakers[0].BreakerId]),
                Is.LessThan(0.01f));

            Assert.That(controller.TryToggleCabinetBreaker(breakers[0]), Is.True);
            Assert.That(breakers[0].IsClosed, Is.False);
            Assert.That(mainBreaker.IsClosed, Is.False);
            Assert.That(mainBreaker.GetConductiveLinks(), Is.Empty);
            yield return new WaitForSecondsRealtime(breakers[0].AnimationDuration + 0.05f);
            Assert.That(Quaternion.Angle(breakers[0].Handle.localRotation, closedRotations[breakers[0].BreakerId]),
                Is.GreaterThan(40f));
            Assert.That(breakers[0].transform.InverseTransformPoint(breakers[0].Handle.position).y,
                Is.LessThan(closedHandleHeights[breakers[0].BreakerId]),
                "The open handle must move downward instead of rotating into the breaker housing");
            Assert.That(breakers[0].InteractionCollider.bounds.Intersects(
                    breakers[0].Handle.GetComponent<Renderer>().bounds),
                Is.True,
                "The open handle must remain inside the breaker interaction area");

            Assert.That(controller.TryToggleCabinetBreaker(breakers[1]), Is.True);
            yield return new WaitForSecondsRealtime(breakers[1].AnimationDuration + 0.05f);
            Assert.That(Quaternion.Angle(breakers[1].Handle.localRotation, closedRotations[breakers[1].BreakerId]),
                Is.GreaterThan(40f));
            Assert.That(breakers[1].transform.InverseTransformPoint(breakers[1].Handle.position).y,
                Is.LessThan(closedHandleHeights[breakers[1].BreakerId]),
                "The open handle must move downward instead of rotating into the breaker housing");
            Assert.That(breakers[1].InteractionCollider.bounds.Intersects(
                    breakers[1].Handle.GetComponent<Renderer>().bounds),
                Is.True,
                "The open handle must remain inside the breaker interaction area");
            Assert.That(mainBreaker.IsClosed, Is.False);
            Assert.That(controller.TryToggleCabinetBreaker(breakers[0]), Is.True);
            Assert.That(mainBreaker.IsClosed, Is.False,
                "Closing only one physical breaker must not restore the main circuit");
            Assert.That(controller.TryToggleCabinetBreaker(breakers[1]), Is.True);
            Assert.That(mainBreaker.IsClosed, Is.True);
            Assert.That(mainBreaker.GetConductiveLinks().Count(), Is.EqualTo(3));

            controller.TryToggleCabinetBreaker(breakers[0]);
            yield return null;
            controller.ResetTraining();
            Assert.That(controller.AreCabinetBreakersClosed, Is.True);
            Assert.That(mainBreaker.IsClosed, Is.True);
            foreach (var breaker in breakers)
            {
                Assert.That(breaker.IsHighlighted, Is.False);
                Assert.That(breaker.HighlightRenderers.All(renderer => !renderer.enabled), Is.True);
                Assert.That(Vector4.Distance(
                        breaker.Handle.GetComponent<Renderer>().material.color,
                        baseHandleColors[breaker.BreakerId]),
                    Is.LessThan(0.001f));
                Assert.That(breaker.IsClosed, Is.True);
                Assert.That(Quaternion.Angle(breaker.Handle.localRotation, closedRotations[breaker.BreakerId]),
                    Is.LessThan(0.01f));
            }
        }

        [UnityTest]
        public IEnumerator TaskEvaluationDoesNotBypassAnOpenCabinetBreaker()
        {
            var controller = Object.FindObjectOfType<SimulationController>();
            var breaker = controller.CabinetBreakers.Single(item => item.BreakerId == "106");
            var mainBreaker = Object.FindObjectsOfType<ElectricalDeviceView>()
                .Single(item => item.Runtime.DeviceId == "QF").Runtime;

            controller.LoadReferenceWiring();
            controller.SetMode(SimulationMode.Drag);
            Assert.That(controller.TryToggleCabinetBreaker(breaker), Is.True);
            controller.SubmitTask();
            yield return null;

            Assert.That(breaker.IsClosed, Is.False);
            Assert.That(mainBreaker.IsClosed, Is.False,
                "The task action initializer must preserve the physical breaker interlock");
            controller.StopAllCoroutines();
        }

        [UnityTest]
        public IEnumerator CabinetBreakerConnectionPointsAppearOnlyInElectricalWiringMode()
        {
            var controller = Object.FindObjectOfType<SimulationController>();
            var views = Object.FindObjectsOfType<ElectricalDeviceView>();
            var breaker106 = views.Single(item => item.Runtime.DeviceId == "QF106");
            var breaker122 = views.Single(item => item.Runtime.DeviceId == "QF122");
            Assert.That(breaker106.Ports.Select(item => item.PortName),
                Is.EquivalentTo(new[] { "L1", "L3", "L5", "L2", "L4", "L6" }));
            Assert.That(breaker122.Ports.Select(item => item.PortName),
                Is.EquivalentTo(new[] { "N1", "L1", "L3", "L5", "N2", "L2", "L4", "L6" }));

            var allPorts = breaker106.Ports.Concat(breaker122.Ports).ToArray();
            Assert.That(allPorts.All(item => item.HoverLabel == item.PortName), Is.True,
                "Breaker connection-point remarks should only display the terminal name.");
            Assert.That(allPorts.All(item => !item.HoverLabel.StartsWith("106-") && !item.HoverLabel.StartsWith("122-")), Is.True,
                "Breaker connection-point remarks should not include the cabinet-number prefix.");
            Assert.That(allPorts.All(item => item.CurrentAnchor != null &&
                                              item.CurrentAnchor.parent != null &&
                                              item.CurrentAnchor.parent.name == "point"),
                Is.True,
                "Every breaker marker must use a detected original point Transform");
            Assert.That(allPorts.All(item => item.ElectricalOnly && item.WiringModeOnly), Is.True);
            Assert.That(allPorts.All(item => !item.IsVisible), Is.True);

            var expectedAliases = new Dictionary<string, string>
            {
                ["L1"] = "QF.L1",
                ["L3"] = "QF.L2",
                ["L5"] = "QF.L3",
                ["L2"] = "QF.T1",
                ["L4"] = "QF.T2",
                ["L6"] = "QF.T3"
            };
            foreach (var view in new[] { breaker106, breaker122 })
            {
                var links = view.Runtime.GetConductiveLinks().ToDictionary(item => item.A, item => item.B);
                Assert.That(links, Is.EquivalentTo(expectedAliases));
                Assert.That(links.ContainsKey("N1") || links.ContainsKey("N2"), Is.False,
                    "The four-pole neutral anchors must not add a neutral circuit topology");
            }

            controller.SetMode(SimulationMode.Fault);
            yield return null;
            Assert.That(allPorts.All(item => !item.IsVisible), Is.True);

            controller.SetWireStyle(Color.red, 0.01f, "ElectricalWire");
            controller.SetMode(SimulationMode.Wiring);
            yield return null;
            Assert.That(allPorts.All(item => item.IsVisible && item.GetComponent<Collider>().enabled), Is.True);
            Assert.That(allPorts.All(item => Vector3.Distance(item.transform.position, item.CurrentAnchor.position) < 0.001f),
                Is.True);
            Physics.SyncTransforms();
            foreach (var port in allPorts)
            {
                var direction = port.transform.position - Camera.main.transform.position;
                Assert.That(Physics.Raycast(Camera.main.transform.position, direction.normalized, out var hit, 100f),
                    Is.True,
                    port.HoverLabel);
                Assert.That(hit.collider.GetComponentInParent<ElectricalPortView>(), Is.EqualTo(port),
                    $"The electrical wiring camera must directly hit {port.HoverLabel}");
            }

            controller.SetWireStyle(Color.red, 0.01f, "JumperLine");
            yield return null;
            Assert.That(allPorts.All(item => !item.IsVisible), Is.True);

            controller.SetWireStyle(Color.red, 0.01f, "ElectricalWire");
            controller.SetMode(SimulationMode.Drag);
            yield return null;
            Assert.That(allPorts.All(item => !item.IsVisible), Is.True);
        }

        [UnityTest]
        public IEnumerator FaultViewRestoresButtonTerminalStripConnectionPointsAndAnnotation()
        {
            Assert.That(GameObject.Find("Fault Button Terminal Strip"), Is.Null,
                "No generated terminal-strip model should remain");
            var environment = GameObject.Find("OriginalLabEnvironment");
            var board = environment.GetComponentsInChildren<Transform>(true)
                .Single(item => item.name == "DuanZiPai_5" && item.Find("point") != null);
            var pointRoot = board.Find("point");
            Assert.That(pointRoot, Is.Not.Null);
            var physicalAnchorNames = new[]
            {
                "a1", "a2", "a3", "a4", "a5", "a6",
                "a7", "a8", "a9", "a10", "a11", "a12"
            };
            Assert.That(physicalAnchorNames.All(item => pointRoot.Find(item) != null), Is.True);

            var label = Object.FindObjectsOfType<TextMesh>(true)
                .Single(item => item.text == "按钮（SB）端子区");
            var orientationReference = Object.FindObjectsOfType<TextMesh>(true)
                .Single(item => item.text == "三相电源端子区");
            var labelRenderer = label.GetComponent<MeshRenderer>();
            Assert.That(labelRenderer, Is.Not.Null);
            Assert.That(label.GetComponent("BackViewPersistentRendererVisibility"), Is.Not.Null);
            var expectedOppositeRotation = orientationReference.transform.rotation *
                                           Quaternion.Euler(0f, 180f, 0f);
            Assert.That(Quaternion.Angle(label.transform.rotation, expectedOppositeRotation),
                Is.LessThan(0.01f),
                "The SB annotation must use the opposite-facing three-phase annotation direction");
            Assert.That(Vector3.Distance(label.transform.localScale, orientationReference.transform.localScale),
                Is.LessThan(0.0001f),
                "The SB annotation must exactly match the three-phase annotation size");
            var anchorCenter = physicalAnchorNames
                .Select(item => pointRoot.Find(item).position)
                .Aggregate(Vector3.zero, (sum, position) => sum + position) /
                               physicalAnchorNames.Length;
            var surfacePosition = anchorCenter + orientationReference.transform.forward * 0.0025f;
            var verticalOffset = Vector3.Dot(
                label.transform.position - surfacePosition,
                label.transform.up);
            Assert.That(verticalOffset,
                Is.EqualTo(labelRenderer.bounds.size.y * 1.15f).Within(0.0001f),
                "The SB annotation must be raised by one additional glyph height");
            Assert.That(labelRenderer.enabled, Is.False,
                "The troubleshooting annotation must not appear in the normal cabinet view");

            var controller = Object.FindObjectOfType<SimulationController>();
            Assert.That(controller, Is.Not.Null);
            controller.SetMode(SimulationMode.Fault);
            yield return null;

            Assert.That(labelRenderer.enabled, Is.True);
            var camera = Camera.main;
            Assert.That(camera, Is.Not.Null);
            Assert.That(Vector3.Dot(-label.transform.forward,
                    (camera.transform.position - label.transform.position).normalized),
                Is.GreaterThan(0f),
                "The SB annotation front face must point toward the troubleshooting camera");
            var faultCameraController = camera.GetComponent<TrainingCameraController>();
            Assert.That(faultCameraController, Is.Not.Null);
            var faultDistance = Vector3.Distance(camera.transform.position, label.transform.position);
            camera.transform.position = label.transform.position + label.transform.forward * faultDistance;
            yield return null;
            Assert.That(labelRenderer.enabled, Is.False,
                "The SB annotation must be hidden when the user walks around to the cabinet front while fault mode remains active");
            faultCameraController.SetFaultView();
            yield return null;
            Assert.That(labelRenderer.enabled, Is.True);
            var boardView = Object.FindObjectsOfType<ElectricalDeviceView>()
                .Single(item => item.Runtime.DeviceId == OriginalTerminalBoardMap.DeviceId);
            var buttonPorts = boardView.Ports
                .Where(item => item.HoverLabel.StartsWith("SB1_", System.StringComparison.OrdinalIgnoreCase) ||
                               item.HoverLabel.StartsWith("SB2_", System.StringComparison.OrdinalIgnoreCase) ||
                               item.HoverLabel.StartsWith("SB3_", System.StringComparison.OrdinalIgnoreCase))
                .ToArray();
            Assert.That(buttonPorts.Length, Is.EqualTo(12));
            Assert.That(buttonPorts.All(item => item.CurrentAnchor != null &&
                                                item.CurrentAnchor.IsChildOf(board)), Is.True);
            Assert.That(buttonPorts.Select(item => item.CurrentAnchor.name),
                Is.EquivalentTo(physicalAnchorNames));
            Assert.That(buttonPorts.Single(item => item.HoverLabel == "SB3_COM2").CurrentAnchor.name,
                Is.EqualTo("a12"));

            foreach (var nonFaultMode in new[]
                     {
                         SimulationMode.View,
                         SimulationMode.Drag,
                         SimulationMode.Simulate
                     })
            {
                controller.SetMode(nonFaultMode);
                yield return null;
                Assert.That(labelRenderer.enabled, Is.True,
                    $"The SB annotation must remain visible behind the cabinet in {nonFaultMode} mode");
                controller.SetMode(SimulationMode.Fault);
                yield return null;
                Assert.That(labelRenderer.enabled, Is.True);
            }

            controller.SetMode(SimulationMode.Wiring);
            yield return null;
            Assert.That(labelRenderer.enabled, Is.True,
                "The SB annotation must remain visible in electrical-wire mode");
            Assert.That(buttonPorts.All(item => item.CurrentAnchor != null &&
                                                item.CurrentAnchor.IsChildOf(board) &&
                                                item.IsVisible), Is.True,
                "Wiring entered from fault view must retain the rear SB connection points");

            controller.SetWireStyle(Color.red, 0.01f, "JumperLine");
            yield return null;
            Assert.That(labelRenderer.enabled, Is.True,
                "Changing to jumper mode must not hide the SB annotation");
            Assert.That(buttonPorts.All(item => !item.IsVisible), Is.True,
                "The rear SB contact points must be hidden in jumper mode");

            controller.SetWireStyle(Color.red, 0.01f, "ElectricalWire");
            yield return null;
            Assert.That(labelRenderer.enabled, Is.True);
            Assert.That(buttonPorts.All(item => item.IsVisible &&
                                                item.CurrentAnchor != null &&
                                                item.CurrentAnchor.IsChildOf(board)), Is.True,
                "The rear SB contact points must return only in electrical-wire mode");
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
        public IEnumerator InverterLowerTerminalBoardAnnotationsUseOriginalLowerBoardGroups()
        {
            var environment = GameObject.Find("OriginalLabEnvironment");
            Assert.That(environment, Is.Not.Null);
            var generatedRoot = environment.transform.Find("Terminal Board Annotations");
            Assert.That(generatedRoot, Is.Not.Null);
            var orientationReference = generatedRoot.Find("Terminal Annotation - Three Phase Power");
            Assert.That(orientationReference, Is.Not.Null);
            var board = environment.GetComponentsInChildren<Transform>(true)
                .Single(item => item.name == "DuanZiPai_4" && item.Find("point") != null);
            var pointRoot = board.Find("point");
            var camera = Camera.main;
            Assert.That(camera, Is.Not.Null);
            var oldAnnotationBoard = environment.GetComponentsInChildren<Transform>(true)
                .Single(item => item.name == "DuanZiPai_5");
            Assert.That(oldAnnotationBoard.GetComponentsInChildren<Canvas>(true), Is.Empty,
                "The old DuanZiPai_5 Canvas annotations must be removed");
            Assert.That(oldAnnotationBoard.GetComponentsInChildren<Component>(true)
                    .Where(item => item.GetType().Name == "TMP_Text"), Is.Empty,
                "The old DuanZiPai_5 TMP annotations must be removed");

            var expectedAnnotations = new[]
            {
                new
                {
                    ObjectName = "Terminal Annotation - G120 Inverter Below Inverter",
                    Text = "G120变频器端子区", Prefix = "G120_"
                },
                new
                {
                    ObjectName = "Terminal Annotation - Contactors KM Below Inverter",
                    Text = "交流接触器（KM）端子区", Prefix = "KM"
                },
                new
                {
                    ObjectName = "Terminal Annotation - FR Below Inverter",
                    Text = "FR端子区KT端子区", Prefix = "FR"
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
                    .Where(item => item.name.StartsWith(expected.Prefix, System.StringComparison.Ordinal))
                    .ToArray();
                Assert.That(anchors, Is.Not.Empty, label.text);
                var center = anchors.Aggregate(Vector3.zero, (sum, anchor) => sum + anchor.position) / anchors.Length;
                var verticalOffset = Vector3.Dot(label.transform.position - center, label.transform.up);
                Assert.That(verticalOffset,
                    Is.EqualTo(renderer.bounds.size.y * -0.95f).Within(0.0005f), label.text);
                var horizontalOffset = Vector3.Dot(label.transform.position - center, label.transform.right);
                Assert.That(horizontalOffset, Is.EqualTo(0f).Within(0.0005f),
                    label.text + " must be centered over its lower terminal group");
                Assert.That(Vector3.Dot(-label.transform.forward,
                    (camera.transform.position - label.transform.position).normalized),
                    Is.GreaterThan(0f), label.text);
            }

            var upperAnnotations = new[]
            {
                new { ObjectName = "Terminal Annotation - G120 Inverter Upper", Text = "G120变频器端子区" },
                new { ObjectName = "Terminal Annotation - Contactors KM Upper", Text = "交流接触器（KM）端子区" },
                new { ObjectName = "Terminal Annotation - FR Upper", Text = "FR端子区" }
            };
            foreach (var expectedUpper in upperAnnotations)
            {
                var label = generatedRoot.Find(expectedUpper.ObjectName)?.GetComponent<TextMesh>();
                Assert.That(label, Is.Not.Null, expectedUpper.ObjectName);
                Assert.That(label.text, Is.EqualTo(expectedUpper.Text));
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

            var camera = Camera.main;
            var wireViews = Object.FindObjectsOfType<ElectricalWireView>();
            Assert.That(wireViews.Length, Is.EqualTo(controller.Graph.Wires.Count));
            foreach (var wireView in wireViews)
            {
                var renderer = wireView.GetComponent<LineRenderer>();
                var points = new Vector3[renderer.positionCount];
                renderer.GetPositions(points);
                var depths = points
                    .Select(point => camera.transform.InverseTransformPoint(point).z)
                    .ToArray();
                Assert.That(depths.Max() - depths.Min(), Is.LessThan(0.0001f),
                    $"{wireView.name} crosses the cabinet depth and may be partially hidden.");
            }
        }

        [UnityTest]
        public IEnumerator DeviceBodyConnectionPointsAppearOnlyInFaultView()
        {
            var controller = Object.FindObjectOfType<SimulationController>();
            var cameraController = Object.FindObjectOfType<TrainingCameraController>();
            var terminalBoardIds = new HashSet<string>(
                OriginalCabinetTerminalBoardMap.Boards.Select(item => item.DeviceId))
            {
                OriginalTerminalBoardMap.DeviceId
            };
            var faultDevices = new[]
            {
                new { Id = "KMF", PortCount = 18, BackNut = "111" },
                new { Id = "KM1", PortCount = 18, BackNut = "112" },
                new { Id = "KMR", PortCount = 18, BackNut = "113" },
                new { Id = "FR", PortCount = 10, BackNut = "114" }
            };
            var faultDeviceIds = new HashSet<string>(faultDevices.Select(item => item.Id));
            var deviceViews = Object.FindObjectsOfType<ElectricalDeviceView>()
                .Where(view => !terminalBoardIds.Contains(view.Runtime.DeviceId) &&
                               !faultDeviceIds.Contains(view.Runtime.DeviceId) &&
                               view.Runtime.Kind != ElectricalDeviceKind.Motor)
                .ToArray();

            Assert.That(deviceViews, Is.Not.Empty);
            foreach (var view in deviceViews)
                Assert.That(view.Ports, Is.Empty,
                    view.Runtime.DeviceId + " must use the original cabinet terminal boards");

            Assert.That(Object.FindObjectsOfType<ElectricalPortView>()
                .All(port => terminalBoardIds.Contains(port.DeviceId) ||
                             faultDeviceIds.Contains(port.DeviceId) ||
                             new[] { "M1", "M_DOUBLE", "M2" }.Contains(port.DeviceId)), Is.True);

            controller.SetMode(SimulationMode.Wiring);
            controller.SetWireStyle(Color.red, 0.01f, "ElectricalWire");
            yield return null;
            foreach (var device in faultDevices)
            {
                var ports = Object.FindObjectsOfType<ElectricalDeviceView>()
                    .Single(view => view.Runtime.DeviceId == device.Id).Ports;
                Assert.That(ports.Count, Is.EqualTo(device.PortCount));
                Assert.That(ports.All(port => port.CurrentAnchor == null), Is.True);
                Assert.That(ports.All(port => !port.IsVisible), Is.True);
                Assert.That(ports.All(port => !port.GetComponent<SphereCollider>().enabled), Is.True);
            }

            controller.SetMode(SimulationMode.Fault);
            yield return null;
            Assert.That(cameraController.CurrentPreset, Is.EqualTo(TrainingViewPreset.FaultBack));
            foreach (var device in faultDevices)
            {
                var ports = Object.FindObjectsOfType<ElectricalDeviceView>()
                    .Single(view => view.Runtime.DeviceId == device.Id).Ports;
                Assert.That(ports.All(port => port.CurrentAnchor != null &&
                                                   HasAncestor(port.CurrentAnchor, device.BackNut)), Is.True);
                Assert.That(ports.All(port => port.IsVisible), Is.True);
                Assert.That(ports.All(port => port.GetComponent<MeshRenderer>().enabled), Is.True);
                Assert.That(ports.All(port => port.GetComponent<SphereCollider>().enabled), Is.True);
            }

            var thermalRelayPorts = Object.FindObjectsOfType<ElectricalDeviceView>()
                .Single(view => view.Runtime.DeviceId == "FR").Ports;
            var t1 = thermalRelayPorts.Single(port => port.PortName == "T1");
            var t2 = thermalRelayPorts.Single(port => port.PortName == "T2");
            var t3 = thermalRelayPorts.Single(port => port.PortName == "T3");
            var expectedT2Position = Vector3.Lerp(t1.CurrentAnchorPosition, t3.CurrentAnchorPosition, 0.5f);
            Assert.That(t2.HoverLabel, Is.EqualTo("4T2"));
            Assert.That(t2.CurrentAnchor.name, Is.EqualTo("FR_4T2_FaultAnchor"));
            Assert.That(Vector3.Distance(t2.CurrentAnchorPosition, expectedT2Position), Is.LessThan(0.0005f));

            controller.SetWireStyle(Color.red, 0.01f, "JumperLine");
            yield return null;
            Assert.That(new[] { t1, t2, t3 }.All(port =>
                port.IsVisible && port.UsesJumperAnchor), Is.True,
                "2T1, 4T2 and 6T3 must remain visible in fault-view jumper mode");
            Assert.That(thermalRelayPorts
                .Except(new[] { t1, t2, t3 })
                .All(port => !port.IsVisible), Is.True,
                "Other FR body terminals must remain electrical-wire-only");
        }

        [UnityTest]
        public IEnumerator WiringLineTypesFollowActualCameraSideWithoutEnteringFaultMode()
        {
            var controller = Object.FindObjectOfType<SimulationController>();
            var cameraController = Object.FindObjectOfType<TrainingCameraController>();
            var topBoard = Object.FindObjectsOfType<ElectricalDeviceView>()
                .Single(view => view.Runtime.DeviceId == OriginalTerminalBoardMap.DeviceId);
            var faultButtonPorts = topBoard.Ports
                .Where(port => port.HoverLabel.StartsWith("SB1_", System.StringComparison.OrdinalIgnoreCase) ||
                               port.HoverLabel.StartsWith("SB2_", System.StringComparison.OrdinalIgnoreCase) ||
                               port.HoverLabel.StartsWith("SB3_", System.StringComparison.OrdinalIgnoreCase))
                .ToArray();
            var motorBoardPorts = Object.FindObjectsOfType<ElectricalDeviceView>()
                .Single(view => view.Runtime.DeviceId == "DuanZiPai_7").Ports.ToArray();
            var faultBodyPorts = Object.FindObjectsOfType<ElectricalDeviceView>()
                .Where(view => new[] { "KMF", "KM1", "KMR", "FR" }.Contains(view.Runtime.DeviceId))
                .SelectMany(view => view.Ports)
                .ToArray();
            var dualLineFrPorts = faultBodyPorts
                .Where(port => port.DeviceId == "FR" &&
                               new[] { "T1", "T2", "T3" }.Contains(port.PortName))
                .ToArray();

            var preservedPreset = cameraController.CurrentPreset;
            var preservedPosition = cameraController.transform.position;
            var preservedRotation = cameraController.transform.rotation;
            controller.SetMode(SimulationMode.Wiring);
            controller.SetWireStyle(Color.red, 0.01f, "ElectricalWire");
            yield return null;
            Assert.That(cameraController.CurrentPreset, Is.EqualTo(preservedPreset));
            Assert.That(Vector3.Distance(cameraController.transform.position, preservedPosition), Is.LessThan(0.0001f));
            Assert.That(Quaternion.Angle(cameraController.transform.rotation, preservedRotation), Is.LessThan(0.0001f));
            Assert.That(cameraController.IsViewingFaultSide, Is.False);
            Assert.That(faultBodyPorts.All(port => !port.IsVisible), Is.True);

            cameraController.transform.position = cameraController.FaultPosition;
            cameraController.transform.LookAt(cameraController.CurrentFaultTarget);
            yield return null;
            yield return null;
            Assert.That(controller.Mode, Is.EqualTo(SimulationMode.Wiring));
            Assert.That(cameraController.CurrentPreset, Is.EqualTo(preservedPreset),
                "Moving behind the cabinet must not require entering fault mode");
            Assert.That(cameraController.IsViewingFaultSide, Is.True);
            Assert.That(faultButtonPorts.All(port => port.IsVisible &&
                                                     HasAncestor(port.CurrentAnchor, "DuanZiPai_5")), Is.True,
                "Electrical-wire points must automatically move to their rear physical terminals");
            Assert.That(faultBodyPorts.All(port => port.IsVisible && port.CurrentAnchor != null), Is.True,
                "Fault-device body terminals must appear automatically behind the cabinet");

            controller.SetWireStyle(Color.red, 0.01f, "JumperLine");
            yield return null;
            Assert.That(faultButtonPorts.All(port => !port.IsVisible), Is.True,
                "The rear SB contact points must be hidden in jumper mode");
            Assert.That(dualLineFrPorts.All(port => port.IsVisible && port.UsesJumperAnchor), Is.True,
                "2T1, 4T2 and 6T3 must remain visible after switching to jumper mode");
            Assert.That(faultBodyPorts.Except(dualLineFrPorts).All(port => !port.IsVisible), Is.True,
                "Other fault-device body terminals must stay hidden in jumper mode");
            Assert.That(motorBoardPorts.All(port => port.IsVisible && port.UsesJumperAnchor), Is.True,
                "Jumper mode must immediately expose its matching connection points in the current rear view");

            controller.SetWireStyle(Color.red, 0.01f, "ElectricalWire");
            yield return null;
            Assert.That(faultButtonPorts.All(port => port.IsVisible &&
                                                     HasAncestor(port.CurrentAnchor, "DuanZiPai_5")), Is.True,
                "Switching back to electrical wire must restore rear points without entering fault mode");

            cameraController.transform.position = cameraController.DefaultPosition;
            yield return null;
            yield return null;
            Assert.That(cameraController.IsViewingFaultSide, Is.False);
            Assert.That(faultBodyPorts.All(port => !port.IsVisible), Is.True,
                "Fault-device body terminals must disappear again on the cabinet front");
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
            var routedDeviceIds = new[] { "KM2", "KMB", "KB", "KT" };
            var views = Object.FindObjectsOfType<ElectricalDeviceView>();
            foreach (var deviceId in routedDeviceIds)
            {
                var view = views.Single(item => item.Runtime.DeviceId == deviceId);
                Assert.That(view.Ports, Is.Empty, deviceId + " must be routed through its original terminal board");
            }
            yield return null;
        }

        [UnityTest]
        public IEnumerator MotorsExposeSixConnectionsOnTheirOriginalTerminalBoxes()
        {
            var controller = Object.FindObjectOfType<SimulationController>();
            var cameraController = Object.FindObjectOfType<TrainingCameraController>();
            var views = Object.FindObjectsOfType<ElectricalDeviceView>();
            var motors = new[]
            {
                new { DeviceId = "M1", Nut = "38" },
                new { DeviceId = "M_DOUBLE", Nut = "118" },
                new { DeviceId = "M2", Nut = "49" }
            };
            var expectedAnchors = new[] { "U1", "V1", "W1", "U2", "V2", "W2" };

            controller.SetMode(SimulationMode.Wiring);
            controller.SetWireStyle(Color.red, 0.01f, "ElectricalWire");
            yield return null;

            foreach (var motor in motors)
            {
                var ports = views.Single(item => item.Runtime.DeviceId == motor.DeviceId).Ports;
                Assert.That(ports.Count, Is.EqualTo(6));
                Assert.That(ports.Select(port => port.CurrentAnchor.name), Is.EquivalentTo(expectedAnchors));
                Assert.That(ports.All(port => !port.IsVisible &&
                                              HasAncestor(port.CurrentAnchor, motor.Nut)), Is.True,
                    motor.DeviceId + " terminals must stay hidden in electrical-wire mode");
                Assert.That(ports.All(port => port.JumperOnly && !port.ElectricalOnly), Is.True);
            }

            controller.SetWireStyle(Color.red, 0.01f, "JumperLine");
            yield return null;
            foreach (var motor in motors)
            {
                var ports = views.Single(item => item.Runtime.DeviceId == motor.DeviceId).Ports;
                Assert.That(ports.All(port => port.IsVisible && port.UsesJumperAnchor), Is.True);
            }

            cameraController.SetFaultView();
            yield return null;
            var faultMotorPorts = views.Single(item => item.Runtime.DeviceId == "M1").Ports;
            Assert.That(faultMotorPorts.Select(port => port.CurrentAnchor.name),
                Is.EquivalentTo(expectedAnchors));
            Assert.That(faultMotorPorts.All(port => port.IsVisible &&
                                                   HasAncestor(port.CurrentAnchor, "107")), Is.True,
                "Troubleshooting-view motor terminals must move to its six rear studs");

            controller.SetWireStyle(Color.red, 0.01f, "ElectricalWire");
            yield return null;
            Assert.That(faultMotorPorts.All(port => !port.IsVisible), Is.True,
                "Troubleshooting-view motor terminals must also be jumper-only");

            var motorBoard = views.Single(item => item.Runtime.DeviceId == "DuanZiPai_7");
            Assert.That(motorBoard.Ports.Count, Is.EqualTo(18));
            Assert.That(motorBoard.Runtime.GetConductiveLinks().Count(), Is.EqualTo(18));
        }

        [UnityTest]
        public IEnumerator MotorSideGreenTerminalsAreRemovedButSixTerminalAssembliesRemain()
        {
            var environment = GameObject.Find("OriginalLabEnvironment");
            Assert.That(environment, Is.Not.Null);

            var motors = new[]
            {
                new { Root = "38", Model = "SanXiangShuLongDianJi" },
                new { Root = "49", Model = "SanXiangShuLongDianJi" },
                new { Root = "107", Model = "SanXiangShuLongDianJi" },
                new { Root = "118", Model = "ShuangSuDianJi" }
            };
            var nuts = environment.transform.Find("Bench/ElectricBench/Nuts");
            Assert.That(nuts, Is.Not.Null);

            foreach (var motor in motors)
            {
                var model = nuts.Find(motor.Root + "/" + motor.Model);
                Assert.That(model, Is.Not.Null, motor.Root + " motor model must remain");
                Assert.That(model.Find("Cube"), Is.Null,
                    motor.Root + " green side terminal must be removed");

                var terminalAssembly = model.Find("mesh/xian_");
                Assert.That(terminalAssembly, Is.Not.Null,
                    motor.Root + " six-terminal assembly must remain");
                Assert.That(terminalAssembly.gameObject.activeInHierarchy, Is.True);
                Assert.That(terminalAssembly.GetComponent<Renderer>().enabled, Is.True);
            }

            yield return null;
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
            var faultDeviceIds = new HashSet<string> { "KMF", "KM1", "KMR", "FR" };
            var faultBodyPorts = electricalPorts.Where(port => faultDeviceIds.Contains(port.DeviceId)).ToArray();
            var terminalElectricalPorts = electricalPorts.Where(port => !faultDeviceIds.Contains(port.DeviceId)).ToArray();
            var motorBoardPorts = ports.Where(port => port.DeviceId == "DuanZiPai_7").ToArray();
            Assert.That(jumperPorts.Length, Is.EqualTo(18));
            Assert.That(jumperPorts.Select(port => port.DeviceId).Distinct(),
                Is.EquivalentTo(new[] { "M1", "M_DOUBLE", "M2" }));
            Assert.That(electricalPorts.Length, Is.GreaterThan(0));
            Assert.That(motorBoardPorts.Length, Is.EqualTo(18));
            Assert.That(motorBoardPorts.All(port => !port.JumperOnly && !port.ElectricalOnly), Is.True);
            Assert.That(jumperPorts.All(port => !port.IsVisible), Is.True);
            Assert.That(terminalElectricalPorts.All(port => port.IsVisible), Is.True);
            Assert.That(faultBodyPorts.All(port => !port.IsVisible), Is.True);
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
        public IEnumerator CabinetTitleKeepsFirstDianCharacterWhenRemovingLegacyLogo()
        {
            yield return new WaitForSecondsRealtime(0.15f);
            var cabinetRenderers = Object.FindObjectsOfType<MeshRenderer>(true)
                .Where(item => item.name == "DQG01")
                .ToArray();
            Assert.That(cabinetRenderers, Is.Not.Empty);

            var cleanedTextures = cabinetRenderers
                .SelectMany(item => item.materials)
                .Where(item => item != null &&
                               item.name.IndexOf("bq", System.StringComparison.OrdinalIgnoreCase) >= 0)
                .Select(item => item.mainTexture as Texture2D)
                .Where(item => item != null)
                .ToArray();
            Assert.That(cleanedTextures, Is.Not.Empty);

            foreach (var texture in cleanedTextures)
            {
                var removedLogoPixel = texture.GetPixel(
                    Mathf.RoundToInt(texture.width * 0.044f),
                    Mathf.RoundToInt(texture.height * 0.95f));
                var firstDianCornerPixel = texture.GetPixel(
                    Mathf.RoundToInt(texture.width * 0.17f),
                    Mathf.RoundToInt(texture.height * 0.921f));
                Assert.That(removedLogoPixel.a, Is.LessThan(0.01f),
                    "The legacy upper-left logo must remain removed.");
                Assert.That(firstDianCornerPixel.a, Is.GreaterThan(0.05f),
                    "The removal mask must not clip the first 电 glyph shared by the front and back faces.");
            }
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
        public IEnumerator WiringToolbarKeepsCameraAndPlacesLineFormBelowToolbar()
        {
            var controller = Object.FindObjectOfType<SimulationController>();
            var cameraController = Object.FindObjectOfType<TrainingCameraController>();
            var toolbar = GameObject.Find("OriginalUI_ExperimentToolbar");
            var lineForm = Object.FindObjectsOfType<RectTransform>(true)
                .Single(item => item.name == "OriginalUI_LineForm");
            Assert.That(toolbar, Is.Not.Null);

            controller.SetMode(SimulationMode.View);
            cameraController.transform.position = cameraController.DefaultPosition + new Vector3(0.23f, 0.11f, 0.37f);
            cameraController.transform.rotation = Quaternion.Euler(13f, 167f, 0f);
            var expectedPosition = cameraController.transform.position;
            var expectedRotation = cameraController.transform.rotation;
            var expectedPreset = cameraController.CurrentPreset;

            toolbar.GetComponentsInChildren<Button>(true).Single(item => item.name == "btn_line").onClick.Invoke();
            yield return null;

            Assert.That(controller.Mode, Is.EqualTo(SimulationMode.Wiring));
            Assert.That(Vector3.Distance(cameraController.transform.position, expectedPosition), Is.LessThan(0.0001f));
            Assert.That(Quaternion.Angle(cameraController.transform.rotation, expectedRotation), Is.LessThan(0.0001f));
            Assert.That(cameraController.CurrentPreset, Is.EqualTo(expectedPreset));
            Assert.That(lineForm.gameObject.activeSelf, Is.True);
            Assert.That(lineForm.anchorMin, Is.EqualTo(new Vector2(0.5f, 1f)));
            Assert.That(lineForm.anchorMax, Is.EqualTo(new Vector2(0.5f, 1f)));

            var toolbarRect = toolbar.GetComponent<RectTransform>();
            var toolbarBottom = toolbarRect.anchoredPosition.y - toolbarRect.rect.height * toolbarRect.pivot.y;
            var lineTop = lineForm.anchoredPosition.y + lineForm.rect.height * (1f - lineForm.pivot.y);
            var expectedGap = 56f - lineForm.rect.height * 0.5f;
            Assert.That(toolbarBottom - lineTop, Is.EqualTo(expectedGap).Within(0.1f),
                "The line-style form should be raised by half of its own height from the reference position.");
        }

        [UnityTest]
        public IEnumerator OriginalLineFormColorControlsConnectionColorAndSurvivesLineTypeChanges()
        {
            var controller = Object.FindObjectOfType<SimulationController>();
            var lineForm = Object.FindObjectsOfType<RectTransform>(true)
                .Single(item => item.name == "OriginalUI_LineForm");
            var colorDropdown = lineForm.GetComponentsInChildren<Dropdown>(true)
                .Single(item => item.name == "Color");
            var lineTypeDropdown = lineForm.GetComponentsInChildren<Dropdown>(true)
                .Single(item => item.name == "LineType");

            Assert.That(colorDropdown.options.Count, Is.EqualTo(6));
            Assert.That(Vector4.Distance(controller.CurrentWireColor, Color.red), Is.LessThan(0.0001f));

            colorDropdown.value = 2;
            yield return null;
            Assert.That(Vector4.Distance(controller.CurrentWireColor, Color.blue), Is.LessThan(0.0001f));

            lineTypeDropdown.value = 1;
            yield return null;
            Assert.That(controller.CurrentLineType, Is.EqualTo("JumperLine"));
            Assert.That(Vector4.Distance(controller.CurrentWireColor, Color.blue), Is.LessThan(0.0001f),
                "Changing the connection type must not reset the selected wire color.");

            colorDropdown.value = 0;
            yield return null;
            Assert.That(Vector4.Distance(controller.CurrentWireColor, Color.red), Is.LessThan(0.0001f),
                "The red option must create a red wire instead of enabling automatic port coloring.");
        }

        [UnityTest]
        public IEnumerator TroubleshootingButtonTogglesToolsAndClosesFaultMode()
        {
            var controller = Object.FindObjectOfType<SimulationController>();
            var cameraController = Object.FindObjectOfType<TrainingCameraController>();
            var toolbar = GameObject.Find("OriginalUI_ExperimentToolbar");
            var instrumentTools = Object.FindObjectsOfType<RectTransform>(true)
                .Single(item => item.name == "InstrumentTools");
            var motorFaultBlocks = Object.FindObjectsOfType<Transform>(true)
                .Single(item => item.name == "MotorFaultBlocks");
            var troubleshootingButton = toolbar.GetComponentsInChildren<Button>(true)
                .Single(item => item.name == "btn_paigu");

            controller.SetMode(SimulationMode.View);
            var expectedPosition = cameraController.transform.position;
            var expectedRotation = cameraController.transform.rotation;
            var expectedPreset = cameraController.CurrentPreset;

            Assert.That(instrumentTools.gameObject.activeSelf, Is.False);
            Assert.That(motorFaultBlocks.gameObject.activeSelf, Is.False);
            Assert.That(motorFaultBlocks.childCount, Is.EqualTo(4));
            Assert.That(motorFaultBlocks.Cast<Transform>().All(item =>
                item.GetComponent<MeshRenderer>() != null && item.GetComponent<BoxCollider>() != null), Is.True);
            Assert.That(instrumentTools.anchorMin, Is.EqualTo(new Vector2(0.6f, 0.77f)));
            Assert.That(instrumentTools.anchorMax, Is.EqualTo(new Vector2(0.6f, 0.77f)));
            Assert.That(instrumentTools.anchoredPosition, Is.EqualTo(new Vector2(598.5f, -220f)));

            troubleshootingButton.onClick.Invoke();
            yield return null;

            Assert.That(instrumentTools.gameObject.activeSelf, Is.True);
            Assert.That(motorFaultBlocks.gameObject.activeSelf, Is.True);
            Assert.That(controller.Mode, Is.EqualTo(SimulationMode.View));
            Assert.That(cameraController.transform.position, Is.EqualTo(expectedPosition));
            Assert.That(cameraController.transform.rotation, Is.EqualTo(expectedRotation));
            Assert.That(cameraController.CurrentPreset, Is.EqualTo(expectedPreset));

            instrumentTools.GetComponentsInChildren<Button>(true)
                .Single(item => item.name == "Instrument_Multimeter").onClick.Invoke();
            yield return null;
            Assert.That(controller.Mode, Is.EqualTo(SimulationMode.Fault));
            Assert.That(instrumentTools.gameObject.activeSelf, Is.True);
            Assert.That(motorFaultBlocks.gameObject.activeSelf, Is.True);

            troubleshootingButton.onClick.Invoke();
            yield return null;
            Assert.That(controller.Mode, Is.EqualTo(SimulationMode.View));
            Assert.That(instrumentTools.gameObject.activeSelf, Is.False);
            Assert.That(motorFaultBlocks.gameObject.activeSelf, Is.False);
        }

        [UnityTest]
        public IEnumerator SchematicStaysInsideRightPanel()
        {
            var rightPanel = Object.FindObjectsOfType<RectTransform>(true)
                .Single(item => item.name == "RightPanel");
            var schematicFrame = Object.FindObjectsOfType<RectTransform>(true)
                .Single(item => item.name == "SchematicFrame");
            var panelCorners = new Vector3[4];
            var schematicCorners = new Vector3[4];
            rightPanel.GetWorldCorners(panelCorners);
            schematicFrame.GetWorldCorners(schematicCorners);

            Assert.That(schematicFrame.offsetMin, Is.EqualTo(new Vector2(18f, -470f)));
            Assert.That(schematicFrame.offsetMax, Is.EqualTo(new Vector2(-18f, -174f)));
            Assert.That(schematicCorners[0].x, Is.GreaterThan(panelCorners[0].x));
            Assert.That(schematicCorners[0].y, Is.GreaterThan(panelCorners[0].y));
            Assert.That(schematicCorners[2].x, Is.LessThan(panelCorners[2].x));
            Assert.That(schematicCorners[2].y, Is.LessThan(panelCorners[2].y));
            yield return null;
        }

        [UnityTest]
        public IEnumerator SidePanelsSlideIndependentlyReverseAndShareNavigationState()
        {
            var canvas = GameObject.Find("Simulation HUD").GetComponent<RectTransform>();
            var statusPanel = Object.FindObjectsOfType<RectTransform>(true)
                .Single(item => item.name == "StatusPanel");
            var rightPanel = Object.FindObjectsOfType<RectTransform>(true)
                .Single(item => item.name == "RightPanel");
            var statusHandle = Object.FindObjectsOfType<Button>(true)
                .Single(item => item.name == "StatusPanelSlideHandle");
            var rightHandle = Object.FindObjectsOfType<Button>(true)
                .Single(item => item.name == "RightPanelSlideHandle");
            var navigation = GameObject.Find("OriginalUI_TopNavigation");
            var homeButton = navigation.GetComponentsInChildren<Button>(true)
                .Single(item => item.name == "homeBtn");
            var scheduleButton = navigation.GetComponentsInChildren<Button>(true)
                .Single(item => item.name == "scheduleBtn");
            var statusExpanded = statusPanel.anchoredPosition;
            var rightExpanded = rightPanel.anchoredPosition;

            statusHandle.onClick.Invoke();
            yield return new WaitForSecondsRealtime(0.25f);
            Assert.That(Vector2.Distance(statusPanel.anchoredPosition, statusExpanded + new Vector2(-305f, 0f)), Is.LessThan(0.01f));
            Assert.That(Vector2.Distance(rightPanel.anchoredPosition, rightExpanded), Is.LessThan(0.01f));
            AssertRectStaysInside(statusHandle.GetComponent<RectTransform>(), canvas);

            rightHandle.onClick.Invoke();
            yield return new WaitForSecondsRealtime(0.25f);
            Assert.That(Vector2.Distance(rightPanel.anchoredPosition, rightExpanded + new Vector2(305f, 0f)), Is.LessThan(0.01f));
            Assert.That(Vector2.Distance(statusPanel.anchoredPosition, statusExpanded + new Vector2(-305f, 0f)), Is.LessThan(0.01f));
            AssertRectStaysInside(rightHandle.GetComponent<RectTransform>(), canvas);

            statusHandle.onClick.Invoke();
            rightHandle.onClick.Invoke();
            yield return new WaitForSecondsRealtime(0.25f);
            Assert.That(Vector2.Distance(statusPanel.anchoredPosition, statusExpanded), Is.LessThan(0.01f));
            Assert.That(Vector2.Distance(rightPanel.anchoredPosition, rightExpanded), Is.LessThan(0.01f));

            statusHandle.onClick.Invoke();
            yield return new WaitForSecondsRealtime(0.08f);
            statusHandle.onClick.Invoke();
            yield return new WaitForSecondsRealtime(0.25f);
            Assert.That(Vector2.Distance(statusPanel.anchoredPosition, statusExpanded), Is.LessThan(0.01f));
            Assert.That(statusHandle.GetComponentInChildren<Text>(true).text, Is.EqualTo("◀"));

            homeButton.onClick.Invoke();
            yield return new WaitForSecondsRealtime(0.25f);
            Assert.That(Vector2.Distance(rightPanel.anchoredPosition, rightExpanded + new Vector2(305f, 0f)), Is.LessThan(0.01f));
            scheduleButton.onClick.Invoke();
            yield return new WaitForSecondsRealtime(0.25f);
            Assert.That(Vector2.Distance(rightPanel.anchoredPosition, rightExpanded), Is.LessThan(0.01f));
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

            foreach (var port in Object.FindObjectsOfType<ElectricalPortView>().Where(item => item.IsVisible))
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

        private static void AssertRectStaysInside(RectTransform inner, RectTransform outer)
        {
            var innerCorners = new Vector3[4];
            var outerCorners = new Vector3[4];
            inner.GetWorldCorners(innerCorners);
            outer.GetWorldCorners(outerCorners);
            Assert.That(innerCorners[0].x, Is.GreaterThanOrEqualTo(outerCorners[0].x - 0.1f));
            Assert.That(innerCorners[0].y, Is.GreaterThanOrEqualTo(outerCorners[0].y - 0.1f));
            Assert.That(innerCorners[2].x, Is.LessThanOrEqualTo(outerCorners[2].x + 0.1f));
            Assert.That(innerCorners[2].y, Is.LessThanOrEqualTo(outerCorners[2].y + 0.1f));
        }

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

    }
}
