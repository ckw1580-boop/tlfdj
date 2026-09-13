using System;
using System.Collections.Generic;
using System.Linq;

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

    public sealed class LiquidSimulationRuntime
    {
        private LiquidConfiguration configuration = new LiquidConfiguration();
        public LiquidConfiguration Configuration => configuration.Copy();
        public double Level { get; private set; }
        public double OverflowVolume { get; private set; }
        public bool IsOverflowing { get; private set; }
        public double Pump1Flow { get; private set; }
        public double Pump2Flow { get; private set; }
        public double DrainFlow { get; private set; }
        public double NetFlow => Pump1Flow + Pump2Flow - DrainFlow;
        public void Configure(LiquidConfiguration value, bool reset)
        {
            if (value == null) throw new ArgumentException("液位配置缺失。");
            value.Validate();
            configuration = value.Copy();
            if (reset) Reset();
        }
        public void Reset()
        {
            Level = configuration.InitialLevelPercent / 100d;
            OverflowVolume = 0;
            Pause();
        }
        public void Pause() { Pump1Flow = Pump2Flow = DrainFlow = 0; IsOverflowing = false; }
        public void Advance(double seconds, float rpm1, float rpm2, bool valve1, bool valve2, bool valve3)
        {
            if (seconds <= 0) return;
            Pump1Flow = valve1 ? Math.Max(0, rpm1) / (1450d * configuration.Pump1FillSeconds) : 0;
            Pump2Flow = valve2 ? Math.Max(0, rpm2) / (1450d * configuration.Pump2FillSeconds) : 0;
            var incoming = Pump1Flow + Pump2Flow;
            // Limit actual outflow by available liquid, including concurrent inflow.
            DrainFlow = valve3 ? Math.Min(1d / configuration.DrainSeconds, Level / seconds + incoming) : 0;
            var next = Level + NetFlow * seconds;
            var spill = Math.Max(0, next - 1);
            IsOverflowing = spill > 1e-10;
            OverflowVolume += spill;
            Level = Math.Max(0, Math.Min(1, next));
        }
    }
}
