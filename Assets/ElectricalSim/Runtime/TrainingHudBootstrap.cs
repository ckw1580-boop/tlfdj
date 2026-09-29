using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace ElectricalSim
{
    public sealed partial class TrainingSceneBootstrap
    {
        private HudReferences CreateHud()
        {
            if (EventSystem.current == null)
            {
                var eventSystem = new GameObject("EventSystem");
                eventSystem.AddComponent<EventSystem>();
                eventSystem.AddComponent<StandaloneInputModule>();
            }

            var canvasObject = new GameObject("Simulation HUD");
            var canvas = canvasObject.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 100;
            var scaler = canvasObject.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;
            canvasObject.AddComponent<GraphicRaycaster>();

            var top = Panel("TopBar", canvas.transform, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0f, -88f), Vector2.zero, darkBlue);
            var title = Label("Title", top.transform, "电气控制系统仿真软件 | Unity 个人项目", 26, TextAnchor.MiddleLeft, Color.white);
            SetRect(title.rectTransform, new Vector2(0f, 0f), new Vector2(0.34f, 1f), new Vector2(30f, 0f), new Vector2(-10f, 0f));

            var mode = Label("Mode", top.transform, "当前模式：视角", 22, TextAnchor.MiddleCenter, cyan);
            SetRect(mode.rectTransform, new Vector2(0.34f, 0f), new Vector2(0.48f, 1f), Vector2.zero, Vector2.zero);

            // Leave 20 canvas units between the task panel and each existing left panel:
            // the device schematic ends 380 units below the top; status ends 180 above the bottom.
            var taskPanel = Panel("TaskPanel", canvas.transform, Vector2.zero, new Vector2(0f, 1f), new Vector2(8f, 200f), new Vector2(305f, -400f), panelBlue);
            var gallery = taskPanel.gameObject.AddComponent<SchematicGalleryPresenter>();
            gallery.Initialize(canvas, uiFont, SchematicCatalog.Load());
            var instrument = Label("InstrumentReadout", taskPanel.transform, "万用表：请选择两个端子", 17, TextAnchor.UpperLeft, new Color(1f, 0.9f, 0.28f));
            SetRect(instrument.rectTransform, new Vector2(0f, 0f), new Vector2(1f, 0.42f), new Vector2(18f, 156f), new Vector2(-18f, -8f));

            var statusPanel = Panel("StatusPanel", canvas.transform, new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(8f, 18f), new Vector2(305f, 180f), new Color(0.12f, 0.28f, 0.29f, 0.94f));
            var status = Label("Status", statusPanel.transform, "系统就绪", 19, TextAnchor.MiddleLeft, new Color(1f, 0.88f, 0.2f));
            SetRect(status.rectTransform, Vector2.zero, Vector2.one, new Vector2(20f, 5f), new Vector2(-20f, -5f));

            taskPanelSlide = ConfigureSlidePanel(taskPanel, "TaskPanelSlideHandle", new Vector2(-305f, 0f), true, "◀", "▶");
            statusPanelSlide = ConfigureSlidePanel(statusPanel, "StatusPanelSlideHandle", new Vector2(-305f, 0f), true, "◀", "▶");

            var hoverObject = new GameObject("PortHoverPresenter");
            var portHover = hoverObject.AddComponent<PortHoverPresenter>();
            portHover.Initialize(canvas, uiFont);

            var references = new HudReferences { Canvas = canvas, Top = top, TaskPanel = taskPanel, Mode = mode, Gallery = gallery, Status = status, Instrument = instrument, PortHover = portHover };
            if (originalVisuals != null && originalVisuals.ResolveUi("TopNavigation") != null)
            {
                top.gameObject.SetActive(false);
                instrument.gameObject.SetActive(false);
            }
            if (showMissingAssetNotice && originalVisuals == null)
            {
                var notice = Label("AssetNotice", canvas.transform, "功能验证场景 · 原始 Assets 子集尚未导入", 18, TextAnchor.MiddleCenter, new Color(1f, 0.78f, 0.18f));
                SetRect(notice.rectTransform, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0f, -118f), new Vector2(-390f, -90f));
            }
            return references;
        }

        private void BindUi(HudReferences ui)
        {
            instrumentTools = new GameObject("InstrumentTools", typeof(RectTransform));
            instrumentTools.transform.SetParent(ui.Canvas.transform, false);
            var instrumentToolsRect = instrumentTools.GetComponent<RectTransform>();
            var instrumentGroupAnchor = new Vector2(0.6f, 0.77f);
            const float instrumentGroupWidth = 342f;
            const float instrumentGroupHeight = 110f;
            var instrumentGroupOffset = new Vector2(instrumentGroupWidth * 1.75f, -instrumentGroupHeight * 2f);
            SetRect(instrumentToolsRect, instrumentGroupAnchor, instrumentGroupAnchor,
                instrumentGroupOffset - new Vector2(instrumentGroupWidth * 0.5f, instrumentGroupHeight * 0.5f),
                instrumentGroupOffset + new Vector2(instrumentGroupWidth * 0.5f, instrumentGroupHeight * 0.5f));

            var modes = new[] { SimulationMode.View, SimulationMode.Drag, SimulationMode.Wiring, SimulationMode.Simulate, SimulationMode.Fault };
            for (var i = 0; i < modes.Length; i++)
            {
                var captured = modes[i];
                var button = Button("Mode_" + captured, ui.Top.transform, ModeLabel(captured), () =>
                {
                    if (captured == SimulationMode.Fault) ToggleInstrumentTools();
                    else controller.SetMode(captured);
                });
                SetRect(button.GetComponent<RectTransform>(), new Vector2(0f, 0f), new Vector2(0f, 1f), new Vector2(930f + i * 112f, 14f), new Vector2(1032f + i * 112f, -14f));
            }

            var open = Button("Open", ui.Top.transform, "打开接线", controller.OpenCc3d);
            SetRect(open.GetComponent<RectTransform>(), Vector2.zero, Vector2.zero, new Vector2(1500f, 14f), new Vector2(1592f, 74f));
            var save = Button("Save", ui.Top.transform, "保存接线", controller.SaveCc3d);
            SetRect(save.GetComponent<RectTransform>(), Vector2.zero, Vector2.zero, new Vector2(1600f, 14f), new Vector2(1692f, 74f));
            var reset = Button("Reset", ui.Top.transform, "重置", controller.ResetTraining);
            SetRect(reset.GetComponent<RectTransform>(), Vector2.zero, Vector2.zero, new Vector2(1700f, 14f), new Vector2(1792f, 74f));

            var instruments = new[] { InstrumentKind.Multimeter, InstrumentKind.VoltageProbe, InstrumentKind.Oscilloscope, InstrumentKind.Tachometer };
            for (var i = 0; i < instruments.Length; i++)
            {
                var captured = instruments[i];
                var button = Button("Instrument_" + captured, instrumentTools.transform, InstrumentLabel(captured), () => controller.SelectInstrument(captured));
                var row = i / 2;
                var col = i % 2;
                SetRect(button.GetComponent<RectTransform>(), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                    new Vector2(-171f + col * 176f, -55f + row * 60f),
                    new Vector2(-5f + col * 176f, -5f + row * 60f));
            }
            instrumentTools.SetActive(false);
        }

        private void BindOriginalUi(HudReferences ui)
        {
            if (originalVisuals == null) return;
            var navigation = InstantiateUi("TopNavigation", ui.Canvas.transform);
            var toolbar = InstantiateUi("ExperimentToolbar", ui.Canvas.transform);
            if (navigation != null)
            {
                BindNamedButton(navigation, "homeBtn", ToggleTaskPanel);
                BindNamedButton(navigation, "scheduleBtn", ToggleTaskPanel);
                BindNamedButton(navigation, "saveBtn", controller.SaveCc3d);
                BindNamedButton(navigation, "resetBtn", controller.ResetTraining);
                SetNamedButtonActive(navigation, "saveBtn", true);
                SetNamedButtonActive(navigation, "submitBtn", false);
                SetNamedButtonActive(navigation, "downloadBtn", false);
                SetNamedButtonActive(navigation, "mineBtn", false);
                SetNamedButtonText(navigation, "scheduleBtn", "原理图");
                SetNamedButtonText(navigation, "saveBtn", "保存接线");
                SetNamedButtonText(navigation, "resetBtn", "重置");
                foreach (var id in new[] { "EditorBtn_A", "EditorBtn_B", "EditorBtn_C", "EditorBtn_D" })
                {
                    var examButton = FindNamed(navigation, id);
                    if (examButton != null) examButton.gameObject.SetActive(false);
                }
            }
            if (toolbar != null)
            {
                BindOriginalViewMenu(toolbar);
                BindNamedButton(toolbar, "btn_paigu", ToggleInstrumentTools);
                BindNamedButton(toolbar, "btn_drag", () => controller.SetMode(SimulationMode.Drag));
                BindNamedButton(toolbar, "btn_line", () => controller.SetMode(SimulationMode.Wiring));
                BindNamedButton(toolbar, "btn_sim", () => controller.SetMode(SimulationMode.Simulate));
                BindNamedButton(toolbar, "btn_resume", controller.ResetTraining);
                BindNamedButton(toolbar, "btn_snapshot", captureRecorder.CaptureScreenshot);
                // Imported object names do not match their icons: btn_submit
                // carries the original "打开" folder sprite; btn_resume is reset.
                BindNamedButton(toolbar, "btn_submit", controller.OpenCc3d);
                // Keep the C/A toolbar icons in place without save actions.
                // Saving is available through the navigation's saveBtn.
                SetNamedButtonActive(toolbar, "btn_record", false);
                SetNamedButtonActive(toolbar, "btn_help", false);
                var helpSeparator = toolbar.GetComponentsInChildren<Transform>(true)
                    .FirstOrDefault(item => item.name == "split_help");
                if (helpSeparator != null) helpSeparator.gameObject.SetActive(false);
                BindNamedButton(toolbar, "btn_audio", () => AudioListener.pause = !AudioListener.pause);
            }

            var lineForm = InstantiateUi("LineForm", ui.Canvas.transform);
            var lineParam = InstantiateUi("LineParam", ui.Canvas.transform);
            var inverterPanel = InstantiateUi("Inverter", ui.Canvas.transform);
            if (lineForm != null)
            {
                PositionLineFormBelowToolbar(lineForm, toolbar);
                BindOriginalLineForm(lineForm);
                lineForm.SetActive(false);
            }
            if (lineParam != null) lineParam.SetActive(false);
            if (inverterPanel != null)
            {
                inverterPanel.SetActive(false);
                var inverterControl = ui.Canvas.gameObject.AddComponent<InverterPanelController>();
                inverterControl.Initialize(inverterPanel, () => inverterPanel.SetActive(false));

                var inverterModel = originalEnvironmentTransforms?.FirstOrDefault(item =>
                    string.Equals(item.name, "Inverte", StringComparison.Ordinal) &&
                    item.gameObject.activeInHierarchy);
                if (inverterModel != null)
                {
                    controller.RegisterInverterPanel(inverterModel, inverterControl, visible =>
                    {
                        inverterPanel.SetActive(visible);
                        if (visible) inverterPanel.transform.SetAsLastSibling();
                    });
                    controller.CreateInverterProperties(ui.Canvas, uiFont);
                }
                else
                {
                    Debug.LogWarning("[OfflineBootstrap] Original inverter model is missing; its control panel cannot be opened.");
                }
            }
            controller.ModeChanged += mode =>
            {
                if (lineForm != null) lineForm.SetActive(mode == SimulationMode.Wiring);
                // The original property window opens only for a selected wire.
                if (lineParam != null) lineParam.SetActive(false);
            };

            var ticker = ui.Canvas.gameObject.AddComponent<OfflineUiTicker>();
            ticker.Initialize(navigation, toolbar);

            void ToggleTaskPanel() => ToggleSlidePanel(taskPanelSlide);
        }

        private SlidePanelState ConfigureSlidePanel(
            RectTransform panel,
            string handleName,
            Vector2 collapsedOffset,
            bool handleOnRight,
            string expandedGlyph,
            string collapsedGlyph)
        {
            var state = new SlidePanelState
            {
                Panel = panel,
                ExpandedPosition = panel.anchoredPosition,
                CollapsedPosition = panel.anchoredPosition + collapsedOffset,
                ExpandedGlyph = expandedGlyph,
                CollapsedGlyph = collapsedGlyph
            };
            var handle = Button(handleName, panel.transform, expandedGlyph, () => ToggleSlidePanel(state), new Color(0.03f, 0.36f, 0.48f, 0.98f));
            var handleAnchor = new Vector2(handleOnRight ? 1f : 0f, 0.5f);
            SetRect(handle.GetComponent<RectTransform>(), handleAnchor, handleAnchor,
                new Vector2(handleOnRight ? 0f : -36f, -28f),
                new Vector2(handleOnRight ? 36f : 0f, 28f));
            state.HandleLabel = handle.GetComponentInChildren<Text>(true);
            return state;
        }

        private void ToggleSlidePanel(SlidePanelState state)
        {
            if (state == null || state.Panel == null) return;
            state.IsCollapsed = !state.IsCollapsed;
            if (state.Animation != null) StopCoroutine(state.Animation);
            if (state.HandleLabel != null)
                state.HandleLabel.text = state.IsCollapsed ? state.CollapsedGlyph : state.ExpandedGlyph;
            var target = state.IsCollapsed ? state.CollapsedPosition : state.ExpandedPosition;
            state.Animation = StartCoroutine(AnimateSlidePanel(state, target));
        }

        private IEnumerator AnimateSlidePanel(SlidePanelState state, Vector2 target)
        {
            var start = state.Panel.anchoredPosition;
            var elapsed = 0f;
            while (elapsed < PanelSlideDuration)
            {
                elapsed += Time.unscaledDeltaTime;
                var progress = Mathf.Clamp01(elapsed / PanelSlideDuration);
                progress = progress * progress * (3f - 2f * progress);
                state.Panel.anchoredPosition = Vector2.LerpUnclamped(start, target, progress);
                yield return null;
            }
            state.Panel.anchoredPosition = target;
            state.Animation = null;
        }

        private void ToggleInstrumentTools()
        {
            if (instrumentTools == null) return;
            var shouldShow = !instrumentTools.activeSelf;
            instrumentTools.SetActive(shouldShow);
            if (motorFaultBlocks != null) motorFaultBlocks.SetActive(shouldShow);
            if (shouldShow && controller != null && controller.Mode != SimulationMode.Fault)
                controller.SetMode(SimulationMode.Fault);
            if (!shouldShow && controller != null && controller.Mode == SimulationMode.Fault)
                controller.SetMode(SimulationMode.View);
        }

        private static void PositionLineFormBelowToolbar(GameObject lineForm, GameObject toolbar)
        {
            var lineRect = lineForm.GetComponent<RectTransform>();
            var toolbarRect = toolbar != null ? toolbar.GetComponent<RectTransform>() : null;
            if (lineRect == null || toolbarRect == null) return;

            const float verticalGap = 56f;
            var toolbarBottom = toolbarRect.anchoredPosition.y - toolbarRect.rect.height * toolbarRect.pivot.y;
            var lineTop = toolbarBottom - verticalGap + lineRect.rect.height * 0.5f;
            lineRect.anchorMin = new Vector2(0.5f, 1f);
            lineRect.anchorMax = new Vector2(0.5f, 1f);
            lineRect.anchoredPosition = new Vector2(
                lineRect.anchoredPosition.x,
                lineTop - lineRect.rect.height * (1f - lineRect.pivot.y));
        }

        private void BindOriginalViewMenu(GameObject toolbar)
        {
            var cameraController = Camera.main != null ? Camera.main.GetComponent<TrainingCameraController>() : null;
            if (cameraController == null) return;
            var menu = toolbar.GetComponentsInChildren<Transform>(true).FirstOrDefault(item => item.name == "twoChange");
            BindNamedButton(toolbar, "btn_viewChange", () =>
            {
                if (menu != null) menu.gameObject.SetActive(!menu.gameObject.activeSelf);
            });
            BindButtonByText(menu, "接线视角", cameraController.SetWiringView);
            BindButtonByText(menu, "排故视角", cameraController.SetFaultView);
            BindButtonByText(menu, "重置视角", cameraController.ResetView);
            if (menu != null) menu.gameObject.SetActive(false);
        }

        private void BindOriginalLineForm(GameObject lineForm)
        {
            var lineTypeObject = lineForm.GetComponentsInChildren<Transform>(true).FirstOrDefault(item => item.name == "LineType");
            var lineTypeDropdown = lineTypeObject != null ? lineTypeObject.GetComponent<Dropdown>() : null;
            var colorObject = lineForm.GetComponentsInChildren<Transform>(true).FirstOrDefault(item => item.name == "Color");
            var colorDropdown = colorObject != null ? colorObject.GetComponent<Dropdown>() : null;
            var selectedColor = Color.red;
            var selectedLineType = "ElectricalWire";

            void ApplyStyle() => controller.SetWireStyle(selectedColor, 0.01f, selectedLineType);

            if (lineTypeDropdown != null)
            {
                lineTypeDropdown.ClearOptions();
                lineTypeDropdown.AddOptions(new List<string> { "电线", "跳线" });
                lineTypeDropdown.onValueChanged.RemoveAllListeners();
                lineTypeDropdown.onValueChanged.AddListener(value =>
                {
                    selectedLineType = value == 0 ? "ElectricalWire" : "JumperLine";
                    ApplyStyle();
                });
                lineTypeDropdown.SetValueWithoutNotify(0);
                lineTypeDropdown.RefreshShownValue();
            }

            if (colorDropdown != null)
            {
                colorDropdown.onValueChanged.RemoveAllListeners();
                colorDropdown.onValueChanged.AddListener(value =>
                {
                    selectedColor = OriginalWireColors[Mathf.Clamp(value, 0, OriginalWireColors.Length - 1)];
                    ApplyStyle();
                });
                colorDropdown.SetValueWithoutNotify(0);
                colorDropdown.RefreshShownValue();
            }

            ApplyStyle();
        }

        private static void BindButtonByText(Transform root, string label, UnityEngine.Events.UnityAction action)
        {
            if (root == null) return;
            var button = root.GetComponentsInChildren<Button>(true).FirstOrDefault(candidate =>
                candidate.GetComponentsInChildren<Text>(true).Any(text => text.text == label));
            if (button == null) return;
            button.onClick.RemoveAllListeners();
            button.onClick.AddListener(() =>
            {
                action();
                root.gameObject.SetActive(false);
            });
        }

        private GameObject InstantiateUi(string id, Transform parent)
        {
            var prefab = originalVisuals.ResolveUi(id);
            if (prefab == null) return null;
            var instance = Instantiate(prefab, parent, false);
            instance.name = "OriginalUI_" + id;
            instance.SetActive(true);
            var rect = instance.GetComponent<RectTransform>();
            if (rect != null)
            {
                rect.localScale = Vector3.one;
                if (rect.rect.width < 10f || rect.rect.height < 10f)
                {
                    rect.anchorMin = Vector2.zero;
                    rect.anchorMax = Vector2.one;
                    rect.offsetMin = Vector2.zero;
                    rect.offsetMax = Vector2.zero;
                }
            }
            return instance;
        }

        private static void BindNamedButton(GameObject root, string name, UnityEngine.Events.UnityAction action)
        {
            var button = FindNamed(root, name);
            if (button == null) return;
            button.onClick.RemoveAllListeners();
            button.onClick.AddListener(action);
        }

        private static Button FindNamed(GameObject root, string name)
            => root.GetComponentsInChildren<Button>(true).FirstOrDefault(item => item.name == name);

        private static void SetNamedButtonActive(GameObject root, string name, bool active)
        {
            var button = FindNamed(root, name);
            if (button != null) button.gameObject.SetActive(active);
        }

        private static void SetNamedButtonText(GameObject root, string name, string value)
        {
            var button = FindNamed(root, name);
            if (button == null) return;
            var labels = button.GetComponentsInChildren<Text>(true);
            foreach (var label in labels)
                if (!string.IsNullOrWhiteSpace(label.text)) label.text = value;
        }

        private RectTransform Panel(string name, Transform parent, Vector2 anchorMin, Vector2 anchorMax, Vector2 offsetMin, Vector2 offsetMax, Color color)
        {
            var gameObject = new GameObject(name, typeof(RectTransform), typeof(Image));
            gameObject.transform.SetParent(parent, false);
            var rect = gameObject.GetComponent<RectTransform>();
            SetRect(rect, anchorMin, anchorMax, offsetMin, offsetMax);
            gameObject.GetComponent<Image>().color = color;
            return rect;
        }

        private Text Label(string name, Transform parent, string text, int fontSize, TextAnchor alignment, Color color)
        {
            var gameObject = new GameObject(name, typeof(RectTransform), typeof(Text));
            gameObject.transform.SetParent(parent, false);
            var label = gameObject.GetComponent<Text>();
            label.font = uiFont;
            label.fontSize = fontSize;
            label.text = text;
            label.alignment = alignment;
            label.color = color;
            label.horizontalOverflow = HorizontalWrapMode.Wrap;
            label.verticalOverflow = VerticalWrapMode.Truncate;
            return label;
        }

        private Button Button(string name, Transform parent, string text, UnityEngine.Events.UnityAction action, Color? color = null)
        {
            var gameObject = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(Button));
            gameObject.transform.SetParent(parent, false);
            var image = gameObject.GetComponent<Image>();
            image.color = color ?? new Color(0.04f, 0.28f, 0.4f, 0.96f);
            var button = gameObject.GetComponent<Button>();
            button.targetGraphic = image;
            button.onClick.AddListener(action);
            var label = Label("Label", gameObject.transform, text, 18, TextAnchor.MiddleCenter, Color.white);
            SetRect(label.rectTransform, Vector2.zero, Vector2.one, new Vector2(5f, 2f), new Vector2(-5f, -2f));
            return button;
        }

        private static void SetRect(RectTransform rect, Vector2 anchorMin, Vector2 anchorMax, Vector2 offsetMin, Vector2 offsetMax)
        {
            rect.anchorMin = anchorMin;
            rect.anchorMax = anchorMax;
            rect.offsetMin = offsetMin;
            rect.offsetMax = offsetMax;
        }

        private static string ModeLabel(SimulationMode mode)
        {
            return mode switch
            {
                SimulationMode.View => "视角 [1]",
                SimulationMode.Drag => "拖动 [2]",
                SimulationMode.Wiring => "接线 [3]",
                SimulationMode.Simulate => "仿真 [4]",
                SimulationMode.Fault => "排故 [5]",
                _ => mode.ToString()
            };
        }

        private static string InstrumentLabel(InstrumentKind kind)
        {
            return kind switch
            {
                InstrumentKind.Multimeter => "万用表",
                InstrumentKind.VoltageProbe => "验电笔",
                InstrumentKind.Oscilloscope => "示波器",
                InstrumentKind.Tachometer => "转速表",
                _ => kind.ToString()
            };
        }

        private sealed class HudReferences
        {
            public Canvas Canvas;
            public RectTransform Top;
            public RectTransform TaskPanel;
            public Text Mode;
            public SchematicGalleryPresenter Gallery;
            public Text Status;
            public Text Instrument;
            public PortHoverPresenter PortHover;
        }

        private sealed class SlidePanelState
        {
            public RectTransform Panel;
            public Text HandleLabel;
            public Vector2 ExpandedPosition;
            public Vector2 CollapsedPosition;
            public string ExpandedGlyph;
            public string CollapsedGlyph;
            public bool IsCollapsed;
            public Coroutine Animation;
        }
    }
}
