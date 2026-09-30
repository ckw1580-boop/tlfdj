using UnityEngine;

namespace ElectricalSim
{
    public enum VoltageProbeAction { PickUp, ToggleMode }
    public sealed class VoltageProbeInteractable : MonoBehaviour
    {
        public VoltageProbeAction Action;
    }
}
