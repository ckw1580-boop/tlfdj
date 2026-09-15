using UnityEngine;

namespace ElectricalSim
{
    public sealed partial class TrainingSceneBootstrap
    {
        private void CreateMultimeter(HudReferences ui)
        {
            var prefab = Resources.Load<MultimeterView>("Multimeter");
            if (prefab == null)
            {
                Debug.LogError("Multimeter model is missing. Run Electrical Sim > Build Multimeter Model.");
                return;
            }
            var view = Instantiate(prefab, transform);
            view.SetFont(uiFont);
            var meter = gameObject.AddComponent<MultimeterController>();
            meter.Initialize(view, Camera.main.GetComponent<TrainingCameraController>(), controller.ResolveWireTerminalName);
            controller.RegisterMultimeter(meter);
            var panel = Panel("MultimeterReadoutPanel", ui.Canvas.transform, new Vector2(1, 0.34f), new Vector2(1, 0.34f),
                new Vector2(-344, -125), new Vector2(-16, 125), new Color(0.025f, 0.105f, 0.15f, 0.97f));
            var readout = Label("MultimeterReadout", panel, string.Empty, 17, UnityEngine.TextAnchor.UpperLeft, new Color(1f, 0.92f, 0.6f));
            SetRect(readout.rectTransform, Vector2.zero, Vector2.one, new Vector2(16, 76), new Vector2(-16, -14));
            var help = Label("MultimeterHelp", panel, "点击表笔接端子 · 拖动表体移动", 14, TextAnchor.MiddleLeft, new Color(0.68f, 0.79f, 0.82f));
            SetRect(help.rectTransform, Vector2.zero, Vector2.one, new Vector2(16, 48), new Vector2(-16, -178));
            var retract = Button("RetractMultimeterProbes", panel, "收回表笔", meter.RetractProbes);
            SetRect(retract.GetComponent<RectTransform>(), Vector2.zero, Vector2.zero, new Vector2(12, 10), new Vector2(159, 44));
            var home = Button("ReturnMultimeterHome", panel, "仪表归位", meter.ReturnHome);
            SetRect(home.GetComponent<RectTransform>(), Vector2.zero, Vector2.zero, new Vector2(169, 10), new Vector2(316, 44));
            gameObject.AddComponent<MultimeterHudPresenter>().Initialize(controller, panel.gameObject, readout);
        }
    }
}
