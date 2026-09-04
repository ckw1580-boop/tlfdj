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
    public sealed class SimulationController : MonoBehaviour
    {
        private readonly CircuitGraph graph = new CircuitGraph();
        private readonly Dictionary<string, ElectricalDeviceRuntime> devices = new Dictionary<string, ElectricalDeviceRuntime>();
        private readonly Dictionary<string, ElectricalPortView> portViews = new Dictionary<string, ElectricalPortView>();
        private readonly List<ElectricalWireView> wireViews = new List<ElectricalWireView>();
        private readonly List<CabinetBreakerInteractable> cabinetBreakers = new List<CabinetBreakerInteractable>();
        private IReadOnlyList<CircuitTaskSpec> tasks;
        private int taskIndex;
        private ElectricalPortView selectedPort;
        private readonly List<Vector3> pendingWirePoints = new List<Vector3>();
        private ElectricalWireDraftView wireDraftView;
        private Vector3 wireDraftCursor;
        private readonly List<ElectricalPortView> meterPorts = new List<ElectricalPortView>();
        private ElectricalDeviceView draggedDevice;
        private Plane dragPlane;
        private Vector3 dragOffset;
        private Transform inverterModel;
        private Action<bool> setInverterPanelVisible;
        private InverterPanelController inverterPanel;
        private Cc3dDocument loadedDocument;
        private SimulationSnapshot lastSnapshot;
        private InstrumentKind instrumentKind = InstrumentKind.Multimeter;
        private readonly Stack<List<WireConnection>> undoWires = new Stack<List<WireConnection>>();
        private readonly Stack<List<WireConnection>> redoWires = new Stack<List<WireConnection>>();
        private Color currentWireColor = Color.red;
        private float currentWireArea = 0.01f;
        private string currentLineType = "ElectricalWire";

        private Text modeText;
        private Text taskText;
        private Text taskDescriptionText;
        private Image taskSchematicImage;
        private Text statusText;
        private Text instrumentText;
        private TrainingCameraController trainingCamera;
        private Transform wireRoot;
        private Material wireMaterial;
        private OriginalVisualRegistry originalVisuals;
        private PortHoverPresenter portHover;

        public SimulationMode Mode { get; private set; } = SimulationMode.View;
        public CircuitGraph Graph => graph;
        public CircuitTaskSpec CurrentTask => tasks[taskIndex];
        public IReadOnlyList<CabinetBreakerInteractable> CabinetBreakers => cabinetBreakers;
        public bool AreCabinetBreakersClosed => cabinetBreakers.Count == 0 || cabinetBreakers.All(item => item.IsClosed);
        public bool IsRoutingWire => selectedPort != null && Mode == SimulationMode.Wiring;
        public Color CurrentWireColor => currentWireColor;
        public float CurrentWireArea => currentWireArea;
        public string CurrentLineType => currentLineType;
        public Transform InverterModel => inverterModel;
        public InverterPanelController InverterPanel => inverterPanel;
        public event Action<SimulationMode> ModeChanged;

        public void Initialize(
            IEnumerable<ElectricalDeviceView> deviceViews,
            TrainingCameraController cameraController,
            Transform wireContainer,
            Text modeLabel,
            Text taskLabel,
            Text taskDescription,
            Image taskSchematic,
            Text statusLabel,
            Text instrumentLabel,
            Material lineMaterial,
            OriginalVisualRegistry visualRegistry,
            PortHoverPresenter hoverPresenter)
        {
            trainingCamera = cameraController;
            trainingCamera.PresetChanged += OnViewPresetChanged;
            trainingCamera.ViewSideChanged += OnViewSideChanged;
            wireRoot = wireContainer;
            modeText = modeLabel;
            taskText = taskLabel;
            taskDescriptionText = taskDescription;
            taskSchematicImage = taskSchematic;
            statusText = statusLabel;
            instrumentText = instrumentLabel;
            wireMaterial = lineMaterial;
            originalVisuals = visualRegistry;
            portHover = hoverPresenter;
            tasks = CircuitTaskCatalog.CreateAll();

            foreach (var view in deviceViews)
            {
                devices[view.Runtime.DeviceId] = view.Runtime;
                graph.RegisterDevice(view.Runtime);
                foreach (var port in view.Ports) portViews[port.QualifiedPort] = port;
            }

            UpdateTaskUi();
            SetMode(SimulationMode.View);
            ApplyPortAnchors();
            SetStatus("系统就绪。请选择任务后进行接线，或点击“标准接线”加载参考拓扑。", false);
        }

        public void RegisterCabinetBreakers(IEnumerable<CabinetBreakerInteractable> breakers)
        {
            foreach (var breaker in cabinetBreakers)
                if (breaker != null)
                {
                    breaker.StateChanged -= OnCabinetBreakerStateChanged;
                    breaker.SetHighlighted(false);
                }

            cabinetBreakers.Clear();
            if (breakers != null)
                cabinetBreakers.AddRange(breakers.Where(item => item != null).Distinct());

            foreach (var breaker in cabinetBreakers)
            {
                breaker.StateChanged += OnCabinetBreakerStateChanged;
                breaker.SetHighlighted(Mode == SimulationMode.Drag);
            }
            ApplyCabinetBreakerState();
        }

        public bool TryToggleCabinetBreaker(CabinetBreakerInteractable breaker)
        {
            if (Mode != SimulationMode.Drag || breaker == null || !cabinetBreakers.Contains(breaker)) return false;
            breaker.Toggle();
            return true;
        }

        public void RegisterInverterPanel(
            Transform model,
            InverterPanelController panel,
            Action<bool> setVisible)
        {
            inverterModel = model;
            inverterPanel = panel;
            setInverterPanelVisible = setVisible;
            setInverterPanelVisible?.Invoke(false);
        }

        public bool TryOpenInverterPanel(Transform clickedTransform)
        {
            if (Mode != SimulationMode.Drag || inverterModel == null || clickedTransform == null ||
                setInverterPanelVisible == null)
                return false;
            if (clickedTransform != inverterModel && !clickedTransform.IsChildOf(inverterModel)) return false;

            setInverterPanelVisible(true);
            return true;
        }

        private void Update()
        {
            HandleHotkeys();
            UpdatePortHover();
            UpdateWiringDraft();
            HandleSceneInput();
            lastSnapshot = graph.Solve(Time.deltaTime);
            foreach (var view in wireViews) view.Refresh();
            UpdateInstrumentReadout();
            if (lastSnapshot.HasShortCircuit)
                SetStatus(lastSnapshot.Errors[0], true);
        }

        public void SetMode(SimulationMode mode)
        {
            Mode = mode;
            if (mode != SimulationMode.Drag) setInverterPanelVisible?.Invoke(false);
            foreach (var breaker in cabinetBreakers)
                if (breaker != null) breaker.SetHighlighted(mode == SimulationMode.Drag);
            ClearSelection();
            if (portHover != null) portHover.Hide();
            foreach (var port in portViews.Values) port.SetVisibleForMode(mode);
            modeText.text = $"当前模式：{ModeName(mode)}";
            // Entering wiring mode must preserve the user's current camera pose.
            // Connection points already follow the actual camera side, while the
            // explicit view menu remains available for choosing a preset.
            if (mode == SimulationMode.Fault) trainingCamera.SetFaultView();
            SetStatus($"已进入{ModeName(mode)}模式。", false);
            ModeChanged?.Invoke(mode);
        }

        public void PreviousTask()
        {
            taskIndex = (taskIndex - 1 + tasks.Count) % tasks.Count;
            UpdateTaskUi();
        }

        public void NextTask()
        {
            taskIndex = (taskIndex + 1) % tasks.Count;
            UpdateTaskUi();
        }

        public void LoadReferenceWiring()
        {
            PushWireHistory();
            graph.ClearWires();
            foreach (var pair in CurrentTask.RequiredConnections)
                graph.AddWire(pair.A, pair.B, ColorForPort(pair.A), "JumperLine");
            RefreshWireViews();
            SetStatus($"已加载“{CurrentTask.Name}”标准接线，可进入仿真并提交验收。", false);
        }

        public void ResetTraining()
        {
            PushWireHistory();
            graph.ClearWires();
            foreach (var breaker in cabinetBreakers) breaker.ResetClosed();
            foreach (var device in devices.Values)
            {
                if (device.Kind == ElectricalDeviceKind.Breaker)
                    device.SetControl(!IsMainBreaker(device) || AreCabinetBreakersClosed);
                else if (device.Kind == ElectricalDeviceKind.Fuse) device.SetControl(true);
                else device.SetControl(false);
            }
            RefreshWireViews();
            trainingCamera.ResetView();
            SetMode(SimulationMode.View);
            SetStatus("训练场景已重置。", false);
        }

        public void SubmitTask()
        {
            StopAllCoroutines();
            StartCoroutine(EvaluateCurrentTask());
        }

        public void OpenCc3d()
        {
            var path = WindowsFileDialog.OpenCc3d(ProjectDirectory());
            if (string.IsNullOrEmpty(path)) return;
            try
            {
                loadedDocument = Cc3dSerializer.Load(path);
                Cc3dCircuitAdapter.ImportWires(loadedDocument, graph);
                RefreshWireViews();
                SetStatus($"已打开：{Path.GetFileName(path)}（{loadedDocument.Elements.Count} 个元件，{graph.Wires.Count} 条线路）", false);
            }
            catch (Exception exception)
            {
                SetStatus("打开失败：" + exception.Message, true);
            }
        }

        public void SaveCc3d()
        {
            var path = WindowsFileDialog.SaveCc3d(ProjectDirectory());
            if (string.IsNullOrEmpty(path)) return;
            try
            {
                var states = FindObjectsOfType<ElectricalDeviceView>()
                    .Select(view => new DeviceSceneState(
                        view.Runtime.DeviceId,
                        view.Runtime.Kind.ToString(),
                        view.gameObject.name,
                        view.transform.position,
                        view.transform.rotation));
                var document = Cc3dCircuitAdapter.Export(graph, states, loadedDocument);
                Cc3dSerializer.Save(path, document);
                loadedDocument = document;
                SetStatus($"已导出：{Path.GetFileName(path)}", false);
            }
            catch (Exception exception)
            {
                SetStatus("导出失败：" + exception.Message, true);
            }
        }

        public void SelectInstrument(InstrumentKind kind)
        {
            instrumentKind = kind;
            meterPorts.Clear();
            instrumentText.text = $"{InstrumentName(kind)}：请选择两个端子";
            SetMode(SimulationMode.Fault);
        }

        public void SetWireStyle(Color color, float area, string lineType)
        {
            ClearSelection();
            currentWireColor = color;
            currentWireArea = Mathf.Clamp(area, 0.001f, 0.2f);
            currentLineType = string.IsNullOrWhiteSpace(lineType) ? "JumperLine" : lineType;
            if (portHover != null) portHover.Hide();
            ApplyPortAnchors();
            SetStatus($"接线参数：{currentLineType}，截面积 {currentWireArea:0.###}，颜色 #{ColorUtility.ToHtmlStringRGB(currentWireColor)}", false);
        }

        private void OnDestroy()
        {
            if (trainingCamera != null)
            {
                trainingCamera.PresetChanged -= OnViewPresetChanged;
                trainingCamera.ViewSideChanged -= OnViewSideChanged;
            }
            foreach (var breaker in cabinetBreakers)
                if (breaker != null)
                {
                    breaker.StateChanged -= OnCabinetBreakerStateChanged;
                    breaker.SetHighlighted(false);
                }
            DestroyWireDraft();
            if (portHover != null) portHover.Hide();
        }

        private void OnViewPresetChanged(TrainingViewPreset preset)
        {
            if (portHover != null) portHover.Hide();
            ApplyPortAnchors();
        }

        private void OnViewSideChanged(bool viewingFaultSide)
        {
            if (portHover != null) portHover.Hide();
            ApplyPortAnchors();
        }

        private void ApplyPortAnchors()
        {
            if (trainingCamera == null) return;
            var jumper = currentLineType.IndexOf("jumper", StringComparison.OrdinalIgnoreCase) >= 0 ||
                         currentLineType.IndexOf("rope", StringComparison.OrdinalIgnoreCase) >= 0 ||
                         currentLineType.IndexOf("跳", StringComparison.Ordinal) >= 0;
            var effectivePreset = trainingCamera.IsViewingFaultSide
                ? TrainingViewPreset.FaultBack
                : TrainingViewPreset.WiringFront;
            foreach (var port in portViews.Values)
                port.ApplyOriginalAnchor(effectivePreset, jumper);
            foreach (var wire in wireViews) wire.Refresh();
        }

        private void UpdatePortHover()
        {
            if (portHover == null) return;
            if (Mode != SimulationMode.Wiring && Mode != SimulationMode.Fault)
            {
                portHover.Hide();
                return;
            }
            if (EventSystem.current != null && EventSystem.current.IsPointerOverGameObject())
            {
                portHover.Hide();
                return;
            }

            var camera = Camera.main;
            if (camera == null)
            {
                portHover.Hide();
                return;
            }
            var ray = camera.ScreenPointToRay(Input.mousePosition);
            if (!Physics.Raycast(ray, out var hit, 100f))
            {
                portHover.Hide();
                return;
            }
            var port = hit.collider.GetComponent<ElectricalPortView>();
            if (port == null)
            {
                portHover.Hide();
                return;
            }
            portHover.Present(port, camera, Input.mousePosition);
        }

        public bool AddBendPointToLastWire(Vector3 worldPosition)
        {
            if (graph.Wires.Count == 0) return false;
            PushWireHistory();
            graph.Wires[graph.Wires.Count - 1].Points.Add(worldPosition);
            RefreshWireViews();
            return true;
        }

        public void UndoWiring()
        {
            if (undoWires.Count == 0) return;
            redoWires.Push(SnapshotWires());
            graph.ReplaceWires(undoWires.Pop());
            RefreshWireViews();
            SetStatus("已撤销接线操作。", false);
        }

        public void RedoWiring()
        {
            if (redoWires.Count == 0) return;
            undoWires.Push(SnapshotWires());
            graph.ReplaceWires(redoWires.Pop());
            RefreshWireViews();
            SetStatus("已恢复接线操作。", false);
        }

        public void ShowStatus(string message, bool error = false) => SetStatus(message, error);

        private IEnumerator EvaluateCurrentTask()
        {
            var result = CircuitTaskEvaluator.EvaluateTopology(graph, CurrentTask);
            if (!result.Passed)
            {
                SetStatus(result.Summary(), true);
                yield break;
            }

            SetStatus("拓扑检查通过，正在执行动作序列……", false);
            foreach (var device in devices.Values)
            {
                if (device.Kind == ElectricalDeviceKind.Breaker)
                    device.SetControl(!IsMainBreaker(device) || AreCabinetBreakersClosed);
                else if (device.Kind == ElectricalDeviceKind.Fuse) device.SetControl(true);
                else device.SetControl(false);
            }

            foreach (var step in CurrentTask.Actions)
            {
                if (!devices.TryGetValue(step.DeviceId, out var device))
                {
                    result.ActionErrors.Add($"找不到动作器件 {step.DeviceId}");
                    continue;
                }

                device.SetControl(step.Active);
                var end = Time.realtimeSinceStartup + Mathf.Max(0.05f, step.HoldSeconds);
                while (Time.realtimeSinceStartup < end)
                {
                    lastSnapshot = graph.Solve(Time.unscaledDeltaTime);
                    yield return null;
                }

                var actual = lastSnapshot.GetMotorDirection(step.ExpectedDeviceId);
                if (actual != step.ExpectedMotorDirection)
                    result.ActionErrors.Add($"{step.ExpectedDeviceId} 期望 {step.ExpectedMotorDirection}，实际 {actual}");
            }

            SetStatus(result.Summary(), !result.Passed);
        }

        private void HandleHotkeys()
        {
            if (Input.GetKeyDown(KeyCode.Alpha1)) SetMode(SimulationMode.View);
            if (Input.GetKeyDown(KeyCode.Alpha2)) SetMode(SimulationMode.Drag);
            if (Input.GetKeyDown(KeyCode.Alpha3)) SetMode(SimulationMode.Wiring);
            if (Input.GetKeyDown(KeyCode.Alpha4)) SetMode(SimulationMode.Simulate);
            if (Input.GetKeyDown(KeyCode.Alpha5)) SetMode(SimulationMode.Fault);
            if (Input.GetKeyDown(KeyCode.Escape)) SetMode(SimulationMode.View);
            if (Input.GetKey(KeyCode.LeftControl) && Input.GetKeyDown(KeyCode.Z)) UndoWiring();
            if (Input.GetKey(KeyCode.LeftControl) && Input.GetKeyDown(KeyCode.Y)) RedoWiring();
            if (Input.GetKeyDown(KeyCode.Delete) && graph.Wires.Count > 0)
            {
                PushWireHistory();
                graph.RemoveWire(graph.Wires[graph.Wires.Count - 1].Id);
                RefreshWireViews();
                SetStatus("已删除最后一条线路。", false);
            }
        }

        private void HandleSceneInput()
        {
            if (EventSystem.current != null && EventSystem.current.IsPointerOverGameObject()) return;
            if (Mode == SimulationMode.Drag)
            {
                HandleDrag();
                return;
            }
            if (!Input.GetMouseButtonDown(0)) return;

            var camera = Camera.main;
            if (camera == null) return;
            var ray = camera.ScreenPointToRay(Input.mousePosition);
            var hasHit = Physics.Raycast(ray, out var hit, 100f);
            var port = hasHit ? hit.collider.GetComponent<ElectricalPortView>() : null;

            if (Mode == SimulationMode.Wiring)
            {
                HandleWiringClick(port, ray);
                return;
            }

            if (!hasHit) return;
            var deviceView = hit.collider.GetComponentInParent<ElectricalDeviceView>();

            if (Mode == SimulationMode.Fault && port != null) HandleMeterPort(port);
            else if (Mode == SimulationMode.Simulate && deviceView != null) HandleDeviceControl(deviceView.Runtime);
        }

        private void HandleDrag()
        {
            var ray = Camera.main.ScreenPointToRay(Input.mousePosition);
            if (Input.GetMouseButtonDown(0) && Physics.Raycast(ray, out var hit, 100f))
            {
                var cabinetBreaker = hit.collider.GetComponentInParent<CabinetBreakerInteractable>();
                if (TryToggleCabinetBreaker(cabinetBreaker))
                {
                    draggedDevice = null;
                    return;
                }

                if (TryOpenInverterPanel(hit.transform))
                {
                    draggedDevice = null;
                    return;
                }

                draggedDevice = hit.collider.GetComponentInParent<ElectricalDeviceView>();
                if (draggedDevice != null)
                {
                    dragPlane = new Plane(-Camera.main.transform.forward, draggedDevice.transform.position);
                    if (dragPlane.Raycast(ray, out var enter)) dragOffset = draggedDevice.transform.position - ray.GetPoint(enter);
                }
            }
            if (draggedDevice != null && Input.GetMouseButton(0) && dragPlane.Raycast(ray, out var distance))
                draggedDevice.transform.position = ray.GetPoint(distance) + dragOffset;
            if (Input.GetMouseButtonUp(0)) draggedDevice = null;
        }

        private void HandleWiringClick(ElectricalPortView port, Ray ray)
        {
            if (selectedPort == null)
            {
                if (port == null) return;
                BeginWireRoute(port);
                return;
            }

            if (port != null)
            {
                if (selectedPort == port)
                {
                    ClearSelection();
                    SetStatus("已取消当前接线。", false);
                    return;
                }

                CompleteWireRoute(port);
                return;
            }

            if (!TryProjectWirePoint(ray, out var point)) return;
            var previous = pendingWirePoints.Count > 0
                ? pendingWirePoints[pendingWirePoints.Count - 1]
                : selectedPort.CurrentAnchorPosition;
            if (Vector3.Distance(previous, point) < ElectricalWireView.WidthForArea(currentWireArea) * 1.5f) return;
            pendingWirePoints.Add(point);
            wireDraftCursor = point;
            wireDraftView?.Refresh(pendingWirePoints, wireDraftCursor);
            SetStatus($"已添加第 {pendingWirePoints.Count} 个路径点；继续点选路径，或点击终点端子完成。", false);
        }

        private void BeginWireRoute(ElectricalPortView port)
        {
            pendingWirePoints.Clear();
            selectedPort = port;
            selectedPort.SetHighlighted(true);
            wireDraftCursor = port.CurrentAnchorPosition;

            DestroyWireDraft();
            var draftObject = new GameObject("Wire_Draft");
            draftObject.transform.SetParent(wireRoot, false);
            wireDraftView = draftObject.AddComponent<ElectricalWireDraftView>();
            var startPortName = port.QualifiedPort;
            wireDraftView.Initialize(
                () => ResolvePortPosition(startPortName),
                wireMaterial,
                currentWireColor,
                currentWireArea);
            wireDraftView.Refresh(pendingWirePoints, wireDraftCursor);
            SetStatus($"起点：{port.QualifiedPort}。左键空白处添加路径点，点击另一个端子完成。", false);
        }

        private void CompleteWireRoute(ElectricalPortView port)
        {
            var startPort = selectedPort;
            var startPortName = startPort.QualifiedPort;
            var beforeCount = graph.Wires.Count;
            PushWireHistory();
            var wire = graph.AddWire(
                startPortName,
                port.QualifiedPort,
                currentWireColor,
                currentLineType,
                currentWireArea);
            if (graph.Wires.Count > beforeCount) wire.Points.AddRange(pendingWirePoints);
            startPort.SetHighlighted(false);
            selectedPort = null;
            pendingWirePoints.Clear();
            DestroyWireDraft();
            RefreshWireViews();
            SetStatus($"线路已连接到 {port.QualifiedPort}。", false);
        }

        private void UpdateWiringDraft()
        {
            if (!IsRoutingWire || wireDraftView == null || Camera.main == null) return;
            if (EventSystem.current != null && EventSystem.current.IsPointerOverGameObject()) return;
            var ray = Camera.main.ScreenPointToRay(Input.mousePosition);
            if (!TryProjectWirePoint(ray, out wireDraftCursor)) return;
            wireDraftView.Refresh(pendingWirePoints, wireDraftCursor);
        }

        private bool TryProjectWirePoint(Ray ray, out Vector3 point)
        {
            point = Vector3.zero;
            if (selectedPort == null || Camera.main == null) return false;
            var plane = new Plane(-Camera.main.transform.forward, selectedPort.CurrentAnchorPosition);
            if (!plane.Raycast(ray, out var distance) || distance < 0f) return false;
            point = ray.GetPoint(distance);
            return true;
        }

        private void DestroyWireDraft()
        {
            if (wireDraftView != null) Destroy(wireDraftView.gameObject);
            wireDraftView = null;
        }

        private void HandleMeterPort(ElectricalPortView port)
        {
            if (meterPorts.Contains(port)) return;
            meterPorts.Add(port);
            port.SetHighlighted(true);
            if (meterPorts.Count > 2)
            {
                meterPorts[0].SetHighlighted(false);
                meterPorts.RemoveAt(0);
            }
            UpdateInstrumentReadout();
        }

        private void HandleDeviceControl(ElectricalDeviceRuntime device)
        {
            if (device.Kind == ElectricalDeviceKind.PushButton)
            {
                StartCoroutine(Pulse(device));
                return;
            }
            if (device.Kind == ElectricalDeviceKind.Breaker || device.Kind == ElectricalDeviceKind.Fuse)
            {
                if (IsMainBreaker(device) && cabinetBreakers.Count > 0)
                {
                    ApplyCabinetBreakerState();
                    return;
                }
                device.SetControl(!device.IsClosed);
            }
            else if (device.Kind == ElectricalDeviceKind.ThermalRelay)
                device.SetControl(!device.IsTripped);
        }

        private void OnCabinetBreakerStateChanged(CabinetBreakerInteractable breaker, bool closed)
        {
            ApplyCabinetBreakerState();
            var mainState = AreCabinetBreakersClosed ? "主回路已接通" : "主回路已断开";
            SetStatus($"{breaker.DisplayName}已{(closed ? "合闸" : "分闸")}，{mainState}。", false);
        }

        private void ApplyCabinetBreakerState()
        {
            if (devices.TryGetValue("QF", out var mainBreaker))
                mainBreaker.SetControl(AreCabinetBreakersClosed);
        }

        private static bool IsMainBreaker(ElectricalDeviceRuntime device)
            => device != null && device.Kind == ElectricalDeviceKind.Breaker &&
               string.Equals(device.DeviceId, "QF", StringComparison.OrdinalIgnoreCase);

        private IEnumerator Pulse(ElectricalDeviceRuntime device)
        {
            device.SetControl(true);
            yield return new WaitForSecondsRealtime(0.22f);
            device.SetControl(false);
        }

        private void UpdateInstrumentReadout()
        {
            if (instrumentText == null) return;
            if (instrumentKind == InstrumentKind.Tachometer)
            {
                var meter = new ElectricalInstrument(instrumentKind);
                instrumentText.text = $"转速表：{meter.SampleMotorSpeed("M1", lastSnapshot):0} r/min";
                return;
            }
            if (meterPorts.Count < 2 || lastSnapshot == null)
            {
                instrumentText.text = $"{InstrumentName(instrumentKind)}：请选择两个端子";
                return;
            }
            var instrument = new ElectricalInstrument(instrumentKind);
            var a = meterPorts[0].QualifiedPort;
            var b = meterPorts[1].QualifiedPort;
            var voltage = instrument.Sample(MeasurementKind.AcVoltage, a, b, lastSnapshot);
            var continuity = instrument.Sample(MeasurementKind.Continuity, a, b, lastSnapshot) > 0.5 ? "导通" : "断开";
            instrumentText.text = instrumentKind == InstrumentKind.Oscilloscope
                ? $"示波器：{voltage:0} V / 50 Hz"
                : $"{InstrumentName(instrumentKind)}：{a} ↔ {b}\n交流 {voltage:0} V · {continuity}";
        }

        private void RefreshWireViews()
        {
            foreach (var view in wireViews) if (view != null) Destroy(view.gameObject);
            wireViews.Clear();
            foreach (var wire in graph.Wires)
            {
                var gameObject = new GameObject("Wire_" + wire.Id);
                gameObject.transform.SetParent(wireRoot, false);
                var view = gameObject.AddComponent<ElectricalWireView>();
                view.Initialize(wire, ResolvePortPosition, wireMaterial);
                wireViews.Add(view);
            }
        }

        private List<WireConnection> SnapshotWires() => graph.Wires.Select(CircuitGraph.CloneWire).ToList();

        private void PushWireHistory()
        {
            undoWires.Push(SnapshotWires());
            redoWires.Clear();
            while (undoWires.Count > 64)
            {
                var keep = undoWires.Reverse().Take(64).Reverse().ToArray();
                undoWires.Clear();
                foreach (var item in keep) undoWires.Push(item);
            }
        }

        private Vector3 ResolvePortPosition(string qualifiedPort)
        {
            return portViews.TryGetValue(qualifiedPort, out var view) ? view.transform.position : Vector3.zero;
        }

        private void ClearSelection()
        {
            if (selectedPort != null) selectedPort.SetHighlighted(false);
            selectedPort = null;
            pendingWirePoints.Clear();
            DestroyWireDraft();
            foreach (var port in meterPorts) port.SetHighlighted(false);
            meterPorts.Clear();
            draggedDevice = null;
        }

        private void UpdateTaskUi()
        {
            taskText.text = $"{taskIndex + 1:00}/{tasks.Count:00}  {CurrentTask.Name}";
            taskDescriptionText.text = CurrentTask.Description;
            if (taskSchematicImage != null)
            {
                var sprite = originalVisuals != null ? originalVisuals.ResolveSchematic(CurrentTask.Id) : null;
                taskSchematicImage.sprite = sprite;
                taskSchematicImage.enabled = sprite != null;
                if (sprite != null && sprite.rect.height > 0f)
                {
                    var fitter = taskSchematicImage.GetComponent<AspectRatioFitter>();
                    if (fitter != null) fitter.aspectRatio = sprite.rect.width / sprite.rect.height;
                }
            }
            SetStatus("已选择任务：" + CurrentTask.Name, false);
        }

        private void SetStatus(string message, bool error)
        {
            if (statusText == null) return;
            statusText.text = message;
            statusText.color = error ? new Color(1f, 0.38f, 0.24f) : new Color(1f, 0.88f, 0.2f);
        }

        private static string ProjectDirectory()
        {
            var path = Path.Combine(Application.persistentDataPath, "Projects");
            Directory.CreateDirectory(path);
            return path;
        }

        private static Color ColorForPort(string port)
        {
            if (port.EndsWith("L1") || port.EndsWith("T1") || port.EndsWith("U")) return new Color(0.95f, 0.18f, 0.14f);
            if (port.EndsWith("L2") || port.EndsWith("T2") || port.EndsWith("V")) return new Color(0.95f, 0.85f, 0.12f);
            if (port.EndsWith("L3") || port.EndsWith("T3") || port.EndsWith("W")) return new Color(0.18f, 0.75f, 0.28f);
            if (port.EndsWith("N")) return new Color(0.18f, 0.45f, 0.95f);
            return Color.red;
        }

        private static string ModeName(SimulationMode mode)
        {
            return mode switch
            {
                SimulationMode.View => "视角",
                SimulationMode.Drag => "拖动",
                SimulationMode.Wiring => "接线",
                SimulationMode.Simulate => "仿真",
                SimulationMode.Fault => "排故",
                _ => mode.ToString()
            };
        }

        private static string InstrumentName(InstrumentKind kind)
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
    }
}
