using UnityEngine;

namespace ElectricalSim
{
    public sealed partial class TrainingSceneBootstrap
    {
        private void CreateOscilloscope(HudReferences ui)
        {
            var prefab = Resources.Load<OscilloscopeView>("Oscilloscope");
            if (prefab == null) { Debug.LogError("Oscilloscope model is missing. Run Electrical Sim > Build Oscilloscope Model."); return; }
            var view = Instantiate(prefab, transform); view.SetFont(uiFont);
            var instrument = gameObject.AddComponent<OscilloscopeController>();
            instrument.Initialize(view, Camera.main.GetComponent<TrainingCameraController>(), port =>
                port.StartsWith(FaultPowerTerminalBlock.DeviceId + ".", System.StringComparison.Ordinal)
                    ? "排故电源 · " + port.Substring(FaultPowerTerminalBlock.DeviceId.Length + 1)
                    : controller.ResolveWireTerminalName(port));
            controller.RegisterOscilloscope(instrument);
            gameObject.AddComponent<OscilloscopePropertiesPresenter>().Initialize(controller, instrument, ui.Canvas, uiFont);
        }
    }
}
