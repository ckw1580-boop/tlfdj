using UnityEngine;
using UnityEngine.UI;

namespace ElectricalSim
{
    public sealed partial class TrainingSceneBootstrap
    {
        private void CreateVoltageProbe(HudReferences ui)
        {
            var prefab = Resources.Load<VoltageProbeView>("VoltageProbe");
            if (prefab == null)
            {
                Debug.LogError("Voltage probe model is missing. Run Electrical Sim > Build Voltage Probe Model.");
                return;
            }
            var view = Instantiate(prefab, transform);
            view.SetFont(uiFont);
            var probe = gameObject.AddComponent<VoltageProbeController>();
            probe.Initialize(view, port => port.StartsWith(FaultPowerTerminalBlock.DeviceId + ".", System.StringComparison.Ordinal)
                ? "排故电源端子区 · " + port.Substring(FaultPowerTerminalBlock.DeviceId.Length + 1)
                : controller.ResolveWireTerminalName(port));
            controller.RegisterVoltageProbe(probe);
            var panel = Panel("VoltageProbePropertiesPanel", ui.Canvas.transform, new Vector2(1, 0.34f), new Vector2(1, 0.34f),
                new Vector2(-360, -155), new Vector2(-16, 155), new Color(0.025f, 0.105f, 0.15f, 0.97f));
            var text = Label("VoltageProbeProperties", panel, string.Empty, 17, TextAnchor.UpperLeft, new Color(1, 0.92f, 0.6f));
            SetRect(text.rectTransform, Vector2.zero, Vector2.one, new Vector2(16, 86), new Vector2(-16, -14));
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            var help = Label("VoltageProbeHelp", panel, "点击端子固定 · 点击笔身拿起", 14, TextAnchor.MiddleLeft, new Color(0.68f, 0.79f, 0.82f));
            SetRect(help.rectTransform, Vector2.zero, Vector2.zero, new Vector2(16, 51), new Vector2(328, 79));
            var ac = Button("VoltageProbeAC", panel, "AC 交流", () => probe.SetMode(VoltageProbeMode.AC));
            var dc = Button("VoltageProbeDC", panel, "DC 直流", () => probe.SetMode(VoltageProbeMode.DC));
            var pick = Button("PickUpVoltageProbe", panel, "拿起验电笔", probe.PickUp);
            SetRect(ac.GetComponent<RectTransform>(), Vector2.zero, Vector2.zero, new Vector2(12, 10), new Vector2(107, 44));
            SetRect(dc.GetComponent<RectTransform>(), Vector2.zero, Vector2.zero, new Vector2(115, 10), new Vector2(210, 44));
            SetRect(pick.GetComponent<RectTransform>(), Vector2.zero, Vector2.zero, new Vector2(218, 10), new Vector2(332, 44));
            gameObject.AddComponent<VoltageProbeHudPresenter>().Initialize(controller, panel.gameObject, text, ac, dc);
        }
    }
}
