using System;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace ElectricalSim
{
    public sealed class HomePagePresenter : MonoBehaviour, ICancelHandler
    {
        public const string Announcement =
            "一个无聊的人所作的作品；一个运气不好的人所作的作品；一个希望此软件对你有所帮助的人所作的作品。\n\n" +
            "也不要小看这个作品，毕竟作者耗费了很多精力投入，作者都快成地中海了，距离成为真正的“聪明绝顶”只差一步之遥，就仓库版本在内测期间都有40多个，后续检测肯定还会更新版本。\n\n" +
            "但公告内容可不会更新，到时候上正式版想起来就更新，想不起来就是你们看到的这段内容。也希望在对你以后的工作有所帮助可能是学弟又或是陌生人，这也是我创造这个软件的初心。\n\n" +
            "最后我祝你们成为“雷电法王”，当然这是玩笑，不管是工作还是学习亦或是生活请注意用电安全。祝你生活愉快";

        private static readonly Color Background = new Color(0.025f, 0.055f, 0.13f);
        private static readonly Color Card = new Color(0.045f, 0.105f, 0.20f);
        private static readonly Color Cyan = new Color(0.10f, 0.78f, 0.96f);
        private static readonly Color Muted = new Color(0.69f, 0.79f, 0.88f);
        private Font font;
        private GameObject page;
        private ScrollRect announcementScroll;

        public bool IsOpen => page != null && page.activeSelf;
        public event Action<bool> VisibilityChanged;

        public void Initialize(Canvas canvas, Font uiFont)
        {
            if (page != null) return;
            font = uiFont;
            var root = Panel("HomePage", canvas.transform, Background);
            Stretch(root, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
            page = root.gameObject;
            var layer = page.AddComponent<Canvas>();
            layer.overrideSorting = true;
            layer.sortingOrder = canvas.sortingOrder + 2000;
            page.AddComponent<GraphicRaycaster>();

            var content = Rect("HomeContent", root);
            Stretch(content, new Vector2(0.12f, 0), new Vector2(0.88f, 1), new Vector2(0, 56), new Vector2(0, -56));
            var title = Label("HomeTitle", content, "首页", 42, Color.white);
            Top(title.rectTransform, 0, 0, 280, 64);
            var product = Label("HomeProductTitle", content, "电气控制系统仿真软件", 20, Muted);
            Top(product.rectTransform, 0, 73, 560, 32);

            var back = Panel("ReturnToTraining", content, new Color(0.045f, 0.25f, 0.36f));
            Stretch(back, Vector2.one, Vector2.one, new Vector2(-204, -64), Vector2.zero);
            var button = back.gameObject.AddComponent<Button>();
            button.targetGraphic = back.GetComponent<Image>();
            button.navigation = new Navigation { mode = Navigation.Mode.None };
            var colors = button.colors;
            colors.highlightedColor = new Color(0.6f, 0.95f, 1f);
            colors.pressedColor = new Color(0.35f, 0.72f, 0.85f);
            button.colors = colors;
            button.onClick.AddListener(Close);
            var backLabel = Label("ReturnLabel", back, "返回实训", 24, Color.white);
            backLabel.alignment = TextAnchor.MiddleCenter;
            Stretch(backLabel.rectTransform, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
            var rule = Panel("HomeHeaderRule", content, Cyan);
            Stretch(rule, new Vector2(0, 1), Vector2.one, new Vector2(0, -126), new Vector2(0, -124));

            Contact(content, "AuthorCard", 0, 0.5f, 0, -12, "作者：汪成康", "邮箱：1873408329@qq.com");
            Contact(content, "TeacherCard", 0.5f, 1, 12, 0, "指导老师：李宁", "邮箱：nli1161@163.com");

            var notice = Panel("AnnouncementCard", content, Card);
            Stretch(notice, Vector2.zero, Vector2.one, new Vector2(0, 42), new Vector2(0, -336));
            var heading = Label("AnnouncementHeading", notice, "公告", 30, Cyan);
            Top(heading.rectTransform, 32, 24, 180, 48);
            var scrollRoot = Rect("AnnouncementScroll", notice);
            Stretch(scrollRoot, Vector2.zero, Vector2.one, new Vector2(32, 28), new Vector2(-24, -92));
            announcementScroll = scrollRoot.gameObject.AddComponent<ScrollRect>();
            announcementScroll.horizontal = false;
            announcementScroll.movementType = ScrollRect.MovementType.Clamped;
            announcementScroll.scrollSensitivity = 48;

            var viewport = Panel("AnnouncementViewport", scrollRoot, Card);
            Stretch(viewport, Vector2.zero, Vector2.one, Vector2.zero, new Vector2(-28, 0));
            viewport.gameObject.AddComponent<RectMask2D>();
            var scrollContent = Rect("AnnouncementContent", viewport);
            scrollContent.pivot = new Vector2(0.5f, 1);
            Stretch(scrollContent, new Vector2(0, 1), Vector2.one, Vector2.zero, Vector2.zero);
            var layout = scrollContent.gameObject.AddComponent<VerticalLayoutGroup>();
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;
            // Leave room below the last baseline, including dynamic-font descenders.
            layout.padding.bottom = 26;
            var fitter = scrollContent.gameObject.AddComponent<ContentSizeFitter>();
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            var body = Label("AnnouncementText", scrollContent, Announcement, 26, new Color(0.9f, 0.94f, 0.98f));
            body.lineSpacing = 1.45f;
            body.horizontalOverflow = HorizontalWrapMode.Wrap;
            body.verticalOverflow = VerticalWrapMode.Overflow;
            announcementScroll.viewport = viewport;
            announcementScroll.content = scrollContent;

            var track = Panel("AnnouncementScrollbar", scrollRoot, new Color(0.08f, 0.17f, 0.26f));
            Stretch(track, new Vector2(1, 0), Vector2.one, new Vector2(-10, 0), Vector2.zero);
            var handle = Panel("ScrollbarHandle", track, Cyan);
            Stretch(handle, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
            var scrollbar = track.gameObject.AddComponent<Scrollbar>();
            scrollbar.handleRect = handle;
            scrollbar.targetGraphic = handle.GetComponent<Image>();
            scrollbar.direction = Scrollbar.Direction.BottomToTop;
            scrollbar.navigation = new Navigation { mode = Navigation.Mode.None };
            announcementScroll.verticalScrollbar = scrollbar;
            announcementScroll.verticalScrollbarVisibility = ScrollRect.ScrollbarVisibility.AutoHide;

            var hint = Label("HomeFooter", content, "按 Esc 返回实训", 18, Muted);
            Stretch(hint.rectTransform, Vector2.zero, new Vector2(1, 0), Vector2.zero, new Vector2(0, 26));
            hint.alignment = TextAnchor.MiddleRight;
            page.SetActive(false);
        }

        public void Open()
        {
            if (page == null || IsOpen) return;
            page.SetActive(true);
            page.transform.SetAsLastSibling();
            EventSystem.current?.SetSelectedGameObject(null);
            Canvas.ForceUpdateCanvases();
            LayoutRebuilder.ForceRebuildLayoutImmediate(announcementScroll.content);
            announcementScroll.StopMovement();
            announcementScroll.verticalNormalizedPosition = 1;
            VisibilityChanged?.Invoke(true);
        }

        public void Close()
        {
            if (!IsOpen) return;
            announcementScroll.StopMovement();
            page.SetActive(false);
            EventSystem.current?.SetSelectedGameObject(null);
            VisibilityChanged?.Invoke(false);
        }

        private void Update()
        {
            if (IsOpen && Input.GetKeyDown(KeyCode.Escape)) OnCancel(null);
        }

        public void OnCancel(BaseEventData eventData) => Close();
        private void OnDisable() => Close();
        private void OnDestroy()
        {
            Close();
            if (page != null) Destroy(page);
        }

        private void Contact(Transform parent, string name, float left, float right, float insetLeft,
            float insetRight, string person, string email)
        {
            var card = Panel(name, parent, Card);
            Stretch(card, new Vector2(left, 1), new Vector2(right, 1), new Vector2(insetLeft, -310), new Vector2(insetRight, -158));
            var accent = Panel("Accent", card, Cyan);
            Stretch(accent, Vector2.zero, new Vector2(0, 1), Vector2.zero, new Vector2(3, 0));
            var nameLabel = Label(name + "Name", card, person, 28, Color.white);
            Stretch(nameLabel.rectTransform, new Vector2(0, 1), Vector2.one, new Vector2(30, -68), new Vector2(-24, -24));
            var emailLabel = Label(name + "Email", card, email, 23, Muted);
            Stretch(emailLabel.rectTransform, new Vector2(0, 1), Vector2.one, new Vector2(30, -124), new Vector2(-24, -86));
        }

        private Text Label(string name, Transform parent, string value, int size, Color color)
        {
            var label = Rect(name, parent).gameObject.AddComponent<Text>();
            label.font = font;
            label.fontSize = size;
            label.text = value;
            label.color = color;
            label.alignment = TextAnchor.UpperLeft;
            label.supportRichText = false;
            label.raycastTarget = false;
            return label;
        }

        private static RectTransform Panel(string name, Transform parent, Color color)
        {
            var rect = Rect(name, parent);
            rect.gameObject.AddComponent<Image>().color = color;
            return rect;
        }

        private static RectTransform Rect(string name, Transform parent)
        {
            var rect = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
            rect.SetParent(parent, false);
            return rect;
        }

        private static void Top(RectTransform rect, float x, float y, float width, float height)
            => Stretch(rect, new Vector2(0, 1), new Vector2(0, 1), new Vector2(x, -y - height), new Vector2(x + width, -y));

        private static void Stretch(RectTransform rect, Vector2 min, Vector2 max, Vector2 offsetMin, Vector2 offsetMax)
        {
            rect.anchorMin = min;
            rect.anchorMax = max;
            rect.offsetMin = offsetMin;
            rect.offsetMax = offsetMax;
        }
    }
}
