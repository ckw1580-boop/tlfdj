using System;

namespace ElectricalSim
{
    public enum OscilloscopeSignalState { MissingContact, Valid, UndefinedReference, Conflict, Unavailable, Unsupported }
    public enum OscilloscopeCoupling { DC, AC }
    public enum OscilloscopeWaveform { Fundamental, PWM, Overlay }

    public readonly struct OscilloscopeSignal
    {
        public readonly OscilloscopeSignalState State;
        public readonly double Dc, AcRms, FrequencyHz, Phase;
        public readonly InverterVoltage Inverter;
        public readonly int PositivePhase, NegativePhase;
        public bool IsInverter => Inverter.SourceId != null;
        public bool PwmSupported => !IsInverter || Inverter.PwmSupported;
        public bool Valid => State == OscilloscopeSignalState.Valid;
        public OscilloscopeSignal(OscilloscopeSignalState state, double dc = 0, double ac = 0, double hz = 0, double phase = 0)
        { State = state; Dc = dc; AcRms = ac; FrequencyHz = hz; Phase = phase; Inverter = default; PositivePhase = NegativePhase = 0; }
        public OscilloscopeSignal(InverterVoltage source, int positive, int negative)
        {
            State = source.Valid ? OscilloscopeSignalState.Valid : OscilloscopeSignalState.Conflict;
            Inverter = source; PositivePhase = positive; NegativePhase = negative; Dc = 0;
            var a = positive * 2 * Math.PI / 3; var b = negative * 2 * Math.PI / 3;
            Phase = source.Phase + Math.Atan2(Math.Sin(b) - Math.Sin(a), Math.Cos(a) - Math.Cos(b));
            AcRms = source.Active && positive != negative ? source.LineRms : 0;
            FrequencyHz = AcRms > 0 ? source.FrequencyHz : 0;
        }
        public double Sample(double seconds, OscilloscopeCoupling coupling) =>
            IsInverter ? Inverter.Fundamental(PositivePhase, NegativePhase, seconds) :
            (coupling == OscilloscopeCoupling.DC ? Dc : 0) + Math.Sqrt(2) * AcRms * Math.Sin(2 * Math.PI * FrequencyHz * seconds + Phase);
        public double Rms(OscilloscopeCoupling coupling) => Math.Sqrt(AcRms * AcRms + (coupling == OscilloscopeCoupling.DC ? Dc * Dc : 0));
        public string Hint => State == OscilloscopeSignalState.MissingContact ? "请连接正负探头" :
            State == OscilloscopeSignalState.UndefinedReference ? "参考不明确" :
            State == OscilloscopeSignalState.Conflict ? "电势或信号冲突" :
            State == OscilloscopeSignalState.Unavailable ? "仿真未就绪或未收敛" :
            State == OscilloscopeSignalState.Unsupported ? "波形类型暂不支持" : "测量有效";
    }

    public static class OscilloscopeMeasurement
    {
        public static OscilloscopeSignal Read(string positive, string negative, SimulationSnapshot snapshot)
        {
            OscilloscopeSignal Result(OscilloscopeSignalState s) => new OscilloscopeSignal(s);
            if (string.IsNullOrEmpty(positive) || string.IsNullOrEmpty(negative)) return Result(OscilloscopeSignalState.MissingContact);
            if (snapshot == null) return Result(OscilloscopeSignalState.Unavailable);
            if (!snapshot.ContainsPort(positive) || !snapshot.ContainsPort(negative)) return Result(OscilloscopeSignalState.MissingContact);
            var a = snapshot.GetPotential(positive); var b = snapshot.GetPotential(negative);
            if (a == ElectricalPotential.Conflict || b == ElectricalPotential.Conflict || snapshot.HasSignalConflict(positive) || snapshot.HasSignalConflict(negative))
                return Result(OscilloscopeSignalState.Conflict);
            if (!snapshot.IsConverged) return Result(OscilloscopeSignalState.Unavailable);
            if (snapshot.TryReadInverterVoltage(positive, negative, out var output)) return output;
            if (snapshot.IsVoltageUnsupported(positive) || snapshot.IsVoltageUnsupported(negative)) return Result(OscilloscopeSignalState.Unsupported);
            if (snapshot.SameNet(positive, negative)) return Result(OscilloscopeSignalState.Valid);
            if (snapshot.TryGetSignalVoltage(positive, negative, out var dc))
                return double.IsNaN(dc) || double.IsInfinity(dc) ? Result(OscilloscopeSignalState.Conflict) : new OscilloscopeSignal(OscilloscopeSignalState.Valid, dc);
            if (snapshot.HasControlSignal(positive) || snapshot.HasControlSignal(negative)) return Result(OscilloscopeSignalState.UndefinedReference);
            if (IsAc(a) && IsAc(b))
            {
                var x = Cos(a) - Cos(b); var y = Sin(a) - Sin(b);
                var rms = snapshot.GetAcVoltage(positive, negative);
                return new OscilloscopeSignal(OscilloscopeSignalState.Valid, 0, rms, rms > 0 ? 50 : 0, Math.Atan2(y, x));
            }
            if (IsDc(a) && IsDc(b)) return new OscilloscopeSignal(OscilloscopeSignalState.Valid, snapshot.GetDcVoltage(positive, negative));
            return Result(OscilloscopeSignalState.UndefinedReference);
        }
        private static bool IsAc(ElectricalPotential p) => p == ElectricalPotential.Neutral || p == ElectricalPotential.PhaseL1 || p == ElectricalPotential.PhaseL2 || p == ElectricalPotential.PhaseL3;
        private static bool IsDc(ElectricalPotential p) => p == ElectricalPotential.DcPositive24 || p == ElectricalPotential.DcNegative;
        private static double Angle(ElectricalPotential p) => p == ElectricalPotential.PhaseL2 ? -2 * Math.PI / 3 : p == ElectricalPotential.PhaseL3 ? 2 * Math.PI / 3 : 0;
        private static double Cos(ElectricalPotential p) => p == ElectricalPotential.Neutral ? 0 : Math.Cos(Angle(p));
        private static double Sin(ElectricalPotential p) => p == ElectricalPotential.Neutral ? 0 : Math.Sin(Angle(p));
    }

    public sealed class OscilloscopeChannel
    {
        public bool Enabled = true;
        public OscilloscopeCoupling Coupling;
        public OscilloscopeWaveform Waveform = OscilloscopeWaveform.Overlay;
        public int VoltageIndex = 9;
        public float Position;
        public OscilloscopeSignal Signal;
        public string PositiveLabel = "未连接", NegativeLabel = "未连接";
        public readonly float[] Samples = new float[801];
        public readonly float[] PwmMin = new float[801], PwmMax = new float[801];
        public readonly System.Collections.Generic.List<PwmInterval> PwmIntervals = new System.Collections.Generic.List<PwmInterval>(20010);
        public double PwmMean, PwmPeakToPeak;
        private double duration, minimum, maximum, integral;
        private readonly Action<double, double, double> receive;
        public OscilloscopeChannel() { receive = Receive; }
        public bool DrawPwm => Signal.IsInverter && Signal.PwmSupported && Waveform != OscilloscopeWaveform.Fundamental;
        public bool DrawFundamental => !Signal.IsInverter || Waveform != OscilloscopeWaveform.PWM || !Signal.PwmSupported && Waveform == OscilloscopeWaveform.Overlay;
        public void RenderPwm(double seconds)
        {
            PwmIntervals.Clear(); PwmMean = PwmPeakToPeak = 0;
            if (!Enabled || !Signal.Valid || !DrawPwm) return;
            duration = seconds; minimum = double.PositiveInfinity; maximum = double.NegativeInfinity; integral = 0;
            for (var i = 0; i < PwmMin.Length; i++) { PwmMin[i] = float.PositiveInfinity; PwmMax[i] = float.NegativeInfinity; }
            Signal.Inverter.Segments(Signal.PositivePhase, Signal.NegativePhase, seconds, receive);
            if (PwmIntervals.Count > 0) { PwmMean = integral / seconds; PwmPeakToPeak = maximum - minimum; }
        }
        private void Receive(double start, double end, double value)
        {
            PwmIntervals.Add(new PwmInterval((float)(start / duration), (float)(end / duration), (float)value));
            minimum = Math.Min(minimum, value); maximum = Math.Max(maximum, value); integral += (end - start) * value;
            var from = Math.Min(800, (int)(start / duration * 801)); var to = Math.Min(800, (int)(end / duration * 801));
            for (var i = from; i <= to; i++) { PwmMin[i] = Math.Min(PwmMin[i], (float)value); PwmMax[i] = Math.Max(PwmMax[i], (float)value); }
            if (Math.Abs(value / VoltsPerDivision + Position) > 4) Clipped = true;
        }
        public bool Clipped;
        public float VoltsPerDivision => OscilloscopeFrame.VoltageSteps[VoltageIndex];
    }
    public readonly struct PwmInterval
    {
        public readonly float Start, End, Voltage;
        public PwmInterval(float start, float end, float value) { Start = start; End = end; Voltage = value; }
    }

    // One captured frame is shared by the model screen and the enlarged property plot.
    public sealed class OscilloscopeFrame
    {
        public static readonly float[] VoltageSteps = { .1f, .2f, .5f, 1, 2, 5, 10, 20, 50, 100, 200, 500 };
        public static readonly float[] TimeStepsMs = { .1f, .2f, .5f, 1, 2, 5, 10, 20, 50, 100 };
        public readonly OscilloscopeChannel[] Channels = { new OscilloscopeChannel(), new OscilloscopeChannel() };
        public int TimeIndex = 5;
        public int Revision;
        public bool Frozen;
        public float MillisecondsPerDivision => TimeStepsMs[TimeIndex];
        public void ResetSettings()
        {
            TimeIndex = 5; Frozen = false;
            foreach (var c in Channels) { c.Enabled = true; c.Coupling = OscilloscopeCoupling.DC; c.Waveform = OscilloscopeWaveform.Overlay; c.VoltageIndex = 9; c.Position = 0; }
        }
        public void AutoScale()
        {
            var hz = 0d; var valid = false;
            foreach (var c in Channels)
            {
                if (!c.Enabled || !c.Signal.Valid) continue;
                valid = true; c.Position = 0;
                var peak = Math.Abs(c.Coupling == OscilloscopeCoupling.DC ? c.Signal.Dc : 0) + Math.Sqrt(2) * c.Signal.AcRms;
                if (c.DrawPwm && c.Signal.AcRms > 0) peak = c.Signal.Inverter.DcBus;
                c.VoltageIndex = VoltageSteps.Length - 1;
                for (var i = 0; i < VoltageSteps.Length; i++) if (peak <= VoltageSteps[i] * 3.2) { c.VoltageIndex = i; break; }
                hz = Math.Max(hz, c.Signal.FrequencyHz);
            }
            if (!valid) return;
            TimeIndex = 5;
            if (hz > 0)
            {
                var target = 200 / hz;
                TimeIndex = TimeStepsMs.Length - 1;
                for (var i = 0; i < TimeStepsMs.Length; i++) if (TimeStepsMs[i] >= target) { TimeIndex = i; break; }
            }
        }
        public void Render()
        {
            foreach (var c in Channels)
            {
                c.Clipped = false;
                for (var i = 0; i < c.Samples.Length; i++)
                {
                    // A shared zero-origin shows the current periodic regime with stable relative phase.
                    c.Samples[i] = c.Enabled && c.Signal.Valid ? (float)c.Signal.Sample(i * MillisecondsPerDivision * .01 / (c.Samples.Length - 1), c.Coupling) : 0;
                    if (c.Enabled && c.Signal.Valid && c.DrawFundamental && Math.Abs(c.Samples[i] / c.VoltsPerDivision + c.Position) > 4) c.Clipped = true;
                }
                c.RenderPwm(MillisecondsPerDivision * .01);
            }
            Revision++;
        }
        public string DescribeChannel(int index)
        {
            var c = Channels[index]; var s = c.Signal;
            var head = $"CH{index + 1} · {(c.Enabled ? c.Coupling.ToString() : "关闭")} · {c.VoltsPerDivision:0.###} V/div\n＋ {c.PositiveLabel}\n－ {c.NegativeLabel}";
            if (!c.Enabled) return head;
            if (!s.Valid) return head + "\n— · " + s.Hint;
            if (s.IsInverter)
            {
                var peak = c.DrawPwm ? c.PwmPeakToPeak : 2 * Math.Sqrt(2) * s.AcRms;
                var measurements = !s.PwmSupported && c.Waveform == OscilloscopeWaveform.PWM ? "\nPWM峰峰值 — · 平均值 —" :
                    $"\n{(c.DrawPwm ? "PWM" : "基波")}峰峰值 {peak:0.##} V · 平均值{(c.DrawPwm ? "（窗口）" : "")} {(c.DrawPwm ? c.PwmMean : 0):0.##} V";
                return head + $"\n基波有效值 {s.AcRms:0.##} V · 输出 {s.FrequencyHz:0.##} Hz" +
                    $"\n载波 {InverterVoltage.CarrierHz:0} Hz · 母线 {s.Inverter.DcBus:0.##} V" +
                    measurements +
                    (!s.PwmSupported && c.Waveform != OscilloscopeWaveform.Fundamental ? "\n超出教学 PWM 模型范围" : "") +
                    (c.Clipped ? "\n超出画面：请增大 V/div 或调整位置" : "");
            }
            return head + $"\n有效值 {s.Rms(c.Coupling):0.##} V · 峰峰值 {2 * Math.Sqrt(2) * s.AcRms:0.##} V" +
                $"\n平均值 {(c.Coupling == OscilloscopeCoupling.DC ? s.Dc : 0):0.##} V · 频率 " + (s.FrequencyHz > 0 ? $"{s.FrequencyHz:0.##} Hz" : "—") + (c.Clipped ? "\n超出画面：请增大 V/div 或调整位置" : "");
        }
    }
}
