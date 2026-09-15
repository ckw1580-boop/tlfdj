using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace ElectricalSim
{
    // All dimensions and drag/zoom coordinates are in viewport-local canvas units.
    public sealed class SchematicZoomViewport : MonoBehaviour, IScrollHandler, IBeginDragHandler, IDragHandler
    {
        private RectTransform viewport;
        private Canvas rootCanvas;
        private Image diagram;
        private Vector2 fittedSize;
        private Vector2 lastViewportSize;
        private Vector2 lastPixelSize;
        private Vector2 dragPoint;
        private bool dragging;
        private Text scaleLabel;

        public float Zoom { get; private set; } = 1f;
        public Image Diagram => diagram;
        public RectTransform Viewport => viewport;

        public void Initialize(Image image, Text label)
        {
            viewport = (RectTransform)transform;
            rootCanvas = GetComponentInParent<Canvas>().rootCanvas;
            diagram = image;
            scaleLabel = label;
            diagram.rectTransform.anchorMin = diagram.rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
            diagram.rectTransform.pivot = new Vector2(0.5f, 0.5f);
        }

        public void Show(Sprite sprite)
        {
            diagram.sprite = sprite;
            Fit();
        }

        public void Fit()
        {
            if (diagram == null || diagram.sprite == null) return;
            lastViewportSize = viewport.rect.size;
            lastPixelSize = rootCanvas.pixelRect.size;
            if (lastViewportSize.x <= 0f || lastViewportSize.y <= 0f) return;
            var size = diagram.sprite.rect.size;
            fittedSize = size * Mathf.Min(lastViewportSize.x / size.x, lastViewportSize.y / size.y);
            Zoom = 1f;
            diagram.rectTransform.sizeDelta = fittedSize;
            diagram.rectTransform.anchoredPosition = Vector2.zero;
            dragging = false;
            RefreshLabel();
        }

        private void OnRectTransformDimensionsChange()
        {
            if (gameObject.activeInHierarchy && SizeChanged()) Fit();
        }

        private void LateUpdate()
        {
            if (SizeChanged()) Fit();
        }

        // CanvasScaler can keep canvas-unit dimensions unchanged during a same-aspect resize.
        private bool SizeChanged() => viewport != null && rootCanvas != null &&
            (viewport.rect.size != lastViewportSize || rootCanvas.pixelRect.size != lastPixelSize);

        public void ZoomIn() => SetZoom(Zoom * 1.25f, Vector2.zero);
        public void ZoomOut() => SetZoom(Zoom / 1.25f, Vector2.zero);

        public void SetZoom(float value, Vector2 focus)
        {
            if (diagram == null || diagram.sprite == null) return;
            if (SizeChanged()) Fit();
            var next = Mathf.Clamp(value, 1f, 4f);
            var rect = diagram.rectTransform;
            var position = focus - (focus - rect.anchoredPosition) * (next / Zoom);
            Zoom = next;
            rect.sizeDelta = fittedSize * Zoom;
            rect.anchoredPosition = ClampPosition(position);
            RefreshLabel();
        }

        public void OnScroll(PointerEventData eventData)
        {
            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(viewport, eventData.position,
                    eventData.enterEventCamera, out var focus)) return;
            SetZoom(Zoom * Mathf.Pow(1.2f, eventData.scrollDelta.y), focus);
        }

        public void OnBeginDrag(PointerEventData eventData)
        {
            dragging = eventData.button == PointerEventData.InputButton.Left &&
                RectTransformUtility.ScreenPointToLocalPointInRectangle(viewport, eventData.position,
                    eventData.pressEventCamera, out dragPoint);
        }

        public void OnDrag(PointerEventData eventData)
        {
            if (!dragging || eventData.button != PointerEventData.InputButton.Left) return;
            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(viewport, eventData.position,
                    eventData.pressEventCamera, out var next)) return;
            diagram.rectTransform.anchoredPosition = ClampPosition(diagram.rectTransform.anchoredPosition + next - dragPoint);
            dragPoint = next;
        }

        private Vector2 ClampPosition(Vector2 position)
        {
            var limit = Vector2.Max(Vector2.zero, (diagram.rectTransform.sizeDelta - viewport.rect.size) * 0.5f);
            return new Vector2(Mathf.Clamp(position.x, -limit.x, limit.x), Mathf.Clamp(position.y, -limit.y, limit.y));
        }

        private void RefreshLabel()
        {
            if (scaleLabel != null) scaleLabel.text = $"{Zoom:0.00}×";
        }
    }
}
