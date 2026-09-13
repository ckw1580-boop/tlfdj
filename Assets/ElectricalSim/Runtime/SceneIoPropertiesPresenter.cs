using System;
using System.Globalization;
using UnityEngine;
using UnityEngine.UI;

namespace ElectricalSim
{
    public sealed class SceneIoPropertiesPresenter : MonoBehaviour
    {
        private SimulationController controller;
        private SceneIoView view;
        private Text details, error;
        private RectTransform content, settings;
        private InputField[] inputs;
        private Button apply, reset;
        private ScrollRect scroll;
        public string DisplayedText => details != null ? details.text : "";
        public void Initialize(SimulationController source, Canvas canvas, Font font)
        {
            controller = source;
            transform.SetParent(canvas.transform, false);
            var panel = (RectTransform)transform;
            panel.anchorMin = new Vector2(1, 0.08f); panel.anchorMax = new Vector2(1, 0.94f);
            panel.pivot = new Vector2(1, 0.5f); panel.offsetMin = new Vector2(-560, 0); panel.offsetMax = new Vector2(-16, 0);
            gameObject.AddComponent<Image>().color = new Color(0.035f, 0.07f, 0.10f, 0.98f);
            var heading = Label(transform, font, 18); heading.text = "场景器件属性"; Place(heading.rectTransform, 16, 14, 400, 30);
            MakeButton(transform, font, "关闭", 452, 12, 72, () => controller.SelectSceneIo(null));
            var scrollObj = new GameObject("Scroll", typeof(RectTransform), typeof(Image), typeof(ScrollRect));
            scrollObj.transform.SetParent(transform, false);
            var rect = (RectTransform)scrollObj.transform; Stretch(rect); rect.offsetMin = new Vector2(16, 16); rect.offsetMax = new Vector2(-16, -56);
            scrollObj.GetComponent<Image>().color = new Color(0.02f, 0.035f, 0.05f);
            var viewport = new GameObject("Viewport", typeof(RectTransform), typeof(RectMask2D));
            viewport.transform.SetParent(scrollObj.transform, false); Stretch((RectTransform)viewport.transform);
            content = new GameObject("Content", typeof(RectTransform)).GetComponent<RectTransform>(); content.SetParent(viewport.transform, false);
            content.anchorMin = new Vector2(0, 1); content.anchorMax = Vector2.one; content.pivot = new Vector2(0.5f, 1); content.sizeDelta = Vector2.zero;
            details = Label(content, font, 16); details.lineSpacing = 1.25f; details.verticalOverflow = VerticalWrapMode.Overflow;
            settings = new GameObject("Liquid settings", typeof(RectTransform)).GetComponent<RectTransform>(); settings.SetParent(content, false);
            inputs = new InputField[4];
            var labels = new[] { "初始液位（%）", "泵1标定注满时间（秒）", "泵2标定注满时间（秒）", "重力排空时间（秒）" };
            for (var i = 0; i < inputs.Length; i++)
            {
                var label = Label(settings, font, 15); label.text = labels[i]; Place(label.rectTransform, 0, i * 46, 300, 32);
                var input = new GameObject("Value" + i, typeof(RectTransform), typeof(Image), typeof(InputField)); input.transform.SetParent(settings, false);
                Place((RectTransform)input.transform, 320, i * 46, 156, 34);
                input.GetComponent<Image>().color = new Color(0.12f, 0.2f, 0.25f);
                var text = Label(input.transform, font, 16); Stretch(text.rectTransform); text.rectTransform.offsetMin = new Vector2(8, 2); text.rectTransform.offsetMax = new Vector2(-8, -2);
                text.alignment = TextAnchor.MiddleLeft;
                inputs[i] = input.GetComponent<InputField>(); inputs[i].textComponent = text;
                inputs[i].targetGraphic = input.GetComponent<Image>(); inputs[i].contentType = InputField.ContentType.DecimalNumber;
            }
            apply = MakeButton(settings, font, "应用配置", 0, 192, 160, Apply);
            reset = MakeButton(settings, font, "重置液位", 180, 192, 160, () => Run(() => controller.ResetLiquid()));
            error = Label(settings, font, 14); error.color = new Color(1, 0.7f, 0.3f); Place(error.rectTransform, 0, 238, 476, 66);
            scroll = scrollObj.GetComponent<ScrollRect>(); scroll.viewport = (RectTransform)viewport.transform; scroll.content = content;
            scroll.horizontal = false; scroll.scrollSensitivity = 30; scroll.movementType = ScrollRect.MovementType.Clamped;
            gameObject.SetActive(false);
        }
        public void Show(SceneIoView selected)
        {
            view = selected; gameObject.SetActive(view != null);
            if (view == null) return;
            transform.SetAsLastSibling(); error.text = "";
            var config = controller.Liquid.Configuration;
            var values = new[] { config.InitialLevelPercent, config.Pump1FillSeconds, config.Pump2FillSeconds, config.DrainSeconds };
            for (var i = 0; i < inputs.Length; i++) inputs[i].text = values[i].ToString("G", CultureInfo.InvariantCulture);
            Refresh(); Canvas.ForceUpdateCanvases(); scroll.StopMovement(); scroll.verticalNormalizedPosition = 1;
        }
        private void Update() => Refresh();
        private void Refresh()
        {
            if (view == null) return;
            details.text = controller.DescribeSceneIo(view.Id);
            Place(details.rectTransform, 10, 8, 482, 400);
            var height = details.preferredHeight + 20;
            details.rectTransform.sizeDelta = new Vector2(482, height);
            var tank = view.Id == "TANK"; settings.gameObject.SetActive(tank);
            Place(settings, 10, height + 12, 482, 312);
            content.sizeDelta = new Vector2(0, height + (tank ? 340 : 20));
            var editable = controller.Mode != SimulationMode.Simulate && !controller.IsFileOperationActive;
            foreach (var input in inputs) input.interactable = editable;
            apply.interactable = reset.interactable = editable;
        }
        private void Apply() => Run(() =>
        {
            var values = new float[4];
            for (var i = 0; i < values.Length; i++)
                if (!float.TryParse(inputs[i].text, NumberStyles.Float, CultureInfo.InvariantCulture, out values[i])) throw new ArgumentException("请输入有效数字。");
            controller.ConfigureLiquid(new LiquidConfiguration { InitialLevelPercent = values[0], Pump1FillSeconds = values[1], Pump2FillSeconds = values[2], DrainSeconds = values[3] });
        });
        private void Run(Action action)
        { try { action(); error.text = "已应用。"; } catch (Exception ex) { error.text = ex.Message; } }
        private static Text Label(Transform parent, Font font, int size)
        {
            var label = new GameObject("Text", typeof(RectTransform), typeof(Text)).GetComponent<Text>(); label.transform.SetParent(parent, false);
            label.font = font; label.fontSize = size; label.color = new Color(0.87f, 0.92f, 0.95f); label.raycastTarget = false; label.supportRichText = false;
            return label;
        }
        private static Button MakeButton(Transform parent, Font font, string caption, float x, float y, float width, Action callback)
        {
            var obj = new GameObject(caption, typeof(RectTransform), typeof(Image), typeof(Button)); obj.transform.SetParent(parent, false);
            Place((RectTransform)obj.transform, x, y, width, 34); obj.GetComponent<Image>().color = new Color(0.1f, 0.31f, 0.4f);
            var text = Label(obj.transform, font, 15); text.text = caption; text.alignment = TextAnchor.MiddleCenter; Stretch(text.rectTransform);
            var button = obj.GetComponent<Button>(); button.targetGraphic = obj.GetComponent<Image>(); button.onClick.AddListener(() => callback()); return button;
        }
        private static void Stretch(RectTransform rect)
        { rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one; rect.offsetMin = rect.offsetMax = Vector2.zero; }
        private static void Place(RectTransform rect, float x, float y, float width, float height)
        { rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0, 1); rect.anchoredPosition = new Vector2(x, -y); rect.sizeDelta = new Vector2(width, height); }
    }
}
