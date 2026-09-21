using UnityEngine;
using UnityEngine.UI;

namespace ElectricalSim
{
    public sealed class FrontBreakerPropertiesPresenter : MonoBehaviour
    {
        private SimulationController controller;
        private FrontBreakerView view;
        private Text title, details;
        private ScrollRect scroll;
        private Button openButton, closeButton;
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
            button.onClick.AddListener(() => controller.SelectFrontBreaker(null));

            var scrollObject = new GameObject("FrontBreakerScroll", typeof(RectTransform), typeof(Image), typeof(ScrollRect));
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
            hint.text = "接线模式连接端子 · 仿真模式操作开合闸";
            openButton = ActionButton("Open", "开闸", 16, font, () => controller.SetFrontBreakerClosed(view, false));
            closeButton = ActionButton("CloseBreaker", "合闸", 138, font, () => controller.SetFrontBreakerClosed(view, true));
            scrollTransform.offsetMin = new Vector2(16, 78);
            gameObject.SetActive(false);
        }

        public void Show(FrontBreakerView selected)
        {
            view = selected;
            gameObject.SetActive(view != null);
            if (view == null) return;
            transform.SetAsLastSibling();
            title.text = view.Runtime.DeviceId + " · 四极断路器属性";
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
            openButton.interactable = controller.Mode == SimulationMode.Simulate && controller.CanOperateFrontBreakers && view.Runtime.IsClosed;
            closeButton.interactable = controller.Mode == SimulationMode.Simulate && controller.CanOperateFrontBreakers && !view.Runtime.IsClosed;
            var text = controller.DescribeFrontBreaker(view);
            if (details.text != text) details.text = text;
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
