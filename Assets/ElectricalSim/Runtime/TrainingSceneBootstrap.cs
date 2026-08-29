using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace ElectricalSim
{
    public sealed class TrainingSceneBootstrap : MonoBehaviour
    {
        [SerializeField] private OriginalVisualRegistry originalVisuals;
        [SerializeField] private bool showMissingAssetNotice = true;
        [SerializeField] private Material primitiveMaterial;
        [SerializeField] private Material wireMaterial;
        [SerializeField] private Texture2D cabinetBrandLogo;

        private readonly List<ElectricalDeviceView> deviceViews = new List<ElectricalDeviceView>();
        private Font uiFont;
        private SimulationController controller;
        private Transform originalEnvironment;
        private Dictionary<string, List<Transform>> originalTerminals;
        private Transform[] originalEnvironmentTransforms;
        private readonly Dictionary<string, Transform> faultButtonTerminalAnchors =
            new Dictionary<string, Transform>(StringComparer.OrdinalIgnoreCase);
        private OfflineExamController examController;
        private LocalCaptureRecorder captureRecorder;

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

        private void Update()
        {
            if (controller == null || examController == null) return;
            if (Input.GetKeyDown(KeyCode.F5)) BeginExam("A");
            else if (Input.GetKeyDown(KeyCode.F6)) BeginExam("B");
            else if (Input.GetKeyDown(KeyCode.F7)) BeginExam("C");
            else if (Input.GetKeyDown(KeyCode.F8)) BeginExam("D");
        }

        private void Build()
        {
            Debug.Log("[OfflineBootstrap] Build started.");
            Application.targetFrameRate = 60;
            uiFont = Font.CreateDynamicFontFromOSFont(new[] { "Microsoft YaHei UI", "Microsoft YaHei", "SimHei", "Arial" }, 18);
            Debug.Log("[OfflineBootstrap] Font ready.");
            CreateEnvironment();
            var cameraController = CreateCamera();
            cameraController.ResetView();
            RefreshTerminalBoardAnnotations(cameraController.transform);
            CreateFaultButtonTerminalConnections(cameraController);
            Debug.Log("[OfflineBootstrap] Environment ready.");
            var wireRoot = new GameObject("ElectricalWires").transform;
            CreateDevices();
            CreateOriginalTerminalBoardPorts();
            CreateOriginalCabinetTerminalBoardPorts();
            Debug.Log("[OfflineBootstrap] Devices ready.");
            var ui = CreateHud();
            Debug.Log("[OfflineBootstrap] HUD ready.");

            controller = gameObject.AddComponent<SimulationController>();
            examController = gameObject.AddComponent<OfflineExamController>();
            captureRecorder = gameObject.AddComponent<LocalCaptureRecorder>();
            controller.Initialize(deviceViews, cameraController, wireRoot, ui.Mode, ui.Task, ui.Description, ui.Schematic, ui.Status, ui.Instrument, wireMaterial, originalVisuals, ui.PortHover);
            BindUi(ui);
            BindOriginalUi(ui);
            if (originalEnvironment != null) Invoke(nameof(RefreshCabinetBranding), 0.1f);
            Debug.Log("[OfflineBootstrap] Build complete.");
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
                objectNameSuffix == "Below Inverter" ? "FR端子区KT端子区" : "FR端子区",
                "FR " + objectNameSuffix, verticalOffsetInGlyphHeights);
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
                originalEnvironment = environment.transform;
                CacheOriginalEnvironmentTransforms();
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
            CreateCube("Original Back Wall", new Vector3(-0.067f, 1.55f, -5.31f), new Vector3(5.62f, 3.2f, 0.10f), new Color(0.82f, 0.84f, 0.84f));
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
            CreateDevice(ElectricalDeviceRuntime.CreateContactor("KMB"), "反接制动", new Vector3(1.22f, 1.68f, -0.16f), new Vector3(0.42f, 0.34f, 0.18f), new Color(0.25f, 0.18f, 0.2f));
            CreateDevice(ElectricalDeviceRuntime.CreateContactor("KB"), "能耗制动", new Vector3(0.72f, 1.68f, -0.16f), new Vector3(0.42f, 0.34f, 0.18f), new Color(0.25f, 0.18f, 0.2f));
            CreateDevice(ElectricalDeviceRuntime.CreateThermalRelay("FR"), "热继电器 FR", new Vector3(-0.65f, 1.62f, -0.16f), new Vector3(0.52f, 0.34f, 0.18f), new Color(0.78f, 0.8f, 0.82f));
            CreateDevice(ElectricalDeviceRuntime.CreateTimeRelay("KT", 0.8f), "时间继电器 KT", new Vector3(0f, 1.62f, -0.16f), new Vector3(0.48f, 0.34f, 0.18f), new Color(0.18f, 0.2f, 0.22f));

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
            CreateMotor("M1", "三相电机 M1", new Vector3(-0.55f, 0.25f, -0.45f));
            CreateMotor("M_DOUBLE", "双速电机", new Vector3(0f, 0.25f, -0.45f));
            CreateMotor("M2", "三相电机 M2", new Vector3(0.45f, 0.25f, -0.45f));
        }

        private void CreateButton(string id, string label, bool normallyClosed, Vector3 position, Color color)
        {
            CreateDevice(ElectricalDeviceRuntime.CreatePushButton(id, normallyClosed), label, position, new Vector3(0.28f, 0.25f, 0.16f), color);
        }

        private void CreateContactorDevice(string id, string label, Vector3 position)
        {
            CreateDevice(ElectricalDeviceRuntime.CreateContactor(id), label, position, new Vector3(0.48f, 0.38f, 0.18f), new Color(0.16f, 0.2f, 0.24f));
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
            deviceViews.Add(view);
        }

        private void CreateDevice(ElectricalDeviceRuntime runtime, string label, Vector3 position, Vector3 size, Color color)
        {
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
            if (originalEnvironment != null && !faultBodyPorts) return;

            // Controls and lower-cabinet switching devices are wired exclusively through
            // their original terminal boards. Keep runtime behaviour, but do not leave a
            // second set of clickable spheres on the device models themselves.
            if (RoutesThroughOriginalTerminalBoard(view.Runtime) &&
                !ShouldExposeContactorBodyPorts(view.Runtime) &&
                !ShouldExposeThermalRelayBodyPorts(view.Runtime)) return;

            var list = ports.ToList();
            var columns = Mathf.Min(6, Mathf.Max(2, Mathf.CeilToInt(list.Count / 2f)));
            for (var index = 0; index < list.Count; index++)
            {
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
                    : frontElectrical != null ? 0.0075f : 0.009f;
                var parentScale = Mathf.Max(Mathf.Abs(parent.lossyScale.x), Mathf.Abs(parent.lossyScale.y), Mathf.Abs(parent.lossyScale.z));
                var markerSize = worldMarkerSize / Mathf.Max(0.0001f, parentScale);
                var portObject = CreatePrimitive(PrimitiveType.Sphere, "Port", parent, localPosition, Vector3.one * markerSize, new Color(0.08f, 1f, 0.32f));
                var port = portObject.AddComponent<ElectricalPortView>();
                port.Initialize(view.Runtime.DeviceId, list[index], new Color(0.12f, 0.86f, 0.36f));
                if (ShouldExposeContactorBodyPorts(view.Runtime))
                    port.ConfigureHover(GetContactorHoverLabel(list[index]), list[index]);
                else if (ShouldExposeThermalRelayBodyPorts(view.Runtime))
                    port.ConfigureHover(GetThermalRelayBodyLabel(list[index]), list[index]);
                port.ConfigureOriginalAnchors(
                    frontElectrical,
                    frontJumper,
                    backElectrical,
                    backElectrical,
                    !faultBodyPorts);
                if (view.Runtime.Kind == ElectricalDeviceKind.Motor)
                    port.ConfigureJumperOnly();
                else
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
                   runtime.Kind == ElectricalDeviceKind.ThermalRelay ||
                   runtime.Kind == ElectricalDeviceKind.TimeRelay;
        }

        private static bool ShouldExposeContactorBodyPorts(ElectricalDeviceRuntime runtime)
        {
            return runtime != null &&
                   runtime.Kind == ElectricalDeviceKind.Contactor &&
                   (runtime.DeviceId == "KMF" || runtime.DeviceId == "KM1" || runtime.DeviceId == "KMR");
        }

        private static bool ShouldExposeThermalRelayBodyPorts(ElectricalDeviceRuntime runtime)
        {
            return runtime != null &&
                   runtime.Kind == ElectricalDeviceKind.ThermalRelay &&
                   runtime.DeviceId == "FR";
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
                        ? 0.0125f
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
                    port.ConfigureHover(terminalName, connectionAnchor.name);
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
                case "KT": return "35";
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
                case "M1": return "38";
                case "M_DOUBLE": return "118";
                case "M2": return "49";
                case "BRAKE": return "35";
                default: return null;
            }
        }

        private static string BackDeviceNut(string deviceId)
        {
            switch (deviceId)
            {
                case "QF": return "123";
                case "POWER": return "123";
                case "KMF": return "111";
                case "KM1": return "112";
                case "KMR": return "113";
                case "KM2": return "113";
                case "FR": return "114";
                case "SB1": return "108";
                case "SB0": return "109";
                case "SB2": return "110";
                case "M1": return "107";
                case "M_DOUBLE": return "118";
                case "M2": return "118";
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

        private void AddCabinetBranding(GameObject root, string cabinetObjectName = null)
        {
            var logoTexture = ResolveCabinetBrandLogo();
            if (root == null || logoTexture == null) return;

            var target = string.IsNullOrWhiteSpace(cabinetObjectName)
                ? root.transform
                : root.GetComponentsInChildren<Transform>(true).FirstOrDefault(item =>
                    string.Equals(item.name, cabinetObjectName, StringComparison.OrdinalIgnoreCase));
            if (target == null) return;

            var meshFilter = target.GetComponent<MeshFilter>();
            if (meshFilter == null || meshFilter.sharedMesh == null) return;
            var cabinetRenderer = target.GetComponent<MeshRenderer>();
            if (cabinetRenderer == null) return;

            var bounds = meshFilter.sharedMesh.bounds;
            if (string.Equals(target.name, "DQG01", StringComparison.OrdinalIgnoreCase))
            {
                RemoveAddedCabinetLogo(cabinetRenderer, meshFilter.sharedMesh.subMeshCount);
                return;
            }

            var logoAspect = logoTexture.width / (float)Mathf.Max(1, logoTexture.height);
            var logoWidth = bounds.size.x * 0.265f;
            var logoHeight = logoWidth / Mathf.Max(0.01f, logoAspect);
            var sideMargin = bounds.size.x * 0.035f;
            var topMargin = bounds.size.y * 0.035f;
            var centerY = bounds.max.y - topMargin - logoHeight * 0.5f;
            var surfaceOffset = Mathf.Max(0.012f, bounds.size.z * 0.02f);

            var frontX = bounds.min.x + sideMargin + logoWidth * 0.5f;
            var backX = bounds.max.x - sideMargin - logoWidth * 0.5f;

            ApplyCabinetBrandingToMesh(
                meshFilter,
                cabinetRenderer,
                logoTexture,
                bounds,
                frontX,
                backX,
                centerY,
                logoWidth,
                logoHeight,
                surfaceOffset);

            CreateCabinetBrandPanel(
                "Cabinet WCK Logo Front",
                target,
                new Vector3(frontX, centerY, bounds.max.z + surfaceOffset * 1.5f),
                Quaternion.identity,
                logoTexture,
                logoWidth,
                logoHeight);
            CreateCabinetBrandPanel(
                "Cabinet WCK Logo Back",
                target,
                new Vector3(backX, centerY, bounds.min.z - surfaceOffset * 1.5f),
                Quaternion.Euler(0f, 180f, 0f),
                logoTexture,
                logoWidth,
                logoHeight);
        }

        private void RefreshCabinetBranding()
        {
            var cabinetMeshes = FindObjectsOfType<MeshFilter>(true)
                .Where(item => string.Equals(item.name, "DQG01", StringComparison.OrdinalIgnoreCase))
                .ToArray();
            foreach (var cabinetMesh in cabinetMeshes)
            {
                if (cabinetMesh.GetComponent<MeshRenderer>() != null)
                    AddCabinetBranding(cabinetMesh.gameObject);
            }
        }

        private static void RemoveAddedCabinetLogo(
            MeshRenderer cabinetRenderer,
            int originalSubMeshCount)
        {
            var materials = new List<Material>(cabinetRenderer.materials.Take(originalSubMeshCount));
            RemoveEmbeddedCabinetLogo(materials);
            cabinetRenderer.materials = materials.ToArray();
        }

        private static void RemoveEmbeddedCabinetLogo(IEnumerable<Material> materials)
        {
            foreach (var material in materials)
            {
                if (material == null || material.mainTexture == null ||
                    material.name.IndexOf("bq", StringComparison.OrdinalIgnoreCase) < 0)
                    continue;

                var source = material.mainTexture;
                var temporary = RenderTexture.GetTemporary(
                    source.width,
                    source.height,
                    0,
                    RenderTextureFormat.ARGB32,
                    RenderTextureReadWrite.sRGB);
                var previous = RenderTexture.active;

                try
                {
                    Graphics.Blit(source, temporary);
                    RenderTexture.active = temporary;
                    var cleaned = new Texture2D(source.width, source.height, TextureFormat.RGBA32, false)
                    {
                        name = source.name + " (Logo Removed)",
                        filterMode = source.filterMode,
                        wrapMode = source.wrapMode
                    };
                    cleaned.ReadPixels(new Rect(0f, 0f, source.width, source.height), 0, 0, false);

                    // The original 同立方/CUBE SPACE mark occupies only the upper-left
                    // portion of bq_0.png. Unity pixel coordinates start at the bottom.
                    var clearX = Mathf.RoundToInt(source.width * 0.015f);
                    var clearY = Mathf.RoundToInt(source.height * 0.915f);
                    var clearWidth = Mathf.RoundToInt(source.width * 0.16f);
                    var clearHeight = Mathf.RoundToInt(source.height * 0.075f);
                    cleaned.SetPixels(
                        clearX,
                        clearY,
                        clearWidth,
                        clearHeight,
                        Enumerable.Repeat(Color.clear, clearWidth * clearHeight).ToArray());
                    cleaned.Apply(false, false);
                    material.mainTexture = cleaned;
                }
                finally
                {
                    RenderTexture.active = previous;
                    RenderTexture.ReleaseTemporary(temporary);
                }
            }
        }

        private static void CreateCabinetBrandPanel(
            string name,
            Transform parent,
            Vector3 localPosition,
            Quaternion localRotation,
            Texture2D logoTexture,
            float logoWidth,
            float logoHeight)
        {
            var panel = GameObject.CreatePrimitive(PrimitiveType.Cube);
            panel.name = name;
            panel.transform.SetParent(parent, false);
            panel.transform.localPosition = localPosition;
            panel.transform.localRotation = localRotation;
            panel.transform.localScale = new Vector3(logoWidth * 1.22f, logoHeight * 1.68f, 0.002f);
            var collider = panel.GetComponent<Collider>();
            if (collider != null) Destroy(collider);

            var logoShader = Shader.Find("Standard") ?? Shader.Find("Legacy Shaders/Diffuse");
            var logoMaterial = new Material(logoShader) { name = name + " Material" };
            logoMaterial.mainTexture = logoTexture;
            logoMaterial.color = Color.white;
            if (logoMaterial.HasProperty("_Metallic")) logoMaterial.SetFloat("_Metallic", 0.05f);
            if (logoMaterial.HasProperty("_Glossiness")) logoMaterial.SetFloat("_Glossiness", 0.28f);
            panel.GetComponent<MeshRenderer>().material = logoMaterial;
        }

        private static void ApplyCabinetBrandingToMesh(
            MeshFilter meshFilter,
            MeshRenderer cabinetRenderer,
            Texture2D logoTexture,
            Bounds bounds,
            float frontX,
            float backX,
            float centerY,
            float logoWidth,
            float logoHeight,
            float surfaceOffset)
        {
            var sourceMesh = meshFilter.sharedMesh;
            var brandedMesh = Instantiate(sourceMesh);
            brandedMesh.name = sourceMesh.name + " WCK Branded";

            var vertices = new List<Vector3>(sourceMesh.vertices);
            var normals = new List<Vector3>(sourceMesh.normals);
            var tangents = new List<Vector4>(sourceMesh.tangents);
            var uv0 = new List<Vector2>(sourceMesh.uv);
            var uv1 = new List<Vector2>(sourceMesh.uv2);
            var backingTriangles = new List<int>();
            var logoTriangles = new List<int>();

            var backingWidth = logoWidth * 1.18f;
            var backingHeight = logoHeight * 1.58f;
            var backingOffset = surfaceOffset;
            var logoOffset = surfaceOffset + Mathf.Max(0.002f, surfaceOffset * 0.25f);

            // The cabinet's local -X is screen-left on the front, and local +X is
            // screen-left on the back. Supplying the vertices in visual left-to-right
            // order keeps WCK readable on both faces.
            AppendBrandQuad(
                vertices, normals, tangents, uv0, uv1, backingTriangles,
                frontX - backingWidth * 0.5f, frontX + backingWidth * 0.5f,
                centerY, backingHeight, bounds.max.z + backingOffset, Vector3.forward,
                false);
            AppendBrandQuad(
                vertices, normals, tangents, uv0, uv1, backingTriangles,
                backX + backingWidth * 0.5f, backX - backingWidth * 0.5f,
                centerY, backingHeight, bounds.min.z - backingOffset, Vector3.back,
                false);
            AppendBrandQuad(
                vertices, normals, tangents, uv0, uv1, logoTriangles,
                frontX - logoWidth * 0.5f, frontX + logoWidth * 0.5f,
                centerY, logoHeight, bounds.max.z + logoOffset, Vector3.forward,
                true);
            AppendBrandQuad(
                vertices, normals, tangents, uv0, uv1, logoTriangles,
                backX + logoWidth * 0.5f, backX - logoWidth * 0.5f,
                centerY, logoHeight, bounds.min.z - logoOffset, Vector3.back,
                true);

            brandedMesh.SetVertices(vertices);
            if (normals.Count == vertices.Count) brandedMesh.SetNormals(normals);
            if (tangents.Count == vertices.Count) brandedMesh.SetTangents(tangents);
            if (uv0.Count == vertices.Count) brandedMesh.SetUVs(0, uv0);
            if (uv1.Count == vertices.Count) brandedMesh.SetUVs(1, uv1);

            var originalSubMeshCount = sourceMesh.subMeshCount;
            brandedMesh.subMeshCount = originalSubMeshCount + 2;
            brandedMesh.SetTriangles(backingTriangles, originalSubMeshCount, false);
            brandedMesh.SetTriangles(logoTriangles, originalSubMeshCount + 1, false);
            brandedMesh.RecalculateBounds();
            meshFilter.sharedMesh = brandedMesh;

            var backingShader = Shader.Find("Standard") ?? Shader.Find("Unlit/Color");
            var backingMaterial = new Material(backingShader) { name = "Cabinet WCK Backing Material" };
            backingMaterial.color = new Color(0.035f, 0.043f, 0.052f, 1f);
            if (backingMaterial.HasProperty("_Metallic")) backingMaterial.SetFloat("_Metallic", 0.32f);
            if (backingMaterial.HasProperty("_Glossiness")) backingMaterial.SetFloat("_Glossiness", 0.42f);

            var logoShader = Shader.Find("UI/Default") ?? Shader.Find("Unlit/Transparent") ?? Shader.Find("Standard");
            var logoMaterial = new Material(logoShader) { name = "Cabinet WCK Logo Material" };
            logoMaterial.mainTexture = logoTexture;
            logoMaterial.color = Color.white;
            logoMaterial.renderQueue = 3000;

            var materials = new List<Material>(cabinetRenderer.materials.Take(originalSubMeshCount))
            {
                backingMaterial,
                logoMaterial
            };
            cabinetRenderer.materials = materials.ToArray();
        }

        private static void AppendBrandQuad(
            List<Vector3> vertices,
            List<Vector3> normals,
            List<Vector4> tangents,
            List<Vector2> uv0,
            List<Vector2> uv1,
            List<int> triangles,
            float screenLeftX,
            float screenRightX,
            float centerY,
            float height,
            float z,
            Vector3 normal,
            bool mapLogoTexture)
        {
            var firstVertex = vertices.Count;
            var bottom = centerY - height * 0.5f;
            var top = centerY + height * 0.5f;
            vertices.Add(new Vector3(screenLeftX, bottom, z));
            vertices.Add(new Vector3(screenRightX, bottom, z));
            vertices.Add(new Vector3(screenRightX, top, z));
            vertices.Add(new Vector3(screenLeftX, top, z));

            for (var index = 0; index < 4; index++)
            {
                normals.Add(normal);
                tangents.Add(new Vector4(1f, 0f, 0f, 1f));
                uv1.Add(Vector2.zero);
            }

            if (mapLogoTexture)
            {
                uv0.Add(new Vector2(0f, 0f));
                uv0.Add(new Vector2(1f, 0f));
                uv0.Add(new Vector2(1f, 1f));
                uv0.Add(new Vector2(0f, 1f));
            }
            else
            {
                for (var index = 0; index < 4; index++) uv0.Add(Vector2.zero);
            }

            triangles.Add(firstVertex);
            triangles.Add(firstVertex + 1);
            triangles.Add(firstVertex + 2);
            triangles.Add(firstVertex);
            triangles.Add(firstVertex + 2);
            triangles.Add(firstVertex + 3);
            triangles.Add(firstVertex + 2);
            triangles.Add(firstVertex + 1);
            triangles.Add(firstVertex);
            triangles.Add(firstVertex + 3);
            triangles.Add(firstVertex + 2);
            triangles.Add(firstVertex);
        }

        private Texture2D ResolveCabinetBrandLogo()
        {
            if (cabinetBrandLogo != null) return cabinetBrandLogo;

            var externalLogoPath = Path.Combine(Application.streamingAssetsPath, "WCKLogo.png");
            if (!File.Exists(externalLogoPath)) return null;

            try
            {
                var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false)
                {
                    name = "WCK Cabinet Logo"
                };
                if (!texture.LoadImage(File.ReadAllBytes(externalLogoPath), false))
                {
                    Destroy(texture);
                    return null;
                }

                texture.wrapMode = TextureWrapMode.Clamp;
                texture.filterMode = FilterMode.Bilinear;
                cabinetBrandLogo = texture;
                return cabinetBrandLogo;
            }
            catch (Exception exception)
            {
                Debug.LogWarning("Unable to load external WCK cabinet logo: " + exception.Message);
                return null;
            }
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
            if (kind == ElectricalDeviceKind.TimeRelay)
            {
                var timer = new Dictionary<string, string[]>
                {
                    { "A1", new[] { "2" } }, { "A2", new[] { "7" } },
                    { "15", new[] { "1" } }, { "16", new[] { "3" } }, { "18", new[] { "4" } }
                };
                if (timer.TryGetValue(port, out var aliases)) return aliases;
            }
            if (kind == ElectricalDeviceKind.PowerSource) return new[] { port };
            if (kind == ElectricalDeviceKind.BrakeUnit)
                return port == "IN" ? new[] { "1" } : new[] { "2" };
            return new[] { port };
        }

        private HudReferences CreateHud()
        {
            if (EventSystem.current == null)
            {
                var eventSystem = new GameObject("EventSystem");
                eventSystem.AddComponent<EventSystem>();
                eventSystem.AddComponent<StandaloneInputModule>();
            }

            var canvasObject = new GameObject("Simulation HUD");
            var canvas = canvasObject.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 100;
            var scaler = canvasObject.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;
            canvasObject.AddComponent<GraphicRaycaster>();

            var top = Panel("TopBar", canvas.transform, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0f, -88f), Vector2.zero, darkBlue);
            var title = Label("Title", top.transform, "同立方 · 电气控制系统仿真软件  |  核心离线版", 26, TextAnchor.MiddleLeft, Color.white);
            SetRect(title.rectTransform, new Vector2(0f, 0f), new Vector2(0.34f, 1f), new Vector2(30f, 0f), new Vector2(-10f, 0f));

            var mode = Label("Mode", top.transform, "当前模式：视角", 22, TextAnchor.MiddleCenter, cyan);
            SetRect(mode.rectTransform, new Vector2(0.34f, 0f), new Vector2(0.48f, 1f), Vector2.zero, Vector2.zero);

            var right = Panel("RightPanel", canvas.transform, new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(-305f, 18f), new Vector2(-8f, 498f), panelBlue);
            var task = Label("TaskTitle", right.transform, "任务", 22, TextAnchor.UpperLeft, Color.white);
            SetRect(task.rectTransform, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(18f, -88f), new Vector2(-18f, -18f));
            var description = Label("TaskDescription", right.transform, "", 18, TextAnchor.UpperLeft, new Color(0.76f, 0.9f, 0.96f));
            SetRect(description.rectTransform, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(18f, -200f), new Vector2(-18f, -92f));

            var schematicFrame = Panel("SchematicFrame", right.transform, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(18f, -506f), new Vector2(-18f, -210f), new Color(0.92f, 0.94f, 0.94f, 0.98f));
            var schematicObject = new GameObject("TaskSchematic", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(AspectRatioFitter));
            schematicObject.transform.SetParent(schematicFrame.transform, false);
            var schematic = schematicObject.GetComponent<Image>();
            schematic.color = Color.white;
            schematic.preserveAspect = true;
            schematic.raycastTarget = false;
            var fitter = schematicObject.GetComponent<AspectRatioFitter>();
            fitter.aspectMode = AspectRatioFitter.AspectMode.FitInParent;
            SetRect(schematic.rectTransform, Vector2.zero, Vector2.one, new Vector2(8f, 8f), new Vector2(-8f, -8f));

            var instrument = Label("InstrumentReadout", right.transform, "万用表：请选择两个端子", 17, TextAnchor.UpperLeft, new Color(1f, 0.9f, 0.28f));
            SetRect(instrument.rectTransform, new Vector2(0f, 0f), new Vector2(1f, 0.42f), new Vector2(18f, 156f), new Vector2(-18f, -8f));

            var statusPanel = Panel("StatusPanel", canvas.transform, new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(-305f, 18f), new Vector2(-8f, 180f), new Color(0.12f, 0.28f, 0.29f, 0.94f));
            var status = Label("Status", statusPanel.transform, "系统就绪", 19, TextAnchor.MiddleLeft, new Color(1f, 0.88f, 0.2f));
            SetRect(status.rectTransform, Vector2.zero, Vector2.one, new Vector2(20f, 5f), new Vector2(-20f, -5f));

            var hoverObject = new GameObject("PortHoverPresenter");
            var portHover = hoverObject.AddComponent<PortHoverPresenter>();
            portHover.Initialize(canvas, uiFont);

            var references = new HudReferences { Canvas = canvas, Top = top, Right = right, Mode = mode, Task = task, Description = description, Schematic = schematic, Status = status, Instrument = instrument, PortHover = portHover };
            if (originalVisuals != null && originalVisuals.ResolveUi("TopNavigation") != null)
            {
                top.gameObject.SetActive(false);
                description.gameObject.SetActive(false);
                instrument.gameObject.SetActive(false);
            }
            if (showMissingAssetNotice && originalVisuals == null)
            {
                var notice = Label("AssetNotice", canvas.transform, "功能验证场景 · 原始 Assets 子集尚未导入", 18, TextAnchor.MiddleCenter, new Color(1f, 0.78f, 0.18f));
                SetRect(notice.rectTransform, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0f, -118f), new Vector2(-390f, -90f));
            }
            return references;
        }

        private void BindUi(HudReferences ui)
        {
            var modes = new[] { SimulationMode.View, SimulationMode.Drag, SimulationMode.Wiring, SimulationMode.Simulate, SimulationMode.Fault };
            for (var i = 0; i < modes.Length; i++)
            {
                var captured = modes[i];
                var button = Button("Mode_" + captured, ui.Top.transform, ModeLabel(captured), () => controller.SetMode(captured));
                SetRect(button.GetComponent<RectTransform>(), new Vector2(0f, 0f), new Vector2(0f, 1f), new Vector2(930f + i * 112f, 14f), new Vector2(1032f + i * 112f, -14f));
            }

            var open = Button("Open", ui.Top.transform, "打开", controller.OpenCc3d);
            SetRect(open.GetComponent<RectTransform>(), Vector2.zero, Vector2.zero, new Vector2(1500f, 14f), new Vector2(1592f, 74f));
            var save = Button("Save", ui.Top.transform, "导出", controller.SaveCc3d);
            SetRect(save.GetComponent<RectTransform>(), Vector2.zero, Vector2.zero, new Vector2(1600f, 14f), new Vector2(1692f, 74f));
            var reset = Button("Reset", ui.Top.transform, "重置", controller.ResetTraining);
            SetRect(reset.GetComponent<RectTransform>(), Vector2.zero, Vector2.zero, new Vector2(1700f, 14f), new Vector2(1792f, 74f));

            var previous = Button("PreviousTask", ui.Right.transform, "◀ 上一项", controller.PreviousTask);
            SetRect(previous.GetComponent<RectTransform>(), new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(18f, -568f), new Vector2(175f, -518f));
            var next = Button("NextTask", ui.Right.transform, "下一项 ▶", controller.NextTask);
            SetRect(next.GetComponent<RectTransform>(), new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-175f, -568f), new Vector2(-18f, -518f));
            var reference = Button("Reference", ui.Right.transform, "加载标准接线", controller.LoadReferenceWiring);
            SetRect(reference.GetComponent<RectTransform>(), new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(18f, -634f), new Vector2(-18f, -580f));
            var submit = Button("Submit", ui.Right.transform, "提交：拓扑 + 动作验收", controller.SubmitTask, new Color(0.04f, 0.58f, 0.78f));
            SetRect(submit.GetComponent<RectTransform>(), new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(18f, -700f), new Vector2(-18f, -646f));

            var instruments = new[] { InstrumentKind.Multimeter, InstrumentKind.VoltageProbe, InstrumentKind.Oscilloscope, InstrumentKind.Tachometer };
            for (var i = 0; i < instruments.Length; i++)
            {
                var captured = instruments[i];
                var button = Button("Instrument_" + captured, ui.Right.transform, InstrumentLabel(captured), () => controller.SelectInstrument(captured));
                var row = i / 2;
                var col = i % 2;
                SetRect(button.GetComponent<RectTransform>(), new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(18f + col * 176f, 62f + row * 60f), new Vector2(184f + col * 176f, 112f + row * 60f));
            }
        }

        private void BindOriginalUi(HudReferences ui)
        {
            if (originalVisuals == null) return;
            var navigation = InstantiateUi("TopNavigation", ui.Canvas.transform);
            var toolbar = InstantiateUi("ExperimentToolbar", ui.Canvas.transform);
            if (navigation != null)
            {
                BindNamedButton(navigation, "homeBtn", ToggleTaskPanel);
                BindNamedButton(navigation, "scheduleBtn", ToggleTaskPanel);
                BindNamedButton(navigation, "saveBtn", controller.SaveCc3d);
                BindNamedButton(navigation, "submitBtn", controller.SubmitTask);
                BindNamedButton(navigation, "resetBtn", controller.ResetTraining);
                SetNamedButtonActive(navigation, "saveBtn", true);
                SetNamedButtonActive(navigation, "submitBtn", true);
                SetNamedButtonActive(navigation, "downloadBtn", false);
                SetNamedButtonActive(navigation, "mineBtn", false);
                SetNamedButtonText(navigation, "scheduleBtn", "任务查询");
                SetNamedButtonText(navigation, "saveBtn", "保存");
                SetNamedButtonText(navigation, "submitBtn", "提交");
                SetNamedButtonText(navigation, "resetBtn", "重置");
                foreach (var id in new[] { "EditorBtn_A", "EditorBtn_B", "EditorBtn_C", "EditorBtn_D" })
                {
                    var examButton = FindNamed(navigation, id);
                    if (examButton != null) examButton.gameObject.SetActive(false);
                }
            }
            if (toolbar != null)
            {
                BindOriginalViewMenu(toolbar);
                BindNamedButton(toolbar, "btn_paigu", () => controller.SetMode(SimulationMode.Fault));
                BindNamedButton(toolbar, "btn_drag", () => controller.SetMode(SimulationMode.Drag));
                BindNamedButton(toolbar, "btn_line", () => controller.SetMode(SimulationMode.Wiring));
                BindNamedButton(toolbar, "btn_sim", () => controller.SetMode(SimulationMode.Simulate));
                BindNamedButton(toolbar, "btn_resume", controller.OpenCc3d);
                BindNamedButton(toolbar, "btn_snapshot", captureRecorder.CaptureScreenshot);
                BindNamedButton(toolbar, "btn_submit", controller.SubmitTask);
                BindNamedButton(toolbar, "btn_localSave", controller.SaveCc3d);
                BindNamedButton(toolbar, "btn_saveAnswer", controller.SaveCc3d);
                BindNamedButton(toolbar, "btn_record", captureRecorder.ToggleRecording);
                BindNamedButton(toolbar, "btn_audio", () => AudioListener.pause = !AudioListener.pause);
            }

            var lineForm = InstantiateUi("LineForm", ui.Canvas.transform);
            var lineParam = InstantiateUi("LineParam", ui.Canvas.transform);
            if (lineForm != null)
            {
                BindOriginalLineForm(lineForm);
                lineForm.SetActive(false);
            }
            if (lineParam != null) lineParam.SetActive(false);
            controller.ModeChanged += mode =>
            {
                if (lineForm != null) lineForm.SetActive(mode == SimulationMode.Wiring);
                // The original property window opens only for a selected wire.
                if (lineParam != null) lineParam.SetActive(false);
            };

            var ticker = ui.Canvas.gameObject.AddComponent<OfflineUiTicker>();
            ticker.Initialize(navigation, toolbar, examController);

            void ToggleTaskPanel() => ui.Right.gameObject.SetActive(!ui.Right.gameObject.activeSelf);
        }

        private void BindOriginalViewMenu(GameObject toolbar)
        {
            var cameraController = Camera.main != null ? Camera.main.GetComponent<TrainingCameraController>() : null;
            if (cameraController == null) return;
            var menu = toolbar.GetComponentsInChildren<Transform>(true).FirstOrDefault(item => item.name == "twoChange");
            BindNamedButton(toolbar, "btn_viewChange", () =>
            {
                if (menu != null) menu.gameObject.SetActive(!menu.gameObject.activeSelf);
            });
            BindButtonByText(menu, "接线视角", cameraController.SetWiringView);
            BindButtonByText(menu, "排故视角", cameraController.SetFaultView);
            BindButtonByText(menu, "重置视角", cameraController.ResetView);
            if (menu != null) menu.gameObject.SetActive(false);
        }

        private void BindOriginalLineForm(GameObject lineForm)
        {
            var lineTypeObject = lineForm.GetComponentsInChildren<Transform>(true).FirstOrDefault(item => item.name == "LineType");
            var dropdown = lineTypeObject != null ? lineTypeObject.GetComponent<Dropdown>() : null;
            if (dropdown == null) return;
            dropdown.ClearOptions();
            dropdown.AddOptions(new List<string> { "电线", "跳线" });
            dropdown.onValueChanged.RemoveAllListeners();
            dropdown.onValueChanged.AddListener(value =>
                controller.SetWireStyle(Color.red, 0.01f, value == 0 ? "ElectricalWire" : "JumperLine"));
            dropdown.value = 0;
            dropdown.RefreshShownValue();
            controller.SetWireStyle(Color.red, 0.01f, "ElectricalWire");
        }

        private static void BindButtonByText(Transform root, string label, UnityEngine.Events.UnityAction action)
        {
            if (root == null) return;
            var button = root.GetComponentsInChildren<Button>(true).FirstOrDefault(candidate =>
                candidate.GetComponentsInChildren<Text>(true).Any(text => text.text == label));
            if (button == null) return;
            button.onClick.RemoveAllListeners();
            button.onClick.AddListener(() =>
            {
                action();
                root.gameObject.SetActive(false);
            });
        }

        private void BeginExam(string package)
        {
            if (!examController.Begin(package))
            {
                controller.ShowStatus("未找到本地考试包 " + package, true);
                return;
            }
            examController.LoadFaultWiring(controller.Graph);
            controller.SetMode(SimulationMode.Fault);
            controller.ShowStatus($"已进入本地 {package} 套考试，时长 {examController.ActivePackage.Duration.TotalHours:0.#} 小时。", false);
        }

        private GameObject InstantiateUi(string id, Transform parent)
        {
            var prefab = originalVisuals.ResolveUi(id);
            if (prefab == null) return null;
            var instance = Instantiate(prefab, parent, false);
            instance.name = "OriginalUI_" + id;
            instance.SetActive(true);
            var rect = instance.GetComponent<RectTransform>();
            if (rect != null)
            {
                rect.localScale = Vector3.one;
                if (rect.rect.width < 10f || rect.rect.height < 10f)
                {
                    rect.anchorMin = Vector2.zero;
                    rect.anchorMax = Vector2.one;
                    rect.offsetMin = Vector2.zero;
                    rect.offsetMax = Vector2.zero;
                }
            }
            return instance;
        }

        private static void BindNamedButton(GameObject root, string name, UnityEngine.Events.UnityAction action)
        {
            var button = FindNamed(root, name);
            if (button == null) return;
            button.onClick.RemoveAllListeners();
            button.onClick.AddListener(action);
        }

        private static Button FindNamed(GameObject root, string name)
            => root.GetComponentsInChildren<Button>(true).FirstOrDefault(item => item.name == name);

        private static void SetNamedButtonActive(GameObject root, string name, bool active)
        {
            var button = FindNamed(root, name);
            if (button != null) button.gameObject.SetActive(active);
        }

        private static void SetNamedButtonText(GameObject root, string name, string value)
        {
            var button = FindNamed(root, name);
            if (button == null) return;
            var labels = button.GetComponentsInChildren<Text>(true);
            foreach (var label in labels)
                if (!string.IsNullOrWhiteSpace(label.text)) label.text = value;
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

        private RectTransform Panel(string name, Transform parent, Vector2 anchorMin, Vector2 anchorMax, Vector2 offsetMin, Vector2 offsetMax, Color color)
        {
            var gameObject = new GameObject(name, typeof(RectTransform), typeof(Image));
            gameObject.transform.SetParent(parent, false);
            var rect = gameObject.GetComponent<RectTransform>();
            SetRect(rect, anchorMin, anchorMax, offsetMin, offsetMax);
            gameObject.GetComponent<Image>().color = color;
            return rect;
        }

        private Text Label(string name, Transform parent, string text, int fontSize, TextAnchor alignment, Color color)
        {
            var gameObject = new GameObject(name, typeof(RectTransform), typeof(Text));
            gameObject.transform.SetParent(parent, false);
            var label = gameObject.GetComponent<Text>();
            label.font = uiFont;
            label.fontSize = fontSize;
            label.text = text;
            label.alignment = alignment;
            label.color = color;
            label.horizontalOverflow = HorizontalWrapMode.Wrap;
            label.verticalOverflow = VerticalWrapMode.Truncate;
            return label;
        }

        private Button Button(string name, Transform parent, string text, UnityEngine.Events.UnityAction action, Color? color = null)
        {
            var gameObject = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(Button));
            gameObject.transform.SetParent(parent, false);
            var image = gameObject.GetComponent<Image>();
            image.color = color ?? new Color(0.04f, 0.28f, 0.4f, 0.96f);
            var button = gameObject.GetComponent<Button>();
            button.targetGraphic = image;
            button.onClick.AddListener(action);
            var label = Label("Label", gameObject.transform, text, 18, TextAnchor.MiddleCenter, Color.white);
            SetRect(label.rectTransform, Vector2.zero, Vector2.one, new Vector2(5f, 2f), new Vector2(-5f, -2f));
            return button;
        }

        private static void SetRect(RectTransform rect, Vector2 anchorMin, Vector2 anchorMax, Vector2 offsetMin, Vector2 offsetMax)
        {
            rect.anchorMin = anchorMin;
            rect.anchorMax = anchorMax;
            rect.offsetMin = offsetMin;
            rect.offsetMax = offsetMax;
        }

        private static string ModeLabel(SimulationMode mode)
        {
            return mode switch
            {
                SimulationMode.View => "视角 [1]",
                SimulationMode.Drag => "拖动 [2]",
                SimulationMode.Wiring => "接线 [3]",
                SimulationMode.Simulate => "仿真 [4]",
                SimulationMode.Fault => "排故 [5]",
                _ => mode.ToString()
            };
        }

        private static string InstrumentLabel(InstrumentKind kind)
        {
            return kind switch
            {
                InstrumentKind.Multimeter => "万用表",
                InstrumentKind.VoltageProbe => "验电笔",
                InstrumentKind.Oscilloscope => "示波器",
                InstrumentKind.Tachometer => "转速表",
                _ => kind.ToString()
            };
        }

        private sealed class HudReferences
        {
            public Canvas Canvas;
            public RectTransform Top;
            public RectTransform Right;
            public Text Mode;
            public Text Task;
            public Text Description;
            public Image Schematic;
            public Text Status;
            public Text Instrument;
            public PortHoverPresenter PortHover;
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
