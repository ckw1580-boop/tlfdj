using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace ElectricalSim
{
    public sealed partial class SimulationController
    {
        private readonly List<ThermalRelayView> thermalRelayViews = new List<ThermalRelayView>();
        private Texture2D thermalRelaySchematicTexture;
        public IReadOnlyList<ThermalRelayView> ThermalRelayViews => thermalRelayViews;
        public ThermalRelayView SelectedThermalRelay { get; private set; }
        public ThermalRelayView SchematicThermalRelay { get; private set; }
        public ThermalRelayPropertiesPresenter ThermalRelayProperties { get; private set; }

        public void RegisterThermalRelays(Transform environment, Font font, Canvas canvas)
        {
            if (environment == null) return;
            foreach (var definition in ThermalRelayDefinition.All)
            {
                var model = environment.Find(definition.ModelPath);
                if (model == null) throw new InvalidOperationException("热继电器模型缺失：" + definition.ModelPath);
                if (!devices.TryGetValue(definition.RuntimeId, out var runtime))
                {
                    runtime = ElectricalDeviceRuntime.CreateThermalRelay(definition.RuntimeId);
                    devices.Add(definition.RuntimeId, runtime);
                    deviceNames.Add(definition.RuntimeId, "热继电器 " + definition.Id);
                    graph.RegisterDevice(runtime);
                }
                var bindings = new Dictionary<string, ElectricalPortView>();
                foreach (var terminal in ThermalRelayDefinition.Ports)
                {
                    var matches = portViews.Values.Where(p => definition.IsRear
                        ? p.DeviceId == definition.RuntimeId && p.PortName == terminal
                        : p.PortName == definition.BindingName(terminal)).ToArray();
                    if (matches.Length != 1) throw new InvalidOperationException("热继电器端子缺失或重复：" + definition.Id + "." + terminal);
                    var port = matches[0];
                    if (definition.IsRear)
                    {
                        var anchor = port.GetOriginalAnchor(TrainingViewPreset.FaultBack, false);
                        if (anchor == null || !anchor.IsChildOf(model))
                            throw new InvalidOperationException("背部热继电器端子未绑定本体：" + port.QualifiedPort);
                    }
                    else
                    {
                        var target = definition.RuntimeId + "." + terminal;
                        var links = devices.Values.Where(d => d.Kind == ElectricalDeviceKind.Terminal)
                            .SelectMany(d => d.FixedLinks.Where(l => l.B == target).Select(l => new { Device = d.DeviceId, Link = l })).ToArray();
                        if (links.Length != 1 || links[0].Device != port.DeviceId || links[0].Link.A != port.PortName)
                            throw new InvalidOperationException("热继电器端子映射无效：" + target);
                    }
                    bindings.Add(terminal, port);
                }
                var view = model.gameObject.AddComponent<ThermalRelayView>();
                view.Initialize(definition, runtime, bindings); thermalRelayViews.Add(view);
            }
            thermalRelaySchematicTexture = Resources.Load<Texture2D>("ThermalRelaySchematic");
            if (thermalRelaySchematicTexture == null) throw new InvalidOperationException("热继电器原理图资源缺失");
            ThermalRelayProperties = new GameObject("Thermal Relay Properties", typeof(RectTransform)).AddComponent<ThermalRelayPropertiesPresenter>();
            ThermalRelayProperties.Initialize(this, canvas, font);
            Debug.Log("[ThermalRelayValidation] 3 个独立热继电器、30/30 连接点绑定通过（正面 20，背部 10）。");
        }
        public void SelectThermalRelay(ThermalRelayView view)
        {
            if (view != null)
            {
                if (!thermalRelayViews.Contains(view) || Mode != SimulationMode.View && Mode != SimulationMode.Simulate && !(view.IsRear && Mode == SimulationMode.Fault)) return;
                ClearWireSelection(); SelectRelay(null); SelectContactor(null); SelectPanelDevice(null); SelectPlc(null);
            }
            SelectedThermalRelay = view; ThermalRelayProperties?.Show(view);
        }
        public void SetThermalRelayTripped(ThermalRelayView view, bool tripped)
        {
            if (Mode != SimulationMode.Simulate || view == null || !thermalRelayViews.Contains(view)) return;
            view.Runtime.SetControl(tripped);
        }
        public void ShowThermalRelaySchematic(ThermalRelayView view)
        {
            if (Mode != SimulationMode.Wiring || view == null || !thermalRelayViews.Contains(view)) return;
            SchematicRelay = null; SchematicContactor = null; SchematicThermalRelay = view;
            RelaySchematic.Show(view.Definition.Id + " · 热继电器原理图", thermalRelaySchematicTexture);
        }
        public void HideThermalRelaySchematic() => HideRelaySchematic();
        public string DescribeThermalRelay(ThermalRelayView view)
        {
            if (view == null) return string.Empty;
            var trip = view.Runtime.IsTripped;
            var rows = new List<string> { view.Definition.Id + " · 热继电器", "位置：柜体" + (view.IsRear ? "背部" : "正面"),
                "对应运行时编号：" + view.Runtime.DeviceId, "状态：" + (trip ? "已脱扣" : "正常／已复位"),
                "三只独立动作 · 无线圈", "整定电流／脱扣延时：未配置", "", "实时通断" };
            foreach (var pair in ThermalRelayDefinition.Heaters)
                rows.Add(ThermalRelayDefinition.TerminalLabel(pair.A) + "–" + ThermalRelayDefinition.TerminalLabel(pair.B) + " 热元件：导通");
            rows.Add("95–96 常闭：" + (trip ? "断开" : "闭合"));
            rows.Add("97–98 常开：" + (trip ? "闭合" : "断开"));
            rows.Add("脱扣通过 95–96 切断接触器控制回路。");
            rows.Add(""); rows.Add("实际端子绑定");
            foreach (var terminal in ThermalRelayDefinition.Ports)
            {
                rows.Add(ThermalRelayDefinition.TerminalLabel(terminal) + " · " + ThermalRelayDefinition.PortRole(terminal));
                rows.Add("  → " + view.Bindings[terminal].QualifiedPort);
            }
            return string.Join("\n", rows);
        }
    }
}
