using UnityEngine;
using UnityEngine.UI;

namespace ElectricalSim
{
    // The original instrument label belongs to the collapsed schematic panel.
    public sealed class MultimeterHudPresenter : MonoBehaviour
    {
        private SimulationController controller;
        private GameObject panel;
        private Text readout;
        public GameObject Panel => panel;
        public string DisplayText => readout != null ? readout.text : string.Empty;
        public void Initialize(SimulationController source, GameObject root, Text text)
        { controller = source; panel = root; readout = text; Refresh(); }
        public void Refresh()
        {
            if (panel == null) return;
            var visible = controller != null && controller.Multimeter != null && controller.Multimeter.IsSelected && !controller.IsInteractionBlocked;
            if (panel.activeSelf != visible) panel.SetActive(visible);
            if (visible) readout.text = controller.Multimeter.DescribeReading();
        }
        private void LateUpdate() => Refresh();
        private void OnDisable() { if (panel != null) panel.SetActive(false); }
    }
}
