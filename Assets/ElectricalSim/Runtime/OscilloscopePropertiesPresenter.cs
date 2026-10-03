using UnityEngine;
using UnityEngine.UI;

namespace ElectricalSim
{
    public sealed class OscilloscopePropertiesPresenter : MonoBehaviour
    {
        private SimulationController simulation;
        private OscilloscopeController scope;
        private GameObject panel;
        private Font font;
        private Text status;
        private readonly Text[] readings = new Text[2];
        private readonly Dropdown[] coupling = new Dropdown[2], voltage = new Dropdown[2], waveform = new Dropdown[2];
        private readonly Text[] enableText = new Text[2], positionText = new Text[2];
        private Dropdown timebase;
        private int revision = -1;
        private RectTransform tools;
        private Vector2 toolsAnchorMin, toolsAnchorMax, toolsOffsetMin, toolsOffsetMax;
        private bool toolsRelocated;
        public GameObject Panel => panel;
        public OscilloscopePlot Plot { get; private set; }
        public void Initialize(SimulationController controller, OscilloscopeController instrument, Canvas canvas, Font uiFont)
        {
            simulation = controller; scope = instrument; font = uiFont;
            tools = canvas.transform.Find("InstrumentTools") as RectTransform;
            if (tools != null) { toolsAnchorMin = tools.anchorMin; toolsAnchorMax = tools.anchorMax; toolsOffsetMin = tools.offsetMin; toolsOffsetMax = tools.offsetMax; }
            panel = new GameObject("OscilloscopeProperties", typeof(RectTransform), typeof(Image)); panel.transform.SetParent(canvas.transform, false);
            var rect = (RectTransform)panel.transform; rect.anchorMin = new Vector2(1, .07f); rect.anchorMax = new Vector2(1, .82f); rect.pivot = new Vector2(1, .5f);
            rect.offsetMin = new Vector2(-476, 0); rect.offsetMax = new Vector2(-16, 0); panel.GetComponent<Image>().color = new Color(.025f, .055f, .075f, .98f);
            Label(panel.transform, "台式双通道差分示波器", 12, 10, 436, 30, 19);
            var scrollObject = new GameObject("Scroll", typeof(RectTransform), typeof(Image), typeof(ScrollRect)); scrollObject.transform.SetParent(panel.transform, false);
            var sr = (RectTransform)scrollObject.transform; sr.anchorMin = Vector2.zero; sr.anchorMax = Vector2.one; sr.offsetMin = new Vector2(10, 10); sr.offsetMax = new Vector2(-10, -48);
            scrollObject.GetComponent<Image>().color = new Color(.02f, .035f, .05f);
            var viewport = new GameObject("Viewport", typeof(RectTransform), typeof(RectMask2D)); viewport.transform.SetParent(sr, false); Stretch((RectTransform)viewport.transform);
            var content = new GameObject("Content", typeof(RectTransform)).GetComponent<RectTransform>(); content.SetParent(viewport.transform, false);
            content.anchorMin = new Vector2(0, 1); content.anchorMax = new Vector2(1, 1); content.pivot = new Vector2(.5f, 1); content.sizeDelta = new Vector2(0, 1340);
            var scroll = scrollObject.GetComponent<ScrollRect>(); scroll.viewport = (RectTransform)viewport.transform; scroll.content = content; scroll.horizontal = false; scroll.scrollSensitivity = 30; scroll.movementType = ScrollRect.MovementType.Clamped;
            status = Label(content, "", 10, 8, 412, 46, 15);
            var plot = new GameObject("Waveform", typeof(RectTransform), typeof(CanvasRenderer), typeof(OscilloscopePlot)); plot.transform.SetParent(content, false); Rect((RectTransform)plot.transform, 14, 65, 400, 240);
            Plot = plot.GetComponent<OscilloscopePlot>(); Plot.Bind(scope.Frame);
            Label(content, "横轴：ms（10格）   纵轴：V（8格）", 10, 310, 412, 24, 13);
            Button(content, "RunStop", "运行／停止", 10, 342, 130, scope.ToggleRunning);
            Button(content, "AutoScale", "自动量程", 150, 342, 130, scope.AutoScale);
            Button(content, "Defaults", "恢复显示", 290, 342, 130, scope.ResetSettings);
            Label(content, "时基 ms/div", 10, 390, 150, 30, 15);
            timebase = Choice(content, "Timebase", 170, 388, 250, Format(OscilloscopeFrame.TimeStepsMs), scope.SetTimeIndex);
            for (var i = 0; i < 2; i++)
            {
                var index = i; var y = 440 + i * 374;
                enableText[i] = Button(content, "Channel" + i, "", 10, y, 130, () => scope.SetChannelEnabled(index, !scope.Frame.Channels[index].Enabled)).GetComponentInChildren<Text>();
                enableText[i].color = OscilloscopePlot.ChannelColors[i];
                coupling[i] = Choice(content, "Coupling" + i, 150, y, 120, new[] { "DC 耦合", "AC 耦合" }, v => scope.SetCoupling(index, (OscilloscopeCoupling)v));
                voltage[i] = Choice(content, "Voltage" + i, 280, y, 140, Format(OscilloscopeFrame.VoltageSteps, " V/div"), v => scope.SetVoltageIndex(index, v));
                waveform[i] = Choice(content, "Waveform" + i, 10, y + 42, 410, new[] { "基波（实线）", "PWM（阶梯）", "叠加（基波虚线＋PWM）" }, v => scope.SetWaveform(index, (OscilloscopeWaveform)v));
                readings[i] = Label(content, "", 10, y + 86, 410, 215, 14); readings[i].color = OscilloscopePlot.ChannelColors[i];
                positionText[i] = Label(content, "", 10, y + 306, 190, 30, 14);
                Button(content, "Down" + i, "下移", 210, y + 304, 100, () => scope.SetPosition(index, scope.Frame.Channels[index].Position - .5f));
                Button(content, "Up" + i, "上移", 320, y + 304, 100, () => scope.SetPosition(index, scope.Frame.Channels[index].Position + .5f));
            }
            Button(content, "Retract", "收回全部探头", 10, 1220, 200, scope.RetractProbes);
            Button(content, "Home", "仪器归位", 220, 1220, 200, scope.ReturnHome);
            Label(content, "点击探头接端子 · 拖动机身移动\nCH1 与 CH2 独立差分测量，不共地", 10, 1270, 410, 55, 14);
            Refresh();
        }
        private static string[] Format(float[] values, string suffix = "") { var result = new string[values.Length]; for (var i = 0; i < values.Length; i++) result[i] = values[i].ToString("0.###") + suffix; return result; }
        public void Refresh()
        {
            if (panel == null) return;
            var visible = scope.IsSelected && !simulation.IsInteractionBlocked;
            if (tools != null && toolsRelocated != visible)
            {
                toolsRelocated = visible;
                if (visible) { tools.anchorMin = tools.anchorMax = new Vector2(1, .88f); tools.offsetMin = new Vector2(-834, -110); tools.offsetMax = new Vector2(-492, 0); }
                else { tools.anchorMin = toolsAnchorMin; tools.anchorMax = toolsAnchorMax; tools.offsetMin = toolsOffsetMin; tools.offsetMax = toolsOffsetMax; }
            }
            panel.SetActive(visible); if (!visible || revision == scope.Frame.Revision) return;
            revision = scope.Frame.Revision; var f = scope.Frame;
            status.text = (f.Frozen ? "已冻结 · 测点与读数为冻结时刻" : "运行 · 当前工况波形") + $"\n公共时基 {f.MillisecondsPerDivision:0.###} ms/div";
            timebase.SetValueWithoutNotify(f.TimeIndex);
            timebase.interactable = !f.Frozen;
            for (var i = 0; i < 2; i++)
            {
                var c = f.Channels[i]; readings[i].text = f.DescribeChannel(i); enableText[i].text = $"CH{i + 1} {(c.Enabled ? "开启" : "关闭")}";
                waveform[i].SetValueWithoutNotify((int)c.Waveform); waveform[i].interactable = !f.Frozen;
                coupling[i].SetValueWithoutNotify((int)c.Coupling); voltage[i].SetValueWithoutNotify(c.VoltageIndex); positionText[i].text = $"垂直位置 {c.Position:+0.0;-0.0;0} 格";
                coupling[i].interactable = !f.Frozen;
            }
        }
        private void LateUpdate() => Refresh();
        private void OnDestroy()
        {
            if (tools != null && toolsRelocated) { tools.anchorMin = toolsAnchorMin; tools.anchorMax = toolsAnchorMax; tools.offsetMin = toolsOffsetMin; tools.offsetMax = toolsOffsetMax; }
            if (panel != null) Destroy(panel);
        }
        private Text Label(Transform parent, string text, float x, float y, float width, float height, int size)
        {
            var go = new GameObject("Label", typeof(RectTransform), typeof(Text)); go.transform.SetParent(parent, false); Rect((RectTransform)go.transform, x, y, width, height);
            var label = go.GetComponent<Text>(); label.font = font; label.fontSize = size; label.text = text; label.color = Color.white; label.raycastTarget = false; label.verticalOverflow = VerticalWrapMode.Truncate; return label;
        }
        private Button Button(Transform parent, string name, string title, float x, float y, float width, UnityEngine.Events.UnityAction action)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(Button)); go.transform.SetParent(parent, false); Rect((RectTransform)go.transform, x, y, width, 34);
            go.GetComponent<Image>().color = new Color(.10f, .19f, .24f); var b = go.GetComponent<Button>(); b.onClick.AddListener(action);
            var text = Label(go.transform, title, 0, 0, width, 34, 14); text.alignment = TextAnchor.MiddleCenter; return b;
        }
        private Dropdown Choice(Transform parent, string name, float x, float y, float width, string[] choices, UnityEngine.Events.UnityAction<int> action)
        {
            // Unity's built-in template supplies keyboard navigation, scrolling and correct input blocking.
            var go = DefaultControls.CreateDropdown(new DefaultControls.Resources()); go.name = name; go.transform.SetParent(parent, false); Rect((RectTransform)go.transform, x, y, width, 34);
            foreach (var t in go.GetComponentsInChildren<Text>(true)) { t.font = font; t.fontSize = 14; }
            var d = go.GetComponent<Dropdown>(); d.ClearOptions(); d.AddOptions(new System.Collections.Generic.List<string>(choices)); d.onValueChanged.AddListener(action); return d;
        }
        private static void Rect(RectTransform r, float x, float y, float width, float height) { r.anchorMin = r.anchorMax = new Vector2(0, 1); r.pivot = new Vector2(0, 1); r.anchoredPosition = new Vector2(x, -y); r.sizeDelta = new Vector2(width, height); }
        private static void Stretch(RectTransform r) { r.anchorMin = Vector2.zero; r.anchorMax = Vector2.one; r.offsetMin = r.offsetMax = Vector2.zero; }
    }
}
