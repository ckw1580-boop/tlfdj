using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using UnityEngine;

namespace ElectricalSim
{
    public sealed partial class SimulationController
    {
        private readonly List<ContactorView> contactorViews = new List<ContactorView>();
        private readonly List<ContactorView> rearContactorViews = new List<ContactorView>();
        private Texture2D contactorSchematicTexture;
        public IReadOnlyList<ContactorView> ContactorViews => contactorViews;
        public IReadOnlyList<ContactorView> RearContactorViews => rearContactorViews;
        public ContactorView SelectedContactor { get; private set; }
        public ContactorView SchematicContactor { get; private set; }
        public ContactorPropertiesPresenter ContactorProperties { get; private set; }

        public void RegisterContactors(Transform environment, Font font, Canvas canvas)
        {
            if (environment == null) return;
            foreach (var definition in ContactorDefinition.All)
            {
                var model = environment.Find(definition.ModelPath);
                if (model == null) throw new InvalidOperationException("交流接触器模型缺失：" + definition.ModelPath);
                var runtime = devices[definition.RuntimeId];
                var bindings = new Dictionary<string, ElectricalPortView>();
                foreach (var terminal in ContactorDefinition.Ports)
                {
                    var name = definition.BindingName(terminal);
                    var target = definition.RuntimeId + "." + terminal;
                    var matches = portViews.Values.Where(p => p.PortName == name).ToArray();
                    if (matches.Length != 1) throw new InvalidOperationException("交流接触器连接点缺失或重复：" + name);
                    var port = matches[0];
                    var links = devices.Values.Where(d => d.Kind == ElectricalDeviceKind.Terminal)
                        .SelectMany(d => d.FixedLinks.Where(l => l.B == target).Select(l => new { Device = d.DeviceId, Link = l })).ToArray();
                    if (!runtime.Ports.Contains(terminal) || links.Length != 1 || links[0].Device != port.DeviceId || links[0].Link.A != port.PortName)
                        throw new InvalidOperationException("交流接触器连接点没有唯一关联逻辑端子：" + target);
                    bindings.Add(terminal, port);
                }
                var view = model.gameObject.AddComponent<ContactorView>();
                view.Initialize(definition, runtime, bindings);
                contactorViews.Add(view);
                if (definition.RearModelPath != null)
                {
                    var rearModel = environment.Find(definition.RearModelPath);
                    if (rearModel == null) throw new InvalidOperationException("背部接触器模型缺失：" + definition.RearModelPath);
                    var rearBindings = new Dictionary<string, ElectricalPortView>();
                    foreach (var terminal in ContactorDefinition.Ports)
                    {
                        var matches = portViews.Values.Where(p => p.DeviceId == definition.RuntimeId && p.PortName == terminal).ToArray();
                        if (matches.Length != 1) throw new InvalidOperationException("背部接触器连接点缺失或重复：" + definition.RuntimeId + "." + terminal);
                        var port = matches[0];
                        var anchor = port.GetOriginalAnchor(TrainingViewPreset.FaultBack, false);
                        if (anchor == null || !anchor.IsChildOf(rearModel))
                            throw new InvalidOperationException("背部接触器连接点未绑定实际模型：" + port.QualifiedPort);
                        rearBindings.Add(terminal, port);
                    }
                    var rearView = rearModel.gameObject.AddComponent<ContactorView>();
                    rearView.Initialize(definition, runtime, rearBindings, true);
                    rearContactorViews.Add(rearView);
                }
            }
            contactorSchematicTexture = Resources.Load<Texture2D>("ContactorSchematic");
            if (contactorSchematicTexture == null) throw new InvalidOperationException("交流接触器原理图资源缺失：ContactorSchematic");
            ContactorProperties = new GameObject("Contactor Properties", typeof(RectTransform)).AddComponent<ContactorPropertiesPresenter>();
            ContactorProperties.Initialize(this, canvas, font);
            Debug.Log("[ContactorValidation] 4 个本体、72/72 端子排连接点绑定通过。");
            Debug.Log("[ContactorValidation] 背部 3 个本体、54/54 本体连接点绑定通过，与正面共用运行时状态。");
        }

        private bool IsRegisteredContactor(ContactorView view)
            => contactorViews.Contains(view) || rearContactorViews.Contains(view);

        public void SelectContactor(ContactorView view)
        {
            if (view != null)
            {
                if (!IsRegisteredContactor(view) || Mode != SimulationMode.View && Mode != SimulationMode.Simulate && !(view.IsRear && Mode == SimulationMode.Fault)) return;
                ClearWireSelection(); SelectThermalRelay(null); SelectRelay(null); SelectPanelDevice(null); SelectPlc(null);
            }
            SelectedContactor = view;
            ContactorProperties?.Show(view);
        }

        public void ShowContactorSchematic(ContactorView view)
        {
            if (Mode != SimulationMode.Wiring || view == null || !IsRegisteredContactor(view)) return;
            SchematicRelay = null; SchematicThermalRelay = null;
            SchematicContactor = view;
            RelaySchematic.Show(view.Definition.Id + (view.IsRear ? " · 背部接触器原理图" : " · 交流接触器原理图"), contactorSchematicTexture);
        }
        public void HideContactorSchematic() => HideRelaySchematic();

        public string DescribeContactor(ContactorView view)
        {
            if (view == null) return string.Empty;
            var runtime = view.Runtime;
            var voltage = runtime.ContactorCoilVoltage;
            var rows = new List<string>
            {
                view.Definition.Id + " · 交流接触器 · AC " + ContactorDefinition.RatedAcVoltage + "V",
                "位置：柜体" + (view.IsRear ? "背部" : "正面"),
                "对应运行时编号：" + runtime.DeviceId,
                "状态：" + (runtime.IsActive ? "已吸合" : "已释放"),
                "线圈：A1–A2 · 交流 220V · 无极性",
                "线圈电压：" + (double.IsNaN(voltage) ? "电位冲突" : voltage.ToString("0.#", CultureInfo.InvariantCulture) + " V AC"),
                "", "触点实时状态"
            };
            foreach (var contact in ContactorDefinition.Contacts)
                rows.Add(ContactorDefinition.TerminalLabel(contact.Input) + "–" + ContactorDefinition.TerminalLabel(contact.Output) +
                    " " + (contact.Main ? "主触点 " : "辅助 ") + (contact.NormallyClosed ? "常闭：" : "常开：") +
                    (runtime.IsActive != contact.NormallyClosed ? "闭合" : "断开"));
            rows.Add(""); rows.Add(view.IsRear ? "背部本体端子绑定（与正面端子排同一电气节点）" : "端子绑定");
            foreach (var terminal in ContactorDefinition.Ports)
            {
                rows.Add(ContactorDefinition.TerminalLabel(terminal) + " · " + ContactorDefinition.PortRole(terminal));
                rows.Add("  → " + view.Bindings[terminal].QualifiedPort);
                if (view.IsRear)
                    rows.Add("  ↔ " + contactorViews.Single(v => v.Definition == view.Definition).Bindings[terminal].QualifiedPort);
            }
            return string.Join("\n", rows);
        }
    }
}
