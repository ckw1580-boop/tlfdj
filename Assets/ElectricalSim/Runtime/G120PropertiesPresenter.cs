using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;
using UnityEngine.UI;

namespace ElectricalSim
{
    public sealed class G120PropertiesPresenter : MonoBehaviour
    {
        private SimulationController controller;
        private Font font;
        private RectTransform content;
        private Text details, notice, summary;
        private ScrollRect scroll;
        private readonly InputField[,] ai = new InputField[2, 5];
        private readonly InputField[] scale = new InputField[2];
        private InputField motorCurrent;
        private readonly Text[] sourceLabels = new Text[2], typeLabels = new Text[2], aoLabels = new Text[2];
        private Text selectedLabel, ptcEnableLabel, ptcStateLabel;
        public string DisplayedText => details != null ? details.text : "";
        public bool IsVisible => gameObject.activeSelf;
        public void Initialize(SimulationController source, Canvas canvas, Font uiFont)
        {
            controller = source; font = uiFont; transform.SetParent(canvas.transform, false);
            var panel = (RectTransform)transform;
            panel.anchorMin = new Vector2(1, 0.08f); panel.anchorMax = new Vector2(1, 0.94f); panel.pivot = new Vector2(1, 0.5f);
            panel.offsetMin = new Vector2(-560, 0); panel.offsetMax = new Vector2(-16, 0);
            gameObject.AddComponent<Image>().color = new Color(0.035f, 0.07f, 0.10f, 0.98f);
            Label(transform, "G120 · 控制端子属性", 16, 14, 340, 32, 18);
            Button(transform, "BOP 参数", 350, 14, 96, () => controller.OpenInverterBop());
            Button(transform, "关闭", 456, 14, 70, () => controller.SelectInverter(false));
            summary = Label(transform, "", 16, 52, 510, 50, 14);
            var obj = new GameObject("Scroll", typeof(RectTransform), typeof(Image), typeof(ScrollRect)); obj.transform.SetParent(transform, false);
            var rect = (RectTransform)obj.transform; Stretch(rect); rect.offsetMin = new Vector2(16, 16); rect.offsetMax = new Vector2(-16, -110);
            obj.GetComponent<Image>().color = new Color(0.02f, 0.035f, 0.05f);
            var viewport = new GameObject("Viewport", typeof(RectTransform), typeof(RectMask2D)); viewport.transform.SetParent(obj.transform, false); Stretch((RectTransform)viewport.transform);
            content = new GameObject("Content", typeof(RectTransform)).GetComponent<RectTransform>(); content.SetParent(viewport.transform, false);
            content.anchorMin = new Vector2(0, 1); content.anchorMax = Vector2.one; content.pivot = new Vector2(0.5f, 1);
            for (var i = 0; i < 2; i++)
            {
                var channel = i; var y = 10 + i * 184;
                sourceLabels[i] = Button(content, "", 10, y, 195, () => { controller.InverterControls.Inputs[channel].Simulated = !controller.InverterControls.Inputs[channel].Simulated; RefreshLabels(); });
                typeLabels[i] = Button(content, "", 215, y, 278, () => { var c = controller.InverterControls; c.SetInputType(channel, (c.InputType(channel) + 1) % 5); LoadFields(); });
                Label(content, "模拟输入值（V / mA）", 10, y + 42, 280, 30);
                ai[i, 0] = Input(content, "AI" + i + "Value", 318, y + 42, 175);
                var names = new[] { "X1", "Y1（%）", "X2", "Y2（%）" };
                for (var j = 0; j < 4; j++)
                { var x = 10 + j % 2 * 248; var row = y + 84 + j / 2 * 40; Label(content, names[j], x, row, 106, 30); ai[i, j + 1] = Input(content, "AI" + i + names[j], x + 108, row, 124); }
            }
            selectedLabel = Button(content, "", 10, 380, 300, () => { controller.InverterControls.SelectedAnalogInput = 1 - controller.InverterControls.SelectedAnalogInput; RefreshLabels(); });
            for (var i = 0; i < 2; i++)
            {
                var channel = i; var y = 426 + i * 86;
                aoLabels[i] = Button(content, "", 10, y, 483, () => { var o = controller.InverterControls.Outputs[channel]; o.Mode = (G120AnalogOutputMode)(((int)o.Mode + 1) % 3); RefreshLabels(); });
                Label(content, i == 0 ? "转速满量程（rpm）" : "电流满量程（A，0=额定值）", 10, y + 40, 300, 30);
                scale[i] = Input(content, "AO" + i + "FullScale", 318, y + 40, 175);
            }
            Label(content, "模拟电机电流（A，模拟值）", 10, 602, 300, 30); motorCurrent = Input(content, "SimulatedMotorCurrent", 318, 602, 175);
            ptcEnableLabel = Button(content, "", 10, 646, 205, () => { controller.InverterControls.PtcEnabled = !controller.InverterControls.PtcEnabled; RefreshLabels(); });
            ptcStateLabel = Button(content, "", 225, 646, 268, () => { var c = controller.InverterControls; c.PtcState = (G120PtcState)(((int)c.PtcState + 1) % 4); RefreshLabels(); });
            Button(content, "应用输入与标定", 10, 690, 190, Apply);
            Button(content, "故障复位", 210, 690, 128, () => controller.InverterPanel.SetFault(false));
            Button(content, "恢复默认", 348, 690, 145, () => { controller.InverterPanel.ResetFactorySettings(); LoadFields(); });
            notice = Label(content, "测试值不保存；滚动查看全部端子。", 10, 733, 483, 46, 14); notice.color = new Color(1, 0.78f, 0.3f);
            details = Label(content, "", 10, 790, 483, 800, 15); details.verticalOverflow = VerticalWrapMode.Overflow; details.lineSpacing = 1.2f;
            scroll = obj.GetComponent<ScrollRect>(); scroll.viewport = (RectTransform)viewport.transform; scroll.content = content;
            scroll.horizontal = false; scroll.scrollSensitivity = 32; scroll.movementType = ScrollRect.MovementType.Clamped;
            gameObject.SetActive(false);
        }
        public void Show(bool visible)
        {
            gameObject.SetActive(visible); if (!visible) return;
            transform.SetAsLastSibling(); LoadFields(); Refresh(); Canvas.ForceUpdateCanvases(); scroll.StopMovement(); scroll.verticalNormalizedPosition = 1;
        }
        private void LoadFields()
        {
            var c = controller.InverterControls; if (c == null) return;
            for (var i = 0; i < 2; i++)
            {
                ai[i, 0].text = Number(c.Inputs[i].SimulatedValue);
                for (var j = 1; j < 5; j++) ai[i, j].text = Number(c.Parameter("P" + (756 + j) + "." + i));
                scale[i].text = Number(c.Outputs[i].FullScale);
            }
            motorCurrent.text = Number(c.SimulatedMotorCurrent); RefreshLabels();
        }
        private void Apply()
        {
            try
            {
                var values = new float[2, 5]; var scales = new float[2];
                for (var i = 0; i < 2; i++)
                {
                    for (var j = 0; j < 5; j++) values[i, j] = Parse(ai[i, j].text);
                    if (Mathf.Abs(values[i, 1] - values[i, 3]) < 0.0001f) throw new ArgumentException("标定 X1 与 X2 不能相同。");
                    for (var j = 1; j < 5; j++)
                        if (values[i,j] < (j % 2 == 1 ? -50 : -1000) || values[i,j] > (j % 2 == 1 ? 160 : 1000)) throw new ArgumentException("标定超出范围：X 为 -50～160，Y 为 -1000～1000%。");
                    scales[i] = Parse(scale[i].text);
                    if (scales[i] < 0 || scales[i] > 210000 || i == 0 && scales[i] == 0) throw new ArgumentException("满量程必须为正数；AO1 可设 0 使用额定电流。");
                }
                var current = Parse(motorCurrent.text); if (current < 0 || current > 10000) throw new ArgumentException("模拟电机电流范围为 0～10000 A。");
                for (var i = 0; i < 2; i++)
                {
                    controller.InverterControls.Inputs[i].SimulatedValue = values[i, 0]; controller.InverterControls.Outputs[i].FullScale = scales[i];
                    for (var j = 1; j < 5; j++) controller.InverterPanel.TrySetParameter("P" + (756 + j) + "." + i, values[i, j]);
                }
                controller.InverterControls.SimulatedMotorCurrent = current; notice.text = "已应用。模拟值仅在选择“模拟输入”后生效。";
            }
            catch (ArgumentException e) { notice.text = e.Message; }
        }
        private void OnEnable() => Canvas.willRenderCanvases += RefreshLayout;
        private void OnDisable() => Canvas.willRenderCanvases -= RefreshLayout;
        private void Update() => Refresh();
        private void RefreshLayout()
        {
            if (details == null || content == null) return;
            var height = details.preferredHeight + 24;
            if (Mathf.Abs(details.rectTransform.sizeDelta.y - height) > .05f)
                details.rectTransform.sizeDelta = new Vector2(483, height);
            if (Mathf.Abs(content.sizeDelta.y - 790 - height) > .05f)
                content.sizeDelta = new Vector2(0, 790 + height);
        }
        private void Refresh()
        {
            if (controller.InverterControls == null) return;
            var c = controller.InverterControls;
            summary.text = "宏 " + controller.InverterPanel.Macro + "：" + controller.InverterPanel.ActiveMacroName + "\n控制供电：" +
                (c.Powered ? "正常" : "未供电") + "  ·  主电源：" + (c.MainSupply ? "正常" : "未接齐") +
                "  ·  " + controller.InverterPanel.ActualSpeedRpm.ToString("F1") + " rpm";
            details.text = controller.DescribeInverter(); RefreshLayout();
            RefreshLabels();
        }
        private void RefreshLabels()
        {
            var c = controller.InverterControls; if (c == null) return;
            var types = new[] { "0–10V", "2–10V", "0–20mA", "4–20mA", "±10V" };
            for (var i = 0; i < 2; i++)
            {
                sourceLabels[i].text = "AI" + i + "：" + (c.Inputs[i].Simulated ? "模拟输入" : "实际接线");
                typeLabels[i].text = "输入类型：" + types[c.InputType(i)] + "（点击切换）";
                aoLabels[i].text = "AO" + i + " 输出：" + (c.Outputs[i].Mode == G120AnalogOutputMode.Voltage10 ? "0–10V" : c.Outputs[i].Mode == G120AnalogOutputMode.Current20 ? "0–20mA" : "4–20mA") + "（点击切换）";
            }
            selectedLabel.text = "模拟调速来源：AI" + c.SelectedAnalogInput + "（点击切换）";
            ptcEnableLabel.text = "PTC 监测：" + (c.PtcEnabled ? "启用" : "关闭");
            ptcStateLabel.text = "模拟 PTC：" + new[] { "正常", "过热", "断线", "短路" }[(int)c.PtcState];
        }
        private static float Parse(string text)
        {
            if (!float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var value) || float.IsNaN(value) || float.IsInfinity(value)) throw new ArgumentException("请输入有效数字。");
            return value;
        }
        private static string Number(float value) => value.ToString("G", CultureInfo.InvariantCulture);
        private Text Label(Transform parent, string caption, float x, float y, float w, float h, int size = 15)
        {
            var text = new GameObject("Text", typeof(RectTransform), typeof(Text)).GetComponent<Text>(); text.transform.SetParent(parent, false);
            text.font = font; text.fontSize = size; text.color = new Color(0.87f, 0.92f, 0.95f); text.raycastTarget = false; text.supportRichText = false; text.text = caption;
            Place(text.rectTransform, x, y, w, h); return text;
        }
        private Text Button(Transform parent, string caption, float x, float y, float w, Action action)
        {
            var obj = new GameObject("Action", typeof(RectTransform), typeof(Image), typeof(Button)); obj.transform.SetParent(parent, false);
            Place((RectTransform)obj.transform, x, y, w, 34); obj.GetComponent<Image>().color = new Color(0.1f, 0.31f, 0.4f);
            var text = Label(obj.transform, caption, 0, 0, w, 34, 14); text.alignment = TextAnchor.MiddleCenter;
            var button = obj.GetComponent<Button>(); button.targetGraphic = obj.GetComponent<Image>(); button.onClick.AddListener(() => action()); return text;
        }
        private InputField Input(Transform parent, string name, float x, float y, float w)
        {
            var obj = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(InputField)); obj.transform.SetParent(parent, false); Place((RectTransform)obj.transform, x, y, w, 32);
            obj.GetComponent<Image>().color = new Color(0.12f, 0.2f, 0.25f);
            var text = Label(obj.transform, "", 8, 2, w - 16, 28); text.alignment = TextAnchor.MiddleLeft;
            var input = obj.GetComponent<InputField>(); input.textComponent = text; input.targetGraphic = obj.GetComponent<Image>(); input.contentType = InputField.ContentType.DecimalNumber; return input;
        }
        private static void Stretch(RectTransform r) { r.anchorMin = Vector2.zero; r.anchorMax = Vector2.one; r.offsetMin = r.offsetMax = Vector2.zero; }
        private static void Place(RectTransform r, float x, float y, float w, float h) { r.anchorMin = r.anchorMax = r.pivot = new Vector2(0, 1); r.anchoredPosition = new Vector2(x, -y); r.sizeDelta = new Vector2(w, h); }
    }
}
