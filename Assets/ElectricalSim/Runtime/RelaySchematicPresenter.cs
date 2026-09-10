using System;
using UnityEngine;
using UnityEngine.UI;

namespace ElectricalSim
{
    public sealed class RelaySchematicPresenter : MonoBehaviour
    {
        private Text title;
        private Texture2D relayTexture;
        public RawImage Diagram { get; private set; }
        public string Title => title != null ? title.text : string.Empty;

        public void Initialize(SimulationController controller, Canvas canvas, Font font)
        {
            var texture = Resources.Load<Texture2D>("RelaySchematic");
            if (texture == null) throw new InvalidOperationException("中间继电器原理图资源缺失：RelaySchematic");
            relayTexture = texture;
            transform.SetParent(canvas.transform, false);
            var rect = (RectTransform)transform;
            Place(rect, 16, 140, 280, 240);
            // Only the window blocks UI raycasts. There is no full-screen overlay.
            gameObject.AddComponent<Image>().color = new Color(0.035f, 0.07f, 0.10f, 0.98f);
            title = Label(transform, "Title", font, 14, 8, 6, 204, 24);
            var close = new GameObject("Close", typeof(RectTransform), typeof(Image), typeof(Button));
            close.transform.SetParent(transform, false);
            Place((RectTransform)close.transform, 220, 6, 52, 24);
            close.GetComponent<Image>().color = new Color(0.10f, 0.31f, 0.40f);
            var caption = Label(close.transform, "Text", font, 12, 0, 0, 52, 24);
            caption.text = "关闭";
            caption.alignment = TextAnchor.MiddleCenter;
            var button = close.GetComponent<Button>();
            button.targetGraphic = close.GetComponent<Image>();
            button.onClick.AddListener(controller.HideRelaySchematic);
            var frame = new GameObject("DiagramFrame", typeof(RectTransform), typeof(Image));
            frame.transform.SetParent(transform, false);
            Place((RectTransform)frame.transform, 8, 36, 264, 196);
            frame.GetComponent<Image>().color = Color.white;
            frame.GetComponent<Image>().raycastTarget = false;
            var image = new GameObject("Diagram", typeof(RectTransform), typeof(RawImage), typeof(AspectRatioFitter));
            image.transform.SetParent(frame.transform, false);
            Diagram = image.GetComponent<RawImage>();
            Diagram.texture = texture;
            Diagram.color = Color.white;
            Diagram.raycastTarget = false;
            var fitter = image.GetComponent<AspectRatioFitter>();
            fitter.aspectMode = AspectRatioFitter.AspectMode.FitInParent;
            fitter.aspectRatio = (float)texture.width / texture.height;
            gameObject.SetActive(false);
        }

        public void Show(IntermediateRelayView view)
            => Show(view.Runtime.DeviceId + " · 中间继电器原理图", relayTexture);

        public void Show(string caption, Texture2D texture)
        {
            title.text = caption;
            Diagram.texture = texture;
            Diagram.GetComponent<AspectRatioFitter>().aspectRatio = (float)texture.width / texture.height;
            transform.SetAsLastSibling();
            gameObject.SetActive(true);
        }

        public void Hide() => gameObject.SetActive(false);

        private static Text Label(Transform parent, string name, Font font, int size,
            float x, float y, float width, float height)
        {
            var obj = new GameObject(name, typeof(RectTransform), typeof(Text));
            obj.transform.SetParent(parent, false);
            var text = obj.GetComponent<Text>();
            text.font = font;
            text.fontSize = size;
            text.color = new Color(0.87f, 0.92f, 0.95f);
            text.alignment = TextAnchor.MiddleLeft;
            text.raycastTarget = false;
            text.supportRichText = false;
            Place(text.rectTransform, x, y, width, height);
            return text;
        }

        private static void Place(RectTransform rect, float x, float y, float width, float height)
        {
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0, 1);
            rect.anchoredPosition = new Vector2(x, -y);
            rect.sizeDelta = new Vector2(width, height);
        }
    }
}
