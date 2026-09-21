using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using UnityEngine;

namespace ElectricalSim
{
    public sealed partial class SimulationController
    {
        private readonly List<IntermediateRelayView> relayViews = new List<IntermediateRelayView>();
        public IReadOnlyList<IntermediateRelayView> RelayViews => relayViews;
        public IntermediateRelayView SelectedRelay { get; private set; }
        public RelayPropertiesPresenter RelayProperties { get; private set; }
        public IntermediateRelayView SchematicRelay { get; private set; }
        public RelaySchematicPresenter RelaySchematic { get; private set; }

        public void RegisterIntermediateRelays(Transform environment, Font font, Canvas canvas)
        {
            if (environment == null) return;
            foreach (var definition in IntermediateRelayDefinition.All)
            {
                var model = environment.Find(definition.ModelPath);
                if (model == null) throw new InvalidOperationException("中间继电器模型缺失：" + definition.Id);
                var bindings = new Dictionary<string, ElectricalPortView>();
                foreach (var terminal in IntermediateRelayDefinition.Ports)
                {
                    var name = definition.Id + "_" + terminal;
                    var target = definition.Id + "." + terminal;
                    var matches = portViews.Values.Where(p => p.PortName == name).ToArray();
                    if (matches.Length != 1)
                        throw new InvalidOperationException("中间继电器连接点缺失或重复：" + name);
                    var port = matches[0];
                    var links = devices.Values.Where(d => d.Kind == ElectricalDeviceKind.Terminal)
                        .SelectMany(d => d.FixedLinks.Where(l => l.B == target).Select(l => new { Device = d.DeviceId, Link = l })).ToArray();
                    if (links.Length != 1 || links[0].Device != port.DeviceId || links[0].Link.A != port.PortName)
                        throw new InvalidOperationException("中间继电器连接点没有唯一关联逻辑端子：" + target);
                    bindings.Add(terminal, port);
                }
                var runtime = ElectricalDeviceRuntime.CreateIntermediateRelay(definition.Id);
                var view = model.gameObject.AddComponent<IntermediateRelayView>();
                view.Initialize(definition, runtime, bindings);
                relayViews.Add(view);
                devices.Add(definition.Id, runtime);
                deviceNames.Add(definition.Id, "中间继电器 " + definition.Id);
                graph.RegisterDevice(runtime);
            }
            RelayProperties = new GameObject("Relay Properties", typeof(RectTransform)).AddComponent<RelayPropertiesPresenter>();
            RelayProperties.Initialize(this, canvas, font);
            RelaySchematic = new GameObject("Relay Schematic", typeof(RectTransform)).AddComponent<RelaySchematicPresenter>();
            RelaySchematic.Initialize(this, canvas, font);
        }

        public void ShowRelaySchematic(IntermediateRelayView view)
        {
            if (Mode != SimulationMode.Wiring || view == null || !relayViews.Contains(view)) return;
            SchematicRelay = view;
            SchematicContactor = null; SchematicThermalRelay = null;
            RelaySchematic.Show(view);
        }

        public void HideRelaySchematic()
        {
            SchematicRelay = null;
            SchematicContactor = null;
            SchematicThermalRelay = null;
            RelaySchematic?.Hide();
        }

        public void SelectRelay(IntermediateRelayView view)
        {
            if (view != null)
            {
                if (!relayViews.Contains(view) || Mode != SimulationMode.View && Mode != SimulationMode.Simulate) return;
                SelectFrontBreaker(null);
                ClearWireSelection();
                SelectPanelDevice(null);
                SelectPlc(null);
                SelectContactor(null); SelectThermalRelay(null);
            }
            SelectedRelay = view;
            RelayProperties?.Show(view);
        }

        public string DescribeRelay(IntermediateRelayView view)
        {
            if (view == null) return string.Empty;
            var runtime = view.Runtime;
            var voltage = runtime.RelayCoilVoltage;
            var rows = new List<string>
            {
                runtime.DeviceId + " · 中间继电器 · DC " + IntermediateRelayDefinition.RatedDcVoltage + "V",
                "状态：" + (runtime.IsActive ? "已吸合" : "已释放"),
                "线圈：13（+24V / PLC 输出）— 14（0V）",
                "线圈电压：" + (double.IsNaN(voltage) ? "电位冲突" : voltage.ToString("0.#", CultureInfo.InvariantCulture) + " V") +
                    (voltage < 0 ? "（反接）" : ""),
                "", "四组转换触点"
            };
            foreach (var contact in IntermediateRelayDefinition.Contacts)
                rows.Add(contact.Common + "–" + contact.NormallyClosed + " 常闭：" + (runtime.IsActive ? "断开" : "闭合") +
                    "    " + contact.Common + "–" + contact.NormallyOpen + " 常开：" + (runtime.IsActive ? "闭合" : "断开"));
            rows.Add("");
            rows.Add("端子绑定");
            foreach (var terminal in IntermediateRelayDefinition.Ports)
            {
                rows.Add(terminal + " · " + IntermediateRelayDefinition.PortRole(terminal));
                rows.Add(view.Bindings.TryGetValue(terminal, out var port) && port != null
                    ? "  → " + port.QualifiedPort : "  → 未绑定");
            }
            return string.Join("\n", rows);
        }
    }
}
