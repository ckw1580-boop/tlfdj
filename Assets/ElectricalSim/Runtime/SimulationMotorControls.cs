using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace ElectricalSim
{
    [Serializable]
    public sealed class MotorProjectConfiguration
    {
        public string DeviceId;
        public MotorConfiguration Configuration;
    }

    [Serializable]
    public sealed class ThermalRelayProjectConfiguration
    {
        public string DeviceId;
        public ThermalRelayConfiguration Configuration;
    }

    public sealed partial class SimulationController
    {
        private string savedMotorConfigurations;
        private string savedThermalConfigurations;

        public string MotorIdForSceneIo(string id)
        {
            if (string.IsNullOrEmpty(id)) return null;
            if (devices.TryGetValue(id, out var direct) && direct.Kind == ElectricalDeviceKind.Motor) return id;
            if (id == SceneIoCatalog.MixerName) return SceneIoCatalog.MixerMotorId;
            return SceneIoCatalog.Pumps.FirstOrDefault(p => p.Name == id)?.MotorId;
        }

        public MotorConfiguration GetMotorConfiguration(string id) => Motor(id).MotorConfiguration.Clone();
        public MotorRuntimeState GetMotorRuntimeState(string id) => Motor(id).MotorState.Copy();

        private ElectricalDeviceRuntime Motor(string id)
        {
            if (id == null || !devices.TryGetValue(id, out var runtime) || runtime.Kind != ElectricalDeviceKind.Motor)
                throw new ArgumentException("电机不存在：" + id);
            return runtime;
        }

        public void ConfigureMotor(string id, MotorConfiguration configuration)
        {
            if (Mode == SimulationMode.Simulate || IsFileOperationActive)
                throw new InvalidOperationException("请退出仿真后修改电机铭牌、惯量和保护参数。");
            ValidateMotorConfiguration(id, configuration);
            Motor(id).ConfigureMotor(configuration);
        }

        public void SetMotorLoad(string id, float factor)
        {
            if (IsFileOperationActive) throw new InvalidOperationException("文件操作期间不能调整负载。");
            Motor(id).SetMotorLoad(factor);
        }

        public void SetMotorStalled(string id, bool stalled)
        {
            if (Mode != SimulationMode.Simulate || IsFileOperationActive)
                throw new InvalidOperationException("堵转模拟仅在仿真模式可用。");
            Motor(id).SetMotorStalled(stalled);
        }

        public string DescribeMotor(string id)
        {
            var motor = Motor(id);
            var c = motor.MotorConfiguration;
            var s = motor.MotorState;
            var connection = s.Connection;
            string kind;
            switch (connection.Kind)
            {
                case MotorConnectionKind.Star: kind = "星形 Y"; break;
                case MotorConnectionKind.Delta: kind = "三角形 Δ"; break;
                case MotorConnectionKind.DoubleLow: kind = "低速 Δ · " + c.PoleCount + " 极"; break;
                case MotorConnectionKind.DoubleHigh: kind = "高速 YY · " + c.HighPoleCount + " 极"; break;
                case MotorConnectionKind.Invalid: kind = "接线异常"; break;
                default: kind = "未通电／未闭合"; break;
            }
            var rows = new List<string>
            {
                MotorBindingDefinition.Find(id).Label + "（" + id + "）· 教学仿真参数",
                c.IsTwoSpeed ? "类型：Δ/YY 变极双速三相异步电机" : "类型：单速三相鼠笼异步电机",
                c.IsTwoSpeed ? $"铭牌：{c.RatedVoltageDelta:G} V · {c.RatedFrequencyHz:G} Hz · {c.PoleCount}/{c.HighPoleCount} 极\n低／高速：{c.RatedSpeedRpm:G}/{c.HighRatedSpeedRpm:G} rpm · {c.RatedPowerKw:G}/{c.HighRatedPowerKw:G} kW · {c.RatedCurrentAmps:G}/{c.HighRatedCurrentAmps:G} A" :
                    $"铭牌：{c.RatedVoltageDelta:G} V Δ／{c.RatedVoltageStar:G} V Y · {c.RatedFrequencyHz:G} Hz · {c.PoleCount} 极\n额定：{c.RatedSpeedRpm:G} rpm · {c.RatedPowerKw:G} kW · {c.RatedCurrentAmps:G} A（Δ）",
                "实际接法／档位：" + kind,
                $"输出：{connection.FrequencyHz:F1} Hz · 线电压 {connection.LineVoltage:F1} V · 绕组 {connection.WindingVoltage:F1} V",
                $"同步／实际转速：{s.SynchronousSpeedRpm:F1}／{s.SpeedRpm:F1} rpm · 转差率 {s.Slip * 100:F2}%",
                $"电磁转矩：{s.TorqueNm:F2} N·m · 机械负载 {c.LoadFactor * 100:F0}%（低速额定转矩）",
                $"三相电流：{s.PhaseCurrentsAmps[0]:F2}／{s.PhaseCurrentsAmps[1]:F2}／{s.PhaseCurrentsAmps[2]:F2} A",
                $"电机热状态：{s.ThermalState * 100:F1}%" + (s.ThermalState >= 1 ? " · 过热" : ""),
                "堵转模拟：" + (s.IsStalled ? "开启" : "关闭"),
                "诊断：" + (string.IsNullOrEmpty(connection.Diagnostic) ? "无" : connection.Diagnostic),
                "端子：U1/V1/W1、U2/V2/W2（文件标识保留 U/V/W）。",
                c.IsTwoSpeed ? "低速：U1/V1/W1 供电，尾端外部悬空；高速：U2/V2/W2 供电，首端短接。" :
                    "星形：尾端相连；三角形：U1–W2、V1–U2、W1–V2。旧工程缺线请补齐。",
                "教学近似：转差—转矩及热积累曲线；不用于工程选型或保护整定。"
            };
            return string.Join("\n", rows);
        }

        private MotorProjectConfiguration[] MotorConfigurations() => devices.Values
            .Where(d => d.Kind == ElectricalDeviceKind.Motor).OrderBy(d => d.DeviceId, StringComparer.Ordinal)
            .Select(d => new MotorProjectConfiguration { DeviceId = d.DeviceId, Configuration = d.MotorConfiguration.Clone() }).ToArray();
        private ThermalRelayProjectConfiguration[] ThermalConfigurations() => devices.Values
            .Where(d => d.Kind == ElectricalDeviceKind.ThermalRelay).OrderBy(d => d.DeviceId, StringComparer.Ordinal)
            .Select(d => new ThermalRelayProjectConfiguration { DeviceId = d.DeviceId, Configuration = d.ThermalConfiguration.Clone() }).ToArray();
        private string MotorConfigurationSignature() => JsonConvert.SerializeObject(MotorConfigurations());
        private string ThermalConfigurationSignature() => JsonConvert.SerializeObject(ThermalConfigurations());

        private void ExportMotorAndThermalConfigurations(Cc3dDocument document)
        {
            document.Extra["motorConfigurations"] = JArray.FromObject(MotorConfigurations());
            document.Extra["thermalRelayConfigurations"] = JArray.FromObject(ThermalConfigurations());
        }

        private void ValidateMotorConfiguration(string id, MotorConfiguration configuration)
        {
            if (configuration == null) throw new ArgumentException(id + " 电机配置不能为空。");
            if (configuration.IsTwoSpeed != MotorConfiguration.CreateDefault(id).IsTwoSpeed)
                throw new ArgumentException(id + " 的电机类型与场景不匹配。");
            if (!configuration.Validate(out var error)) throw new ArgumentException(id + "：" + error);
        }

        // Stage and validate every record before callers mutate any project state.
        private MotorProjectConfiguration[] ReadMotorConfigurations(Cc3dDocument document)
        {
            var configs = MotorConfigurations();
            foreach (var config in configs) config.Configuration = MotorConfiguration.CreateDefault(config.DeviceId);
            if (!document.Extra.TryGetValue("motorConfigurations", out var token)) return configs;
            if (token.Type != JTokenType.Array) throw new ArgumentException("电机配置必须为数组。");
            var incoming = token.ToObject<MotorProjectConfiguration[]>();
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var record in incoming)
            {
                if (record == null || string.IsNullOrEmpty(record.DeviceId) || !seen.Add(record.DeviceId))
                    throw new ArgumentException("电机配置编号不能为空或重复。");
                var target = configs.FirstOrDefault(c => c.DeviceId == record.DeviceId);
                if (target == null) throw new ArgumentException("未知电机配置：" + record.DeviceId);
                ValidateMotorConfiguration(record.DeviceId, record.Configuration);
                target.Configuration = record.Configuration.Clone();
            }
            return configs;
        }

        private ThermalRelayProjectConfiguration[] ReadThermalConfigurations(Cc3dDocument document)
        {
            var configs = ThermalConfigurations();
            foreach (var config in configs) config.Configuration = new ThermalRelayConfiguration();
            if (!document.Extra.TryGetValue("thermalRelayConfigurations", out var token)) return configs;
            if (token.Type != JTokenType.Array) throw new ArgumentException("热继电器配置必须为数组。");
            var incoming = token.ToObject<ThermalRelayProjectConfiguration[]>();
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var record in incoming)
            {
                if (record == null || string.IsNullOrEmpty(record.DeviceId) || !seen.Add(record.DeviceId))
                    throw new ArgumentException("热继电器配置编号不能为空或重复。");
                var target = configs.FirstOrDefault(c => c.DeviceId == record.DeviceId);
                if (target == null) throw new ArgumentException("未知热继电器配置：" + record.DeviceId);
                if (record.Configuration == null) throw new ArgumentException(record.DeviceId + " 热继电器配置不能为空。");
                record.Configuration.Validate();
                target.Configuration = record.Configuration.Clone();
            }
            return configs;
        }

        private void ReplaceMotorAndThermalConfigurations(MotorProjectConfiguration[] motors, ThermalRelayProjectConfiguration[] relays)
        {
            foreach (var record in motors) Motor(record.DeviceId).ConfigureMotor(record.Configuration);
            foreach (var record in relays) devices[record.DeviceId].ConfigureThermalRelay(record.Configuration);
            savedMotorConfigurations = MotorConfigurationSignature();
            savedThermalConfigurations = ThermalConfigurationSignature();
        }
    }
}
