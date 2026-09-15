using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace ElectricalSim
{
    public sealed partial class SimulationController
    {
        private readonly Dictionary<string, SceneIoDeviceRuntime> sceneIoDevices = new Dictionary<string, SceneIoDeviceRuntime>();
        private readonly List<SceneIoView> sceneIoViews = new List<SceneIoView>();
        private LiquidTankView liquidView;
        private double liquidStepAccumulator;
        private string savedLiquidConfiguration = "";
        public LiquidSimulationRuntime Liquid { get; private set; }
        public IReadOnlyDictionary<string, SceneIoDeviceRuntime> SceneIoDevices => sceneIoDevices;
        public IReadOnlyList<SceneIoView> SceneIoViews => sceneIoViews;
        public SceneIoView SelectedSceneIo { get; private set; }
        public SceneIoPropertiesPresenter SceneIoProperties { get; private set; }

        public void RegisterSceneIo(Transform environment, Font font, Canvas canvas)
        {
            if (environment == null) return;
            Liquid = new LiquidSimulationRuntime();
            foreach (var definition in SceneIoCatalog.Devices)
            {
                var model = RequireSceneIoModel(environment, definition.ModelPath);
                var runtime = new SceneIoDeviceRuntime(definition);
                sceneIoDevices.Add(definition.Id, runtime);
                graph.RegisterDevice(runtime);
                foreach (var port in definition.Ports)
                {
                    var physical = definition.Prefix + "_" + port;
                    if (!portViews.ContainsKey("DuanZiPai_8." + physical) ||
                        !devices["DuanZiPai_8"].FixedLinks.Any(l => l.A == physical && l.B == runtime.Port(port)))
                        throw new InvalidOperationException("场景器件端子未绑定：" + definition.Name + "_" + port);
                }
                AddSceneIoView(model, definition.Id, font);
            }
            foreach (var pump in SceneIoCatalog.Pumps)
            {
                if (!devices.ContainsKey(pump.MotorId)) throw new InvalidOperationException("泵对应电机缺失：" + pump.MotorId);
                AddSceneIoView(RequireSceneIoModel(environment, pump.ModelPath), pump.Name, font);
                AddSceneIoView(RequireSceneIoModel(environment, MotorBindingDefinition.Find(pump.MotorId).ModelPath), pump.Name, font);
            }
            var mixer = RequireSceneIoModel(environment, SceneIoCatalog.MixerModelPath);
            if (!devices.TryGetValue(SceneIoCatalog.MixerMotorId, out var mixerMotor))
                throw new InvalidOperationException("搅拌机对应电机缺失：" + SceneIoCatalog.MixerMotorId);
            AddSceneIoView(mixer, SceneIoCatalog.MixerName, font);
            AddSceneIoView(RequireSceneIoModel(environment, MotorBindingDefinition.Find(SceneIoCatalog.MixerMotorId).ModelPath), SceneIoCatalog.MixerName, font);
            // Only the shaft/paddle mesh rotates; the motor housing and tank stay fixed.
            var paddles = RequireSceneIoModel(mixer, "mesh/JiaoBanJi");
            var mixerAxis = new GameObject("MixerRotationAxis").transform;
            mixerAxis.SetParent(mixer, false);
            mixerAxis.position = paddles.position;
            mixerAxis.rotation = Quaternion.LookRotation(mixer.up, mixer.forward);
            mixer.gameObject.AddComponent<MotorRotorView>().Initialize(mixerMotor, mixerAxis, new[] { paddles });
            var tank = RequireSceneIoModel(environment, SceneIoCatalog.EnvironmentPath + "/mesh/View/YeTiHunHe/jiaobanxiang");
            AddSceneIoView(tank, "TANK", font);
            liquidView = tank.gameObject.AddComponent<LiquidTankView>();
            liquidView.Initialize(environment, this);
            foreach (var runtime in sceneIoDevices.Values.Where(d => d.Definition.IsSensor))
            {
                var probe = RequireSceneIoModel(environment, runtime.Definition.ModelPath);
                runtime.TriggerLevel = liquidView.LevelAtWorldHeight(probe.position.y);
                if (runtime.TriggerLevel <= 0 || runtime.TriggerLevel >= 1)
                    throw new InvalidOperationException("液位探头超出罐体范围：" + runtime.Definition.Name);
            }
            UpdateLiquidSensors(true);
            SceneIoProperties = new GameObject("Scene IO Properties", typeof(RectTransform)).AddComponent<SceneIoPropertiesPresenter>();
            SceneIoProperties.Initialize(this, canvas, font);
            savedLiquidConfiguration = LiquidConfigurationSignature();
        }

        private static Transform RequireSceneIoModel(Transform environment, string path)
            => environment.Find(path) ?? throw new InvalidOperationException("场景液位器件模型缺失：" + path);
        private void AddSceneIoView(Transform model, string id, Font font)
        {
            var view = model.gameObject.AddComponent<SceneIoView>();
            view.Initialize(this, id, font);
            sceneIoViews.Add(view);
        }
        public void SelectSceneIo(SceneIoView view)
        {
            if (view != null)
            {
                if (!sceneIoViews.Contains(view) || Mode != SimulationMode.View && Mode != SimulationMode.Simulate) return;
                ClearWireSelection(); SelectPlc(null); SelectRelay(null); SelectContactor(null); SelectThermalRelay(null); SelectPanelDevice(null);
            }
            SelectedSceneIo = view;
            SceneIoProperties?.Show(view);
        }
        public void ConfigureLiquid(LiquidConfiguration configuration)
        {
            if (Mode == SimulationMode.Simulate || IsFileOperationActive) throw new InvalidOperationException("请退出仿真后修改液位配置。");
            Liquid.Configure(configuration, false);
        }
        public void ResetLiquid()
        {
            if (Liquid == null) return;
            if (Mode == SimulationMode.Simulate || IsFileOperationActive) throw new InvalidOperationException("请退出仿真后重置液位。");
            ResetLiquidState();
        }
        private void ResetLiquidState()
        {
            Liquid?.Reset();
            liquidStepAccumulator = 0;
            UpdateLiquidSensors(true);
            liquidView?.ResetVisuals();
            liquidView?.Refresh();
        }
        private void UpdateLiquidSensors(bool reset = false)
        {
            if (Liquid == null) return;
            foreach (var sensor in sceneIoDevices.Values) sensor.SetLevel(Liquid.Level, reset);
        }

        // Callable by deterministic scene tests. Re-solving after liquid updates uses
        // zero time so motor coast/braking and the liquid advance exactly once per tick.
        public SimulationSnapshot AdvanceSimulation(float elapsedSeconds)
        {
            if (Liquid == null) return graph.Solve(elapsedSeconds);
            if (Mode != SimulationMode.Simulate || IsFileOperationActive)
            {
                liquidStepAccumulator = 0;
                if (IsFileOperationActive) Liquid.Pause();
                else { Liquid.AdvanceIdle(Math.Max(0, elapsedSeconds)); liquidView?.Refresh(); }
                return graph.Solve(IsFileOperationActive ? 0 : elapsedSeconds);
            }
            liquidStepAccumulator += Math.Max(0, elapsedSeconds);
            var snapshot = lastSnapshot;
            while (liquidStepAccumulator + 1e-9 >= 0.02)
            {
                snapshot = graph.Solve(0.02f);
                Liquid.Advance(0.02, snapshot.GetMotorSpeedRpm("M_DOUBLE"), snapshot.GetMotorSpeedRpm("M1"),
                    sceneIoDevices["SOLENOID1"].IsActive, sceneIoDevices["SOLENOID2"].IsActive, sceneIoDevices["SOLENOID3"].IsActive);
                UpdateLiquidSensors();
                snapshot = graph.Solve(0);
                liquidStepAccumulator -= 0.02;
            }
            liquidView?.Refresh();
            return snapshot ?? graph.Solve(0);
        }

        public string DescribeSceneIo(string id)
        {
            if (Liquid == null) return "";
            if (id == "TANK")
                return $"混合罐 · 液位属性\n液位：{Liquid.Level * 100:F1}%\n泵1进液：{Liquid.Pump1Flow * 100:F2}%/秒{(Liquid.Pump1Transporting ? " · 输送中" : "")}\n泵2进液：{Liquid.Pump2Flow * 100:F2}%/秒{(Liquid.Pump2Transporting ? " · 输送中" : "")}\n重力排液：{Liquid.DrainFlow * 100:F2}%/秒\n净流量：{Liquid.NetFlow * 100:+0.00;-0.00;0.00}%/秒\n累计溢流：{Liquid.OverflowVolume * 100:F2}%罐容\n" +
                    (Liquid.IsOverflowing ? "报警：满罐溢流，进液阀仍由接线控制" : Mode == SimulationMode.Simulate ? "液位仿真运行中" : "液位已暂停") +
                    "\n标定转速：1450转/分钟\n修改初始值后，点击重置液位生效。";
            if (id == SceneIoCatalog.MixerName)
            {
                var rpm = devices[SceneIoCatalog.MixerMotorId].ActualSpeedRpm;
                var binding = MotorBindingDefinition.Find(SceneIoCatalog.MixerMotorId);
                return $"搅拌电机 · 关联属性\n柜体位置：正面左侧\n柜体电机：{binding.Label}（{binding.Id}）\n电机安装位：{binding.Nut}\n场景搅拌机安装位：{SceneIoCatalog.MixerMount}\n实际转速：{rpm:F1} 转/分钟\n方向：{(rpm > 0 ? "正转" : rpm < 0 ? "反转" : "停止")}\n搅拌轴和叶片跟随电机实际转速。\n供电及控制：共用柜体电机接线\n接线端口：M2.U、M2.V、M2.W\n末端端口：M2.U2、M2.V2、M2.W2";
            }
            var pump = SceneIoCatalog.Pumps.FirstOrDefault(p => p.Name == id);
            if (pump != null)
            {
                var rpm = devices[pump.MotorId].ActualSpeedRpm;
                var valve = sceneIoDevices[pump.ValveId];
                var flow = pump.Index == 0 ? Liquid.Pump1Flow : Liquid.Pump2Flow;
                var transporting = pump.Index == 0 ? Liquid.Pump1Transporting : Liquid.Pump2Transporting;
                return $"{pump.Name} · 电机与管路属性\n柜体电机：{MotorBindingDefinition.Find(pump.MotorId).Label}（{pump.MotorId}）\n电机安装位：{MotorBindingDefinition.Find(pump.MotorId).Nut}\n场景泵安装位：{pump.Mount}\n实际转速：{rpm:F1} 转/分钟\n方向：{(rpm > 0 ? "正转" : rpm < 0 ? "反转" : "停止")}\n对应阀门：{valve.Definition.Name}\n阀门：{(valve.IsActive ? "打开" : "关闭")}\n实际进液：{flow * 100:F2}%/秒{(transporting ? " · 输送中，尚未到达罐内" : "")}\n正向转速决定流速，阀门只控制通断。";
            }
            if (!sceneIoDevices.TryGetValue(id, out var runtime)) return "";
            var d = runtime.Definition;
            var rows = new List<string> { d.Name + " · 属性", d.IsSensor ? "类型：PNP常开液位传感器" : "类型：常闭电磁阀", "额定电压：DC 24V", "供电：" + runtime.SupplyStatus, "安装位：" + d.Mount };
            if (d.IsSensor)
            {
                rows.Add($"触发高度：{runtime.TriggerLevel * 100:F1}%罐高");
                rows.Add("液位检测：" + (runtime.Wet ? "达到探头" : "低于探头"));
                var potential = lastSnapshot?.GetPotential(runtime.Port("SIGNAL")) ?? ElectricalPotential.Floating;
                rows.Add("输出：" + (runtime.SignalShortCircuit ? "SIGNAL与GND短接，输出已保护断开" : potential == ElectricalPotential.Conflict ? "信号端电势冲突" : runtime.IsActive ? "24V" : "高阻"));
            }
            else rows.Add("阀门：" + (runtime.IsActive ? "打开" : "关闭") + (id == "SOLENOID3" ? " · 重力排液" : " · 进液"));
            rows.Add("所属端子排：场景中传感器、电磁阀端子");
            rows.AddRange(d.Ports.Select(p => d.Name + "_" + p + " → DuanZiPai_8." + d.Prefix + "_" + p));
            return string.Join("\n", rows);
        }

        private string LiquidConfigurationSignature() => Liquid == null ? "" : JsonConvert.SerializeObject(Liquid.Configuration);
        private void ExportLiquidConfiguration(Cc3dDocument document)
        {
            if (Liquid != null) document.Extra["liquidConfiguration"] = JObject.FromObject(Liquid.Configuration);
        }
        private LiquidConfiguration ReadLiquidConfiguration(Cc3dDocument document)
        {
            var config = document.Extra.TryGetValue("liquidConfiguration", out var token)
                ? token.ToObject<LiquidConfiguration>() : new LiquidConfiguration();
            if (config == null) throw new ArgumentException("液位配置不能为空。");
            config.Validate();
            return config;
        }
    }
}
