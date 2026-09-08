using System;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace ElectricalSim
{
    public sealed class UnityWiringFileDialogs : IWiringFileDialogs
    {
        private readonly Canvas canvas;
        private readonly Font font;

        public UnityWiringFileDialogs(Canvas canvas, Font font)
        {
            this.canvas = canvas;
            this.font = font;
        }

        public string ChooseOpen(string directory) => WindowsFileDialog.OpenCc3d(directory);
        public string ChooseSave(string directory, string fileName) => WindowsFileDialog.SaveCc3d(directory, fileName);

        public void ConfirmUnsaved(Action<UnsavedWiringChoice> completed)
        {
            var overlay = new GameObject("UnsavedWiringDialog", typeof(RectTransform), typeof(Canvas),
                typeof(GraphicRaycaster), typeof(Image));
            overlay.transform.SetParent(canvas.transform, false);
            var modalCanvas = overlay.GetComponent<Canvas>();
            modalCanvas.overrideSorting = true;
            modalCanvas.sortingOrder = 32760;
            Fill(overlay.GetComponent<RectTransform>());
            overlay.GetComponent<Image>().color = new Color(0, 0, 0, 0.65f);

            var panel = new GameObject("Panel", typeof(RectTransform), typeof(Image), typeof(Outline));
            panel.transform.SetParent(overlay.transform, false);
            var rect = panel.GetComponent<RectTransform>();
            rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = new Vector2(700, 270);
            panel.GetComponent<Image>().color = new Color(0.08f, 0.19f, 0.25f, 1);
            panel.GetComponent<Outline>().effectColor = new Color(0.15f, 0.75f, 0.95f);
            var heading = Label(panel.transform, "Title", "接线尚未保存", 27);
            Place(heading.rectTransform, new Vector2(24, 193), new Vector2(676, 245));
            var message = Label(panel.transform, "Message", "当前接线有未保存的修改。打开另一份文件前，请选择如何处理。", 21);
            Place(message.rectTransform, new Vector2(24, 104), new Vector2(676, 183));
            var resolved = false;
            Action<UnsavedWiringChoice> finish = choice =>
            {
                if (resolved) return;
                resolved = true;
                overlay.SetActive(false);
                UnityEngine.Object.Destroy(overlay);
                EventSystem.current?.SetSelectedGameObject(null);
                completed(choice);
            };
            var save = MakeButton(panel.transform, "SaveThenOpen", "保存后打开", 24, () => finish(UnsavedWiringChoice.Save));
            var discard = MakeButton(panel.transform, "DiscardThenOpen", "放弃修改并打开", 246, () => finish(UnsavedWiringChoice.Discard));
            var cancel = MakeButton(panel.transform, "CancelOpen", "取消", 468, () => finish(UnsavedWiringChoice.Cancel));
            var buttons = new[] { save, discard, cancel };
            for (var i = 0; i < buttons.Length; i++)
            {
                buttons[i].navigation = new Navigation
                {
                    mode = Navigation.Mode.Explicit,
                    selectOnLeft = buttons[(i + 2) % 3],
                    selectOnRight = buttons[(i + 1) % 3],
                    selectOnUp = buttons[i],
                    selectOnDown = buttons[i]
                };
                var cancelEvent = new EventTrigger.Entry { eventID = EventTriggerType.Cancel };
                cancelEvent.callback.AddListener(_ => finish(UnsavedWiringChoice.Cancel));
                buttons[i].gameObject.AddComponent<EventTrigger>().triggers.Add(cancelEvent);
            }
            EventSystem.current?.SetSelectedGameObject(cancel.gameObject);
        }

        private Button MakeButton(Transform parent, string name, string caption, float left, Action clicked)
        {
            var obj = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(Button));
            obj.transform.SetParent(parent, false);
            Place(obj.GetComponent<RectTransform>(), new Vector2(left, 26), new Vector2(left + 208, 81));
            obj.GetComponent<Image>().color = new Color(0.15f, 0.38f, 0.46f);
            var button = obj.GetComponent<Button>();
            button.targetGraphic = obj.GetComponent<Image>();
            button.onClick.AddListener(() => clicked());
            var text = Label(obj.transform, "Label", caption, 21);
            Fill(text.rectTransform);
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
