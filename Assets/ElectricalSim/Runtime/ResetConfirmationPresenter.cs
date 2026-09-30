using System;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace ElectricalSim
{
    public sealed class ResetConfirmationPresenter : MonoBehaviour, ICancelHandler
    {
        private GameObject overlay;
        private Button confirm;
        private Button cancel;
        private Font font;
        private Action confirmed;

        public bool IsOpen => overlay != null && overlay.activeSelf;
        public event Action<bool> VisibilityChanged;

        public void Initialize(Canvas canvas, Font uiFont)
        {
            if (overlay != null) return;
            font = uiFont;
            overlay = new GameObject("ResetConfirmationDialog", typeof(RectTransform), typeof(Canvas),
                typeof(GraphicRaycaster), typeof(Image));
            overlay.transform.SetParent(canvas.transform, false);
            var layer = overlay.GetComponent<Canvas>();
            layer.overrideSorting = true;
            layer.sortingOrder = 32760;
            Fill(overlay.GetComponent<RectTransform>());
            overlay.GetComponent<Image>().color = new Color(0, 0, 0, 0.65f);

            var panel = new GameObject("Panel", typeof(RectTransform), typeof(Image), typeof(Outline));
            panel.transform.SetParent(overlay.transform, false);
            var rect = panel.GetComponent<RectTransform>();
            rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = new Vector2(560, 240);
            panel.GetComponent<Image>().color = new Color(0.08f, 0.19f, 0.25f, 1);
            panel.GetComponent<Outline>().effectColor = new Color(0.15f, 0.75f, 0.95f);
            var message = Label(panel.transform, "ResetConfirmationMessage", "是否确认重置", 27);
            Place(message.rectTransform, new Vector2(24, 110), new Vector2(536, 212));
            confirm = MakeButton(panel.transform, "ConfirmReset", "确认", 48, () => Finish(true));
            cancel = MakeButton(panel.transform, "CancelReset", "取消", 304, () => Finish(false));
            foreach (var button in new[] { confirm, cancel })
            {
                var other = button == confirm ? cancel : confirm;
                button.navigation = new Navigation
                {
                    mode = Navigation.Mode.Explicit,
                    selectOnLeft = other,
                    selectOnRight = other,
                    selectOnUp = button,
                    selectOnDown = button
                };
                var cancelEvent = new EventTrigger.Entry { eventID = EventTriggerType.Cancel };
                cancelEvent.callback.AddListener(data => OnCancel(data));
                button.gameObject.AddComponent<EventTrigger>().triggers.Add(cancelEvent);
            }
            overlay.SetActive(false);
        }

        public void Open(Action onConfirmed)
        {
            if (overlay == null || IsOpen || !isActiveAndEnabled) return;
            confirmed = onConfirmed;
            overlay.SetActive(true);
            overlay.transform.SetAsLastSibling();
            VisibilityChanged?.Invoke(true);
            EventSystem.current?.SetSelectedGameObject(cancel.gameObject);
        }

        private void Finish(bool shouldReset)
        {
            if (!IsOpen) return;
            var callback = confirmed;
            confirmed = null;
            overlay.SetActive(false);
            EventSystem.current?.SetSelectedGameObject(null);
            VisibilityChanged?.Invoke(false);
            if (shouldReset) callback?.Invoke();
        }

        private void Update()
        {
            if (!IsOpen) return;
            if (Input.GetKeyDown(KeyCode.Escape)) { OnCancel(null); return; }
            var events = EventSystem.current;
            if (events != null && events.currentSelectedGameObject != confirm.gameObject &&
                events.currentSelectedGameObject != cancel.gameObject)
                events.SetSelectedGameObject(cancel.gameObject);
        }

        public void OnCancel(BaseEventData eventData) => Finish(false);
        private void OnDisable() => Finish(false);
        private void OnDestroy()
        {
            Finish(false);
            if (overlay != null) Destroy(overlay);
        }

        private Button MakeButton(Transform parent, string name, string caption, float left, Action clicked)
        {
            var obj = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(Button));
            obj.transform.SetParent(parent, false);
            Place(obj.GetComponent<RectTransform>(), new Vector2(left, 28), new Vector2(left + 208, 83));
            obj.GetComponent<Image>().color = new Color(0.15f, 0.38f, 0.46f);
            var button = obj.GetComponent<Button>();
            button.targetGraphic = obj.GetComponent<Image>();
            button.onClick.AddListener(() => clicked());
            Fill(Label(obj.transform, "Label", caption, 21).rectTransform);
            return button;
        }

        private Text Label(Transform parent, string name, string value, int size)
        {
            var obj = new GameObject(name, typeof(RectTransform), typeof(Text));
            obj.transform.SetParent(parent, false);
            var text = obj.GetComponent<Text>();
            text.font = font;
            text.fontSize = size;
            text.color = Color.white;
            text.text = value;
            text.alignment = TextAnchor.MiddleCenter;
            text.raycastTarget = false;
            text.supportRichText = false;
            return text;
        }

        private static void Fill(RectTransform rect)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = rect.offsetMax = Vector2.zero;
        }

        private static void Place(RectTransform rect, Vector2 low, Vector2 high)
        {
            rect.anchorMin = rect.anchorMax = Vector2.zero;
            rect.offsetMin = low;
            rect.offsetMax = high;
        }
    }
}
