using UnityEngine;

namespace ElectricalSim
{
    public sealed class MotorRotorView : MonoBehaviour
    {
        private ElectricalDeviceRuntime motor;
        private Transform[] discs;
        private Transform axis;
        public void Initialize(ElectricalDeviceRuntime runtime, Transform shaft, Transform[] rotatingDiscs)
        { motor = runtime; axis = shaft; discs = rotatingDiscs; }
        private void Update()
        {
            if (motor == null || axis == null) return;
            foreach (var disc in discs)
                if (disc != null) disc.RotateAround(axis.position, axis.forward, motor.ActualSpeedRpm * 6f * Time.deltaTime);
        }
    }
}
