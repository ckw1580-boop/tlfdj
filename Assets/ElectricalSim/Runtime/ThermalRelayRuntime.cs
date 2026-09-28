using System;
using System.Linq;

namespace ElectricalSim
{
    [Serializable]
    public sealed class ThermalRelayConfiguration
    {
        public float SettingCurrent = 3.1f;
        public float TimeConstantSeconds = 180f;
        public float ResetThreshold = 0.5f;
        public ThermalRelayConfiguration Clone() => (ThermalRelayConfiguration)MemberwiseClone();
        public void Validate()
        {
            if (!Finite(SettingCurrent) || SettingCurrent <= 0 || !Finite(TimeConstantSeconds) || TimeConstantSeconds <= 0 ||
                !Finite(ResetThreshold) || ResetThreshold <= 0 || ResetThreshold >= 1)
                throw new ArgumentException("热继电器整定电流和热时间常数必须为正数，复位热状态必须大于0且小于1。");
        }
        private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
    }

    public sealed class ThermalRelayRuntimeState
    {
        public float[] Currents = new float[3];
        public float Heat;
        public string Diagnostic = "";
        public string TripReason = "";
        public bool CanReset = true;
        public ThermalRelayRuntimeState Copy() => new ThermalRelayRuntimeState
        { Currents = (float[])Currents.Clone(), Heat = Heat, Diagnostic = Diagnostic, TripReason = TripReason, CanReset = CanReset };
    }

    public sealed partial class ElectricalDeviceRuntime
    {
        public ThermalRelayConfiguration ThermalConfiguration { get; private set; } = new ThermalRelayConfiguration();
        public ThermalRelayRuntimeState ThermalState { get; private set; } = new ThermalRelayRuntimeState();
        public void ConfigureThermalRelay(ThermalRelayConfiguration configuration)
        {
            if (Kind != ElectricalDeviceKind.ThermalRelay) throw new InvalidOperationException("所选器件不是热继电器。");
            if (configuration == null) throw new ArgumentNullException(nameof(configuration));
            configuration.Validate(); ThermalConfiguration = configuration.Clone(); ResetThermalState();
        }
        public void ResetThermalState() { ThermalState = new ThermalRelayRuntimeState(); IsTripped = false; }
        internal bool TrySetThermalTripped(bool tripped)
        {
            if (!tripped && !ThermalState.CanReset) return false;
            IsTripped = tripped;
            ThermalState.TripReason = tripped ? "手动模拟脱扣" : "";
            return true;
        }
        internal bool AdvanceThermal(float[] currents, string diagnostic, float seconds)
        {
            ThermalState.Currents = (float[])currents.Clone();
            ThermalState.Diagnostic = diagnostic;
            var known = currents.Where(i => !float.IsNaN(i) && !float.IsInfinity(i)).ToArray();
            // Unknown current must not be reported as zero or interpreted as a safe, cold circuit.
            var target = known.Length == 0 ? ThermalState.Heat :
                Math.Pow(known.Max() / (1.15 * ThermalConfiguration.SettingCurrent), 2);
            if (currents.Any(float.IsNaN)) target = Math.Max(target, ThermalState.Heat);
            if (seconds > 0)
                ThermalState.Heat = (float)(target + (ThermalState.Heat - target) * Math.Exp(-seconds / ThermalConfiguration.TimeConstantSeconds));
            ThermalState.CanReset = ThermalState.Heat <= ThermalConfiguration.ResetThreshold;
            if (ThermalState.Heat < 1 || IsTripped) return false;
            IsTripped = true;
            ThermalState.TripReason = "持续过电流／缺相热积累自动脱扣";
            return true;
        }
    }
}
