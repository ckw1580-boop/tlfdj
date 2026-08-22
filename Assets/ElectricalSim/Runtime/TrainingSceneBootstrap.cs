using System;
using System.Collections.Generic;
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

        private readonly List<ElectricalDeviceView> deviceViews = new List<ElectricalDeviceView>();
        private Font uiFont;
        private SimulationController controller;

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
            Application.targetFrameRate = 60;
            uiFont = Font.CreateDynamicFontFromOSFont(new[] { "Microsoft YaHei UI", "Microsoft YaHei", "SimHei", "Arial" }, 18);
            CreateEnvironment();
            var cameraController = CreateCamera();
            var wireRoot = new GameObject("ElectricalWires").transform;
            CreateDevices();
            var ui = CreateHud();

            controller = gameObject.AddComponent<SimulationController>();
            controller.Initialize(deviceViews, cameraController, wireRoot, ui.Mode, ui.Task, ui.Description, ui.Schematic, ui.Status, ui.Instrument, wireMaterial, originalVisuals);
            BindUi(ui);
        }

        private void CreateEnvironment()
        {
            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(0.55f, 0.58f, 0.62f);
            RenderSettings.ambientIntensity = 1.25f;
            RenderSettings.fog = false;
            CreateMainLight();
            CreatePlaceholderEnvironment();
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
            }
            else
            {
                CreateCube("Cabinet", new Vector3(0f, 1.65f, 0.2f), new Vector3(2.5f, 3.3f, 0.42f), new Color(0.055f, 0.065f, 0.07f));
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
            camera.fieldOfView = 52f;
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
            CreateDevice(ElectricalDeviceRuntime.CreateContactor("KMF"), "正转接触器", new Vector3(-0.95f, 2.22f, -0.16f), new Vector3(0.48f, 0.38f, 0.18f), new Color(0.16f, 0.2f, 0.24f));
            CreateDevice(ElectricalDeviceRuntime.CreateContactor("KM1"), "接触器 KM1", new Vector3(-0.35f, 2.22f, -0.16f), new Vector3(0.48f, 0.38f, 0.18f), new Color(0.16f, 0.2f, 0.24f));
            CreateDevice(ElectricalDeviceRuntime.CreateContactor("KMR"), "反转接触器", new Vector3(0.25f, 2.22f, -0.16f), new Vector3(0.48f, 0.38f, 0.18f), new Color(0.16f, 0.2f, 0.24f));
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
            CreateMotor("M2", "三相电机 M2", new Vector3(0.45f, 0.25f, -0.45f));
        }

        private void CreateButton(string id, string label, bool normallyClosed, Vector3 position, Color color)
        {
            CreateDevice(ElectricalDeviceRuntime.CreatePushButton(id, normallyClosed), label, position, new Vector3(0.28f, 0.25f, 0.16f), color);
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

            var view = root.AddComponent<ElectricalDeviceView>();
            view.Initialize(runtime, label);
            CreatePorts(view, root.transform, runtime.Ports, size);
            if (original == null)
                CreateWorldLabel(label, position + new Vector3(0f, size.y * 0.7f, -0.15f), 0.034f, Color.white);
            deviceViews.Add(view);
        }

        private void CreatePorts(ElectricalDeviceView view, Transform parent, IReadOnlyCollection<string> ports, Vector3 bounds)
        {
            var list = ports.ToList();
            var columns = Mathf.Min(6, Mathf.Max(2, Mathf.CeilToInt(list.Count / 2f)));
            for (var index = 0; index < list.Count; index++)
            {
                var row = index / columns;
                var column = index % columns;
                var x = columns == 1 ? 0f : Mathf.Lerp(-bounds.x * 0.42f, bounds.x * 0.42f, column / (float)(columns - 1));
                var y = row == 0 ? bounds.y * 0.48f : -bounds.y * 0.48f;
                var fallback = new Vector3(x, y, -bounds.z * 0.68f - 0.025f);
                var terminal = FindTerminal(parent, view.Runtime.Kind, list[index]);
                var localPosition = terminal != null ? parent.InverseTransformPoint(terminal.position) : fallback;
                var worldMarkerSize = terminal != null ? 0.026f : 0.032f;
                var parentScale = Mathf.Max(Mathf.Abs(parent.lossyScale.x), Mathf.Abs(parent.lossyScale.y), Mathf.Abs(parent.lossyScale.z));
                var markerSize = worldMarkerSize / Mathf.Max(0.0001f, parentScale);
                var portObject = CreatePrimitive(PrimitiveType.Sphere, "Port", parent, localPosition, Vector3.one * markerSize, new Color(0.08f, 1f, 0.32f));
                var port = portObject.AddComponent<ElectricalPortView>();
                port.Initialize(view.Runtime.DeviceId, list[index], new Color(0.12f, 0.86f, 0.36f));
                view.AddPort(port);
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
                    .FirstOrDefault(item => string.Equals(item.name, alias, StringComparison.OrdinalIgnoreCase));
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
                    { "L1", new[] { "1L1" } }, { "L2", new[] { "3L2" } }, { "L3", new[] { "5L3" } },
                    { "T1", new[] { "2T1" } }, { "T2", new[] { "4T2" } }, { "T3", new[] { "6T3" } },
                    { "13", new[] { "13NO" } }, { "14", new[] { "14NO" } },
                    { "21", new[] { "21NC" } }, { "22", new[] { "22NC" } }
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
                var breaker = new Dictionary<string, string>
                {
                    { "L1", "1" }, { "L2", "3" }, { "L3", "5" },
                    { "T1", "2" }, { "T2", "4" }, { "T3", "6" }
                };
                if (breaker.TryGetValue(port, out var alias)) return new[] { alias };
            }
            if (kind == ElectricalDeviceKind.PushButton)
                return port == "COM" ? new[] { "COM1", "COM2" } : new[] { port + "1", port + "2", port };
            if (kind == ElectricalDeviceKind.Motor)
            {
                var motor = new Dictionary<string, string[]>
                {
                    { "U", new[] { "U1" } }, { "V", new[] { "V1" } }, { "W", new[] { "W1" } }
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

            var right = Panel("RightPanel", canvas.transform, new Vector2(1f, 0f), new Vector2(1f, 1f), new Vector2(-390f, 24f), new Vector2(-18f, -110f), panelBlue);
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

            var statusPanel = Panel("StatusPanel", canvas.transform, new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(18f, 20f), new Vector2(-426f, 92f), new Color(0.02f, 0.1f, 0.14f, 0.92f));
            var status = Label("Status", statusPanel.transform, "系统就绪", 19, TextAnchor.MiddleLeft, new Color(1f, 0.88f, 0.2f));
            SetRect(status.rectTransform, Vector2.zero, Vector2.one, new Vector2(20f, 5f), new Vector2(-20f, -5f));

            var references = new HudReferences { Canvas = canvas, Top = top, Right = right, Mode = mode, Task = task, Description = description, Schematic = schematic, Status = status, Instrument = instrument };
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
        }
    }
}
