using System.Globalization;

namespace ElectricalSim
{
    public enum VoltageProbeMode { AC, DC }
    public enum VoltageProbeState { Valid, MissingContact, UndefinedReference, ModeMismatch, Conflict, Unsupported, Unavailable }

    public readonly struct VoltageProbeReading
    {
        public const string AcReference = "POWER.N";
        public const string DcReference = "TERMINAL_BUS.DC_NEGATIVE";
        public VoltageProbeMode Mode { get; }
        public VoltageProbeState State { get; }
        public double Value { get; }
        public string Unit => "V";
        public string ReferencePort => Mode == VoltageProbeMode.AC ? AcReference : DcReference;
        public string ReferenceLabel => Mode == VoltageProbeMode.AC ? "系统零线 N" : "系统 24V−";
        public string DisplayValue => State == VoltageProbeState.Valid ? Value.ToString("0.0", CultureInfo.InvariantCulture) :
            State == VoltageProbeState.Conflict ? "Err" : "—";
        public string DisplayText => DisplayValue + (State == VoltageProbeState.Valid ? " V" : "");
        public string Hint => State == VoltageProbeState.Valid ? "测量有效" :
            State == VoltageProbeState.MissingContact ? "请接触端子" :
            State == VoltageProbeState.UndefinedReference ? "测点悬空或参考不明确" :
            State == VoltageProbeState.ModeMismatch ? "档位不匹配，请切换 AC/DC" :
            State == VoltageProbeState.Conflict ? "电势或信号冲突" :
            State == VoltageProbeState.Unsupported ? "电压类型暂不支持" : "无法判断：仿真尚未就绪或未收敛";

        public VoltageProbeReading(VoltageProbeMode mode, VoltageProbeState state, double value = double.NaN)
        { Mode = mode; State = state; Value = value; }
    }
}
