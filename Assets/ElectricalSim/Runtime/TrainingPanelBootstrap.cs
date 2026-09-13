using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace ElectricalSim
{
    public sealed partial class TrainingSceneBootstrap
    {
        private readonly Dictionary<string, ElectricalDeviceRuntime> panelDevices = new Dictionary<string, ElectricalDeviceRuntime>();
        private readonly List<PanelDeviceView> panelViews = new List<PanelDeviceView>();
        private PanelPowerState panelPower;

        private void CreatePanelDevices()
        {
            if (originalEnvironment == null) return;
            foreach (var definition in PanelDeviceCatalog.Create())
            {
                var model = originalEnvironment.Find(definition.ModelPath);
                if (model == null) throw new InvalidOperationException("面板模型缺失：" + definition.ModelPath);
                var runtime = ElectricalDeviceRuntime.CreatePanel(definition);
                panelDevices.Add(definition.Id, runtime);
                var view = model.gameObject.AddComponent<PanelDeviceView>();
                view.Initialize(runtime);
                panelViews.Add(view);
                AddPanelRuntime(runtime, definition.Label);
                if (!string.IsNullOrEmpty(definition.RearModelPath))
                {
                    var rearModel = originalEnvironment.Find(definition.RearModelPath);
                    if (rearModel == null) throw new InvalidOperationException("背面按钮模型缺失：" + definition.RearModelPath);
                    // Both faces operate the same contacts and existing terminal-strip endpoints.
                    var rearView = rearModel.gameObject.AddComponent<PanelDeviceView>();
                    rearView.Initialize(runtime, true);
                    panelViews.Add(rearView);
                }
            }
            panelPower = new PanelPowerState(panelDevices);
            var dc = new ElectricalDeviceRuntime("TERMINAL_BUS", ElectricalDeviceKind.PowerSource, new[] { "DC_POSITIVE", "DC_NEGATIVE" })
                { IsDcSource = true, SupplyEnabled = () => panelPower.Enabled };
            AddPanelRuntime(dc, "柜内 DC 24V 电源");
            // Internal control-circuit terminals remain registered and inspectable, without new visible dots.
            panelDevices["PANEL_KEY"].AddFixedLink("NO1", "PANEL_ESTOP.COM2");
            panelDevices["PANEL_ESTOP"].AddFixedLink("NC2", "PANEL_STOP.COM2");
            panelDevices["PANEL_STOP"].AddFixedLink("NC2", "PANEL_START.COM1");
        }

        private void AddPanelRuntime(ElectricalDeviceRuntime runtime, string label)
        {
            var holder = new GameObject(runtime.DeviceId + " Runtime");
            holder.transform.SetParent(transform, false);
            var view = holder.AddComponent<ElectricalDeviceView>();
            view.Initialize(runtime, label);
            deviceViews.Add(view);
        }

        private bool CreateLegacyPanelControl(string id, bool normallyClosed)
        {
            if (panelDevices.Count == 0) return false;
            if (panelDevices.ContainsKey(id)) return true;
            var targetId = PanelDeviceCatalog.LegacyTarget(id);
            if (!panelDevices.TryGetValue(targetId, out var target)) return false;
            var runtime = ElectricalDeviceRuntime.CreatePushButton(id, normallyClosed);
            runtime.ControlTarget = target;
            runtime.AddFixedLink("COM", targetId + (normallyClosed ? ".COM2" : ".COM1"));
            runtime.AddFixedLink(normallyClosed ? "NC" : "NO", targetId + (normallyClosed ? ".NC2" : ".NO1"));
            AddPanelRuntime(runtime, target.PanelDefinition.Label + "（兼容 " + id + "）");
            return true;
        }

        private void ValidatePanelBindings()
        {
            if (panelDevices.Count == 0) return;
            var graph = controller.Graph;
            var count = 0;
            foreach (var board in deviceViews.Where(v => v.Runtime.Kind == ElectricalDeviceKind.Terminal))
            foreach (var link in board.Runtime.FixedLinks)
            {
                var dot = link.B.IndexOf('.');
                if (dot <= 0) continue;
                var id = link.B.Substring(0, dot);
                if (!panelDevices.ContainsKey(id) && id != "TERMINAL_BUS") continue;
                if (!graph.Devices.TryGetValue(id, out var target) || !target.Ports.Contains(link.B.Substring(dot + 1)))
                    throw new InvalidOperationException("面板绑定目标不存在：" + link.B);
                count++;
            }
            if (panelViews.Count(v => !v.IsRear) != 20 || panelViews.Count(v => v.IsRear) != 3 ||
                panelViews.Select(v => v.Runtime.DeviceId).Distinct().Count() != 20 || count != 72)
                throw new InvalidOperationException($"面板绑定数量错误：{panelViews.Count} 元件，{count} 端子");
            foreach (var rear in panelViews.Where(v => v.IsRear))
            foreach (var port in rear.Definition.Ports)
            {
                var anchor = ResolveFaultButtonTerminalAnchor(rear.Definition.Id + "_" + port);
                if (anchor == null || anchor.parent.parent.name != "DuanZiPai_5")
                    throw new InvalidOperationException("背面按钮连接点缺失：" + rear.Definition.Id + "." + port);
            }
            foreach (var runtime in panelDevices.Values.Where(d => !d.PanelDefinition.Internal && d.Kind != ElectricalDeviceKind.Indicator))
            {
                var links = runtime.FixedLinks.Concat(runtime.GetConductiveLinks());
                if (links.Any(l => l.A == "COM1" && l.B == "COM2" || l.A == "COM2" && l.B == "COM1"))
                    throw new InvalidOperationException("面板公共端被短接：" + runtime.DeviceId);
            }
            Debug.Log($"[PanelValidation] 20 个正面元件、3 个背面按钮，{count}/72 端子绑定通过。");
        }
    }
}
