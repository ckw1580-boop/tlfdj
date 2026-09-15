using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace ElectricalSim
{
    public sealed class SceneIoDefinition
    {
        public string Id { get; }
        public string Name { get; }
        public string Prefix { get; }
        public int Mount { get; }
        public bool IsSensor { get; }
        public string[] Ports => IsSensor ? new[] { "VCC", "SIGNAL", "GND" } : new[] { "VCC", "GND" };
        public string ModelPath => SceneIoCatalog.EnvironmentPath + "/rivet/" + Mount + "/" + (IsSensor ? "XianWeiSensor" : "DianCiFa");
        public SceneIoDefinition(string id, string name, string prefix, int mount, bool sensor)
        { Id = id; Name = name; Prefix = prefix; Mount = mount; IsSensor = sensor; }
    }

    public sealed class PumpBindingDefinition
    {
        public int Index { get; }
        public string MotorId { get; }
        public int Mount { get; }
        public string Name => "泵" + (Index + 1);
        public string ValveId => "SOLENOID" + (Index + 1);
        public string ModelPath => SceneIoCatalog.EnvironmentPath + "/rivet/" + Mount + "/BengSensor";
        public PumpBindingDefinition(int index, string motor, int mount) { Index = index; MotorId = motor; Mount = mount; }
    }

    public static class SceneIoCatalog
    {
        public const string EnvironmentPath = "Bench/ElectricBench/Nuts/200/EnvironmentBench";
        public const string MixerName = "搅拌电机";
        public const string MixerMotorId = "M2";
        public const int MixerMount = 4;
        public const string MixerModelPath = EnvironmentPath + "/rivet/4/Jiaoban";
        public static readonly IReadOnlyList<SceneIoDefinition> Devices = new[]
        {
            new SceneIoDefinition("SENSOR_A", "下限位", "A", 12, true),
            new SceneIoDefinition("SENSOR_B", "中限位", "B", 11, true),
            new SceneIoDefinition("SENSOR_C", "上限位", "C", 10, true),
            new SceneIoDefinition("SENSOR_D", "上上限位", "D", 9, true),
            new SceneIoDefinition("SOLENOID1", "电磁阀1", "Diancifa1", 1, false),
            new SceneIoDefinition("SOLENOID2", "电磁阀2", "Diancifa2", 2, false),
            new SceneIoDefinition("SOLENOID3", "电磁阀3", "Diancifa3", 3, false)
        };
        public static readonly IReadOnlyList<PumpBindingDefinition> Pumps = new[]
        {
            new PumpBindingDefinition(0, "M_DOUBLE", 14),
            new PumpBindingDefinition(1, "M1", 13)
        };
        public static string DisplayPort(string port)
        {
            if (string.IsNullOrEmpty(port)) return port;
            var split = port.IndexOf('_');
            if (split < 1) return port;
            var definition = Devices.FirstOrDefault(d => string.Equals(d.Prefix, port.Substring(0, split), StringComparison.OrdinalIgnoreCase));
            return definition == null ? port : definition.Name + port.Substring(split);
        }
    }

    // SIGNAL is a controlled high-side source, not a bidirectional VCC contact.
    // A short on SIGNAL therefore cannot back-feed the sensor supply terminal.
    public sealed class SceneIoDeviceRuntime : IElectricalDevice, IElectricalSource
    {
        public SceneIoDefinition Definition { get; }
        public string DeviceId => Definition.Id;
        public ElectricalDeviceKind Kind => Definition.IsSensor ? ElectricalDeviceKind.Sensor : ElectricalDeviceKind.SolenoidValve;
        public IReadOnlyCollection<string> Ports => Definition.Ports;
        public bool IsActive { get; private set; }
        public bool Powered { get; private set; }
        public bool Wet { get; private set; }
        public bool SignalShortCircuit { get; private set; }
        public double SupplyVoltage { get; private set; }
        public string SupplyStatus { get; private set; } = "未供电";
        public float TriggerLevel { get; set; }
        public string Port(string name) => DeviceId + "." + name;
        public SceneIoDeviceRuntime(SceneIoDefinition definition) { Definition = definition; }
        public void SetLevel(double level, bool reset = false)
        {
            if (!Definition.IsSensor) return;
            Wet = reset || !Wet ? level >= TriggerLevel : level >= TriggerLevel - 0.005;
        }
        public bool Evaluate(SimulationSnapshot snapshot, float deltaTime)
        {
            var previous = IsActive;
            SupplyVoltage = snapshot.GetDcVoltage(Port("VCC"), Port("GND"));
            Powered = SupplyVoltage == 24;
            var a = snapshot.GetPotential(Port("VCC"));
            var b = snapshot.GetPotential(Port("GND"));
            SupplyStatus = Powered ? "DC 24V · 供电正常" : double.IsNaN(SupplyVoltage) ? "供电冲突" :
                SupplyVoltage < 0 ? "极性反接" : IsAc(a) || IsAc(b) ? "交流误接" :
                a == ElectricalPotential.Floating || b == ElectricalPotential.Floating ? "未供电或断线" : "无有效压差";
            SignalShortCircuit = Definition.IsSensor && snapshot.SameNet(Port("SIGNAL"), Port("GND"));
            IsActive = Powered && (!Definition.IsSensor || Wet && !SignalShortCircuit);
            return previous != IsActive;
        }
        private static bool IsAc(ElectricalPotential p) => p == ElectricalPotential.PhaseL1 || p == ElectricalPotential.PhaseL2 || p == ElectricalPotential.PhaseL3;
        public IEnumerable<PortPair> GetConductiveLinks() { yield break; }
        public IEnumerable<KeyValuePair<string, ElectricalPotential>> GetSourcePotentials()
        {
            if (Definition.IsSensor && IsActive)
                yield return new KeyValuePair<string, ElectricalPotential>(Port("SIGNAL"), ElectricalPotential.DcPositive24);
        }
        public void ApplyVisualState(SimulationSnapshot snapshot) { }
    }

    [Serializable]
    public sealed class LiquidConfiguration
    {
        public float InitialLevelPercent = 0;
        public float Pump1FillSeconds = 30;
        public float Pump2FillSeconds = 30;
        public float DrainSeconds = 20;
        public LiquidConfiguration Copy() => (LiquidConfiguration)MemberwiseClone();
        public void Validate()
        {
            Check(InitialLevelPercent, 0, 100, "初始液位");
            Check(Pump1FillSeconds, 1, 3600, "泵1标定注满时间");
            Check(Pump2FillSeconds, 1, 3600, "泵2标定注满时间");
            Check(DrainSeconds, 1, 3600, "重力排空时间");
        }
        private static void Check(float value, float min, float max, string label)
        {
            if (float.IsNaN(value) || float.IsInfinity(value) || value < min || value > max)
                throw new ArgumentException(label + "必须在 " + min + "～" + max + " 之间。");
        }
    }

    public enum LiquidContents { Empty, A, B, Mixed }

    public sealed class LiquidSimulationRuntime
    {
        private const double EmptyTolerance = 1e-12;
        private LiquidConfiguration configuration = new LiquidConfiguration();
        private float tankBottom, tankHeight, receiverY;
        private Color mixStartColor;
        public LiquidConfiguration Configuration => configuration.Copy();
        public LiquidStreamRuntime[] Streams { get; private set; }
        public double VolumeA { get; private set; }
        public double VolumeB { get; private set; }
        public double Level => VolumeA + VolumeB;
        public double MixProgress { get; private set; }
        public LiquidContents Contents => Level <= EmptyTolerance ? LiquidContents.Empty :
            VolumeA <= EmptyTolerance ? LiquidContents.B : VolumeB <= EmptyTolerance ? LiquidContents.A : LiquidContents.Mixed;
        public Color CurrentColor => Contents == LiquidContents.A ? LiquidTankView.LiquidAColor :
            Contents == LiquidContents.B ? LiquidTankView.LiquidBColor : Contents == LiquidContents.Mixed ?
            Color.Lerp(mixStartColor, LiquidTankView.MixedColor, (float)MixProgress) : LiquidTankView.MixedColor;
        public Color DischargeColor { get; private set; } = LiquidTankView.MixedColor;
        public double OverflowVolume { get; private set; }
        public bool IsOverflowing { get; private set; }
        public double Pump1Flow { get; private set; }
        public double Pump2Flow { get; private set; }
        public double DrainFlow { get; private set; }
        public bool Pump1Transporting { get; private set; }
        public bool Pump2Transporting { get; private set; }
        public double NetFlow => Pump1Flow + Pump2Flow - DrainFlow;

        public void InitializeTransport(LiquidPipeRoute[] routes, float bottom, float height, float receiverSurfaceY)
        {
            if (routes == null || routes.Length != 3 || height <= 0) throw new ArgumentException("液体输送路径或罐体尺寸无效。");
            Streams = routes.Select(route => new LiquidStreamRuntime(route)).ToArray();
            tankBottom = bottom; tankHeight = height; receiverY = receiverSurfaceY;
        }
        public void Configure(LiquidConfiguration value, bool reset)
        {
            if (value == null) throw new ArgumentException("液位配置缺失。");
            value.Validate();
            configuration = value.Copy();
            if (reset) Reset();
        }
        public void Reset()
        {
            VolumeA = VolumeB = configuration.InitialLevelPercent / 200d;
            MixProgress = Level > EmptyTolerance ? 1 : 0;
            mixStartColor = DischargeColor = LiquidTankView.MixedColor;
            OverflowVolume = 0;
            if (Streams != null) foreach (var stream in Streams) stream.Reset();
            Pause();
        }
        public void Pause()
        {
            Pump1Flow = Pump2Flow = DrainFlow = 0; IsOverflowing = false;
            Pump1Transporting = Pump2Transporting = false;
        }
        public void AdvanceIdle(double seconds)
        {
            Pause();
            if (Streams == null) return;
            for (var i = 0; i < Streams.Length; i++)
                Streams[i].Advance(seconds, 0, false, false, i < 2 ? tankBottom + tankHeight * (float)Level : receiverY);
        }
        public void Advance(double seconds, float rpm1, float rpm2, bool valve1, bool valve2, bool valve3)
        {
            if (seconds <= 0) return;
            var supplyA = valve1 ? Math.Max(0, rpm1) / (1450d * configuration.Pump1FillSeconds) : 0;
            var supplyB = valve2 ? Math.Max(0, rpm2) / (1450d * configuration.Pump2FillSeconds) : 0;
            // Without scene geometry this remains the direct mass-balance API used
            // by unit tests. The training scene always binds all three routes.
            var arrivedA = seconds; var arrivedB = seconds;
            if (Streams != null)
            {
                var surface = tankBottom + tankHeight * (float)Level;
                arrivedA = Streams[0].Advance(seconds, Mathf.Max(0, rpm1) / 1450f * 0.8f, valve1, true, surface);
                arrivedB = Streams[1].Advance(seconds, Mathf.Max(0, rpm2) / 1450f * 0.8f, valve2, true, surface);
            }
            Pump1Flow = supplyA * arrivedA / seconds;
            Pump2Flow = supplyB * arrivedB / seconds;
            Pump1Transporting = supplyA > 0 && arrivedA == 0;
            Pump2Transporting = supplyB > 0 && arrivedB == 0;
            DrainFlow = 0; IsOverflowing = false;
            var startA = seconds - arrivedA; var startB = seconds - arrivedB;
            // Split at the two contact times so first arrival, concurrent drainage,
            // and the one-second colour transition use the same physical interval.
            var first = Math.Min(startA, startB); var second = Math.Max(startA, startB);
            var drained = ApplyVolumes(first, 0, 0, valve3);
            drained += ApplyVolumes(second - first, startA <= first ? supplyA : 0, startB <= first ? supplyB : 0, valve3);
            drained += ApplyVolumes(seconds - second, supplyA, supplyB, valve3);
            DrainFlow = drained / seconds;
            if (Streams != null) Streams[2].Advance(seconds, DrainFlow > 1e-9 ? 0.5f : 0, valve3, true, receiverY);
        }
        private double ApplyVolumes(double seconds, double rateA, double rateB, bool draining)
        {
            if (seconds <= 0) return 0;
            var before = Contents;
            var beforeColor = CurrentColor;
            VolumeA += rateA * seconds; VolumeB += rateB * seconds;
            if (Contents == LiquidContents.Mixed)
            {
                if (before != LiquidContents.Mixed)
                {
                    mixStartColor = before == LiquidContents.Empty ?
                        Color.Lerp(LiquidTankView.LiquidAColor, LiquidTankView.LiquidBColor, (float)(VolumeB / Level)) : beforeColor;
                    MixProgress = 0;
                }
                MixProgress = Math.Min(1, MixProgress + seconds);
            }
            var total = Level;
            var removed = draining ? Math.Min(seconds / configuration.DrainSeconds, total) : 0;
            if (removed > 0) DischargeColor = CurrentColor;
            var spill = Math.Max(0, total - removed - 1);
            IsOverflowing |= spill > 1e-10;
            OverflowVolume += spill;
            var retained = total <= EmptyTolerance ? 0 : Math.Max(0, total - removed - spill) / total;
            VolumeA *= retained; VolumeB *= retained;
            if (Level <= EmptyTolerance) { VolumeA = VolumeB = 0; MixProgress = 0; mixStartColor = LiquidTankView.MixedColor; }
            return removed;
        }
    }
}
