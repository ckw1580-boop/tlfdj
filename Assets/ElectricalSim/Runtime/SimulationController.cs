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
    public sealed partial class SimulationController : MonoBehaviour
    {
        private readonly CircuitGraph graph = new CircuitGraph();
        private readonly Dictionary<string, ElectricalDeviceRuntime> devices = new Dictionary<string, ElectricalDeviceRuntime>();
        private readonly Dictionary<string, ElectricalPortView> portViews = new Dictionary<string, ElectricalPortView>();
        private readonly Dictionary<string, string> deviceNames = new Dictionary<string, string>();
        private readonly List<ElectricalWireView> wireViews = new List<ElectricalWireView>();
        private readonly List<CabinetBreakerInteractable> cabinetBreakers = new List<CabinetBreakerInteractable>();
        private IReadOnlyList<CircuitTaskSpec> tasks;
        private int taskIndex;
        private ElectricalPortView selectedPort;
        private readonly List<Vector3> pendingWirePoints = new List<Vector3>();
        private ElectricalWireDraftView wireDraftView;
        private Vector3 wireDraftCursor;
        private ElectricalWireView selectedWire;
        private int selectedWirePointIndex = -1;
        private bool draggingWirePoint;
        private bool wirePointDragChanged;
        private string lastWireClickId = string.Empty;
        private float lastWireClickTime = float.NegativeInfinity;
        private Vector2 lastWireClickPosition;
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
        public TachometerController Tachometer { get; private set; }
        public void RegisterTachometer(TachometerController tachometer)
        {
            Tachometer = tachometer;
            if (Tachometer != null) Tachometer.SetFaultMode(Mode == SimulationMode.Fault);
        }
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
        private WireSurfacePlane wireSurface;
        private WireSurfacePlane frontWireSurface;
        private WireSurfacePlane faultWireSurface;
        private OriginalVisualRegistry originalVisuals;
        private PortHoverPresenter portHover;

        private const float WireHitDistancePixels = 10f;
        private const float WireNodeHitDistancePixels = 14f;
        private const float DoubleClickSeconds = 0.3f;
        private const float DoubleClickDistancePixels = 12f;

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
        public event Action<WireConnection> SelectedWireChanged;
        public event Action<string, bool> StatusChanged;
        public WireConnection SelectedWire => selectedWire != null ? selectedWire.Connection : null;

        public string ResolveWireTerminalName(string qualifiedPort)
        {
            if (string.IsNullOrEmpty(qualifiedPort)) return "未知端子";
            if (!portViews.TryGetValue(qualifiedPort, out var port) ||
                !deviceNames.TryGetValue(port.DeviceId, out var name)) return qualifiedPort;
            var label = string.IsNullOrWhiteSpace(port.HoverLabel) ? port.PortName : port.HoverLabel;
            if (devices[port.DeviceId].Kind == ElectricalDeviceKind.Motor)
                label = port.PortName == "U" || port.PortName == "V" || port.PortName == "W" ? port.PortName + "1" : port.PortName;
            var device = string.IsNullOrWhiteSpace(name) ? port.DeviceId :
                name.Contains(port.DeviceId) ? name : name + " " + port.DeviceId;
            return device + " · " + label;
        }

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
            WireSurfacePlane frontSurface,
            WireSurfacePlane faultSurface,
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
            var wireShader = Resources.Load<Shader>("CabinetWire");
            wireMaterial = wireShader != null ? new Material(wireShader) { name = "Cabinet Surface Wire" } : lineMaterial;
            frontWireSurface = frontSurface;
            faultWireSurface = faultSurface;
            wireSurface = trainingCamera.IsViewingFaultSide ? faultWireSurface : frontWireSurface;
            originalVisuals = visualRegistry;
            portHover = hoverPresenter;
            tasks = CircuitTaskCatalog.CreateAll();

            foreach (var view in deviceViews)
            {
                devices[view.Runtime.DeviceId] = view.Runtime;
                deviceNames[view.Runtime.DeviceId] = view.DisplayName;
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
            graph.RegisterDevice(new InverterDriveRuntime("G120", () => panel.ActualSpeedRpm, () => panel.HasFault));
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
            if (!IsFileOperationActive && Time.frameCount > fileInputResumeFrame)
            {
                HandleHotkeys();
                UpdatePortHover();
                UpdateWiringDraft();
                HandleSceneInput();
            }
            lastSnapshot = graph.Solve(Time.deltaTime);
            foreach (var view in wireViews) view.Refresh();
            UpdateInstrumentReadout();
            if (lastSnapshot.HasShortCircuit)
                SetStatus(lastSnapshot.Errors[0], true);
        }

        public void SetMode(SimulationMode mode)
        {
            Mode = mode;
            if (Tachometer != null) Tachometer.SetFaultMode(mode == SimulationMode.Fault);
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
                if (device.Kind == ElectricalDeviceKind.Motor) device.ResetMotorSpeed();
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

        public void SelectInstrument(InstrumentKind kind)
        {
            foreach (var port in meterPorts) port.SetHighlighted(false);
            if (Tachometer != null) Tachometer.Deselect();
            instrumentKind = kind;
            meterPorts.Clear();
            instrumentText.text = $"{InstrumentName(kind)}：请选择两个端子";
            SetMode(SimulationMode.Fault);
            if (kind == InstrumentKind.Tachometer && Tachometer != null) Tachometer.Select();
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
            if (wireMaterial != null && wireMaterial.name == "Cabinet Surface Wire") Destroy(wireMaterial);
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
            ApplyWireSurfaceForView();
            ApplyPortAnchors();
        }

        private void OnViewSideChanged(bool viewingFaultSide)
        {
            if (portHover != null) portHover.Hide();
            ApplyWireSurfaceForView();
            ApplyPortAnchors();
        }

        private void ApplyWireSurfaceForView()
        {
            if (trainingCamera == null) return;
            var nextSurface = trainingCamera.IsViewingFaultSide ? faultWireSurface : frontWireSurface;
            if (Vector3.Dot(nextSurface.Normal, wireSurface.Normal) < 0f && IsRoutingWire) ClearSelection();
            wireSurface = nextSurface;
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
            if (Mode == SimulationMode.Fault && instrumentKind == InstrumentKind.Tachometer)
            {
                portHover.Hide();
                return;
            }
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
            var wire = graph.Wires[graph.Wires.Count - 1];
            var surface = wire.FaultSide.HasValue ? (wire.FaultSide.Value ? faultWireSurface : frontWireSurface) : wireSurface;
            wire.Points.Add(surface.Project(worldPosition));
            RefreshWireViews();
            return true;
        }

        public void UndoWiring()
        {
            if (undoWires.Count == 0) return;
            ClearWireSelection();
            redoWires.Push(SnapshotWires());
            graph.ReplaceWires(undoWires.Pop());
            RefreshWireViews();
            SetStatus("已撤销接线操作。", false);
        }

        public void RedoWiring()
        {
            if (redoWires.Count == 0) return;
            ClearWireSelection();
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
                    // Update owns the simulation clock, including shaft coast-down.
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
            if (Input.GetKeyDown(KeyCode.Delete)) DeleteWireSelectionOrLast();
        }

        private void HandleSceneInput()
        {
            if (Mode == SimulationMode.Fault && instrumentKind == InstrumentKind.Tachometer)
            {
                if (Tachometer != null) Tachometer.HandleInput(Camera.main);
                return;
            }
            if (Mode == SimulationMode.Drag)
            {
                if (EventSystem.current != null && EventSystem.current.IsPointerOverGameObject()) return;
                HandleDrag();
                return;
            }

            if (Mode == SimulationMode.Wiring)
            {
                HandleWiringInput();
                return;
            }

            if (EventSystem.current != null && EventSystem.current.IsPointerOverGameObject()) return;
            if (!Input.GetMouseButtonDown(0)) return;

            var camera = Camera.main;
            if (camera == null) return;
            var ray = camera.ScreenPointToRay(Input.mousePosition);
            var hasHit = Physics.Raycast(ray, out var hit, 100f);
            var port = hasHit ? hit.collider.GetComponent<ElectricalPortView>() : null;

            if (!hasHit) return;
            var deviceView = hit.collider.GetComponentInParent<ElectricalDeviceView>();

            if (Mode == SimulationMode.Fault && port != null) HandleMeterPort(port);
            else if (Mode == SimulationMode.Simulate && deviceView != null) HandleDeviceControl(deviceView.Runtime);
        }

        private void HandleWiringInput()
        {
            var camera = Camera.main;
            if (camera == null) return;
            var pointerOverUi = EventSystem.current != null && EventSystem.current.IsPointerOverGameObject();

            if (draggingWirePoint)
            {
                if (!Input.GetMouseButton(0))
                {
                    draggingWirePoint = false;
                    if (wirePointDragChanged)
                        SetStatus($"已移动第 {selectedWirePointIndex + 1} 个导线节点。", false);
                    return;
                }

                if (pointerOverUi || selectedWire == null || selectedWirePointIndex < 0) return;
                if (!selectedWire.Surface.Raycast(camera.ScreenPointToRay(Input.mousePosition), out var point)) return;
                point = selectedWire.Surface.Project(point);
                var current = selectedWire.Connection.Points[selectedWirePointIndex];
                if (Vector3.Distance(current, point) < 0.0001f) return;
                if (!wirePointDragChanged)
                {
                    PushWireHistory();
                    wirePointDragChanged = true;
                }
                selectedWire.Connection.Points[selectedWirePointIndex] = point;
                selectedWire.Refresh();
                selectedWire.SetSelected(true, selectedWirePointIndex);
                return;
            }

            if (pointerOverUi || !Input.GetMouseButtonDown(0)) return;

            var ray = camera.ScreenPointToRay(Input.mousePosition);
            var hasHit = Physics.Raycast(ray, out var hit, 100f);
            var port = hasHit ? hit.collider.GetComponent<ElectricalPortView>() : null;

            if (selectedPort != null)
            {
                HandleWiringClick(port, ray);
                return;
            }

            if (port != null)
            {
                ClearWireSelection();
                BeginWireRoute(port);
                return;
            }

            if (selectedWire != null &&
                selectedWire.TryHitNode(camera, Input.mousePosition, WireNodeHitDistancePixels, out var pointIndex))
            {
                selectedWirePointIndex = pointIndex;
                selectedWire.SetSelectedPoint(pointIndex);
                draggingWirePoint = true;
                wirePointDragChanged = false;
                return;
            }

            if (!TryFindWireAt(camera, Input.mousePosition, out var hitWire, out var insertionIndex, out var surfacePoint))
            {
                ClearWireSelection();
                lastWireClickId = string.Empty;
                return;
            }

            var wireId = hitWire.Connection.Id;
            var doubleClick = string.Equals(lastWireClickId, wireId, StringComparison.Ordinal) &&
                              Time.unscaledTime - lastWireClickTime <= DoubleClickSeconds &&
                              Vector2.Distance(lastWireClickPosition, Input.mousePosition) <= DoubleClickDistancePixels;
            SelectWire(hitWire);
            if (doubleClick && insertionIndex >= 0)
            {
                PushWireHistory();
                hitWire.Connection.Points.Insert(insertionIndex, hitWire.Surface.Project(surfacePoint));
                selectedWirePointIndex = insertionIndex;
                hitWire.Refresh();
                hitWire.SetSelected(true, selectedWirePointIndex);
                lastWireClickId = string.Empty;
                SetStatus($"已添加第 {insertionIndex + 1} 个导线节点。", false);
                return;
            }

            lastWireClickId = wireId;
            lastWireClickTime = Time.unscaledTime;
            lastWireClickPosition = Input.mousePosition;
            SetStatus("已选中导线；拖动节点可调整路径，双击线段可添加节点。", false);
        }

        private bool TryFindWireAt(
            Camera camera,
            Vector2 screenPosition,
            out ElectricalWireView closestView,
            out int insertionIndex,
            out Vector3 surfacePoint)
        {
            closestView = null;
            insertionIndex = -1;
            surfacePoint = Vector3.zero;
            var closestDistance = float.PositiveInfinity;

            foreach (var view in wireViews)
            {
                if (view == null || !view.TryHitLine(
                        camera,
                        screenPosition,
                        WireHitDistancePixels,
                        out var distance,
                        out var candidateIndex,
                        out var candidatePoint) ||
                    distance >= closestDistance)
                    continue;

                closestDistance = distance;
                closestView = view;
                insertionIndex = candidateIndex;
                surfacePoint = candidatePoint;
            }

            return closestView != null;
        }

        private void SelectWire(ElectricalWireView view)
        {
            if (selectedWire != view)
            {
                if (selectedWire != null) selectedWire.SetSelected(false);
                selectedWire = view;
            }
            selectedWirePointIndex = -1;
            selectedWire?.SetSelected(true);
            SelectedWireChanged?.Invoke(SelectedWire);
        }

        private void DeleteWireSelectionOrLast()
        {
            if (Mode != SimulationMode.Wiring || graph.Wires.Count == 0) return;

            if (selectedWire != null && selectedWirePointIndex >= 0 &&
                selectedWirePointIndex < selectedWire.Connection.Points.Count)
            {
                PushWireHistory();
                var removedIndex = selectedWirePointIndex;
                selectedWire.Connection.Points.RemoveAt(removedIndex);
                selectedWirePointIndex = -1;
                selectedWire.Refresh();
                selectedWire.SetSelected(true);
                SetStatus($"已删除第 {removedIndex + 1} 个导线节点。", false);
                return;
            }

            var wireId = selectedWire != null
                ? selectedWire.Connection.Id
                : graph.Wires[graph.Wires.Count - 1].Id;
            var selected = selectedWire != null;
            PushWireHistory();
            graph.RemoveWire(wireId);
            RefreshWireViews();
            SetStatus(selected ? "已删除选中的导线。" : "已删除最后一条线路。", false);
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
                : wireSurface.Project(selectedPort.CurrentAnchorPosition);
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
                currentWireArea,
                wireSurface,
                () => new WireEndpointGeometry(port.CurrentAnchorPosition, trainingCamera.IsViewingFaultSide ? port.RearWireBody : null));
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
            if (graph.Wires.Count > beforeCount)
            {
                if (!WireRenderPath.IsMotorJumper(startPortName, port.QualifiedPort, currentLineType))
                    wire.Points.AddRange(pendingWirePoints);
                wire.FaultSide = trainingCamera.IsViewingFaultSide;
            }
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
            if ((EventSystem.current != null && EventSystem.current.IsPointerOverGameObject()) ||
                Input.GetMouseButton(1) || Input.GetKey(KeyCode.W) || Input.GetKey(KeyCode.A) ||
                Input.GetKey(KeyCode.S) || Input.GetKey(KeyCode.D) || Input.GetKey(KeyCode.Q) ||
                Input.GetKey(KeyCode.E) || Mathf.Abs(Input.mouseScrollDelta.y) > 0f)
            {
                wireDraftView.SetVisible(false);
                return;
            }
            var ray = Camera.main.ScreenPointToRay(Input.mousePosition);
            var port = Physics.Raycast(ray, out var hit, 100f)
                ? hit.collider.GetComponent<ElectricalPortView>()
                : null;
            if (port != null) wireDraftCursor = port.CurrentAnchorPosition;
            else if (!TryProjectWirePoint(ray, out wireDraftCursor))
            {
                wireDraftView.SetVisible(false);
                return;
            }
            wireDraftView.SetVisible(true);
            wireDraftView.Refresh(pendingWirePoints, wireDraftCursor,
                port != null ? new WireEndpointGeometry(port.CurrentAnchorPosition, trainingCamera.IsViewingFaultSide ? port.RearWireBody : null) : (WireEndpointGeometry?)null,
                port != null && WireRenderPath.IsMotorJumper(selectedPort.QualifiedPort, port.QualifiedPort, currentLineType));
        }

        private bool TryProjectWirePoint(Ray ray, out Vector3 point)
        {
            if (selectedPort != null) return wireSurface.Raycast(ray, out point);
            point = Vector3.zero;
            return false;
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
                if (Tachometer != null) Tachometer.Refresh(lastSnapshot);
                var target = Tachometer != null ? Tachometer.AttachedTarget : null;
                instrumentText.text = target != null && lastSnapshot != null
                    ? $"转速表 · {target.MotorId}：{Mathf.Abs(lastSnapshot.GetMotorSpeedRpm(target.MotorId)):0} rpm\n点击表体可拿起"
                    : "转速表：将探头移至电机前端轴头，点击绿色测点放置";
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
            ClearWireSelection();
            foreach (var view in wireViews) if (view != null) Destroy(view.gameObject);
            wireViews.Clear();
            foreach (var wire in graph.Wires)
            {
                wire.FaultSide ??= trainingCamera.IsViewingFaultSide;
                var preset = wire.FaultSide.Value ? TrainingViewPreset.FaultBack : TrainingViewPreset.WiringFront;
                var jumper = wire.LineType.IndexOf("jumper", StringComparison.OrdinalIgnoreCase) >= 0 ||
                             wire.LineType.IndexOf("rope", StringComparison.OrdinalIgnoreCase) >= 0;
                var startAnchor = ResolveWireAnchor(wire.StartPort, preset, jumper);
                var endAnchor = ResolveWireAnchor(wire.EndPort, preset, jumper);
                // Unknown imported logical nodes must never produce a wire to world origin.
                if (startAnchor == null || endAnchor == null) continue;
                var gameObject = new GameObject("Wire_" + wire.Id);
                gameObject.transform.SetParent(wireRoot, false);
                var view = gameObject.AddComponent<ElectricalWireView>();
                view.Initialize(wire, port => port == wire.StartPort ? startAnchor.position : endAnchor.position,
                    wireMaterial, wire.FaultSide.Value ? faultWireSurface : frontWireSurface,
                    port => new WireEndpointGeometry(port == wire.StartPort ? startAnchor.position : endAnchor.position,
                        wire.FaultSide.Value && portViews.TryGetValue(port, out var endpoint) ? endpoint.RearWireBody : null));
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
            return portViews.TryGetValue(qualifiedPort, out var view) ? view.CurrentAnchorPosition : Vector3.zero;
        }

        private Transform ResolveWireAnchor(string qualifiedPort, TrainingViewPreset preset, bool jumper)
        {
            if (portViews.TryGetValue(qualifiedPort, out var port))
            {
                var anchor = port.GetOriginalAnchor(preset, jumper) ?? port.GetOriginalAnchor(preset, false);
                if (anchor != null) return anchor;
            }
            foreach (var device in devices.Values)
            foreach (var link in device.FixedLinks)
            {
                if (link.B != qualifiedPort || !portViews.TryGetValue(CircuitGraph.Port(device.DeviceId, link.A), out var terminal)) continue;
                var anchor = terminal.GetOriginalAnchor(preset, jumper) ?? terminal.GetOriginalAnchor(preset, false);
                if (anchor != null) return anchor;
            }
            return null;
        }

        private void ClearSelection()
        {
            if (selectedPort != null) selectedPort.SetHighlighted(false);
            selectedPort = null;
            pendingWirePoints.Clear();
            DestroyWireDraft();
            ClearWireSelection();
            foreach (var port in meterPorts) port.SetHighlighted(false);
            meterPorts.Clear();
            draggedDevice = null;
        }

        private void ClearWireSelection()
        {
            if (selectedWire != null) selectedWire.SetSelected(false);
            selectedWire = null;
            selectedWirePointIndex = -1;
            draggingWirePoint = false;
            wirePointDragChanged = false;
            lastWireClickId = string.Empty;
            SelectedWireChanged?.Invoke(null);
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
            StatusChanged?.Invoke(message, error);
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
