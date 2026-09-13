using System;
using UnityEngine;
using UnityEngine.UI;

namespace ElectricalSim
{
    public sealed class PanelPropertiesPresenter : MonoBehaviour
    {
        private SimulationController controller;
        private Text status;
        private Text properties;
        private Action expand;
        private bool errorVisible;
        public string DisplayedText => properties != null ? properties.text : "";

        public void Initialize(SimulationController source, Text statusLabel, Action expandPanel)
        {
            controller = source;
            status = statusLabel;
            expand = expandPanel;
            var obj = new GameObject("PanelDeviceProperties", typeof(RectTransform), typeof(Text));
            obj.transform.SetParent(transform, false);
            properties = obj.GetComponent<Text>();
            properties.font = status.font;
            properties.fontSize = 14;
            properties.color = new Color(1f, 0.88f, 0.2f);
            properties.alignment = TextAnchor.UpperLeft;
            properties.raycastTarget = false;
            properties.supportRichText = false;
            properties.rectTransform.anchorMin = Vector2.zero;
            properties.rectTransform.anchorMax = Vector2.one;
            properties.rectTransform.offsetMin = new Vector2(16, 5);
            properties.rectTransform.offsetMax = new Vector2(-12, -5);
            controller.PanelSelectionChanged += OnSelection;
            controller.PowerTerminalSelectionChanged += OnPowerSelection;
            controller.StatusChanged += OnStatus;
            OnSelection(null);
        }
        private void OnSelection(PanelDeviceView view)
        {
            var selected = view != null || controller.SelectedPowerTerminalBlock != null;
            errorVisible = false;
            properties.gameObject.SetActive(selected);
            if (selected) { status.gameObject.SetActive(false); expand?.Invoke(); }
            else if (controller.SelectedWire == null) status.gameObject.SetActive(true);
            Refresh();
        }
        private void OnPowerSelection() => OnSelection(controller.SelectedPanelDevice);
        private void OnStatus(string message, bool error)
        {
            errorVisible = error;
            if (controller.SelectedPanelDevice == null && controller.SelectedPowerTerminalBlock == null) return;
            properties.gameObject.SetActive(!error);
            status.gameObject.SetActive(error);
            if (error) expand?.Invoke();
        }
        private void Update() => Refresh();
        private void Refresh()
        {
            if (controller != null && controller.SelectedPanelDevice != null && !errorVisible)
                properties.text = controller.DescribePanelDevice(controller.SelectedPanelDevice);
            else if (controller != null && controller.SelectedPowerTerminalBlock != null && !errorVisible)
                properties.text = controller.DescribePowerTerminalBlock();
        }
        private void OnDestroy()
        {
            if (controller == null) return;
            controller.PanelSelectionChanged -= OnSelection;
            controller.PowerTerminalSelectionChanged -= OnPowerSelection;
            controller.StatusChanged -= OnStatus;
        }
    }
}
