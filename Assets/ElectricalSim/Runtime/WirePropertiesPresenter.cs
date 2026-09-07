using System;
using UnityEngine;
using UnityEngine.UI;

namespace ElectricalSim
{
    public sealed class WirePropertiesPresenter : MonoBehaviour
    {
        private SimulationController controller;
        private Text status;
        private GameObject content;
        private Image swatch;
        private Text colorLabel;
        private Text terminals;
        private Action expand;

        public void Initialize(SimulationController source, Text statusLabel, Action expandPanel)
        {
            controller = source;
            status = statusLabel;
            expand = expandPanel;
            content = new GameObject("WireProperties", typeof(RectTransform));
            content.transform.SetParent(transform, false);
            Place(content.GetComponent<RectTransform>(), Vector2.zero, Vector2.one, new Vector2(16, 10), new Vector2(-16, -10));
            var title = CreateText("WirePropertiesTitle", "导线属性", 18);
            Place(title.rectTransform, new Vector2(0, 1), Vector2.one, new Vector2(0, -24), Vector2.zero);
            var patch = new GameObject("WireColorSwatch", typeof(RectTransform), typeof(Image));
            patch.transform.SetParent(content.transform, false);
            swatch = patch.GetComponent<Image>();
            swatch.raycastTarget = false;
            Place(swatch.rectTransform, new Vector2(0, 1), new Vector2(0, 1), new Vector2(0, -51), new Vector2(20, -31));
            colorLabel = CreateText("WireColorLabel", "", 17);
            Place(colorLabel.rectTransform, new Vector2(0, 1), Vector2.one, new Vector2(28, -55), new Vector2(0, -29));
            terminals = CreateText("WireTerminalLabels", "", 16);
            Place(terminals.rectTransform, Vector2.zero, Vector2.one, Vector2.zero, new Vector2(0, -59));
            terminals.resizeTextForBestFit = true;
            terminals.resizeTextMinSize = 12;
            terminals.resizeTextMaxSize = 16;
            controller.SelectedWireChanged += OnSelection;
            controller.StatusChanged += OnStatus;
            OnSelection(controller.SelectedWire);
        }

        private Text CreateText(string name, string value, int size)
        {
            var obj = new GameObject(name, typeof(RectTransform), typeof(Text));
            obj.transform.SetParent(content.transform, false);
            var text = obj.GetComponent<Text>();
            text.font = status.font;
            text.fontSize = size;
            text.color = new Color(1f, 0.88f, 0.2f);
            text.alignment = TextAnchor.UpperLeft;
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            text.supportRichText = false;
            text.raycastTarget = false;
            text.text = value;
            return text;
        }

        private static void Place(RectTransform rect, Vector2 min, Vector2 max, Vector2 low, Vector2 high)
        {
            rect.anchorMin = min;
            rect.anchorMax = max;
            rect.offsetMin = low;
            rect.offsetMax = high;
        }

        public static string ColorName(Color color)
        {
            var hex = ColorUtility.ToHtmlStringRGB(color);
            switch (hex)
            {
                case "FF0000": return "红色";
                case "FFFF00": return "黄色";
                case "00FF00": return "绿色";
                case "0000FF": return "蓝色";
                case "00FFFF": return "青色";
                case "FF00FF": return "品红色";
                case "FFFFFF": return "白色";
                case "000000": return "黑色";
                default: return "#" + hex;
            }
        }

        private void OnSelection(WireConnection wire)
        {
            content.SetActive(wire != null);
            status.gameObject.SetActive(wire == null);
            if (wire == null) return;
            swatch.color = wire.Color;
            colorLabel.text = "颜色：" + ColorName(wire.Color);
            terminals.text = "起点：" + controller.ResolveWireTerminalName(wire.StartPort) +
                             "\n终点：" + controller.ResolveWireTerminalName(wire.EndPort);
            expand?.Invoke();
        }

        private void OnStatus(string message, bool error)
        {
            var showProperties = controller.SelectedWire != null && !error;
            content.SetActive(showProperties);
            status.gameObject.SetActive(!showProperties);
            if (error) expand?.Invoke();
        }

        private void OnDestroy()
        {
            if (controller == null) return;
            controller.SelectedWireChanged -= OnSelection;
            controller.StatusChanged -= OnStatus;
        }
    }
}
