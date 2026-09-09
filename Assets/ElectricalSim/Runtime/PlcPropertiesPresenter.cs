using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;

namespace ElectricalSim
{
    public sealed class PlcPropertiesPresenter : MonoBehaviour
    {
        private SimulationController controller;
        private PlcSession session;
        private Font font;
        private Text title, status, cpuText;
        private Button connect, disconnect, apply, cpu;
        private InputField deviceName, ip, port, rack, slot, interval, timeout;
        private readonly List<InputField> addresses = new List<InputField>();
        private readonly List<Text> values = new List<Text>();
        private readonly List<Selectable> editable = new List<Selectable>();
        private SiemensCpu selectedCpu;
        private GameObject marker;
        private Material markerMaterial;
        private string feedback = "";
        private string deviceId;
        public ElectricalPortView HighlightedTerminal { get; private set; }
        public string StatusText => status != null ? status.text : "";
        public bool ConfigurationEditable => session != null && !session.IsBusy;
        public void Initialize(SimulationController source, Canvas canvas, Font uiFont)
        {
            controller = source; font = uiFont;
            transform.SetParent(canvas.transform, false);
            var rect = (RectTransform)transform;
            rect.anchorMin = new Vector2(1, 0.08f); rect.anchorMax = new Vector2(1, 0.94f);
            rect.pivot = new Vector2(1, 0.5f); rect.offsetMin = new Vector2(-560, 0); rect.offsetMax = new Vector2(-16, 0);
            gameObject.AddComponent<Image>().color = new Color(0.035f, 0.07f, 0.10f, 0.98f);
            title = Label(transform, "Title", "PLC 属性", 18, 16, 14, 430, 32);
            ButtonAt(transform, "Close", "关闭", 474, 14, 54, 30, () => controller.SelectPlc(null));
            Label(transform, "NameLabel", "名称", 14, 16, 58, 66, 30);
            deviceName = Field(transform, "DeviceName", 82, 58, 228);
            cpu = ButtonAt(transform, "Cpu", "S7-1200", 322, 58, 206, 30, () =>
            {
                selectedCpu = selectedCpu == SiemensCpu.S71200 ? SiemensCpu.S71500 : SiemensCpu.S71200;
                UpdateCpuLabel();
            });
            cpuText = cpu.GetComponentInChildren<Text>(); editable.Add(cpu);
            Label(transform, "IpLabel", "IP 地址", 14, 16, 98, 66, 30);
            ip = Field(transform, "Ip", 82, 98, 228);
            connect = ButtonAt(transform, "Connect", "连接", 322, 98, 96, 30, () => Guard(() =>
            {
                ApplyConfiguration(); controller.ConnectPlc(session.Configuration.DeviceId); feedback = "正在连接…";
            }));
            disconnect = ButtonAt(transform, "Disconnect", "断开", 430, 98, 98, 30, () => Guard(() =>
            { _ = session.DisconnectAsync(); feedback = "正在断开并清理已写入的虚拟输入…"; }));
            port = SmallField("端口", "Port", 16, 138, 102);
            rack = SmallField("Rack", "Rack", 194, 138, 78);
            slot = SmallField("Slot", "Slot", 372, 138, 84);
            interval = SmallField("轮询 ms", "Interval", 16, 178, 96);
            timeout = SmallField("超时 ms", "Timeout", 194, 178, 88);
            apply = ButtonAt(transform, "Apply", "保存属性", 390, 178, 138, 30, () => Guard(() =>
            { ApplyConfiguration(); feedback = "属性已应用；请保存工程以保存到文件。"; })); editable.Add(apply);
            Label(transform, "TerminalColumn", "场景端子", 14, 22, 218, 184, 26);
            Label(transform, "AddressColumn", "真实 PLC 地址", 14, 208, 218, 172, 26);
            Label(transform, "StateColumn", "状态", 14, 388, 218, 54, 26);
            Label(transform, "LocateColumn", "定位", 14, 452, 218, 62, 26);
            var scroll = new GameObject("PointScroll", typeof(RectTransform), typeof(Image), typeof(ScrollRect));
            scroll.transform.SetParent(transform, false);
            var scrollRect = (RectTransform)scroll.transform;
            scrollRect.anchorMin = Vector2.zero; scrollRect.anchorMax = Vector2.one;
            scrollRect.offsetMin = new Vector2(16, 140); scrollRect.offsetMax = new Vector2(-16, -252);
            scroll.GetComponent<Image>().color = new Color(0.02f, 0.035f, 0.05f, 1);
            var viewport = new GameObject("Viewport", typeof(RectTransform), typeof(RectMask2D));
            viewport.transform.SetParent(scroll.transform, false); Stretch((RectTransform)viewport.transform);
            ((RectTransform)viewport.transform).offsetMax = new Vector2(-12, 0);
            var content = new GameObject("Points", typeof(RectTransform)); content.transform.SetParent(viewport.transform, false);
            var contentRect = (RectTransform)content.transform;
            contentRect.anchorMin = new Vector2(0, 1); contentRect.anchorMax = Vector2.one; contentRect.pivot = new Vector2(0.5f, 1);
            contentRect.sizeDelta = new Vector2(0, 30 * 36 + 4);
            var scroller = scroll.GetComponent<ScrollRect>(); scroller.viewport = (RectTransform)viewport.transform;
            scroller.content = contentRect; scroller.horizontal = false; scroller.scrollSensitivity = 30;
            scroller.movementType = ScrollRect.MovementType.Clamped;
            var bar = new GameObject("Scrollbar", typeof(RectTransform), typeof(Image), typeof(Scrollbar));
            bar.transform.SetParent(scroll.transform, false);
            var barRect = (RectTransform)bar.transform; barRect.anchorMin = new Vector2(1, 0); barRect.anchorMax = Vector2.one;
            barRect.offsetMin = new Vector2(-7, 0); barRect.offsetMax = Vector2.zero;
            bar.GetComponent<Image>().color = new Color(0.08f, 0.13f, 0.17f);
            var handle = new GameObject("Handle", typeof(RectTransform), typeof(Image)); handle.transform.SetParent(bar.transform, false);
            Stretch((RectTransform)handle.transform); handle.GetComponent<Image>().color = new Color(0.24f, 0.52f, 0.61f);
            var scrollbar = bar.GetComponent<Scrollbar>(); scrollbar.handleRect = (RectTransform)handle.transform;
            scrollbar.targetGraphic = handle.GetComponent<Image>(); scrollbar.direction = Scrollbar.Direction.BottomToTop;
            scroller.verticalScrollbar = scrollbar;
            var terminals = PlcConfiguration.InputTerminals.Concat(PlcConfiguration.OutputTerminals).Concat(PlcConfiguration.SupplyTerminals).ToArray();
            for (var i = 0; i < terminals.Length; i++)
            {
                var terminal = terminals[i]; var rowY = i * 36 + 2;
                Label(content.transform, "Terminal_" + terminal, terminal + (i < 14 ? "  输入 → PLC" : i < 24 ? "  输出 ← PLC" : "  供电 / 公共端"), 13, 6, rowY, 184, 30);
                if (i < 24) addresses.Add(Field(content.transform, "Address_" + terminal, 192, rowY, 172));
                else Label(content.transform, "Supply_" + terminal, "仅参与电路", 13, 194, rowY, 170, 30);
                values.Add(Label(content.transform, "Value_" + terminal, "—", 13, 372, rowY, 54, 30));
                ButtonAt(content.transform, "Locate_" + terminal, "定位", 436, rowY, 62, 30, () => Locate(terminal));
            }
            status = Label(transform, "Status", "未连接", 13, 16, 0, 512, 114);
            status.alignment = TextAnchor.UpperLeft;
            status.rectTransform.anchorMin = status.rectTransform.anchorMax = Vector2.zero;
            status.rectTransform.pivot = Vector2.zero; status.rectTransform.anchoredPosition = new Vector2(16, 12);
            gameObject.SetActive(false);
        }
        private InputField SmallField(string caption, string name, float x, float y, float width)
        {
            Label(transform, name + "Label", caption, 13, x, y, 68, 30);
            return Field(transform, name, x + 68, y, width);
        }
        public void Show(PlcSession source)
        {
            ClearHighlight(); session = source; feedback = "";
            gameObject.SetActive(source != null);
            if (source == null) return;
            transform.SetAsLastSibling();
            var c = source.Configuration;
            deviceId = c.DeviceId;
            title.text = c.DeviceId + " · PLC 属性"; selectedCpu = c.Cpu; UpdateCpuLabel();
            deviceName.text = c.DisplayName; ip.text = c.Ip; port.text = c.Port.ToString(); rack.text = c.Rack.ToString(); slot.text = c.Slot.ToString();
            interval.text = c.PollIntervalMs.ToString(); timeout.text = c.TimeoutMs.ToString();
            var points = c.Inputs.Concat(c.Outputs).ToArray();
            for (var i = 0; i < points.Length; i++) addresses[i].text = points[i].Address;
            Refresh();
        }
        public void ApplyConfiguration()
        {
            if (session == null || session.IsBusy) throw new InvalidOperationException("请先断开 PLC 后修改配置。");
            var c = session.Configuration;
            c.DisplayName = deviceName.text.Trim(); c.Ip = ip.text.Trim(); c.Cpu = selectedCpu;
            if (!int.TryParse(port.text, out c.Port) || !short.TryParse(rack.text, out c.Rack) || !short.TryParse(slot.text, out c.Slot) ||
                !int.TryParse(interval.text, out c.PollIntervalMs) || !int.TryParse(timeout.text, out c.TimeoutMs)) throw new ArgumentException("通信参数必须为整数。");
            var points = c.Inputs.Concat(c.Outputs).ToArray();
            for (var i = 0; i < points.Length; i++) points[i].Address = addresses[i].text;
            session.Configure(c);
        }
        public void Locate(string terminal)
        {
            if (session == null) return;
            HighlightedTerminal = controller.ResolvePlcTerminal(session.Configuration.DeviceId, terminal);
            if (marker == null)
            {
                marker = GameObject.CreatePrimitive(PrimitiveType.Sphere); marker.name = "PLC Terminal Highlight";
                Destroy(marker.GetComponent<Collider>());
                markerMaterial = new Material(Resources.Load<Shader>("CabinetWire")) { color = Color.yellow };
                marker.GetComponent<Renderer>().sharedMaterial = markerMaterial;
                marker.transform.localScale = Vector3.one * 0.012f;
            }
            marker.SetActive(true); PositionMarker();
            feedback = "已定位：" + session.Configuration.DeviceId + "_" + terminal + "（黄色标记）";
        }
        private void PositionMarker()
        {
            if (HighlightedTerminal == null || marker == null) return;
            var camera = Camera.main;
            var cameraController = camera != null ? camera.GetComponent<TrainingCameraController>() : null;
            var anchor = HighlightedTerminal.GetOriginalAnchor(cameraController != null && cameraController.IsViewingFaultSide ? TrainingViewPreset.FaultBack : TrainingViewPreset.WiringFront, false);
            marker.transform.position = anchor != null ? anchor.position : HighlightedTerminal.CurrentAnchorPosition;
        }
        private void ClearHighlight() { HighlightedTerminal = null; if (marker != null) marker.SetActive(false); }
        private void Update() { Refresh(); PositionMarker(); }
        private void Refresh()
        {
            if (session == null) return;
            var busy = session.IsBusy;
            foreach (var control in editable) control.interactable = !busy;
            connect.interactable = !busy; disconnect.interactable = session.State == PlcConnectionState.Connected || session.State == PlcConnectionState.Connecting;
            var names = new[] { "未连接", "连接中", "已连接", "断开中", "通信故障" };
            var fresh = session.HasFreshSample;
            var inputValues = session.RemoteInputs; var outputValues = session.RemoteOutputs;
            var runtime = controller.SelectedPlc?.Runtime;
            for (var i = 0; i < 24; i++)
            {
                var value = i < 14 ? inputValues[i] : outputValues[i - 14];
                values[i].text = fresh ? value ? "1" : "0" : "—";
                values[i].color = fresh && value ? new Color(0.35f, 1f, 0.58f) : Color.gray;
            }
            for (var i = 24; i < values.Count; i++) values[i].text = controller.PlcSupplyState(deviceId, PlcConfiguration.SupplyTerminals[i - 24]);
            status.text = names[(int)session.State] + (busy ? " · 配置已锁定" : "") +
                "\n" + (controller.Mode == SimulationMode.Simulate ? "仿真模式：双向联动" : "只读监控；进入仿真模式后写入虚拟输入") +
                "\n本体供电：" + (runtime != null && runtime.IsActive ? "正常" : "未就绪") + "    输出供电：" + (runtime != null && runtime.OutputSupplyReady ? "正常" : "未就绪") +
                "\n" + (session.State == PlcConnectionState.Faulted ? session.Error : feedback) +
                "\n滚动列表可查看全部 Q 输出和供电端子。";
        }
        private void UpdateCpuLabel() { if (cpuText != null) cpuText.text = selectedCpu == SiemensCpu.S71200 ? "型号：S7-1200  ⇄" : "型号：S7-1500  ⇄"; }
        private void Guard(Action action)
        {
            try { action(); } catch (Exception exception) { feedback = exception.Message; }
            Refresh();
        }
        private Text Label(Transform parent, string name, string text, int size, float x, float y, float width, float height)
        {
            var obj = new GameObject(name, typeof(RectTransform), typeof(Text)); obj.transform.SetParent(parent, false);
            Place((RectTransform)obj.transform, x, y, width, height);
            var label = obj.GetComponent<Text>(); label.font = font; label.fontSize = size; label.text = text;
            label.color = new Color(0.87f, 0.92f, 0.95f); label.alignment = TextAnchor.MiddleLeft; label.raycastTarget = false; label.supportRichText = false;
            return label;
        }
        private InputField Field(Transform parent, string name, float x, float y, float width)
        {
            var obj = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(InputField)); obj.transform.SetParent(parent, false);
            Place((RectTransform)obj.transform, x, y, width, 30);
            obj.GetComponent<Image>().color = new Color(0.14f, 0.20f, 0.25f);
            var text = Label(obj.transform, "Text", "", 14, 6, 0, width - 12, 30);
            var field = obj.GetComponent<InputField>(); field.textComponent = text; field.targetGraphic = obj.GetComponent<Image>(); field.characterLimit = 64;
            editable.Add(field); return field;
        }
        private Button ButtonAt(Transform parent, string name, string caption, float x, float y, float width, float height, Action action)
        {
            var obj = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(Button)); obj.transform.SetParent(parent, false);
            Place((RectTransform)obj.transform, x, y, width, height);
            obj.GetComponent<Image>().color = new Color(0.10f, 0.31f, 0.40f);
            var label = Label(obj.transform, "Text", caption, 13, 0, 0, width, height); label.alignment = TextAnchor.MiddleCenter;
            var button = obj.GetComponent<Button>(); button.targetGraphic = obj.GetComponent<Image>(); button.onClick.AddListener(() => action()); return button;
        }
        private static void Place(RectTransform rect, float x, float y, float width, float height)
        { rect.anchorMin = rect.anchorMax = new Vector2(0, 1); rect.pivot = new Vector2(0, 1); rect.anchoredPosition = new Vector2(x, -y); rect.sizeDelta = new Vector2(width, height); }
        private static void Stretch(RectTransform rect)
        { rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one; rect.offsetMin = rect.offsetMax = Vector2.zero; }
        private void OnDisable() => ClearHighlight();
        private void OnDestroy() { if (marker != null) Destroy(marker); if (markerMaterial != null) Destroy(markerMaterial); }
    }
}
