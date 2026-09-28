using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;
using UnityEngine.UI;

namespace ElectricalSim
{
    // Embedded in the shared scene properties panel so a pump and its motor never
    // produce competing selections or two copies of editable motor configuration.
    public sealed class MotorConfigurationEditor : MonoBehaviour
    {
        private sealed class Field
        {
            public InputField Input;
            public Func<MotorConfiguration, float> Read;
            public Action<MotorConfiguration, float> Write;
            public bool HighOnly, Integer;
        }
        private readonly List<Field> fields = new List<Field>();
        private SimulationController controller;
        private string motorId;
        private InputField load;
        private Text status, stallCaption;
        private Button apply, setLoad, stall;
        public float Height { get; private set; }

        public void Initialize(SimulationController source, Font font)
        {
            controller = source;
            var title = Label(transform, font, 17); title.text = "电机配置 · 教学值（非仿真模式可编辑）"; Place(title.rectTransform, 0, 0, 476, 30);
            var loadLabel = Label(transform, font, 15); loadLabel.text = "机械负载（低速额定转矩 %）"; Place(loadLabel.rectTransform, 0, 40, 296, 30);
            load = Input(transform, font, "LoadPercent", 316, 40, 150);
            setLoad = Button(transform, font, "设置负载", 0, 82, 144, ApplyLoad);
            stall = Button(transform, font, "模拟堵转", 164, 82, 144, ToggleStall);
            stallCaption = stall.GetComponentInChildren<Text>();
            var hint = Label(transform, font, 14); hint.text = "负载可实时调整；堵转注入仅用于本次仿真，不写入工程。"; Place(hint.rectTransform, 0, 124, 476, 42);
            Add(font, "额定频率（Hz）", "RatedFrequencyHz", c => c.RatedFrequencyHz, (c,v) => c.RatedFrequencyHz = v);
            Add(font, "额定线电压 Δ／双速（V）", "RatedVoltageDelta", c => c.RatedVoltageDelta, (c,v) => c.RatedVoltageDelta = v);
            Add(font, "额定星形线电压（V）", "RatedVoltageStar", c => c.RatedVoltageStar, (c,v) => c.RatedVoltageStar = v);
            Add(font, "单速／低速极数", "PoleCount", c => c.PoleCount, (c,v) => c.PoleCount = (int)v, false, true);
            Add(font, "单速／低速额定转速（rpm）", "RatedSpeedRpm", c => c.RatedSpeedRpm, (c,v) => c.RatedSpeedRpm = v);
            Add(font, "单速／低速额定功率（kW）", "RatedPowerKw", c => c.RatedPowerKw, (c,v) => c.RatedPowerKw = v);
            Add(font, "单速／低速额定电流（A）", "RatedCurrentAmps", c => c.RatedCurrentAmps, (c,v) => c.RatedCurrentAmps = v);
            Add(font, "高速极数（仅双速）", "HighPoleCount", c => c.HighPoleCount, (c,v) => c.HighPoleCount = (int)v, true, true);
            Add(font, "高速额定转速（rpm，仅双速）", "HighRatedSpeedRpm", c => c.HighRatedSpeedRpm, (c,v) => c.HighRatedSpeedRpm = v, true);
            Add(font, "高速额定功率（kW，仅双速）", "HighRatedPowerKw", c => c.HighRatedPowerKw, (c,v) => c.HighRatedPowerKw = v, true);
            Add(font, "高速额定电流（A，仅双速）", "HighRatedCurrentAmps", c => c.HighRatedCurrentAmps, (c,v) => c.HighRatedCurrentAmps = v, true);
            Add(font, "转动惯量（kg·m²）", "InertiaKgM2", c => c.InertiaKgM2, (c,v) => c.InertiaKgM2 = v);
            Add(font, "绕组电阻（Ω）", "WindingResistanceOhms", c => c.WindingResistanceOhms, (c,v) => c.WindingResistanceOhms = v);
            Add(font, "最大转矩／额定转矩", "MaximumTorqueMultiple", c => c.MaximumTorqueMultiple, (c,v) => c.MaximumTorqueMultiple = v);
            Add(font, "最大转矩对应转差率（0–1）", "BreakdownSlip", c => c.BreakdownSlip, (c,v) => c.BreakdownSlip = v);
            Add(font, "启动转矩／额定转矩", "StartingTorqueMultiple", c => c.StartingTorqueMultiple, (c,v) => c.StartingTorqueMultiple = v);
            Add(font, "启动电流／额定电流", "StartingCurrentMultiple", c => c.StartingCurrentMultiple, (c,v) => c.StartingCurrentMultiple = v);
            Add(font, "电机热时间常数（秒）", "ThermalTimeConstantSeconds", c => c.ThermalTimeConstantSeconds, (c,v) => c.ThermalTimeConstantSeconds = v);
            var bottom = 174 + fields.Count * 43;
            apply = Button(transform, font, "应用电机铭牌", 0, bottom, 170, Apply);
            status = Label(transform, font, 14); status.color = new Color(1, .7f, .3f); Place(status.rectTransform, 0, bottom + 42, 476, 72);
            Height = bottom + 122;
        }

        private void Add(Font font, string caption, string name, Func<MotorConfiguration,float> read, Action<MotorConfiguration,float> write, bool high = false, bool integer = false)
        {
            var y = 174 + fields.Count * 43;
            var label = Label(transform, font, 15); label.text = caption; Place(label.rectTransform, 0, y, 310, 34);
            fields.Add(new Field { Input = Input(transform, font, name, 316, y, 150), Read = read, Write = write, HighOnly = high, Integer = integer });
        }

        public void Show(string id)
        {
            motorId = id; gameObject.SetActive(id != null);
            if (id == null) return;
            var config = controller.GetMotorConfiguration(id);
            foreach (var field in fields) field.Input.text = field.Read(config).ToString("G", CultureInfo.InvariantCulture);
            load.text = (config.LoadFactor * 100).ToString("G", CultureInfo.InvariantCulture);
            status.text = ""; Refresh();
        }

        public void Refresh()
        {
            if (motorId == null) return;
            var editable = controller.Mode != SimulationMode.Simulate && !controller.IsFileOperationActive;
            var config = controller.GetMotorConfiguration(motorId);
            foreach (var field in fields) field.Input.interactable = editable && (!field.HighOnly || config.IsTwoSpeed);
            apply.interactable = editable;
            load.interactable = setLoad.interactable = !controller.IsFileOperationActive;
            stall.interactable = controller.Mode == SimulationMode.Simulate && !controller.IsFileOperationActive;
            stallCaption.text = controller.GetMotorRuntimeState(motorId).IsStalled ? "解除堵转" : "模拟堵转";
        }

        private static float Parse(InputField input)
        {
            if (!float.TryParse(input.text, NumberStyles.Float, CultureInfo.InvariantCulture, out var value) || float.IsNaN(value) || float.IsInfinity(value))
                throw new ArgumentException("请输入有效数字：" + input.name);
            return value;
        }

        private void Apply() => Run(() =>
        {
            var config = controller.GetMotorConfiguration(motorId);
            foreach (var field in fields)
            {
                if (field.HighOnly && !config.IsTwoSpeed) continue;
                var value = Parse(field.Input);
                if (field.Integer && (value < 2 || value > 100 || value != Math.Floor(value))) throw new ArgumentException("极数必须为2至100的偶数。");
                field.Write(config, value);
            }
            config.LoadFactor = Parse(load) / 100;
            controller.ConfigureMotor(motorId, config);
        });
        private void ApplyLoad() => Run(() => controller.SetMotorLoad(motorId, Parse(load) / 100));
        private void ToggleStall() => Run(() => controller.SetMotorStalled(motorId, !controller.GetMotorRuntimeState(motorId).IsStalled));
        private void Run(Action action)
        {
            try { action(); status.text = "已应用。"; }
            catch (Exception exception) { status.text = exception.Message; }
            Refresh();
        }

        internal static InputField Input(Transform parent, Font font, string name, float x, float y, float width)
        {
            var obj = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(InputField)); obj.transform.SetParent(parent, false);
            Place((RectTransform)obj.transform, x, y, width, 34); obj.GetComponent<Image>().color = new Color(.12f, .2f, .25f);
            var text = Label(obj.transform, font, 16); Stretch(text.rectTransform); text.rectTransform.offsetMin = new Vector2(8, 2); text.rectTransform.offsetMax = new Vector2(-8, -2); text.alignment = TextAnchor.MiddleLeft;
            var input = obj.GetComponent<InputField>(); input.textComponent = text; input.targetGraphic = obj.GetComponent<Image>(); input.contentType = InputField.ContentType.DecimalNumber;
            return input;
        }
        internal static Button Button(Transform parent, Font font, string caption, float x, float y, float width, Action action)
        {
            var obj = new GameObject(caption, typeof(RectTransform), typeof(Image), typeof(Button)); obj.transform.SetParent(parent, false);
            Place((RectTransform)obj.transform, x, y, width, 34); obj.GetComponent<Image>().color = new Color(.1f, .31f, .4f);
            var text = Label(obj.transform, font, 15); Stretch(text.rectTransform); text.text = caption; text.alignment = TextAnchor.MiddleCenter;
            var button = obj.GetComponent<Button>(); button.targetGraphic = obj.GetComponent<Image>(); button.onClick.AddListener(() => action()); return button;
        }
        internal static Text Label(Transform parent, Font font, int size)
        {
            var obj = new GameObject("Text", typeof(RectTransform), typeof(Text)); obj.transform.SetParent(parent, false);
            var label = obj.GetComponent<Text>(); label.font = font; label.fontSize = size; label.color = new Color(.87f, .92f, .95f); label.raycastTarget = false; label.supportRichText = false; return label;
        }
        internal static void Stretch(RectTransform rect)
        { rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one; rect.offsetMin = rect.offsetMax = Vector2.zero; }
        internal static void Place(RectTransform rect, float x, float y, float width, float height)
        { rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0, 1); rect.anchoredPosition = new Vector2(x, -y); rect.sizeDelta = new Vector2(width, height); }
    }
}
