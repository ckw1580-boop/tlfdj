using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace ElectricalSim
{
    public sealed partial class TrainingSceneBootstrap : MonoBehaviour
    {
        [SerializeField] private OriginalVisualRegistry originalVisuals;
        [SerializeField] private bool showMissingAssetNotice = true;
        [SerializeField] private Material primitiveMaterial;
        [SerializeField] private Material wireMaterial;
        [SerializeField] private Texture2D cabinetBrandLogo;

        private readonly List<ElectricalDeviceView> deviceViews = new List<ElectricalDeviceView>();
        private readonly List<GameObject> cabinetWireDuctCoverGroups = new List<GameObject>();
        private Font uiFont;
        private SimulationController controller;
        private Transform originalEnvironment;
        private Dictionary<string, List<Transform>> originalTerminals;
        private Transform[] originalEnvironmentTransforms;
        private readonly Dictionary<string, Transform> faultButtonTerminalAnchors =
            new Dictionary<string, Transform>(StringComparer.OrdinalIgnoreCase);
        private LocalCaptureRecorder captureRecorder;
        private GameObject instrumentTools;
        private GameObject motorFaultBlocks;
        private SlidePanelState statusPanelSlide;
        private SlidePanelState taskPanelSlide;

        private const float PanelSlideDuration = 0.2f;

        private static readonly Color[] OriginalWireColors =
        {
            Color.red,
            Color.yellow,
            Color.blue,
            Color.green,
            Color.white,
            Color.black
        };

        private readonly Color darkBlue = new Color(0.015f, 0.075f, 0.16f, 0.96f);
        private readonly Color cyan = new Color(0.05f, 0.72f, 0.95f, 1f);
        private readonly Color panelBlue = new Color(0.04f, 0.19f, 0.27f, 0.94f);

        private void Awake()
        {
            if (FindObjectsOfType<TrainingSceneBootstrap>().Length > 1)
            {
                Destroy(gameObject);
                return;
            }
            Build();
        }

        private void Build()
        {
            var missing = originalVisuals == null
                ? new List<string> { "TrainingSceneBootstrap.originalVisuals is missing." }
                : originalVisuals.FindMissingVisuals();
            if (missing.Count > 0)
            {
                enabled = false;
                throw new InvalidOperationException("[ResourceIntegrity] 原始资源缺失，已停止创建实训场景。"
                    + "请下载包含 Git LFS 实际资源的完整项目；详见 Docs/project-recovery.md。\n"
                    + string.Join("\n", missing));
            }
            Debug.Log("[OfflineBootstrap] Build started.");
            Application.targetFrameRate = 60;
            uiFont = Font.CreateDynamicFontFromOSFont(new[] { "Microsoft YaHei UI", "Microsoft YaHei", "SimHei", "Arial" }, 18);
            Debug.Log("[OfflineBootstrap] Font ready.");
            CreateEnvironment();
            var cameraController = CreateCamera();
            cameraController.ResetView();
            CreateG120ControlAnchors();
            RefreshTerminalBoardAnnotations(cameraController.transform);
            CreateFaultButtonTerminalConnections(cameraController);
            Debug.Log("[OfflineBootstrap] Environment ready.");
            var wireRoot = new GameObject("ElectricalWires").transform;
            CreatePanelDevices();
            CreateDevices();
            CreateOriginalTerminalBoardPorts();
            CreateOriginalCabinetTerminalBoardPorts();
            CreateFaultPowerTerminalBlock(cameraController);
            CreateCabinetBreakerConnectionPorts();
            CreateFrontBreakers();
            Debug.Log("[OfflineBootstrap] Devices ready.");
            var ui = CreateHud();
            Debug.Log("[OfflineBootstrap] HUD ready.");

            controller = gameObject.AddComponent<SimulationController>();
            captureRecorder = gameObject.AddComponent<LocalCaptureRecorder>();
            var frontWireSurface = ResolveWireSurface(cameraController.transform);
            var faultWireSurface = ResolveFaultWireSurface(frontWireSurface);
            var cabinetBounds = originalEnvironment != null
                ? originalEnvironment.GetComponentsInChildren<Renderer>(true)
                    .FirstOrDefault(item => string.Equals(item.name, "DQG01", StringComparison.OrdinalIgnoreCase))
                : null;
            if (cabinetBounds != null)
            {
                frontWireSurface = new WireSurfacePlane(frontWireSurface.SurfacePoint, frontWireSurface.Normal,
                    frontWireSurface.SurfaceOffset, cabinetBounds.bounds);
                faultWireSurface = new WireSurfacePlane(faultWireSurface.SurfacePoint, faultWireSurface.Normal,
                    faultWireSurface.SurfaceOffset, cabinetBounds.bounds);
            }
            var rearDuctLids = originalEnvironment != null
                ? originalEnvironment.Find("Bench/ElectricBench/mesh/xiancaogai_1")
                : null;
            var rearPlates = originalEnvironment != null
                ? originalEnvironment.Find("Bench/ElectricBench/mesh/model/DQG")
                    ?.GetComponentsInChildren<MeshFilter>(true)
                    .Where(item => item.name == "DQG11" || item.name.StartsWith("DQG11 (", StringComparison.Ordinal) ||
                        item.name == "DQG18") // Support rail immediately below the rear breakers.
                    .ToArray() ?? Array.Empty<MeshFilter>()
                : Array.Empty<MeshFilter>();
            if (rearDuctLids != null || rearPlates.Length > 0)
            {
                var profile = new WireDuctRoutingProfile(rearDuctLids != null
                    ? rearDuctLids.GetComponentsInChildren<MeshFilter>(true) : Array.Empty<MeshFilter>(),
                    faultWireSurface, rearPlates);
                profile.AddMountingPanelsBehind(
                    originalEnvironment.Find("Bench/ElectricBench/mesh/model/DQG/DQG01")?.GetComponent<MeshFilter>(),
                    deviceViews.SelectMany(view => view.Ports)
                        .Where(port => port.DeviceId == "QF106" || port.DeviceId == "QF122")
                        .Select(port => port.CurrentAnchorPosition));
                faultWireSurface = new WireSurfacePlane(faultWireSurface.SurfacePoint, faultWireSurface.Normal,
                    faultWireSurface.SurfaceOffset, faultWireSurface.SurfaceBounds,
                    profile);
            }
            controller.Initialize(deviceViews, cameraController, wireRoot, ui.Mode, ui.Status, ui.Instrument, wireMaterial, frontWireSurface, faultWireSurface, ui.PortHover);
            controller.RegisterSchematicGallery(ui.Gallery);
            controller.RegisterPanel(panelViews, panelPower);
            controller.RegisterPlcs(originalEnvironment, uiFont, ui.Status.canvas);
            controller.RegisterIntermediateRelays(originalEnvironment, uiFont, ui.Status.canvas);
            controller.RegisterContactors(originalEnvironment, uiFont, ui.Status.canvas);
            controller.RegisterThermalRelays(originalEnvironment, uiFont, ui.Status.canvas);
            controller.RegisterSceneIo(originalEnvironment, uiFont, ui.Status.canvas);
            ValidatePanelBindings();
            ui.Status.transform.parent.gameObject.AddComponent<WirePropertiesPresenter>().Initialize(controller, ui.Status, () =>
            {
                if (statusPanelSlide.IsCollapsed) ToggleSlidePanel(statusPanelSlide);
            });
            ui.Status.transform.parent.gameObject.AddComponent<PanelPropertiesPresenter>().Initialize(controller, ui.Status, () =>
            {
                if (statusPanelSlide.IsCollapsed) ToggleSlidePanel(statusPanelSlide);
            });
            controller.ModeChanged += SetCabinetWireDuctCoversForMode;
            SetCabinetWireDuctCoversForMode(controller.Mode);
            controller.RegisterCabinetBreakers(CreateCabinetBreakerInteractions());
            controller.RegisterFrontBreakers(frontBreakerViews, ui.Status.canvas, uiFont);
            BindUi(ui);
            BindOriginalUi(ui);
            CreateTachometer();
            CreateMultimeter(ui);
            if (originalEnvironment != null) Invoke(nameof(RefreshCabinetBranding), 0.1f);
            Debug.Log("[OfflineBootstrap] Build complete.");
        }

        private WireSurfacePlane ResolveWireSurface(Transform viewingCamera)
        {
            const float annotationOffset = 0.0025f;
            const float wireOffset = 0.003f;
            var annotation = originalEnvironment != null
                ? originalEnvironment.Find("Terminal Board Annotations/Terminal Annotation - Three Phase Power")
                : null;
            if (annotation != null)
            {
                var normal = -annotation.forward;
                if (viewingCamera != null && Vector3.Dot(normal, viewingCamera.position - annotation.position) < 0f)
                    normal = -normal;
                var board = originalEnvironment.Find(OriginalTerminalBoardMap.BoardTransformPath);
                var pointRoot = board != null ? board.Find("point") : null;
                var electricalAnchors = pointRoot != null
                    ? pointRoot.Cast<Transform>()
                        .Where(item => item.name.Length > 1 && item.name[0] == 'a' &&
                                       int.TryParse(item.name.Substring(1), out _))
                        .ToArray()
                    : Array.Empty<Transform>();
                var surfacePoint = electricalAnchors.Length > 0
                    ? electricalAnchors.Aggregate(Vector3.zero, (sum, item) => sum + item.position) /
                      electricalAnchors.Length
                    : annotation.position - normal * annotationOffset;
                return new WireSurfacePlane(surfacePoint, normal, wireOffset);
            }

            var cabinetRenderer = originalEnvironment != null
                ? originalEnvironment.GetComponentsInChildren<Renderer>(true)
                    .FirstOrDefault(item => string.Equals(item.name, "DQG01", StringComparison.OrdinalIgnoreCase))
                : null;
            if (cabinetRenderer != null)
            {
                var normal = Vector3.forward;
                if (viewingCamera != null &&
                    Vector3.Dot(normal, viewingCamera.position - cabinetRenderer.bounds.center) < 0f)
                    normal = -normal;
                var extents = cabinetRenderer.bounds.extents;
                var surfaceDistance = Mathf.Abs(normal.x) * extents.x +
                                      Mathf.Abs(normal.y) * extents.y +
                                      Mathf.Abs(normal.z) * extents.z;
                return new WireSurfacePlane(
                    cabinetRenderer.bounds.center + normal * surfaceDistance,
                    normal,
                    wireOffset);
            }

            var fallbackNormal = Vector3.forward;
            var fallbackCenter = new Vector3(0f, 1.65f, 0.41f);
            if (viewingCamera != null && Vector3.Dot(fallbackNormal, viewingCamera.position - fallbackCenter) < 0f)
                fallbackNormal = -fallbackNormal;
            return new WireSurfacePlane(fallbackCenter, fallbackNormal, wireOffset);
        }

        private WireSurfacePlane ResolveFaultWireSurface(WireSurfacePlane frontSurface)
        {
            const float wireOffset = 0.003f;
            // DQG11 is the rear mounting plate. DQG01's world AABB also includes
            // the rotated frame/base: its support point is ~0.59 m off this plate.
            var rearPlate = originalEnvironment != null
                ? originalEnvironment.Find("Bench/ElectricBench/mesh/model/DQG/DQG11")?.GetComponent<MeshFilter>()
                : null;
            if (rearPlate != null && rearPlate.sharedMesh != null)
            {
                var rearNormal = -rearPlate.transform.up.normalized;
                var vertices = rearPlate.sharedMesh.vertices;
                var rearDepth = vertices.Max(vertex => Vector3.Dot(rearPlate.transform.TransformPoint(vertex), rearNormal));
                var center = rearPlate.GetComponent<Renderer>().bounds.center;
                var rearPoint = center + rearNormal * (rearDepth - Vector3.Dot(center, rearNormal));
                return new WireSurfacePlane(rearPoint, rearNormal, wireOffset);
            }
            var normal = -frontSurface.Normal;
            var cabinetRenderer = originalEnvironment != null
                ? originalEnvironment.GetComponentsInChildren<Renderer>(true)
                    .FirstOrDefault(item => string.Equals(item.name, "DQG01", StringComparison.OrdinalIgnoreCase))
                : null;
            if (cabinetRenderer != null)
            {
                var extents = cabinetRenderer.bounds.extents;
                var surfaceDistance = Mathf.Abs(normal.x) * extents.x +
                                      Mathf.Abs(normal.y) * extents.y +
                                      Mathf.Abs(normal.z) * extents.z;
                return new WireSurfacePlane(
                    cabinetRenderer.bounds.center + normal * surfaceDistance,
                    normal,
                    wireOffset);
            }

            // Deterministic rear face when the imported cabinet shell is unavailable.
            return new WireSurfacePlane(frontSurface.SurfacePoint - frontSurface.Normal * 0.08f, normal, wireOffset);
        }

        private void OnDestroy()
        {
            if (controller != null)
                controller.ModeChanged -= SetCabinetWireDuctCoversForMode;
        }

        private void RefreshTerminalBoardAnnotations(Transform viewingCamera)
        {
            if (originalEnvironment == null) return;

            // The imported top strip already carries three correctly placed label
            // rectangles.  Keep their geometry as the layout reference, but render
            // the annotations as independent TextMesh objects instead of reviving
            // the unsupported World Space Canvas/TMP hierarchy.
            var annotationLayouts = CaptureTopTerminalAnnotationLayouts();

            var generatedRoot = originalEnvironment.Find("Terminal Board Annotations");
            if (generatedRoot != null)
            {
                generatedRoot.gameObject.SetActive(false);
                Destroy(generatedRoot.gameObject);
            }

            foreach (var canvas in originalEnvironment.GetComponentsInChildren<Canvas>(true))
            {
                if (!HasTerminalBoardAncestor(canvas.transform)) continue;
                canvas.gameObject.SetActive(false);
                Destroy(canvas.gameObject);
            }

            foreach (var textMesh in originalEnvironment.GetComponentsInChildren<TextMesh>(true))
            {
                if (!HasTerminalBoardAncestor(textMesh.transform)) continue;
                textMesh.gameObject.SetActive(false);
                Destroy(textMesh.gameObject);
            }

            RemoveOriginalBoardAnnotations("DuanZiPai_5");

            CreateTopTerminalBoardAnnotations(viewingCamera, annotationLayouts);
            CreatePlcRelayTerminalBoardAnnotations(viewingCamera);
            CreateInverterUpperTerminalBoardAnnotations(viewingCamera);
            CreateInverterLowerTerminalBoardAnnotations(viewingCamera);
            CreateAuxiliaryTerminalBoardAnnotations(viewingCamera);
        }

        private void RemoveOriginalBoardAnnotations(string boardName)
        {
            var board = originalEnvironmentTransforms.FirstOrDefault(item =>
                string.Equals(item.name, boardName, StringComparison.Ordinal));
            if (board == null) return;

            foreach (var canvas in board.GetComponentsInChildren<Canvas>(true))
            {
                canvas.gameObject.SetActive(false);
                Destroy(canvas.gameObject);
            }
        }

        private TerminalAnnotationLayout[] CaptureTopTerminalAnnotationLayouts()
        {
            var board = originalEnvironment.Find(OriginalTerminalBoardMap.BoardTransformPath);
            var canvas = board != null ? board.GetComponentInChildren<Canvas>(true) : null;
            if (canvas == null) return Array.Empty<TerminalAnnotationLayout>();

            return canvas.transform.Cast<Transform>()
                .OfType<RectTransform>()
                .OrderBy(item => item.anchoredPosition.x)
                .Take(3)
                .Select(item => new TerminalAnnotationLayout(item.position))
                .ToArray();
        }

        private void CreateTopTerminalBoardAnnotations(
            Transform viewingCamera,
            IReadOnlyList<TerminalAnnotationLayout> layouts)
        {
            var board = originalEnvironment.Find(OriginalTerminalBoardMap.BoardTransformPath);
            var pointRoot = board != null ? board.Find("point") : null;
            if (pointRoot == null || viewingCamera == null) return;

            OriginalTerminalBoardMap map;
            try
            {
                var configurationPath = Path.Combine(
                    Application.streamingAssetsPath,
                    OriginalTerminalBoardMap.RelativeConfigurationPath);
                map = OriginalTerminalBoardMap.Load(configurationPath);
            }
            catch (Exception exception)
            {
                Debug.LogWarning("[OfflineBootstrap] Terminal annotations could not be created: " + exception.Message);
                return;
            }

            var root = new GameObject("Terminal Board Annotations").transform;
            root.SetParent(originalEnvironment, true);
            CreateTerminalBoardAnnotation(root, pointRoot, viewingCamera, map,
                OriginalTerminalZone.ThreePhasePower, "三相电源端子区", "Three Phase Power",
                layouts.Count == 3 ? layouts[0] : (TerminalAnnotationLayout?)null);
            CreateTerminalBoardAnnotation(root, pointRoot, viewingCamera, map,
                OriginalTerminalZone.Indicator, "指示灯（HL）端子区", "Indicator HL",
                layouts.Count == 3 ? layouts[1] : (TerminalAnnotationLayout?)null);
            CreateTerminalBoardAnnotation(root, pointRoot, viewingCamera, map,
                OriginalTerminalZone.SelectorAndButton, "旋钮（SA）、按钮SB端子区", "Selector SA and Button SB",
                layouts.Count == 3 ? layouts[2] : (TerminalAnnotationLayout?)null);
        }

        private void CreateTerminalBoardAnnotation(
            Transform root,
            Transform pointRoot,
            Transform viewingCamera,
            OriginalTerminalBoardMap map,
            OriginalTerminalZone zone,
            string content,
            string objectName,
            TerminalAnnotationLayout? layout)
        {
            var anchors = map.Bindings
                .Where(binding => binding.Zone == zone)
                .Select(binding => pointRoot.Find(binding.AnchorId))
                .Where(anchor => anchor != null)
                .ToArray();
            if (anchors.Length == 0) return;

            var center = Vector3.zero;
            foreach (var anchor in anchors)
                center += anchor.position;
            center /= anchors.Length;
            var front = ResolveBoardFacingDirection(pointRoot);
            if (Vector3.Dot(front, viewingCamera.position - center) < 0f) front = -front;

            var labelObject = new GameObject("Terminal Annotation - " + objectName);
            labelObject.transform.SetParent(root, true);
            if (layout.HasValue)
            {
                var centeredLayoutPosition = layout.Value.Position;
                centeredLayoutPosition.y = center.y;
                labelObject.transform.position = centeredLayoutPosition + front * 0.0025f;
            }
            else
            {
                labelObject.transform.position = center + front * 0.0025f;
            }
            labelObject.transform.rotation = Quaternion.LookRotation(-front, Vector3.up);

            var textMesh = labelObject.AddComponent<TextMesh>();
            textMesh.text = content;
            textMesh.font = uiFont;
            textMesh.fontSize = 96;
            textMesh.fontStyle = FontStyle.Bold;
            textMesh.characterSize = 0.002f;
            textMesh.anchor = TextAnchor.MiddleCenter;
            textMesh.alignment = TextAlignment.Center;
            textMesh.color = new Color(1f, 0.9f, 0f, 1f);
            labelObject.transform.localScale = new Vector3(0.42f, 0.65f, 1f);

            var renderer = labelObject.GetComponent<MeshRenderer>();
            if (renderer == null) return;
            // Raise the label by one and a quarter rendered glyph heights.
            labelObject.transform.position += Vector3.up * renderer.bounds.size.y * 1.25f;
            renderer.sharedMaterial = uiFont.material;
            renderer.sortingOrder = 100;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
            renderer.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;
            labelObject.AddComponent<FrontFaceOnlyTextVisibility>()
                .Configure(renderer, viewingCamera);
        }

        private readonly struct TerminalAnnotationLayout
        {
            public readonly Vector3 Position;

            public TerminalAnnotationLayout(Vector3 position)
            {
                Position = position;
            }
        }

        private void CreatePlcRelayTerminalBoardAnnotations(Transform viewingCamera)
        {
            if (viewingCamera == null) return;
            if (originalEnvironmentTransforms == null) CacheOriginalEnvironmentTransforms();

            var root = originalEnvironment.Find("Terminal Board Annotations");
            if (root == null) return;

            CreatePlcRelayTerminalBoardAnnotations(
                root, viewingCamera, "DuanZiPai_1",
                "PLC_1DI端子区", "PLC_2DI端子区",
                "中间继电器（KA）1、2、3、5、6、7、8端子区",
                "PLC 1 DI", "PLC 2 DI", "Intermediate Relays KA1-3 and KA5-8", -0.45f);
            CreatePlcRelayTerminalBoardAnnotations(
                root, viewingCamera, "DuanZiPai_2",
                "PLC_1端子区", "PLC_2端子区",
                "中间继电器（KA）4、9、10、11、12、13、14端子区",
                "PLC 1 Output", "PLC 2 Output", "Intermediate Relays KA4 and KA9-14", 1.55f);
        }

        private void CreatePlcRelayTerminalBoardAnnotations(
            Transform root,
            Transform viewingCamera,
            string boardName,
            string plc1Content,
            string plc2Content,
            string relayContent,
            string plc1ObjectName,
            string plc2ObjectName,
            string relayObjectName,
            float verticalOffsetInGlyphHeights)
        {
            var board = originalEnvironmentTransforms.FirstOrDefault(item =>
                string.Equals(item.name, boardName, StringComparison.Ordinal) && item.Find("point") != null);
            var pointRoot = board != null ? board.Find("point") : null;
            if (pointRoot == null) return;

            var anchors = pointRoot.Cast<Transform>()
                .Where(item => OriginalCabinetTerminalBoardMap.IsTerminalName(item.name))
                .ToArray();
            CreateCabinetTerminalBoardAnnotation(root, viewingCamera,
                anchors.Where(item => item.name.StartsWith("PLC_1_", StringComparison.Ordinal)).ToArray(),
                plc1Content, plc1ObjectName, verticalOffsetInGlyphHeights);
            CreateCabinetTerminalBoardAnnotation(root, viewingCamera,
                anchors.Where(item => item.name.StartsWith("PLC_2_", StringComparison.Ordinal)).ToArray(),
                plc2Content, plc2ObjectName, verticalOffsetInGlyphHeights);
            CreateCabinetTerminalBoardAnnotation(root, viewingCamera,
                anchors.Where(item => item.name.StartsWith("KA", StringComparison.Ordinal)).ToArray(),
                relayContent, relayObjectName, verticalOffsetInGlyphHeights);
        }

        private void CreateAuxiliaryTerminalBoardAnnotations(Transform viewingCamera)
        {
            if (viewingCamera == null) return;
            if (originalEnvironmentTransforms == null) CacheOriginalEnvironmentTransforms();

            var root = originalEnvironment.Find("Terminal Board Annotations");
            if (root == null) return;
            CreateWholeTerminalBoardAnnotation(
                root, viewingCamera, "DuanZiPai_6", "电源端子区", "Power Terminals");
            CreateWholeTerminalBoardAnnotation(
                root, viewingCamera, "DuanZiPai_7", "电机端子区", "Motor Terminals");
            CreateWholeTerminalBoardAnnotation(
                root, viewingCamera, "DuanZiPai_8", "场景中传感器、电磁阀端子", "Scene Sensors and Valves");
        }

        private void CreateInverterUpperTerminalBoardAnnotations(Transform viewingCamera)
        {
            CreateInverterTerminalBoardAnnotations(
                viewingCamera, "DuanZiPai_3", "Upper", -0.95f);
        }

        private void CreateInverterLowerTerminalBoardAnnotations(Transform viewingCamera)
        {
            CreateInverterTerminalBoardAnnotations(
                viewingCamera, "DuanZiPai_4", "Below Inverter", -0.95f);
        }

        private void CreateInverterTerminalBoardAnnotations(
            Transform viewingCamera,
            string boardName,
            string objectNameSuffix,
            float verticalOffsetInGlyphHeights)
        {
            if (viewingCamera == null) return;
            if (originalEnvironmentTransforms == null) CacheOriginalEnvironmentTransforms();

            var root = originalEnvironment.Find("Terminal Board Annotations");
            var board = originalEnvironmentTransforms.FirstOrDefault(item =>
                string.Equals(item.name, boardName, StringComparison.Ordinal) && item.Find("point") != null);
            var pointRoot = board != null ? board.Find("point") : null;
            if (root == null || pointRoot == null) return;

            var boardDefinition = OriginalCabinetTerminalBoardMap.Boards.FirstOrDefault(item =>
                string.Equals(item.DeviceId, boardName, StringComparison.Ordinal));
            if (boardDefinition == null) return;

            var lowerAnchors = pointRoot.Cast<Transform>()
                .Where(item => OriginalCabinetTerminalBoardMap.IsTerminalName(boardDefinition, item.name))
                .ToArray();
            CreateCabinetTerminalBoardAnnotation(
                root, viewingCamera,
                lowerAnchors.Where(item => item.name.StartsWith("G120_", StringComparison.Ordinal)).ToArray(),
                "G120变频器端子区", "G120 Inverter " + objectNameSuffix, verticalOffsetInGlyphHeights);
            CreateCabinetTerminalBoardAnnotation(
                root, viewingCamera,
                lowerAnchors.Where(item => item.name.StartsWith("KM", StringComparison.Ordinal)).ToArray(),
                "交流接触器（KM）端子区", "Contactors KM " + objectNameSuffix, verticalOffsetInGlyphHeights);
            CreateCabinetTerminalBoardAnnotation(
                root, viewingCamera,
                lowerAnchors.Where(item => item.name.StartsWith("FR", StringComparison.Ordinal)).ToArray(),
                "FR端子区",
                "FR " + objectNameSuffix, verticalOffsetInGlyphHeights);

            if (boardName == "DuanZiPai_4")
            {
                var inverterLabel = root.Find("Terminal Annotation - G120 Inverter " + objectNameSuffix);
                var contactorLabel = root.Find("Terminal Annotation - Contactors KM " + objectNameSuffix);
                if (inverterLabel != null && contactorLabel != null)
                    inverterLabel.position += inverterLabel.up *
                        Vector3.Dot(contactorLabel.position - inverterLabel.position, inverterLabel.up);
            }
        }

        private void CreateWholeTerminalBoardAnnotation(
            Transform root,
            Transform viewingCamera,
            string boardName,
            string content,
            string objectName)
        {
            var board = originalEnvironmentTransforms.FirstOrDefault(item =>
                string.Equals(item.name, boardName, StringComparison.Ordinal) && item.Find("point") != null);
            var pointRoot = board != null ? board.Find("point") : null;
            var boardDefinition = OriginalCabinetTerminalBoardMap.Boards.FirstOrDefault(item =>
                string.Equals(item.DeviceId, boardName, StringComparison.Ordinal));
            if (pointRoot == null || boardDefinition == null) return;

            var anchors = pointRoot.Cast<Transform>()
                .Where(item => OriginalCabinetTerminalBoardMap.IsTerminalName(boardDefinition, item.name))
                .ToArray();
            CreateCabinetTerminalBoardAnnotation(
                root, viewingCamera, anchors, content, objectName, -0.45f);
        }

        private void CreateCabinetTerminalBoardAnnotation(
            Transform root,
            Transform viewingCamera,
            IReadOnlyCollection<Transform> anchors,
            string content,
            string objectName,
            float verticalOffsetInGlyphHeights,
            TerminalAnnotationLayout? layout = null)
        {
            if (anchors.Count == 0) return;

            var center = Vector3.zero;
            foreach (var anchor in anchors) center += anchor.position;
            center /= anchors.Count;
            var orientationReference = root.Find("Terminal Annotation - Three Phase Power");
            if (orientationReference == null) return;
            var front = -orientationReference.forward;

            var labelObject = new GameObject("Terminal Annotation - " + objectName);
            labelObject.transform.SetParent(root, true);
            var labelPosition = center + front * 0.0025f;
            if (layout.HasValue)
            {
                labelPosition = layout.Value.Position;
                labelPosition.y = center.y;
                labelPosition += front * 0.0025f;
            }
            labelObject.transform.SetPositionAndRotation(labelPosition, orientationReference.rotation);

            var textMesh = labelObject.AddComponent<TextMesh>();
            textMesh.text = content;
            textMesh.font = uiFont;
            textMesh.fontSize = 96;
            textMesh.fontStyle = FontStyle.Bold;
            textMesh.characterSize = 0.002f;
            textMesh.anchor = TextAnchor.MiddleCenter;
            textMesh.alignment = TextAlignment.Center;
            textMesh.color = new Color(1f, 0.9f, 0f, 1f);
            labelObject.transform.localScale = orientationReference.localScale;

            var renderer = labelObject.GetComponent<MeshRenderer>();
            if (renderer == null) return;
            labelObject.transform.position += labelObject.transform.up * renderer.bounds.size.y *
                                              verticalOffsetInGlyphHeights;

            renderer.sharedMaterial = uiFont.material;
            renderer.sortingOrder = 100;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
            renderer.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;
            labelObject.AddComponent<FrontFaceOnlyTextVisibility>()
                .Configure(renderer, viewingCamera);
        }

        private static Vector3 ResolveBoardFacingDirection(Transform pointRoot)
        {
            var forward = pointRoot.forward;
            forward.y = 0f;
            return forward.sqrMagnitude > 0.0001f ? forward.normalized : Vector3.forward;
        }

        private static bool HasTerminalBoardAncestor(Transform transform)
        {
            for (var current = transform; current != null; current = current.parent)
            {
                if (!current.name.StartsWith("DuanZiPai_", StringComparison.Ordinal)) continue;
                var suffix = current.name.Substring("DuanZiPai_".Length);
                if (int.TryParse(suffix, out _)) return true;
            }
            return false;
        }

        private void CreateEnvironment()
        {
            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(0.55f, 0.58f, 0.62f);
            RenderSettings.ambientIntensity = 1.25f;
            RenderSettings.fog = false;
            if (originalVisuals != null && originalVisuals.EnvironmentPrefab != null)
            {
                var environment = Instantiate(originalVisuals.EnvironmentPrefab, Vector3.zero, Quaternion.identity);
                environment.name = "OriginalLabEnvironment";
                RebuildMotorFaultBlocks(environment.transform);
                originalEnvironment = environment.transform;
                CacheOriginalEnvironmentTransforms();
                CacheCabinetWireDuctCoverGroups();
                CreateOriginalRoomShell();
                if (environment.GetComponentInChildren<Light>(true) == null) CreateMainLight();
            }
            else
            {
                CreateMainLight();
                CreatePlaceholderEnvironment();
            }
        }

        private void CreateMainLight()
        {
            var lightObject = new GameObject("Main Directional Light");
            var light = lightObject.AddComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = 1.45f;
            light.color = new Color(0.94f, 0.97f, 1f);
            lightObject.transform.eulerAngles = new Vector3(45f, -35f, 0f);

            var fillObject = new GameObject("Cabinet Fill Light");
            var fill = fillObject.AddComponent<Light>();
            fill.type = LightType.Directional;
            fill.intensity = 0.65f;
            fill.color = new Color(0.72f, 0.84f, 1f);
            fillObject.transform.eulerAngles = new Vector3(20f, 145f, 0f);
        }

        private void CreateOriginalRoomShell()
        {
            // The original scene creates its Floor root from a removed runtime script. Rebuild the
            // same open-front training room from the measured Experiment renderer bounds.
            CreateCube("Original Floor", new Vector3(-0.067f, -0.055f, -2.62f), new Vector3(5.62f, 0.11f, 5.35f), new Color(0.08f, 0.58f, 0.49f));
            CreateCube("Original Back Wall", new Vector3(-0.067f, 1.55f, -5.31f), new Vector3(5.62f, 3.2f, 0.10f), new Color(0.35f, 0.35f, 0.35f));
            CreateCube("Original Left Wall", new Vector3(-2.90f, 1.55f, -2.62f), new Vector3(0.10f, 3.2f, 5.35f), new Color(0.74f, 0.77f, 0.78f));
            CreateCube("Original Right Wall", new Vector3(2.77f, 1.55f, -2.62f), new Vector3(0.10f, 3.2f, 5.35f), new Color(0.74f, 0.77f, 0.78f));
        }

        private void CreatePlaceholderEnvironment()
        {
            CreateCube("Floor", new Vector3(0f, -0.08f, 1f), new Vector3(10f, 0.16f, 10f), new Color(0.05f, 0.43f, 0.37f));
            CreateCube("BackWall", new Vector3(0f, 2.4f, 4.5f), new Vector3(10f, 4.8f, 0.2f), new Color(0.72f, 0.76f, 0.78f));
            CreateCube("LeftWall", new Vector3(-5f, 2.4f, 0f), new Vector3(0.2f, 4.8f, 9f), new Color(0.67f, 0.71f, 0.74f));
            CreateCube("RightWall", new Vector3(5f, 2.4f, 0f), new Vector3(0.2f, 4.8f, 9f), new Color(0.67f, 0.71f, 0.74f));

            if (originalVisuals != null && originalVisuals.CabinetPrefab != null)
            {
                var cabinet = Instantiate(originalVisuals.CabinetPrefab, new Vector3(0f, 1.65f, 0.2f), Quaternion.Euler(0f, 180f, 0f));
                cabinet.name = "Original Electrical Cabinet";
                FitOriginalVisual(cabinet, new Vector3(0f, 1.65f, 0.2f), new Vector3(2.5f, 3.3f, 0.5f));
                AddCabinetBranding(cabinet);
            }
            else
            {
                var cabinet = CreateCube("Cabinet", new Vector3(0f, 1.65f, 0.2f), new Vector3(2.5f, 3.3f, 0.42f), new Color(0.055f, 0.065f, 0.07f));
                AddCabinetBranding(cabinet);
                for (var row = 0; row < 5; row++)
                    CreateCube("DIN_Rail_" + row, new Vector3(0f, 0.55f + row * 0.55f, -0.04f), new Vector3(2.18f, 0.075f, 0.08f), new Color(0.68f, 0.72f, 0.73f));
                CreateWorldLabel("电气控制实训柜", new Vector3(0f, 3.38f, -0.12f), 0.11f, Color.white);
            }
        }

        private TrainingCameraController CreateCamera()
        {
            var cameraObject = new GameObject("Training Camera");
            cameraObject.tag = "MainCamera";
            var camera = cameraObject.AddComponent<Camera>();
            camera.fieldOfView = 60f;
            camera.nearClipPlane = 0.04f;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.07f, 0.11f, 0.16f);
            cameraObject.AddComponent<AudioListener>();
            var controllerComponent = cameraObject.AddComponent<TrainingCameraController>();
            return controllerComponent;
        }

        private void CreateDevices()
        {
            CreateDevice(ElectricalDeviceRuntime.CreatePowerSource(), "三相电源", new Vector3(-1.15f, 2.82f, -0.16f), new Vector3(0.52f, 0.3f, 0.18f), new Color(0.18f, 0.22f, 0.26f));
            CreateDevice(ElectricalDeviceRuntime.CreateBreaker("QF"), "断路器 QF", new Vector3(-0.45f, 2.82f, -0.16f), new Vector3(0.55f, 0.34f, 0.18f), new Color(0.86f, 0.88f, 0.9f));
            CreateContactorDevice("KMF", "正转接触器", new Vector3(-0.95f, 2.22f, -0.16f));
            CreateContactorDevice("KM1", "接触器 KM1", new Vector3(-0.35f, 2.22f, -0.16f));
            CreateContactorDevice("KMR", "反转接触器", new Vector3(0.25f, 2.22f, -0.16f));
            CreateDevice(ElectricalDeviceRuntime.CreateContactor("KM2"), "接触器 KM2", new Vector3(0.85f, 2.22f, -0.16f), new Vector3(0.48f, 0.38f, 0.18f), new Color(0.16f, 0.2f, 0.24f));
            CreateRearContactorDevice("KMBACK1", "背部接触器 KM5");
            CreateRearContactorDevice("KMBACK2", "背部接触器 KM6");
            CreateRearContactorDevice("KMBACK3", "背部接触器 KM7");
            CreateDevice(ElectricalDeviceRuntime.CreateContactor("KMB"), "反接制动", new Vector3(1.22f, 1.68f, -0.16f), new Vector3(0.42f, 0.34f, 0.18f), new Color(0.25f, 0.18f, 0.2f));
            CreateDevice(ElectricalDeviceRuntime.CreateContactor("KB"), "能耗制动", new Vector3(0.72f, 1.68f, -0.16f), new Vector3(0.42f, 0.34f, 0.18f), new Color(0.25f, 0.18f, 0.2f));
            CreateDevice(ElectricalDeviceRuntime.CreateThermalRelay("FR"), "热继电器 FR", new Vector3(-0.65f, 1.62f, -0.16f), new Vector3(0.52f, 0.34f, 0.18f), new Color(0.78f, 0.8f, 0.82f));

            CreateButton("SB0", "停止", true, new Vector3(-1.12f, 1.05f, -0.17f), Color.red);
            CreateButton("SB1", "启动", false, new Vector3(-0.72f, 1.05f, -0.17f), new Color(0.1f, 0.75f, 0.25f));
            CreateButton("SB2", "顺序启动", false, new Vector3(-0.32f, 1.05f, -0.17f), new Color(0.1f, 0.75f, 0.25f));
            CreateButton("SBF", "正转", false, new Vector3(0.08f, 1.05f, -0.17f), new Color(0.1f, 0.75f, 0.25f));
            CreateButton("SBR", "反转", false, new Vector3(0.48f, 1.05f, -0.17f), new Color(0.95f, 0.7f, 0.08f));
            CreateButton("SBB", "制动", false, new Vector3(0.32f, 0.62f, -0.17f), new Color(0.95f, 0.46f, 0.08f));
            CreateButton("SBE", "能耗制动", false, new Vector3(-0.72f, 0.62f, -0.17f), new Color(0.18f, 0.58f, 0.95f));
            CreateButton("SB0A", "停 A", true, new Vector3(0.88f, 1.05f, -0.17f), Color.red);
            CreateButton("SB0B", "停 B", true, new Vector3(1.23f, 1.05f, -0.17f), Color.red);
            CreateButton("SB1A", "启 A", false, new Vector3(0.82f, 0.62f, -0.17f), new Color(0.1f, 0.75f, 0.25f));
            CreateButton("SB1B", "启 B", false, new Vector3(1.22f, 0.62f, -0.17f), new Color(0.1f, 0.75f, 0.25f));

            CreateDevice(new ElectricalDeviceRuntime("BRAKE", ElectricalDeviceKind.BrakeUnit, new[] { "IN", "OUT" }), "制动单元", new Vector3(-0.2f, 0.6f, -0.17f), new Vector3(0.52f, 0.32f, 0.18f), new Color(0.3f, 0.32f, 0.35f));
            foreach (var binding in MotorBindingDefinition.All)
                CreateMotor(binding.Id, binding.Label, binding.FallbackPosition);
        }

        private IReadOnlyList<CabinetBreakerInteractable> CreateCabinetBreakerInteractions()
        {
            var result = new List<CabinetBreakerInteractable>();
            if (originalEnvironment == null) return result;
            if (originalEnvironmentTransforms == null) CacheOriginalEnvironmentTransforms();

            AddCabinetBreaker("106", "KongQiKaiGuan_3PK", "三极断路器 106", 45f, 1f);
            AddCabinetBreaker("122", "KongQiKaiGuan_4PK", "四极断路器 122", -45f, 0.25f);
            return result;

            void AddCabinetBreaker(
                string breakerId,
                string modelName,
                string displayName,
                float openAngleDegrees,
                float positionTravelScale)
            {
                var deviceRoot = originalEnvironmentTransforms.FirstOrDefault(item =>
                    item.name == breakerId && item.gameObject.activeInHierarchy);
                var model = deviceRoot != null
                    ? deviceRoot.GetComponentsInChildren<Transform>(true).FirstOrDefault(item =>
                        string.Equals(item.name, modelName, StringComparison.OrdinalIgnoreCase) &&
                        item.gameObject.activeInHierarchy)
                    : null;
                var handle = model != null
                    ? model.GetComponentsInChildren<Transform>(true).FirstOrDefault(item =>
                        string.Equals(item.name, "switch", StringComparison.OrdinalIgnoreCase) &&
                        item.gameObject.activeInHierarchy)
                    : null;
                var pivot = model != null ? model.Find("RotateCenter") : null;
                var picker = model != null ? model.Find("picker")?.GetComponent<Collider>() : null;
                if (model == null || handle == null || pivot == null || picker == null)
                {
                    Debug.LogWarning($"[OfflineBootstrap] Cabinet breaker {breakerId}/{modelName} is incomplete and cannot be interactive.");
                    return;
                }

                var interaction = model.gameObject.AddComponent<CabinetBreakerInteractable>();
                interaction.Initialize(
                    breakerId,
                    displayName,
                    handle,
                    pivot,
                    picker,
                    openAngleDegrees,
                    0.2f,
                    positionTravelScale);
                result.Add(interaction);
            }
        }

        private void CacheCabinetWireDuctCoverGroups()
        {
            cabinetWireDuctCoverGroups.Clear();
            if (originalEnvironment == null) return;

            foreach (var path in new[]
                     {
                         "Bench/ElectricBench/mesh/xiancaogai",
                         "Bench/ElectricBench/mesh/xiancaogai_1"
                     })
            {
                var group = originalEnvironment.Find(path);
                if (group != null)
                    cabinetWireDuctCoverGroups.Add(group.gameObject);
                else
                    Debug.LogWarning("[OfflineBootstrap] Cabinet wire-duct cover group not found: " + path);
            }
        }

        private void SetCabinetWireDuctCoversForMode(SimulationMode mode)
        {
            var visible = mode != SimulationMode.Wiring;
            foreach (var group in cabinetWireDuctCoverGroups)
            {
                if (group != null && group.activeSelf != visible)
                    group.SetActive(visible);
            }
        }

        private void CreateCabinetBreakerConnectionPorts()
        {
            if (originalEnvironment == null) return;
            if (originalEnvironmentTransforms == null) CacheOriginalEnvironmentTransforms();

            AddBreakerPorts(
                "106",
                "KongQiKaiGuan_3PK",
                "三极断路器 106",
                new[] { "L1", "L3", "L5", "L2", "L4", "L6" });
            AddBreakerPorts(
                "122",
                "KongQiKaiGuan_4PK",
                "四极断路器 122",
                new[] { "N1", "L1", "L3", "L5", "N2", "L2", "L4", "L6" });

            void AddBreakerPorts(
                string breakerId,
                string modelName,
                string displayName,
                IReadOnlyList<string> terminalNames)
            {
                var deviceRoot = originalEnvironmentTransforms.FirstOrDefault(item =>
                    item.name == breakerId && item.gameObject.activeInHierarchy);
                var model = deviceRoot != null
                    ? deviceRoot.GetComponentsInChildren<Transform>(true).FirstOrDefault(item =>
                        string.Equals(item.name, modelName, StringComparison.OrdinalIgnoreCase) &&
                        item.gameObject.activeInHierarchy)
                    : null;
                var pointRoot = model != null ? model.Find("point") : null;
                if (pointRoot == null)
                {
                    Debug.LogWarning($"[OfflineBootstrap] Cabinet breaker terminal root is missing: {breakerId}/{modelName}/point");
                    return;
                }

                var anchors = terminalNames
                    .Select(pointRoot.Find)
                    .Where(item => item != null && item.gameObject.activeInHierarchy)
                    .ToList();
                if (anchors.Count != terminalNames.Count)
                {
                    var found = new HashSet<string>(anchors.Select(item => item.name), StringComparer.Ordinal);
                    var missing = terminalNames.Where(item => !found.Contains(item));
                    Debug.LogWarning($"[OfflineBootstrap] Cabinet breaker {breakerId} terminals are missing: {string.Join(", ", missing)}");
                }
                if (anchors.Count == 0) return;

                var runtimeId = "QF" + breakerId;
                var runtime = new ElectricalDeviceRuntime(
                    runtimeId,
                    ElectricalDeviceKind.Terminal,
                    anchors.Select(item => item.name));
                AddPhaseAlias("L1", "L1");
                AddPhaseAlias("L3", "L2");
                AddPhaseAlias("L5", "L3");
                AddPhaseAlias("L2", "T1");
                AddPhaseAlias("L4", "T2");
                AddPhaseAlias("L6", "T3");

                var portRoot = new GameObject(runtimeId + " Original Breaker Connection Points");
                portRoot.transform.SetParent(originalEnvironment, false);
                var view = portRoot.AddComponent<ElectricalDeviceView>();
                view.Initialize(runtime, displayName + "连接点");
                var wireBody = new WireBodyGeometry(model);
                foreach (var anchor in anchors)
                {
                    var portObject = CreatePrimitive(
                        PrimitiveType.Sphere,
                        "Port",
                        portRoot.transform,
                        portRoot.transform.InverseTransformPoint(anchor.position),
                        Vector3.one * 0.0085f,
                        new Color(0.12f, 0.86f, 0.36f));
                    var collider = portObject.GetComponent<SphereCollider>();
                    if (collider != null) collider.radius = 0.9f;
                    var port = portObject.AddComponent<ElectricalPortView>();
                    port.Initialize(runtimeId, anchor.name, new Color(0.12f, 0.86f, 0.36f));
                    port.ConfigureHover(anchor.name, anchor.name);
                    port.ConfigureOriginalAnchors(anchor, anchor, anchor, anchor);
                    port.ConfigureRearWireBody(wireBody);
                    port.ConfigureElectricalOnly();
                    port.ConfigureWiringModeOnly();
                    view.AddPort(port);
                }

                deviceViews.Add(view);
                Debug.Log($"[OfflineBootstrap] Cabinet breaker {breakerId} ready: {view.Ports.Count} physical connection points.");

                void AddPhaseAlias(string physicalPort, string qfPort)
                {
                    if (anchors.Any(item => item.name == physicalPort))
                        runtime.AddFixedLink(physicalPort, CircuitGraph.Port("QF", qfPort));
                }
            }
        }

        private void CreateButton(string id, string label, bool normallyClosed, Vector3 position, Color color)
        {
            if (CreateLegacyPanelControl(id, normallyClosed)) return;
            CreateDevice(ElectricalDeviceRuntime.CreatePushButton(id, normallyClosed), label, position, new Vector3(0.28f, 0.25f, 0.16f), color);
        }

        private void CreateContactorDevice(string id, string label, Vector3 position)
        {
            CreateDevice(ElectricalDeviceRuntime.CreateContactor(id), label, position, new Vector3(0.48f, 0.38f, 0.18f), new Color(0.16f, 0.2f, 0.24f));
        }

        private void CreateRearContactorDevice(string id, string label)
        {
            var root = new GameObject(id + "_" + label);
            var view = root.AddComponent<ElectricalDeviceView>();
            view.Initialize(ElectricalDeviceRuntime.CreateContactor(id), label);
            CreatePorts(view, root.transform, view.Runtime.Ports, new Vector3(0.48f, 0.38f, 0.18f));
            deviceViews.Add(view);
        }

        private void CreateMotor(string id, string label, Vector3 position)
        {
            position.x *= 0.72f;
            var runtime = ElectricalDeviceRuntime.CreateMotor(id);
            var original = originalVisuals != null ? originalVisuals.Resolve(id, runtime.Kind.ToString()) : null;
            GameObject root;
            if (original != null)
            {
                root = Instantiate(original, position, Quaternion.Euler(0f, 180f, 0f));
                root.name = id + "_" + label;
                FitOriginalVisual(root, position, new Vector3(0.65f, 0.55f, 0.72f));
                if (root.GetComponentInChildren<Collider>() == null) root.AddComponent<BoxCollider>();
            }
            else
            {
                root = new GameObject(id + "_" + label);
                root.transform.position = position;
                var body = CreatePrimitive(PrimitiveType.Cylinder, "Body", root.transform, Vector3.zero, new Vector3(0.25f, 0.42f, 0.25f), new Color(0.12f, 0.24f, 0.32f));
                body.transform.localEulerAngles = new Vector3(90f, 0f, 0f);
                var rotor = CreatePrimitive(PrimitiveType.Cylinder, "Rotor", root.transform, new Vector3(0f, 0f, -0.32f), new Vector3(0.08f, 0.22f, 0.08f), new Color(0.75f, 0.76f, 0.78f));
                rotor.transform.localEulerAngles = new Vector3(90f, 0f, 0f);
                var collider = root.AddComponent<BoxCollider>();
                collider.size = new Vector3(0.65f, 0.55f, 0.72f);
            }
            if (originalEnvironment != null) HideDuplicateVisual(root);
            var view = root.AddComponent<ElectricalDeviceView>();
            view.Initialize(runtime, label);
            CreatePorts(view, root.transform, runtime.Ports, new Vector3(0.62f, 0.5f, 0.1f));
            if (originalEnvironment != null)
            {
                var model = originalEnvironment.Find(MotorBindingDefinition.Find(id).ModelPath);
                if (model == null || view.Ports.Count != 6)
                    throw new InvalidOperationException("Incomplete motor binding: " + id);
                var positions = view.Ports.Select(p => p.GetOriginalAnchor(TrainingViewPreset.WiringFront, true).position).ToArray();
                if (positions.Distinct().Count() != 6)
                    throw new InvalidOperationException("Motor terminals must have six distinct anchors: " + id);
                var body = model.GetComponentsInChildren<MeshRenderer>(true)
                    .Where(r => r.GetComponent<TextMesh>() == null && !IsTerminalPointTransform(r.transform)).ToArray();
                if (body.Length == 0) throw new InvalidOperationException("Motor body is missing: " + id);
                var bounds = body[0].bounds;
                foreach (var renderer in body) bounds.Encapsulate(renderer.bounds);
                var outward = MotorBindingDefinition.TerminalOutward(positions, bounds.center);
                foreach (var port in view.Ports) port.ConfigureMotorTerminal(id, model, outward);
                Debug.Log("[MotorValidation] " + id + " @ " + MotorBindingDefinition.Find(id).Nut + ": 6/6 terminals");
            }
            deviceViews.Add(view);
        }

        private void CreateDevice(ElectricalDeviceRuntime runtime, string label, Vector3 position, Vector3 size, Color color)
        {
            if (runtime.Kind == ElectricalDeviceKind.PowerSource && panelPower != null)
                runtime.SupplyEnabled = () => panelPower.Enabled;
            position.x *= 0.72f;
            var original = originalVisuals != null ? originalVisuals.Resolve(runtime.DeviceId, runtime.Kind.ToString()) : null;
            GameObject root;
            if (original != null)
            {
                root = Instantiate(original, position, Quaternion.Euler(0f, 180f, 0f));
                root.name = runtime.DeviceId + "_" + label;
                FitOriginalVisual(root, position, size);
                if (root.GetComponentInChildren<Collider>() == null) root.AddComponent<BoxCollider>();
            }
            else
            {
                root = new GameObject(runtime.DeviceId + "_" + label);
                root.transform.position = position;
                CreatePrimitive(PrimitiveType.Cube, "PlaceholderVisual", root.transform, Vector3.zero, size, color);
                var collider = root.AddComponent<BoxCollider>();
                collider.size = size;
            }

            if (originalEnvironment != null) HideDuplicateVisual(root);
            var view = root.AddComponent<ElectricalDeviceView>();
            view.Initialize(runtime, label);
            CreatePorts(view, root.transform, runtime.Ports, size);
            if (original == null)
                CreateWorldLabel(label, position + new Vector3(0f, size.y * 0.7f, -0.15f), 0.034f, Color.white);
            deviceViews.Add(view);
        }

        private static void HideDuplicateVisual(GameObject root)
        {
            foreach (var renderer in root.GetComponentsInChildren<Renderer>(true)) renderer.enabled = false;
        }

        private void CreatePorts(ElectricalDeviceView view, Transform parent, IReadOnlyCollection<string> ports, Vector3 bounds)
        {
            // The brake unit remains in the circuit model for legacy task/save compatibility,
            // but its generic IN/OUT points are not physical cabinet connection points.
            if (view.Runtime.Kind == ElectricalDeviceKind.BrakeUnit) return;

            // In the original environment most devices are wired only through terminal boards.
            // The three main contactors and FR are the exception: troubleshooting needs their
            // rear physical terminals. Keep those ports, but give them no front anchor so they
            // cannot reappear as detached markers in the wiring view.
            var faultBodyPorts = originalEnvironment != null &&
                                 (ShouldExposeContactorBodyPorts(view.Runtime) ||
                                  ShouldExposeThermalRelayBodyPorts(view.Runtime));
            var motorBodyPorts = originalEnvironment != null &&
                                 view.Runtime.Kind == ElectricalDeviceKind.Motor;
            if (originalEnvironment != null && !faultBodyPorts && !motorBodyPorts) return;

            // Controls and lower-cabinet switching devices are wired exclusively through
            // their original terminal boards. Keep runtime behaviour, but do not leave a
            // second set of clickable spheres on the device models themselves.
            if (RoutesThroughOriginalTerminalBoard(view.Runtime) &&
                !ShouldExposeContactorBodyPorts(view.Runtime) &&
                !ShouldExposeThermalRelayBodyPorts(view.Runtime)) return;

            var list = ports.ToList();
            WireBodyGeometry rearWireBody = null;
            var columns = Mathf.Min(6, Mathf.Max(2, Mathf.CeilToInt(list.Count / 2f)));
            for (var index = 0; index < list.Count; index++)
            {
                var dualLineFaultTerminal = faultBodyPorts &&
                                            ShouldExposeThermalRelayTerminalInBothLineModes(
                                                view.Runtime, list[index]);
                var row = index / columns;
                var column = index % columns;
                var x = columns == 1 ? 0f : Mathf.Lerp(-bounds.x * 0.42f, bounds.x * 0.42f, column / (float)(columns - 1));
                var y = row == 0 ? bounds.y * 0.48f : -bounds.y * 0.48f;
                var fallback = new Vector3(x, y, -bounds.z * 0.68f - 0.025f);
                Transform frontElectrical;
                Transform frontJumper;
                Transform backElectrical;
                Vector3 localPosition;
                if (faultBodyPorts)
                {
                    frontElectrical = null;
                    frontJumper = null;
                    backElectrical = ResolveFaultBodyTerminal(
                        view.Runtime.DeviceId, view.Runtime.Kind, list[index], true);
                    if (backElectrical == null)
                    {
                        Debug.LogWarning($"[OfflineBootstrap] Rear terminal is missing: {view.Runtime.DeviceId}/{list[index]}");
                        continue;
                    }
                    localPosition = parent.InverseTransformPoint(backElectrical.position);
                }
                else if (motorBodyPorts)
                {
                    frontElectrical = FindMappedEnvironmentTerminal(
                        view.Runtime.DeviceId, view.Runtime.Kind, list[index], false);
                    if (frontElectrical == null)
                    {
                        Debug.LogWarning($"[OfflineBootstrap] Motor terminal is missing: {view.Runtime.DeviceId}/{list[index]}");
                        continue;
                    }

                    frontJumper = frontElectrical;
                    backElectrical = frontElectrical;
                    localPosition = parent.InverseTransformPoint(frontElectrical.position);
                }
                else
                {
                    frontElectrical = FindTerminal(parent, view.Runtime.Kind, list[index]) ??
                                      FindOriginalEnvironmentTerminal(view.Runtime.DeviceId, view.Runtime.Kind, list[index], false) ??
                                      FindMappedEnvironmentTerminal(view.Runtime.DeviceId, view.Runtime.Kind, list[index], false);
                    frontJumper = FindOriginalEnvironmentTerminal(view.Runtime.DeviceId, view.Runtime.Kind, list[index], true) ?? frontElectrical;
                    backElectrical = FindMappedEnvironmentTerminal(view.Runtime.DeviceId, view.Runtime.Kind, list[index], true) ?? frontElectrical;
                    localPosition = frontElectrical != null ? parent.InverseTransformPoint(frontElectrical.position) : fallback;
                }
                if (!faultBodyPorts && ShouldExposeThermalRelayBodyPorts(view.Runtime) && list[index] == "T2")
                    localPosition += new Vector3(0f, -0.012f, 0.018f);
                // Original terminal highlights are small snap dots, not device-sized bulbs.
                var worldMarkerSize = ShouldExposeContactorBodyPorts(view.Runtime) ||
                                      ShouldExposeThermalRelayBodyPorts(view.Runtime)
                    ? 0.016f
                    : motorBodyPorts ? 0.0125f
                    : frontElectrical != null ? 0.0075f : 0.009f;
                if (faultBodyPorts) worldMarkerSize *= 0.5f;
                var parentScale = Mathf.Max(Mathf.Abs(parent.lossyScale.x), Mathf.Abs(parent.lossyScale.y), Mathf.Abs(parent.lossyScale.z));
                var markerSize = worldMarkerSize / Mathf.Max(0.0001f, parentScale);
                var portObject = CreatePrimitive(PrimitiveType.Sphere, "Port", parent, localPosition, Vector3.one * markerSize, new Color(0.08f, 1f, 0.32f));
                var port = portObject.AddComponent<ElectricalPortView>();
                port.Initialize(view.Runtime.DeviceId, list[index], new Color(0.12f, 0.86f, 0.36f));
                if (faultBodyPorts)
                {
                    if (rearWireBody == null)
                    {
                        var bodyRoot = backElectrical;
                        while (bodyRoot != null && bodyRoot.name != BackDeviceNut(view.Runtime.DeviceId)) bodyRoot = bodyRoot.parent;
                        if (bodyRoot != null) rearWireBody = new WireBodyGeometry(bodyRoot);
                    }
                    port.ConfigureRearWireBody(rearWireBody);
                }
                if (ShouldExposeContactorBodyPorts(view.Runtime))
                    port.ConfigureHover(GetContactorHoverLabel(list[index]), list[index]);
                else if (ShouldExposeThermalRelayBodyPorts(view.Runtime))
                    port.ConfigureHover(GetThermalRelayBodyLabel(list[index]), list[index]);
                else if (motorBodyPorts)
                    port.ConfigureHover(frontElectrical.name, frontElectrical.name);
                port.ConfigureOriginalAnchors(
                    frontElectrical,
                    frontJumper,
                    backElectrical,
                    backElectrical,
                    !faultBodyPorts || dualLineFaultTerminal);
                if (view.Runtime.Kind == ElectricalDeviceKind.Motor)
                    port.ConfigureJumperOnly();
                else if (!dualLineFaultTerminal)
                    port.ConfigureElectricalOnly();
                view.AddPort(port);
            }
        }

        private bool RoutesThroughOriginalTerminalBoard(ElectricalDeviceRuntime runtime)
        {
            if (originalEnvironment == null || runtime == null) return false;
            if (runtime.Kind == ElectricalDeviceKind.PowerSource ||
                   runtime.Kind == ElectricalDeviceKind.PushButton ||
                   runtime.Kind == ElectricalDeviceKind.Indicator ||
                   runtime.Kind == ElectricalDeviceKind.SelectorSwitch)
                return true;

            return runtime.Kind == ElectricalDeviceKind.Contactor ||
                   runtime.Kind == ElectricalDeviceKind.ThermalRelay;
        }

        private static bool ShouldExposeContactorBodyPorts(ElectricalDeviceRuntime runtime)
        {
            return runtime != null &&
                   runtime.Kind == ElectricalDeviceKind.Contactor &&
                   (runtime.DeviceId == "KMBACK1" || runtime.DeviceId == "KMBACK2" || runtime.DeviceId == "KMBACK3");
        }

        private static bool ShouldExposeThermalRelayBodyPorts(ElectricalDeviceRuntime runtime)
        {
            return runtime != null &&
                   runtime.Kind == ElectricalDeviceKind.ThermalRelay &&
                   runtime.DeviceId == "FR";
        }

        private static bool ShouldExposeThermalRelayTerminalInBothLineModes(
            ElectricalDeviceRuntime runtime,
            string port)
        {
            return ShouldExposeThermalRelayBodyPorts(runtime) &&
                   (port == "T1" || port == "T2" || port == "T3");
        }

        private static string GetThermalRelayBodyLabel(string port)
        {
            switch (port)
            {
                case "L1": return "1L1";
                case "L2": return "3L2";
                case "L3": return "5L3";
                case "T1": return "2T1";
                case "T2": return "4T2";
                case "T3": return "6T3";
                case "95": return "95NC";
                case "96": return "96NC";
                case "97": return "97NO";
                case "98": return "98NO";
                default: return port;
            }
        }

        private void RebuildMotorFaultBlocks(Transform environment)
        {
            var markerPaths = MotorBindingDefinition.All.Select(m => m.ModelPath + "/Cube").ToArray();

            motorFaultBlocks = new GameObject("MotorFaultBlocks");
            motorFaultBlocks.transform.SetParent(environment, false);
            for (var index = 0; index < markerPaths.Length; index++)
            {
                var markerPath = markerPaths[index];
                var marker = environment.Find(markerPath);
                if (marker == null)
                {
                    Debug.LogWarning("[OfflineBootstrap] Motor-side terminal is missing: " + markerPath);
                    continue;
                }

                var replacement = Instantiate(marker.gameObject, marker.parent);
                replacement.name = "MotorFaultBlock_" + (index + 1);
                replacement.transform.SetParent(motorFaultBlocks.transform, true);
                replacement.SetActive(true);
                marker.gameObject.SetActive(false);
                Destroy(marker.gameObject);
            }
            motorFaultBlocks.SetActive(false);
        }

        private static string GetContactorHoverLabel(string port)
        {
            switch (port)
            {
                case "L1": return "1L1";
                case "L2": return "3L2";
                case "L3": return "5L3";
                case "T1": return "2T1";
                case "T2": return "4T2";
                case "T3": return "6T3";
                case "13": return "13NO";
                case "14": return "14NO";
                case "53": return "53NO";
                case "54": return "54NO";
                case "61": return "61NC";
                case "62": return "62NC";
                case "71": return "71NC";
                case "72": return "72NC";
                case "83": return "83NO";
                case "84": return "84NO";
                default: return port;
            }
        }

        private void CreateOriginalTerminalBoardPorts()
        {
            if (originalEnvironment == null) return;
            var configurationPath = Path.Combine(Application.streamingAssetsPath, OriginalTerminalBoardMap.RelativeConfigurationPath);
            OriginalTerminalBoardMap map;
            try
            {
                map = OriginalTerminalBoardMap.Load(configurationPath);
            }
            catch (Exception exception)
            {
                Debug.LogError("[OfflineBootstrap] Original terminal map could not be loaded: " + exception.Message);
                return;
            }

            var board = originalEnvironment.Find(OriginalTerminalBoardMap.BoardTransformPath);
            var pointRoot = board != null ? board.Find("point") : null;
            if (pointRoot == null)
            {
                Debug.LogError("[OfflineBootstrap] Original DuanZiPai_0/point hierarchy is missing.");
                return;
            }

            var runtime = new ElectricalDeviceRuntime(
                OriginalTerminalBoardMap.DeviceId,
                ElectricalDeviceKind.Terminal,
                map.Bindings.Select(item => item.AnchorId));
            var root = new GameObject("Original Top Terminal Board Ports");
            root.transform.SetParent(originalEnvironment, false);
            var view = root.AddComponent<ElectricalDeviceView>();
            view.Initialize(runtime, "顶部控制面板端子排");

            foreach (var binding in map.Bindings)
            {
                var electricalAnchor = pointRoot.Find(binding.AnchorId);
                if (electricalAnchor == null)
                {
                    Debug.LogWarning($"[OfflineBootstrap] Terminal anchor is missing: {binding.AnchorId}");
                    continue;
                }

                runtime.AddFixedLink(binding.AnchorId, binding.LogicalNode);
                var localPosition = root.transform.InverseTransformPoint(electricalAnchor.position);
                var portObject = CreatePrimitive(
                    PrimitiveType.Sphere,
                    "Port",
                    root.transform,
                    localPosition,
                    Vector3.one * 0.0075f,
                    new Color(0.12f, 0.86f, 0.36f));
                var collider = portObject.GetComponent<SphereCollider>();
                if (collider != null) collider.radius = 0.85f;
                var port = portObject.AddComponent<ElectricalPortView>();
                port.Initialize(OriginalTerminalBoardMap.DeviceId, binding.AnchorId, new Color(0.12f, 0.86f, 0.36f));
                port.ConfigureHover(binding.DisplayName, binding.AnchorId);
                // The terminal strip is the sole physical endpoint. Its uppercase A* points
                // were duplicate jump-wire markers and must not appear in the runtime view.
                // In troubleshooting view the SB1-SB3 terminals move to the physical
                // twelve-position strip already mounted in the imported cabinet.
                var faultAnchor = ResolveFaultButtonTerminalAnchor(binding.DisplayName);
                port.ConfigureOriginalAnchors(
                    electricalAnchor,
                    null,
                    faultAnchor != null ? faultAnchor : electricalAnchor,
                    null,
                    false);
                port.ConfigureElectricalOnly();
                view.AddPort(port);
            }

            deviceViews.Add(view);
            Debug.Log($"[OfflineBootstrap] Original top terminal board ready: {view.Ports.Count}/{map.Bindings.Count} terminals.");
        }

        private void CreateFaultButtonTerminalConnections(TrainingCameraController cameraController)
        {
            faultButtonTerminalAnchors.Clear();
            if (originalEnvironment == null || cameraController == null)
            {
                return;
            }
            if (originalEnvironmentTransforms == null) CacheOriginalEnvironmentTransforms();

            var board = originalEnvironmentTransforms.FirstOrDefault(item =>
                string.Equals(item.name, "DuanZiPai_5", StringComparison.Ordinal) &&
                item.Find("point") != null);
            var pointRoot = board != null ? board.Find("point") : null;
            if (pointRoot == null)
            {
                Debug.LogError("[OfflineBootstrap] Existing DuanZiPai_5/point hierarchy is missing.");
                return;
            }

            var semanticNames = new[]
            {
                "SB1_NO1", "SB1_COM1", "SB1_NC2", "SB1_COM2",
                "SB2_NO1", "SB2_COM1", "SB2_NC2", "SB2_COM2",
                "SB3_NO1", "SB3_COM1", "SB3_NC2", "SB3_COM2"
            };
            var physicalNames = new[]
            {
                "a1", "a2", "a3", "a4", "a5", "a6",
                "a7", "a8", "a9", "a10", "a11", "a12"
            };
            for (var index = 0; index < semanticNames.Length; index++)
            {
                var anchor = pointRoot.Find(physicalNames[index]);
                if (anchor == null) continue;
                faultButtonTerminalAnchors[semanticNames[index]] = anchor;
            }

            if (faultButtonTerminalAnchors.Count != 12)
                Debug.LogWarning($"[OfflineBootstrap] Existing DuanZiPai_5 exposes {faultButtonTerminalAnchors.Count}/12 SB anchors.");
            CreateFaultButtonTerminalAnnotation(cameraController);
        }

        private void CreateFaultButtonTerminalAnnotation(TrainingCameraController cameraController)
        {
            if (faultButtonTerminalAnchors.Count == 0) return;

            var anchors = faultButtonTerminalAnchors.Values.ToArray();
            var center = anchors.Aggregate(Vector3.zero, (sum, anchor) => sum + anchor.position) /
                         anchors.Length;
            var annotationRoot = originalEnvironment.Find("Terminal Board Annotations");
            var orientationReference = annotationRoot != null
                ? annotationRoot.Find("Terminal Annotation - Three Phase Power")
                : null;
            if (orientationReference == null) return;
            var oppositeFacingRotation = orientationReference.rotation * Quaternion.Euler(0f, 180f, 0f);
            var front = orientationReference.forward;

            var labelObject = new GameObject("Terminal Annotation - Fault Buttons SB");
            labelObject.transform.SetParent(originalEnvironment, true);
            labelObject.transform.SetPositionAndRotation(
                center + front * 0.0025f,
                oppositeFacingRotation);

            var textMesh = labelObject.AddComponent<TextMesh>();
            textMesh.text = "按钮（SB）端子区";
            textMesh.font = uiFont;
            textMesh.fontSize = 96;
            textMesh.fontStyle = FontStyle.Bold;
            textMesh.characterSize = 0.002f;
            textMesh.anchor = TextAnchor.MiddleCenter;
            textMesh.alignment = TextAlignment.Center;
            textMesh.color = new Color(1f, 0.9f, 0f, 1f);
            labelObject.transform.localScale = orientationReference.localScale;

            var renderer = labelObject.GetComponent<MeshRenderer>();
            if (renderer == null) return;
            renderer.sharedMaterial = uiFont.material;
            renderer.sortingOrder = 101;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
            renderer.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;
            labelObject.transform.position += labelObject.transform.up * renderer.bounds.size.y * 1.15f;
            labelObject.AddComponent<BackViewPersistentRendererVisibility>()
                .Configure(new Renderer[] { renderer }, cameraController);
        }

        private Transform ResolveFaultButtonTerminalAnchor(string displayName)
        {
            if (string.IsNullOrWhiteSpace(displayName)) return null;
            faultButtonTerminalAnchors.TryGetValue(displayName, out var anchor);
            return anchor;
        }

        private void CreateOriginalCabinetTerminalBoardPorts()
        {
            if (originalEnvironment == null) return;
            if (originalEnvironmentTransforms == null) CacheOriginalEnvironmentTransforms();

            foreach (var definition in OriginalCabinetTerminalBoardMap.Boards)
            {
                var board = originalEnvironmentTransforms.FirstOrDefault(item =>
                    string.Equals(item.name, definition.DeviceId, StringComparison.Ordinal) &&
                    item.Find("point") != null);
                var pointRoot = board != null ? board.Find("point") : null;
                if (pointRoot == null)
                {
                    Debug.LogError($"[OfflineBootstrap] Original {definition.DeviceId}/point hierarchy is missing.");
                    continue;
                }

                var anchors = pointRoot.Cast<Transform>()
                    .Where(item => OriginalCabinetTerminalBoardMap.IsTerminalName(definition, item.name))
                    .GroupBy(item => item.name, StringComparer.Ordinal)
                    .Select(group => group.First())
                    .OrderBy(item => item.GetSiblingIndex())
                    .ToList();
                if (anchors.Count == 0)
                {
                    Debug.LogError($"[OfflineBootstrap] Original {definition.DeviceId} contains no named terminal anchors.");
                    continue;
                }

                var runtime = new ElectricalDeviceRuntime(
                    definition.DeviceId,
                    ElectricalDeviceKind.Terminal,
                    anchors.Select(item => OriginalCabinetTerminalBoardMap.GetPortName(definition, item.name)));
                var root = new GameObject(definition.DeviceId + " Original Connection Points");
                root.transform.SetParent(originalEnvironment, false);
                var view = root.AddComponent<ElectricalDeviceView>();
                view.Initialize(runtime, definition.DisplayName);

                if (definition.Kind == OriginalCabinetTerminalBoardKind.PowerDistribution)
                    board.gameObject.AddComponent<PowerTerminalBlockView>().Initialize(runtime);

                foreach (var anchor in anchors)
                {
                    var physicalAnchorName = anchor.name;
                    var terminalName = OriginalCabinetTerminalBoardMap.GetPortName(definition, physicalAnchorName);
                    var jumperAnchorName = OriginalCabinetTerminalBoardMap.GetJumperAnchorName(definition, physicalAnchorName);
                    var jumperAnchor = definition.UsesSeparateJumperAnchors ? pointRoot.Find(jumperAnchorName) : anchor;
                    if (definition.UsesSeparateJumperAnchors && jumperAnchor == null)
                    {
                        Debug.LogWarning($"[OfflineBootstrap] Jumper anchor is missing: {definition.DeviceId}/{jumperAnchorName}");
                        continue;
                    }
                    // The upper cabinet strip keeps its original upper electrical points,
                    // while the lower strip always exposes its lower physical points. Other
                    // boards may still switch between their original electrical/jumper rows.
                    var connectionAnchor = definition.AlwaysUsesJumperAnchor ? jumperAnchor : anchor;

                    runtime.AddFixedLink(terminalName, OriginalCabinetTerminalBoardMap.ResolveLogicalNode(definition, terminalName));

                    var markerSize = definition.Kind == OriginalCabinetTerminalBoardKind.Motor
                        ? 0.00625f
                        : 0.0075f;
                    var portObject = CreatePrimitive(
                        PrimitiveType.Sphere,
                        "Port",
                        root.transform,
                        root.transform.InverseTransformPoint(connectionAnchor.position),
                        Vector3.one * markerSize,
                        new Color(0.12f, 0.86f, 0.36f));
                    var collider = portObject.GetComponent<SphereCollider>();
                    if (collider != null) collider.radius = 1.6f;
                    var port = portObject.AddComponent<ElectricalPortView>();
                    port.Initialize(definition.DeviceId, terminalName, new Color(0.12f, 0.86f, 0.36f));
                    port.ConfigureHover(OriginalCabinetTerminalBoardMap.GetDisplayName(definition, terminalName), connectionAnchor.name);
                    // The original semantic point Transform remains authoritative. The explicit
                    // marker makes the connection location visible even when the ripped point
                    // renderer is inactive or occluded in the Unity 2022 player.
                    if (definition.AlwaysUsesElectricalAnchor || definition.AlwaysUsesJumperAnchor)
                        port.ConfigureOriginalAnchors(connectionAnchor, connectionAnchor, connectionAnchor, connectionAnchor);
                    else
                        port.ConfigureOriginalAnchors(anchor, jumperAnchor, anchor, jumperAnchor);
                    // The motor terminal strip (DuanZiPai_7, including C_w2) is
                    // visible in both line modes: its upper points are electrical
                    // endpoints and its lower points are jumper endpoints.
                    // All other cabinet strips belong to electrical-wire mode only.
                    if (definition.Kind != OriginalCabinetTerminalBoardKind.Motor)
                        port.ConfigureElectricalOnly();
                    view.AddPort(port);
                }

                deviceViews.Add(view);
                if (view.Ports.Count != definition.ExpectedPortCount)
                    Debug.LogWarning($"[OfflineBootstrap] Original {definition.DeviceId} expected {definition.ExpectedPortCount} terminals, found {view.Ports.Count}.");
                Debug.Log($"[OfflineBootstrap] Original {definition.DeviceId} ready: {view.Ports.Count} named terminals.");
            }
        }

        private Transform FindOriginalEnvironmentTerminal(string deviceId, ElectricalDeviceKind kind, string port, bool jumper)
        {
            if (originalEnvironment == null) return null;
            if (originalTerminals == null) CacheOriginalEnvironmentTransforms();
            foreach (var alias in TerminalAliases(kind, port))
            {
                var prefixes = deviceId == "FR" ? new[] { "FR1", "FR" } :
                    new[] { deviceId };
                foreach (var prefix in prefixes)
                {
                    var suffix = jumper ? alias.ToUpperInvariant() : alias.ToLowerInvariant();
                    var expected = string.IsNullOrEmpty(prefix) ? suffix : prefix + "_" + suffix;
                    if (originalTerminals.TryGetValue(expected, out var exact))
                    {
                        var point = exact.FirstOrDefault(IsTerminalPointTransform);
                        if (point != null) return point;
                    }
                }
                var scoped = originalEnvironmentTransforms.FirstOrDefault(item => IsTerminalPointTransform(item) &&
                                                        item.name.StartsWith(deviceId + "_", StringComparison.Ordinal) &&
                                                        item.name.EndsWith(jumper ? alias.ToUpperInvariant() : alias.ToLowerInvariant(), StringComparison.Ordinal));
                if (scoped != null) return scoped;
            }
            return null;
        }

        private Transform FindMappedEnvironmentTerminal(string deviceId, ElectricalDeviceKind kind, string port, bool back)
        {
            if (originalEnvironment == null) return null;
            if (originalEnvironmentTransforms == null) CacheOriginalEnvironmentTransforms();
            var nut = back ? BackDeviceNut(deviceId) : FrontDeviceNut(deviceId);
            if (string.IsNullOrEmpty(nut)) return null;
            foreach (var alias in TerminalAliases(kind, port))
            {
                var match = originalEnvironmentTransforms.FirstOrDefault(item =>
                    string.Equals(item.name, alias, StringComparison.OrdinalIgnoreCase) &&
                    IsTerminalPointTransform(item) && HasAncestor(item, nut));
                if (match != null) return match;
            }
            // Some original models (notably PE/chassis terminals) do not carry
            // a semantic name.  Keep those logical ports on the mapped device
            // instead of falling back to the near-camera hidden prefab.
            return originalEnvironmentTransforms.FirstOrDefault(item =>
                IsTerminalPointTransform(item) && HasAncestor(item, nut));
        }

        private Transform ResolveFaultBodyTerminal(string deviceId, ElectricalDeviceKind kind, string port, bool back)
        {
            var mapped = FindMappedEnvironmentTerminal(deviceId, kind, port, back);
            if (!back || deviceId != "FR" || port != "T2") return mapped;

            var left = FindMappedEnvironmentTerminal(deviceId, kind, "T1", true);
            var right = FindMappedEnvironmentTerminal(deviceId, kind, "T3", true);
            if (left == null || right == null) return mapped;

            var anchorObject = new GameObject("FR_4T2_FaultAnchor");
            anchorObject.transform.SetParent(left.parent, true);
            anchorObject.transform.position = Vector3.Lerp(left.position, right.position, 0.5f);
            anchorObject.transform.rotation = Quaternion.Slerp(left.rotation, right.rotation, 0.5f);
            return anchorObject.transform;
        }

        private static string FrontDeviceNut(string deviceId)
        {
            var motor = MotorBindingDefinition.Find(deviceId);
            if (motor != null) return motor.Nut;
            switch (deviceId)
            {
                case "POWER": return "123";
                case "QF": return "123";
                case "KMF": return "29";
                case "KM1": return "30";
                case "KMR": return "31";
                case "KM2": return "32";
                case "KMB": return "29";
                case "KB": return "30";
                case "FR": return "33";
                case "SB0": return "9";
                case "SB1": return "8";
                case "SB2": return "11";
                case "SBF": return "39";
                case "SBR": return "40";
                case "SBB": return "42";
                case "SBE": return "41";
                case "SB0A": return "12";
                case "SB0B": return "10";
                case "SB1A": return "7";
                case "SB1B": return "11";
                case "BRAKE": return "35";
                default: return null;
            }
        }

        private static string BackDeviceNut(string deviceId)
        {
            var motor = MotorBindingDefinition.Find(deviceId);
            if (motor != null) return motor.Nut;
            switch (deviceId)
            {
                case "QF": return "123";
                case "POWER": return "123";
                case "KMBACK1": return "111";
                case "KMBACK2": return "112";
                case "KMBACK3": return "113";
                case "FR": return "114";
                case "SB1": return "108";
                case "SB0": return "109";
                case "SB2": return "110";
                default: return null;
            }
        }

        private static bool HasAncestor(Transform item, string name)
        {
            for (var current = item.parent; current != null; current = current.parent)
                if (current.name == name) return true;
            return false;
        }

        private static bool IsTerminalPointTransform(Transform item)
        {
            return item != null && item.parent != null && item.parent.name == "point";
        }

        private void CacheOriginalEnvironmentTransforms()
        {
            originalEnvironmentTransforms = originalEnvironment != null
                ? originalEnvironment.GetComponentsInChildren<Transform>(true)
                : Array.Empty<Transform>();
            originalTerminals = new Dictionary<string, List<Transform>>(StringComparer.Ordinal);
            foreach (var item in originalEnvironmentTransforms)
            {
                if (!originalTerminals.TryGetValue(item.name, out var matches))
                {
                    matches = new List<Transform>();
                    originalTerminals.Add(item.name, matches);
                }
                matches.Add(item);
            }
        }

        private static void FitOriginalVisual(GameObject root, Vector3 targetCenter, Vector3 targetSize)
        {
            var renderers = root.GetComponentsInChildren<Renderer>(true);
            if (renderers.Length == 0) return;
            var bounds = renderers[0].bounds;
            for (var i = 1; i < renderers.Length; i++) bounds.Encapsulate(renderers[i].bounds);
            var widthScale = targetSize.x / Mathf.Max(0.001f, bounds.size.x);
            var heightScale = targetSize.y / Mathf.Max(0.001f, bounds.size.y);
            var scale = Mathf.Clamp(Mathf.Min(widthScale, heightScale), 0.25f, 20f);
            root.transform.localScale *= scale;

            bounds = renderers[0].bounds;
            for (var i = 1; i < renderers.Length; i++) bounds.Encapsulate(renderers[i].bounds);
            root.transform.position += targetCenter - bounds.center;
        }

        private static Transform FindTerminal(Transform root, ElectricalDeviceKind kind, string port)
        {
            foreach (var alias in TerminalAliases(kind, port))
            {
                var match = root.GetComponentsInChildren<Transform>(true)
                    .FirstOrDefault(item => string.Equals(item.name, alias, StringComparison.OrdinalIgnoreCase) &&
                                            IsTerminalPointTransform(item));
                if (match != null) return match;
            }
            return null;
        }

        private static IEnumerable<string> TerminalAliases(ElectricalDeviceKind kind, string port)
        {
            if (kind == ElectricalDeviceKind.Contactor)
            {
                var contactor = new Dictionary<string, string[]>
                {
                    { "A1", new[] { "A1" } }, { "A2", new[] { "A2" } },
                    { "L1", new[] { "1L1" } }, { "L2", new[] { "3L2" } }, { "L3", new[] { "5L3" } },
                    { "T1", new[] { "2T1" } }, { "T2", new[] { "4T2" } }, { "T3", new[] { "6T3" } },
                    { "13", new[] { "13", "13NO" } }, { "14", new[] { "14", "14NO" } },
                    { "53", new[] { "53", "53NO" } }, { "54", new[] { "54", "54NO" } },
                    { "61", new[] { "61", "61NC" } }, { "62", new[] { "62", "62NC" } },
                    { "71", new[] { "71", "71NC" } }, { "72", new[] { "72", "72NC" } },
                    { "83", new[] { "83", "83NO" } }, { "84", new[] { "84", "84NO" } }
                };
                if (contactor.TryGetValue(port, out var aliases)) return aliases;
            }
            if (kind == ElectricalDeviceKind.ThermalRelay)
            {
                var thermal = new Dictionary<string, string[]>
                {
                    { "L1", new[] { "1L1" } }, { "L2", new[] { "3L2" } }, { "L3", new[] { "5L3" } },
                    { "T1", new[] { "2T1" } }, { "T2", new[] { "4T2" } }, { "T3", new[] { "6T3" } },
                    { "95", new[] { "95NC" } }, { "96", new[] { "96NC" } },
                    { "97", new[] { "97NO" } }, { "98", new[] { "98NO" } }
                };
                if (thermal.TryGetValue(port, out var aliases)) return aliases;
            }
            if (kind == ElectricalDeviceKind.Breaker)
            {
                var breaker = new Dictionary<string, string[]>
                {
                    { "L1", new[] { "L1", "1" } }, { "L2", new[] { "L3", "3" } }, { "L3", new[] { "L5", "5" } },
                    { "T1", new[] { "L2", "2" } }, { "T2", new[] { "L4", "4" } }, { "T3", new[] { "L6", "6" } }
                };
                if (breaker.TryGetValue(port, out var aliases)) return aliases;
            }
            if (kind == ElectricalDeviceKind.PushButton)
                return port == "COM" ? new[] { "COM1", "COM2" } : new[] { port + "1", port + "2", port };
            if (kind == ElectricalDeviceKind.Motor)
            {
                var motor = new Dictionary<string, string[]>
                {
                    { "U", new[] { "U1" } }, { "V", new[] { "V1" } }, { "W", new[] { "W1" } },
                    { "U2", new[] { "U2" } }, { "V2", new[] { "V2" } }, { "W2", new[] { "W2" } }
                };
                if (motor.TryGetValue(port, out var aliases)) return aliases;
            }
            if (kind == ElectricalDeviceKind.PowerSource) return new[] { port };
            if (kind == ElectricalDeviceKind.BrakeUnit)
                return port == "IN" ? new[] { "1" } : new[] { "2" };
            return new[] { port };
        }

        private GameObject CreateCube(string name, Vector3 position, Vector3 scale, Color color)
        {
            return CreatePrimitive(PrimitiveType.Cube, name, null, position, scale, color, true);
        }

        private GameObject CreatePrimitive(PrimitiveType type, string name, Transform parent, Vector3 position, Vector3 scale, Color color, bool worldPosition = false)
        {
            var gameObject = GameObject.CreatePrimitive(type);
            gameObject.name = name;
            if (parent != null) gameObject.transform.SetParent(parent, false);
            if (worldPosition || parent == null) gameObject.transform.position = position;
            else gameObject.transform.localPosition = position;
            gameObject.transform.localScale = scale;
            var renderer = gameObject.GetComponent<Renderer>();
            var source = primitiveMaterial;
            if (source == null)
            {
                var shader = Shader.Find("Standard") ?? Shader.Find("Legacy Shaders/Diffuse");
                if (shader != null) source = new Material(shader);
            }
            if (source == null) return gameObject;
            renderer.material = new Material(source);
            renderer.material.color = color;
            return gameObject;
        }

        private void CreateWorldLabel(string text, Vector3 position, float size, Color color)
        {
            var gameObject = new GameObject("Label_" + text);
            gameObject.transform.position = position;
            gameObject.transform.eulerAngles = new Vector3(0f, 0f, 0f);
            var mesh = gameObject.AddComponent<TextMesh>();
            mesh.text = text;
            mesh.font = uiFont;
            mesh.fontSize = 36;
            mesh.characterSize = size;
            mesh.anchor = TextAnchor.MiddleCenter;
            mesh.alignment = TextAlignment.Center;
            mesh.color = color;
        }

    }

    internal sealed class FrontFaceOnlyTextVisibility : MonoBehaviour
    {
        private Renderer targetRenderer;
        private Transform viewingCamera;

        public void Configure(Renderer renderer, Transform cameraTransform)
        {
            targetRenderer = renderer;
            viewingCamera = cameraTransform;
            RefreshVisibility();
        }

        private void LateUpdate()
        {
            RefreshVisibility();
        }

        private void RefreshVisibility()
        {
            if (targetRenderer == null || viewingCamera == null) return;
            var directionToCamera = viewingCamera.position - transform.position;
            targetRenderer.enabled = Vector3.Dot(-transform.forward, directionToCamera) > 0f;
        }
    }

    internal sealed class BackViewPersistentRendererVisibility : MonoBehaviour
    {
        private Renderer[] targetRenderers = Array.Empty<Renderer>();
        private TrainingCameraController cameraController;

        public void Configure(Renderer[] renderers, TrainingCameraController controller)
        {
            targetRenderers = renderers ?? Array.Empty<Renderer>();
            cameraController = controller;
            if (cameraController != null) cameraController.ViewSideChanged += OnViewSideChanged;
            RefreshVisibility();
        }

        private void OnDestroy()
        {
            if (cameraController != null) cameraController.ViewSideChanged -= OnViewSideChanged;
        }

        private void OnViewSideChanged(bool viewingFaultSide)
        {
            RefreshVisibility();
        }

        private void LateUpdate()
        {
            RefreshVisibility();
        }

        private void RefreshVisibility()
        {
            var directionToCamera = cameraController != null
                ? cameraController.transform.position - transform.position
                : Vector3.zero;
            var viewedFromTextFront = directionToCamera.sqrMagnitude > 0.0001f &&
                                      Vector3.Dot(-transform.forward, directionToCamera) > 0f;
            var visible = cameraController != null &&
                          cameraController.IsViewingFaultSide &&
                          viewedFromTextFront;
            foreach (var targetRenderer in targetRenderers)
                if (targetRenderer != null) targetRenderer.enabled = visible;
        }
    }
}
