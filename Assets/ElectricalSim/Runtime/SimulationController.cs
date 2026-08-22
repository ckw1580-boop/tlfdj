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
        private IReadOnlyList<CircuitTaskSpec> tasks;
        private int taskIndex;
        private ElectricalPortView selectedPort;
        private readonly List<ElectricalPortView> meterPorts = new List<ElectricalPortView>();
        private ElectricalDeviceView draggedDevice;
        private Plane dragPlane;
        private Vector3 dragOffset;
        private Cc3dDocument loadedDocument;
        private SimulationSnapshot lastSnapshot;
        private InstrumentKind instrumentKind = InstrumentKind.Multimeter;

        private Text modeText;
        private Text taskText;
        private Text taskDescriptionText;
        private Text statusText;
        private Text instrumentText;
        private TrainingCameraController trainingCamera;
        private Transform wireRoot;
        private Material wireMaterial;

        public SimulationMode Mode { get; private set; } = SimulationMode.View;
        public CircuitGraph Graph => graph;
        public CircuitTaskSpec CurrentTask => tasks[taskIndex];

        public void Initialize(
            IEnumerable<ElectricalDeviceView> deviceViews,
            TrainingCameraController cameraController,
            Transform wireContainer,
            Text modeLabel,
            Text taskLabel,
            Text taskDescription,
            Text statusLabel,
            Text instrumentLabel,
            Material lineMaterial)
        {
            trainingCamera = cameraController;
            wireRoot = wireContainer;
            modeText = modeLabel;
            taskText = taskLabel;
            taskDescriptionText = taskDescription;
            statusText = statusLabel;
            instrumentText = instrumentLabel;
            wireMaterial = lineMaterial;
            tasks = CircuitTaskCatalog.CreateAll();

            foreach (var view in deviceViews)
            {
                devices[view.Runtime.DeviceId] = view.Runtime;
                graph.RegisterDevice(view.Runtime);
                foreach (var port in view.Ports) portViews[port.QualifiedPort] = port;
            }

            UpdateTaskUi();
            SetMode(SimulationMode.View);
            SetStatus("系统就绪。请选择任务后进行接线，或点击“标准接线”加载参考拓扑。", false);
        }

        private void Update()
        {
            HandleHotkeys();
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
            ClearSelection();
            modeText.text = $"当前模式：{ModeName(mode)}";
            if (mode == SimulationMode.Wiring) trainingCamera.SetWiringView();
            else if (mode == SimulationMode.Fault) trainingCamera.SetFaultView();
            SetStatus($"已进入{ModeName(mode)}模式。", false);
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
            graph.ClearWires();
            foreach (var pair in CurrentTask.RequiredConnections)
                graph.AddWire(pair.A, pair.B, ColorForPort(pair.A), "JumperLine");
            RefreshWireViews();
            SetStatus($"已加载“{CurrentTask.Name}”标准接线，可进入仿真并提交验收。", false);
        }

        public void ResetTraining()
        {
            graph.ClearWires();
            foreach (var device in devices.Values)
            {
                if (device.Kind == ElectricalDeviceKind.Breaker || device.Kind == ElectricalDeviceKind.Fuse) device.SetControl(true);
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
                if (device.Kind == ElectricalDeviceKind.Breaker || device.Kind == ElectricalDeviceKind.Fuse) device.SetControl(true);
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
            if (Input.GetKeyDown(KeyCode.Delete) && graph.Wires.Count > 0)
            {
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

            var ray = Camera.main.ScreenPointToRay(Input.mousePosition);
            if (!Physics.Raycast(ray, out var hit, 100f)) return;
            var port = hit.collider.GetComponent<ElectricalPortView>();
            var deviceView = hit.collider.GetComponentInParent<ElectricalDeviceView>();

            if (Mode == SimulationMode.Wiring && port != null) HandleWiringPort(port);
            else if (Mode == SimulationMode.Fault && port != null) HandleMeterPort(port);
            else if (Mode == SimulationMode.Simulate && deviceView != null) HandleDeviceControl(deviceView.Runtime);
        }

        private void HandleDrag()
        {
            var ray = Camera.main.ScreenPointToRay(Input.mousePosition);
            if (Input.GetMouseButtonDown(0) && Physics.Raycast(ray, out var hit, 100f))
            {
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

        private void HandleWiringPort(ElectricalPortView port)
        {
            if (selectedPort == null)
            {
                selectedPort = port;
                selectedPort.SetHighlighted(true);
                SetStatus($"起点：{port.QualifiedPort}，请选择终点。", false);
                return;
            }

            if (selectedPort == port)
            {
                ClearSelection();
                return;
            }

            graph.AddWire(selectedPort.QualifiedPort, port.QualifiedPort, ColorForPort(selectedPort.QualifiedPort));
            selectedPort.SetHighlighted(false);
            selectedPort = null;
            RefreshWireViews();
            SetStatus($"线路已连接到 {port.QualifiedPort}。", false);
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
                device.SetControl(!device.IsClosed);
            else if (device.Kind == ElectricalDeviceKind.ThermalRelay)
                device.SetControl(!device.IsTripped);
        }

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

        private Vector3 ResolvePortPosition(string qualifiedPort)
        {
            return portViews.TryGetValue(qualifiedPort, out var view) ? view.transform.position : Vector3.zero;
        }

        private void ClearSelection()
        {
            if (selectedPort != null) selectedPort.SetHighlighted(false);
            selectedPort = null;
            foreach (var port in meterPorts) port.SetHighlighted(false);
            meterPorts.Clear();
            draggedDevice = null;
        }

        private void UpdateTaskUi()
        {
            taskText.text = $"{taskIndex + 1:00}/{tasks.Count:00}  {CurrentTask.Name}";
            taskDescriptionText.text = CurrentTask.Description;
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
