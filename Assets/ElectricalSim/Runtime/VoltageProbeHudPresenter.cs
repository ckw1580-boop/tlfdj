using UnityEngine;
using UnityEngine.UI;

namespace ElectricalSim
{
    public sealed class VoltageProbeHudPresenter : MonoBehaviour
    {
        private SimulationController controller;
        private GameObject panel;
        private Text text;
        private Button ac, dc;
        public GameObject Panel => panel;
        public string DisplayText => text != null ? text.text : string.Empty;
        public void Initialize(SimulationController source, GameObject root, Text label, Button acButton, Button dcButton)
        { controller = source; panel = root; text = label; ac = acButton; dc = dcButton; Refresh(); }
        public void Refresh()
        {
            if (panel == null) return;
            var probe = controller != null ? controller.VoltageProbe : null;
            var visible = probe != null && probe.IsSelected && !controller.IsInteractionBlocked;
            panel.SetActive(visible);
            if (!visible) return;
            text.text = probe.DescribeProperties();
            ac.interactable = probe.MeasurementMode != VoltageProbeMode.AC;
            dc.interactable = probe.MeasurementMode != VoltageProbeMode.DC;
        }
        private void LateUpdate() => Refresh();
        private void OnDisable() { if (panel != null) panel.SetActive(false); }
    }
}
