using System.Globalization;

namespace ElectricalSim
{
    public enum MultimeterMode { Off, AcVoltage, DcVoltage, Continuity }
    public enum MultimeterReadingState { Off, MissingProbe, Valid, OpenCircuit, Energized, UndefinedReference, Conflict, Unsupported }

    public readonly struct MultimeterReading
    {
        public MultimeterMode Mode { get; }
        public MultimeterReadingState State { get; }
        public double Value { get; }
        public string Unit => Mode == MultimeterMode.AcVoltage || Mode == MultimeterMode.DcVoltage ? "V" : string.Empty;
        public bool ShouldBeep => Mode == MultimeterMode.Continuity && State == MultimeterReadingState.Valid;
        public MultimeterReading(MultimeterMode mode, MultimeterReadingState state, double value = double.NaN)
        { Mode = mode; State = state; Value = value; }
        public string DisplayValue => State == MultimeterReadingState.Off ? "OFF" :
            State == MultimeterReadingState.Conflict ? "Err" :
            State == MultimeterReadingState.OpenCircuit ? "OL" :
            State != MultimeterReadingState.Valid ? "—" :
            Mode == MultimeterMode.Continuity ? "导通" : Value.ToString("0.0", CultureInfo.InvariantCulture);
        public string Hint => State == MultimeterReadingState.MissingProbe ? "请连接红黑表笔" :
            State == MultimeterReadingState.Energized ? "请先断电" :
            State == MultimeterReadingState.UndefinedReference ? "参考不明确" :
            State == MultimeterReadingState.Conflict ? "电势冲突" :
            State == MultimeterReadingState.Unsupported ? "变频输出电压暂不支持" :
            State == MultimeterReadingState.OpenCircuit ? "断开" :
            State == MultimeterReadingState.Off ? "仪表已关闭" :
            Mode == MultimeterMode.Continuity ? "导通" : string.Empty;
        public static string ModeLabel(MultimeterMode mode) => mode == MultimeterMode.AcVoltage ? "交流 V~" :
            mode == MultimeterMode.DcVoltage ? "直流 V⎓" : mode == MultimeterMode.Continuity ? "蜂鸣通断" : "OFF";
    }
}
