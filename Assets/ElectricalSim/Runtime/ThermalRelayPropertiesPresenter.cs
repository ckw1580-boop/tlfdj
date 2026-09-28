using System;
using System.Globalization;
using UnityEngine;
using UnityEngine.UI;

namespace ElectricalSim
{
    public sealed class ThermalRelayPropertiesPresenter : MonoBehaviour
    {
        private SimulationController controller;
        private ThermalRelayView view;
        private Text title, details;
        private ScrollRect scroll;
        private Button tripButton, resetButton;
        private Button applyConfiguration;
        private InputField[] configurationInputs;
        private Text configurationStatus;
        public string DisplayedText => details != null ? details.text : string.Empty;

        public void Initialize(SimulationController source, Canvas canvas, Font font)
        {
            controller = source;
            transform.SetParent(canvas.transform, false);
            var rect = (RectTransform)transform;
            rect.anchorMin = new Vector2(1, 0.08f);
            rect.anchorMax = new Vector2(1, 0.94f);
            rect.pivot = new Vector2(1, 0.5f);
            rect.offsetMin = new Vector2(-560, 0);
            rect.offsetMax = new Vector2(-16, 0);
            gameObject.AddComponent<Image>().color = new Color(0.035f, 0.07f, 0.10f, 0.98f);
            title = Label(transform, "Title", font, 18);
            Place(title.rectTransform, 16, 14, 430, 32);
            var close = new GameObject("Close", typeof(RectTransform), typeof(Image), typeof(Button));
            close.transform.SetParent(transform, false);
            Place((RectTransform)close.transform, 474, 14, 54, 30);
            close.GetComponent<Image>().color = new Color(0.10f, 0.31f, 0.40f);
            var closeText = Label(close.transform, "Text", font, 14);
            Stretch(closeText.rectTransform);
            closeText.text = "关闭";
            closeText.alignment = TextAnchor.MiddleCenter;
            var button = close.GetComponent<Button>();
            button.targetGraphic = close.GetComponent<Image>();
            button.onClick.AddListener(() => controller.SelectThermalRelay(null));

            var scrollObject = new GameObject("ThermalRelayScroll", typeof(RectTransform), typeof(Image), typeof(ScrollRect));
            scrollObject.transform.SetParent(transform, false);
            var scrollTransform = (RectTransform)scrollObject.transform;
            Stretch(scrollTransform);
            scrollTransform.offsetMin = new Vector2(16, 40);
            scrollTransform.offsetMax = new Vector2(-16, -58);
            scrollObject.GetComponent<Image>().color = new Color(0.02f, 0.035f, 0.05f);
            var viewport = new GameObject("Viewport", typeof(RectTransform), typeof(RectMask2D));
            viewport.transform.SetParent(scrollObject.transform, false);
            var viewportRect = (RectTransform)viewport.transform;
            Stretch(viewportRect);
            viewportRect.offsetMin = new Vector2(10, 6);
            viewportRect.offsetMax = new Vector2(-20, -6);
            details = Label(viewport.transform, "Details", font, 16);
            details.alignment = TextAnchor.UpperLeft;
            details.lineSpacing = 1.3f;
            details.horizontalOverflow = HorizontalWrapMode.Wrap;
            details.verticalOverflow = VerticalWrapMode.Overflow;
            var contentRect = details.rectTransform;
            contentRect.anchorMin = new Vector2(0, 1);
            contentRect.anchorMax = Vector2.one;
            contentRect.pivot = new Vector2(0.5f, 1);
            contentRect.sizeDelta = Vector2.zero;
            contentRect.anchoredPosition = Vector2.zero;
            details.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            scroll = scrollObject.GetComponent<ScrollRect>();
            scroll.viewport = viewportRect;
            scroll.content = contentRect;
            scroll.horizontal = false;
            scroll.scrollSensitivity = 30;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            var bar = new GameObject("Scrollbar", typeof(RectTransform), typeof(Image), typeof(Scrollbar));
            bar.transform.SetParent(scrollObject.transform, false);
            var barRect = (RectTransform)bar.transform;
            barRect.anchorMin = new Vector2(1, 0);
            barRect.anchorMax = Vector2.one;
            barRect.offsetMin = new Vector2(-7, 0);
            barRect.offsetMax = Vector2.zero;
            bar.GetComponent<Image>().color = new Color(0.08f, 0.13f, 0.17f);
            var handle = new GameObject("Handle", typeof(RectTransform), typeof(Image));
            handle.transform.SetParent(bar.transform, false);
            Stretch((RectTransform)handle.transform);
            handle.GetComponent<Image>().color = new Color(0.24f, 0.52f, 0.61f);
            var scrollbar = bar.GetComponent<Scrollbar>();
            scrollbar.handleRect = (RectTransform)handle.transform;
            scrollbar.targetGraphic = handle.GetComponent<Image>();
            scrollbar.direction = Scrollbar.Direction.BottomToTop;
            scroll.verticalScrollbar = scrollbar;
            var hint = Label(transform, "Hint", font, 13);
            hint.rectTransform.anchorMin = hint.rectTransform.anchorMax = Vector2.zero;
            hint.rectTransform.pivot = Vector2.zero;
            hint.rectTransform.anchoredPosition = new Vector2(16, 8);
            hint.rectTransform.sizeDelta = new Vector2(512, 24);
            hint.text = "教学热曲线 · 仿真可模拟脱扣，冷却后才能复位";
            tripButton = ActionButton("Trip", "模拟脱扣", 16, font, () => controller.SetThermalRelayTripped(view, true));
            resetButton = ActionButton("Reset", "复位", 138, font, () => controller.SetThermalRelayTripped(view, false));
            var settings = new GameObject("Thermal settings", typeof(RectTransform)).GetComponent<RectTransform>();
            settings.SetParent(transform, false); settings.anchorMin = settings.anchorMax = settings.pivot = Vector2.zero;
            settings.anchoredPosition = new Vector2(16, 78); settings.sizeDelta = new Vector2(512, 204);
            configurationInputs = new InputField[3];
            var labels = new[] { "整定电流（A）", "热时间常数（秒）", "允许复位热状态（%）" };
            var names = new[] { "SettingCurrent", "TimeConstantSeconds", "ResetThresholdPercent" };
            for (var i = 0; i < 3; i++)
            {
                var label = Label(settings, names[i] + "Label", font, 15); label.text = labels[i]; Place(label.rectTransform, 0, i * 39, 300, 32);
                configurationInputs[i] = MotorConfigurationEditor.Input(settings, font, names[i], 320, i * 39, 174);
            }
            applyConfiguration = MotorConfigurationEditor.Button(settings, font, "应用保护配置", 0, 120, 170, ApplyConfiguration);
            configurationStatus = Label(settings, "ConfigurationStatus", font, 14); configurationStatus.color = new Color(1, .7f, .3f);
            Place(configurationStatus.rectTransform, 0, 160, 498, 42);
            scrollTransform.offsetMin = new Vector2(16, 292);
            gameObject.SetActive(false);
        }

        public void Show(ThermalRelayView selected)
        {
            view = selected;
            gameObject.SetActive(view != null);
            if (view == null) return;
            transform.SetAsLastSibling();
            title.text = view.Definition.Id + (view.IsRear ? " · 背部热继电器属性" : " · 热继电器属性");
            var config = view.Runtime.ThermalConfiguration;
            var values = new[] { config.SettingCurrent, config.TimeConstantSeconds, config.ResetThreshold * 100 };
            for (var i = 0; i < values.Length; i++) configurationInputs[i].text = values[i].ToString("G", CultureInfo.InvariantCulture);
            configurationStatus.text = "";
            Refresh();
            Canvas.ForceUpdateCanvases();
            scroll.StopMovement();
            scroll.verticalNormalizedPosition = 1;
        }

        private Button ActionButton(string name, string caption, float x, Font font, UnityEngine.Events.UnityAction action)
        {
            var obj = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(Button));
            obj.transform.SetParent(transform, false);
            var rect = (RectTransform)obj.transform;
            rect.anchorMin = rect.anchorMax = rect.pivot = Vector2.zero;
            rect.anchoredPosition = new Vector2(x, 40); rect.sizeDelta = new Vector2(110, 30);
            obj.GetComponent<Image>().color = new Color(0.10f, 0.31f, 0.40f);
            var label = Label(obj.transform, "Text", font, 14); Stretch(label.rectTransform);
            label.text = caption; label.alignment = TextAnchor.MiddleCenter;
            var button = obj.GetComponent<Button>(); button.targetGraphic = obj.GetComponent<Image>();
            button.onClick.AddListener(action); return button;
        }
        private void Update() => Refresh();
        private void Refresh()
        {
            if (view == null) return;
            tripButton.interactable = controller.Mode == SimulationMode.Simulate && !view.Runtime.IsTripped;
            resetButton.interactable = controller.Mode == SimulationMode.Simulate && view.Runtime.IsTripped && view.Runtime.ThermalState.CanReset;
            var editable = controller.Mode != SimulationMode.Simulate && !controller.IsFileOperationActive;
            foreach (var input in configurationInputs) input.interactable = editable;
            applyConfiguration.interactable = editable;
            var text = controller.DescribeThermalRelay(view);
            if (details.text != text) details.text = text;
        }

        private void ApplyConfiguration()
        {
            try
            {
                var values = new float[3];
                for (var i = 0; i < values.Length; i++)
                    if (!float.TryParse(configurationInputs[i].text, NumberStyles.Float, CultureInfo.InvariantCulture, out values[i]) || float.IsNaN(values[i]) || float.IsInfinity(values[i]))
                        throw new ArgumentException("请输入有效数字。");
                controller.ConfigureThermalRelay(view, new ThermalRelayConfiguration
                { SettingCurrent = values[0], TimeConstantSeconds = values[1], ResetThreshold = values[2] / 100 });
                configurationStatus.text = "已应用教学保护参数。";
            }
            catch (Exception exception) { configurationStatus.text = exception.Message; }
        }

        private static Text Label(Transform parent, string name, Font font, int size)
        {
            var obj = new GameObject(name, typeof(RectTransform), typeof(Text));
            obj.transform.SetParent(parent, false);
            var label = obj.GetComponent<Text>();
            label.font = font;
            label.fontSize = size;
            label.color = new Color(0.87f, 0.92f, 0.95f);
            label.raycastTarget = false;
            label.supportRichText = false;
            return label;
        }
        private static void Stretch(RectTransform rect)
        { rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one; rect.offsetMin = rect.offsetMax = Vector2.zero; }
        private static void Place(RectTransform rect, float x, float y, float width, float height)
        {
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0, 1);
            rect.anchoredPosition = new Vector2(x, -y);
            rect.sizeDelta = new Vector2(width, height);
        }
    }
}
