using System;

namespace ElectricalSim
{
    public enum MotorConnectionKind { Disconnected, Star, Delta, DoubleLow, DoubleHigh, Invalid }

    public sealed class MotorConnectionResult
    {
        public MotorConnectionKind Kind;
        public bool Energized;
        public bool PhaseLoss;
        public int DirectionSign;
        public float FrequencyHz;
        public float LineVoltage;
        public float WindingVoltage;
        public bool IsHighSpeed;
        public string[] SupplyPorts = Array.Empty<string>();
        public int[] PhaseIndices = Array.Empty<int>();
        public string Diagnostic = string.Empty;
        public string DriveId = string.Empty;
        public bool IsValid => Kind != MotorConnectionKind.Invalid && Kind != MotorConnectionKind.Disconnected;
        public MotorConnectionResult Copy()
        {
            var result = (MotorConnectionResult)MemberwiseClone();
            result.SupplyPorts = (string[])SupplyPorts.Clone();
            result.PhaseIndices = (int[])PhaseIndices.Clone();
            return result;
        }
    }

    public sealed class MotorRuntimeState
    {
        public MotorConnectionResult Connection = new MotorConnectionResult();
        public float SpeedRpm;
        public float SynchronousSpeedRpm;
        public float Slip;
        public float TorqueNm;
        public float[] PhaseCurrentsAmps = new float[3];
        public float ThermalState;
        public bool IsStalled;
        public float CurrentAmps => Math.Max(PhaseCurrentsAmps[0], Math.Max(PhaseCurrentsAmps[1], PhaseCurrentsAmps[2]));
        public float TemperatureC => 25f + ThermalState * 100f;
        public bool IsOverheated => ThermalState >= 1f;
        public string SourceId => Connection.DriveId;
        public MotorRuntimeState Copy()
        {
            var result = (MotorRuntimeState)MemberwiseClone();
            result.Connection = Connection.Copy();
            result.PhaseCurrentsAmps = (float[])PhaseCurrentsAmps.Clone();
            return result;
        }
    }
}
