using System;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace ElectricalSim
{
    public sealed class SchematicGalleryPresenter : MonoBehaviour, ICancelHandler
    {
        private SchematicCatalog catalog;
        private Font font;
        private Text title;
        private Text viewerTitle;
        private GameObject viewer;
        private AspectRatioFitter previewFitter;
        public int CurrentIndex { get; private set; }
        public SchematicPage CurrentPage => catalog.Pages[CurrentIndex];
        public int PageCount => catalog.Pages.Count;
        public string Caption => $"{CurrentPage.Number:00}/{PageCount:00}  {CurrentPage.Title}";
        public Image Preview { get; private set; }
        public SchematicZoomViewport ZoomViewport { get; private set; }
        public bool IsViewerOpen => viewer != null && viewer.activeSelf;
        public event Action<bool> ViewerVisibilityChanged;

        public void Initialize(Canvas canvas, Font uiFont, SchematicCatalog source)
        {
            catalog = source;
            catalog.Validate();
            font = uiFont;
            title = Label("SchematicTitle", transform, 20, TextAnchor.UpperLeft);
            Place(title.rectTransform, 12, 12, -12, 60);
            var previous = Button("PreviousSchematic", transform, "上一张", Previous);
            Place(previous.GetComponent<RectTransform>(), 12, 72, 97, 32, false);
            var next = Button("NextSchematic", transform, "下一张", Next);
            Place(next.GetComponent<RectTransform>(), 105, 72, 190, 32, false);
            var enlarge = Button("EnlargeSchematic", transform, "放大", OpenViewer);
            Place(enlarge.GetComponent<RectTransform>(), 198, 72, -12, 32);

            var frame = Rect("SchematicFrame", transform);
            Stretch(frame, Vector2.zero, Vector2.one, new Vector2(12, 12), new Vector2(-12, -116));
            frame.gameObject.AddComponent<Image>().color = Color.white;
            var open = frame.gameObject.AddComponent<Button>();
            open.targetGraphic = frame.GetComponent<Image>();
            open.transition = Selectable.Transition.None;
            open.onClick.AddListener(OpenViewer);
            var preview = Rect("TaskSchematic", frame);
            Preview = preview.gameObject.AddComponent<Image>();
            Preview.preserveAspect = true;
            Preview.raycastTarget = false;
            previewFitter = preview.gameObject.AddComponent<AspectRatioFitter>();
            previewFitter.aspectMode = AspectRatioFitter.AspectMode.FitInParent;

            BuildViewer(canvas);
            RefreshPage();
        }

        private void BuildViewer(Canvas canvas)
        {
            var overlay = Rect("SchematicViewer", canvas.transform);
            Stretch(overlay, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
            viewer = overlay.gameObject;
            // A separate sorting layer stays above device windows that bring themselves to front.
            var viewerCanvas = viewer.AddComponent<Canvas>();
            viewerCanvas.overrideSorting = true;
            viewerCanvas.sortingOrder = canvas.sortingOrder + 1000;
            viewer.AddComponent<GraphicRaycaster>();
            viewer.AddComponent<Image>().color = new Color(0f, 0f, 0f, 0.65f);

            var dialog = Rect("SchematicDialog", overlay);
            Stretch(dialog, new Vector2(0.05f, 0.05f), new Vector2(0.95f, 0.95f), Vector2.zero, Vector2.zero);
            dialog.gameObject.AddComponent<Image>().color = new Color(0.035f, 0.10f, 0.15f, 1f);
            viewerTitle = Label("ViewerTitle", dialog, 24, TextAnchor.MiddleLeft);
            Place(viewerTitle.rectTransform, 20, 8, -125, 42);
            var close = Button("CloseSchematicViewer", dialog, "关闭", CloseViewer);
            var closeRect = close.GetComponent<RectTransform>();
            Stretch(closeRect, Vector2.one, Vector2.one, new Vector2(-105, -46), new Vector2(-16, -10));

            string[] names = { "ViewerPrevious", "ViewerNext", "ZoomOut", "ZoomIn", "FitSchematic" };
            string[] captions = { "上一张", "下一张", "缩小 −", "放大 +", "适应窗口" };
            Action[] actions = { Previous, Next, () => ZoomViewport.ZoomOut(), () => ZoomViewport.ZoomIn(), () => ZoomViewport.Fit() };
            for (var i = 0; i < names.Length; i++)
            {
                var button = Button(names[i], dialog, captions[i], actions[i]);
                Place(button.GetComponent<RectTransform>(), 16 + i * 108, 60, 116 + i * 108, 36, false);
            }
            var scale = Label("SchematicZoomScale", dialog, 18, TextAnchor.MiddleLeft);
            Place(scale.rectTransform, 570, 60, -16, 36);
            var viewport = Rect("SchematicViewport", dialog);
            Stretch(viewport, Vector2.zero, Vector2.one, new Vector2(16, 16), new Vector2(-16, -110));
            viewport.gameObject.AddComponent<Image>().color = Color.white;
            viewport.gameObject.AddComponent<RectMask2D>();
            var diagram = Rect("EnlargedSchematic", viewport).gameObject.AddComponent<Image>();
            diagram.raycastTarget = false;
            diagram.preserveAspect = true;
            ZoomViewport = viewport.gameObject.AddComponent<SchematicZoomViewport>();
            ZoomViewport.Initialize(diagram, scale);
            viewer.SetActive(false);
        }

        public void Previous() => Select((CurrentIndex - 1 + PageCount) % PageCount);
        public void Next() => Select((CurrentIndex + 1) % PageCount);

        private void Select(int index)
        {
            CurrentIndex = index;
            RefreshPage();
        }

        private void RefreshPage()
        {
            title.text = Caption;
            viewerTitle.text = Caption;
            Preview.sprite = CurrentPage.Sprite;
            previewFitter.aspectRatio = CurrentPage.Sprite.rect.width / CurrentPage.Sprite.rect.height;
            if (IsViewerOpen) ZoomViewport.Show(CurrentPage.Sprite);
        }

        public void OpenViewer()
        {
            if (IsViewerOpen) return;
            viewer.SetActive(true);
            viewer.transform.SetAsLastSibling();
            EventSystem.current?.SetSelectedGameObject(null);
            Canvas.ForceUpdateCanvases();
            ZoomViewport.Show(CurrentPage.Sprite);
            ViewerVisibilityChanged?.Invoke(true);
        }

        public void CloseViewer()
        {
            if (!IsViewerOpen) return;
            viewer.SetActive(false);
            EventSystem.current?.SetSelectedGameObject(null);
            ViewerVisibilityChanged?.Invoke(false);
        }

        private void Update()
        {
            if (IsViewerOpen && Input.GetKeyDown(KeyCode.Escape)) OnCancel(null);
        }

        public void OnCancel(BaseEventData eventData) => CloseViewer();

        private void OnDisable() => CloseViewer();
        private void OnDestroy()
        {
            CloseViewer();
            if (viewer != null) Destroy(viewer);
        }

        private Text Label(string name, Transform parent, int size, TextAnchor alignment)
        {
            var label = Rect(name, parent).gameObject.AddComponent<Text>();
            label.font = font;
            label.fontSize = size;
            label.color = Color.white;
            label.alignment = alignment;
            label.supportRichText = false;
            label.raycastTarget = false;
            return label;
        }

        private Button Button(string name, Transform parent, string caption, Action action)
        {
            var rect = Rect(name, parent);
            var image = rect.gameObject.AddComponent<Image>();
            image.color = new Color(0.03f, 0.36f, 0.48f, 1f);
            var button = rect.gameObject.AddComponent<Button>();
            button.targetGraphic = image;
            button.navigation = new Navigation { mode = Navigation.Mode.None };
            button.onClick.AddListener(() => action());
            var label = Label("Text", rect, 17, TextAnchor.MiddleCenter);
            label.text = caption;
            Stretch(label.rectTransform, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
            return button;
        }

        private static RectTransform Rect(string name, Transform parent)
        {
            var rect = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
            rect.SetParent(parent, false);
            return rect;
        }

        private static void Place(RectTransform rect, float left, float top, float right, float height, bool stretch = true)
            => Stretch(rect, new Vector2(0, 1), new Vector2(stretch ? 1 : 0, 1), new Vector2(left, -top - height), new Vector2(right, -top));

        private static void Stretch(RectTransform rect, Vector2 min, Vector2 max, Vector2 low, Vector2 high)
        {
            rect.anchorMin = min;
            rect.anchorMax = max;
            rect.offsetMin = low;
            rect.offsetMax = high;
        }
    }
}
