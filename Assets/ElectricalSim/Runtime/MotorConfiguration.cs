using System;

namespace ElectricalSim
{
    /// <summary>Editable teaching parameters, not an identified engineering motor model.</summary>
    [Serializable]
    public sealed class MotorConfiguration
    {
        public bool IsTwoSpeed;
        public float RatedFrequencyHz = 50f;
        public float RatedVoltageDelta = 380f;
        public float RatedVoltageStar = 660f;
        public int PoleCount = 4;
        public int HighPoleCount = 2;
        public float RatedSpeedRpm = 1450f;
        public float HighRatedSpeedRpm = 2900f;
        public float RatedPowerKw = 1.1f;
        public float HighRatedPowerKw = 2.2f;
        public float RatedCurrentAmps = 3.1f;
        public float HighRatedCurrentAmps = 6.2f;
        public float InertiaKgM2 = 0.02f;
        public float LoadFactor = 0.5f;
        public float MaximumTorqueMultiple = 2.5f;
        public float BreakdownSlip = 0.2f;
        public float StartingTorqueMultiple = 2f;
        public float StartingCurrentMultiple = 6f;
        public float ThermalTimeConstantSeconds = 180f;
        public float WindingResistanceOhms = 10f;

        public static MotorConfiguration CreateDefault(string deviceId)
            => new MotorConfiguration { IsTwoSpeed = deviceId == "M_DOUBLE" };
        public MotorConfiguration Clone() => (MotorConfiguration)MemberwiseClone();
        public float RatedTorqueNm(bool high = false)
            => (high ? HighRatedPowerKw : RatedPowerKw) * 1000f /
               ((high ? HighRatedSpeedRpm : RatedSpeedRpm) * (float)(2d * Math.PI / 60d));

        public bool Validate(out string error)
        {
            error = null;
            foreach (var value in new[] { RatedFrequencyHz, RatedVoltageDelta, RatedVoltageStar, RatedSpeedRpm,
                RatedPowerKw, RatedCurrentAmps, InertiaKgM2, MaximumTorqueMultiple, BreakdownSlip,
                StartingTorqueMultiple, StartingCurrentMultiple, ThermalTimeConstantSeconds, WindingResistanceOhms })
                if (!Positive(value)) { error = "铭牌、惯量和模型参数必须为有限正数。"; return false; }
            if (float.IsNaN(LoadFactor) || float.IsInfinity(LoadFactor) || LoadFactor < 0f)
                error = "负载比例必须为有限非负数。";
            else if (PoleCount < 2 || PoleCount % 2 != 0 || RatedSpeedRpm >= 120f * RatedFrequencyHz / PoleCount)
                error = "极数必须为正偶数，额定转速必须低于同步转速。";
            else if (MaximumTorqueMultiple <= 1f || StartingTorqueMultiple > MaximumTorqueMultiple ||
                StartingCurrentMultiple <= 1f || BreakdownSlip >= 1f ||
                BreakdownSlip <= 1f - RatedSpeedRpm / (120f * RatedFrequencyHz / PoleCount))
                error = "最大转矩、堵转点及额定转差参数不满足教学曲线约束。";
            else if (IsTwoSpeed && (HighPoleCount < 2 || HighPoleCount % 2 != 0 || PoleCount != HighPoleCount * 2 ||
                !Positive(HighRatedSpeedRpm) || !Positive(HighRatedPowerKw) || !Positive(HighRatedCurrentAmps) ||
                HighRatedSpeedRpm >= 120f * RatedFrequencyHz / HighPoleCount ||
                BreakdownSlip <= 1f - HighRatedSpeedRpm / (120f * RatedFrequencyHz / HighPoleCount)))
                error = "双速极数比必须为2:1，两档额定转速均须低于同步转速且额定转差低于最大转矩点。";
            return error == null;
        }
        private static bool Positive(float value) => value > 0f && !float.IsInfinity(value) && !float.IsNaN(value);
    }
}
