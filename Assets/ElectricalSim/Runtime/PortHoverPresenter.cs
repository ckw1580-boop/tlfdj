using UnityEngine;
using UnityEngine.UI;

namespace ElectricalSim
{
    public sealed class PortHoverPresenter : MonoBehaviour
    {
        private RectTransform canvasRect;
        private RectTransform tooltipRect;
        private RectTransform leaderRect;
        private Text tooltipText;
        private ElectricalPortView currentPort;

        public bool IsVisible => gameObject.activeSelf;
        public string CurrentText => tooltipText != null ? tooltipText.text : string.Empty;
        public ElectricalPortView CurrentPort => currentPort;
        public Vector2 LeaderEndCanvasPosition { get; private set; }

        public void Initialize(Canvas canvas, Font font)
        {
            canvasRect = canvas.transform as RectTransform;
            transform.SetParent(canvas.transform, false);
            gameObject.name = "PortHoverPresenter";

            var leaderObject = new GameObject("Leader", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            leaderObject.transform.SetParent(transform, false);
            leaderRect = leaderObject.GetComponent<RectTransform>();
            leaderRect.pivot = new Vector2(0.5f, 0.5f);
            var leaderImage = leaderObject.GetComponent<Image>();
            leaderImage.color = new Color(1f, 0.9f, 0f, 1f);
            leaderImage.raycastTarget = false;

            var tooltipObject = new GameObject("Tooltip", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            tooltipObject.transform.SetParent(transform, false);
            tooltipRect = tooltipObject.GetComponent<RectTransform>();
            tooltipRect.anchorMin = tooltipRect.anchorMax = new Vector2(0.5f, 0.5f);
            tooltipRect.pivot = new Vector2(0f, 0.5f);
            var background = tooltipObject.GetComponent<Image>();
            background.color = new Color(0.12f, 0.15f, 0.17f, 0.9f);
            background.raycastTarget = false;

            var textObject = new GameObject("Text", typeof(RectTransform), typeof(CanvasRenderer), typeof(Text));
            textObject.transform.SetParent(tooltipObject.transform, false);
            tooltipText = textObject.GetComponent<Text>();
            tooltipText.font = font;
            tooltipText.fontSize = 22;
            tooltipText.alignment = TextAnchor.MiddleCenter;
            tooltipText.color = Color.white;
            tooltipText.raycastTarget = false;
            var textRect = tooltipText.rectTransform;
            textRect.anchorMin = Vector2.zero;
            textRect.anchorMax = Vector2.one;
            textRect.offsetMin = new Vector2(8f, 2f);
            textRect.offsetMax = new Vector2(-8f, -2f);

            gameObject.SetActive(false);
        }

        public void Present(ElectricalPortView port, Camera worldCamera, Vector2 pointerScreenPosition)
        {
            if (port == null || !port.IsVisible || worldCamera == null)
            {
                Hide();
                return;
            }

            var portScreen = worldCamera.WorldToScreenPoint(port.CurrentAnchorPosition);
            if (portScreen.z <= 0f || !RectTransformUtility.RectangleContainsScreenPoint(canvasRect, portScreen))
            {
                Hide();
                return;
            }

            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(canvasRect, pointerScreenPosition, null, out var pointerLocal) ||
                !RectTransformUtility.ScreenPointToLocalPointInRectangle(canvasRect, portScreen, null, out var portLocal))
            {
                Hide();
                return;
            }

            currentPort = port;
            tooltipText.text = port.HoverLabel;
            var width = Mathf.Clamp(tooltipText.preferredWidth + 20f, 108f, 260f);
            var size = new Vector2(width, 38f);
            tooltipRect.sizeDelta = size;

            var position = pointerLocal + new Vector2(14f, 20f);
            var canvasBounds = canvasRect.rect;
            if (position.x + size.x > canvasBounds.xMax - 8f) position.x = pointerLocal.x - size.x - 14f;
            position.x = Mathf.Clamp(position.x, canvasBounds.xMin + 8f, canvasBounds.xMax - size.x - 8f);
            position.y = Mathf.Clamp(position.y, canvasBounds.yMin + size.y * 0.5f + 8f, canvasBounds.yMax - size.y * 0.5f - 8f);
            tooltipRect.anchoredPosition = position;

            var tooltipConnection = new Vector2(
                Mathf.Clamp(portLocal.x, position.x, position.x + size.x),
                Mathf.Clamp(portLocal.y, position.y - size.y * 0.5f, position.y + size.y * 0.5f));
            DrawLeader(tooltipConnection, portLocal);
            LeaderEndCanvasPosition = portLocal;
            if (!gameObject.activeSelf) gameObject.SetActive(true);
            transform.SetAsLastSibling();
        }

        public void Hide()
        {
            currentPort = null;
            if (gameObject.activeSelf) gameObject.SetActive(false);
        }

        private void DrawLeader(Vector2 start, Vector2 end)
        {
            var delta = end - start;
            leaderRect.anchorMin = leaderRect.anchorMax = new Vector2(0.5f, 0.5f);
            leaderRect.anchoredPosition = (start + end) * 0.5f;
            leaderRect.sizeDelta = new Vector2(delta.magnitude, 2f);
            leaderRect.localEulerAngles = new Vector3(0f, 0f, Mathf.Atan2(delta.y, delta.x) * Mathf.Rad2Deg);
        }
    }
}
