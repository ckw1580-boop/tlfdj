using UnityEngine;
namespace ElectricalSim
{
    public enum OscilloscopeAction { Probe0, Probe1, Probe2, Probe3, MoveBody, RunStop, AutoScale, Timebase, Voltage1, Voltage2, Retract, Home }
    public sealed class OscilloscopeInteractable : MonoBehaviour { public OscilloscopeAction Action; }
}
