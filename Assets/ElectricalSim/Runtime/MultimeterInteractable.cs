using UnityEngine;

namespace ElectricalSim
{
    public enum MultimeterAction { MoveBody, RedProbe, BlackProbe, Off, AcVoltage, DcVoltage, Continuity, Retract, Home, CycleMode }
    public sealed class MultimeterInteractable : MonoBehaviour
    {
        public MultimeterAction Action;
    }
}
