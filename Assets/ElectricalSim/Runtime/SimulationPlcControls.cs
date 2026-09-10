using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace ElectricalSim
{
    public sealed partial class SimulationController
    {
        private readonly Dictionary<string, PlcSession> plcSessions = new Dictionary<string, PlcSession>();
        private readonly List<PlcDeviceView> plcViews = new List<PlcDeviceView>();
        private readonly List<Task> closingPlcs = new List<Task>();
        private readonly Dictionary<string, string> reportedPlcErrors = new Dictionary<string, string>();
        private PlcPropertiesPresenter plcProperties;
        private string savedPlcConfiguration = "";
        public IReadOnlyList<PlcDeviceView> PlcViews => plcViews;
        public IReadOnlyDictionary<string, PlcSession> PlcSessions => plcSessions;
        public PlcDeviceView SelectedPlc { get; private set; }
        public PlcPropertiesPresenter PlcProperties => plcProperties;
        public Func<IPlcTransport> PlcTransportFactory { get; set; }

        public void RegisterPlcs(Transform environment, Font font, Canvas canvas)
        {
            if (environment == null) return;
            for (var i = 0; i < 2; i++)
            {
                var id = "PLC_" + (i + 1);
                var model = environment.Find("Bench/ElectricBench/Nuts/" + (116 + i) + "/VirtualPLC");
                if (model == null) throw new InvalidOperationException("PLC 模型缺失：" + id);
                var runtime = new PlcDeviceRuntime(id);
                graph.RegisterDevice(runtime);
                var view = model.gameObject.AddComponent<PlcDeviceView>(); view.Initialize(runtime); plcViews.Add(view);
                plcSessions.Add(id, NewPlcSession(PlcConfiguration.Create(id)));
                foreach (var terminal in runtime.Ports)
                {
                    var name = id + "_" + terminal;
                    if (portViews.Values.Count(p => p.PortName == name) != 1)
                        throw new InvalidOperationException("PLC 连接点缺失或重复：" + name);
                    var target = id + "." + terminal;
                    if (!devices.Values.Any(d => d.Kind == ElectricalDeviceKind.Terminal && d.FixedLinks.Any(l => l.A == name && l.B == target)))
                        throw new InvalidOperationException("PLC 连接点没有关联逻辑端子：" + target);
                }
            }
            plcProperties = new GameObject("PLC Properties", typeof(RectTransform)).AddComponent<PlcPropertiesPresenter>();
            plcProperties.Initialize(this, canvas, font);
            savedPlcConfiguration = PlcConfigurationSignature();
        }
        private PlcSession NewPlcSession(PlcConfiguration config) => new PlcSession(config, () => PlcTransportFactory?.Invoke() ?? new S7PlcTransport());
        public void SelectPlc(PlcDeviceView view)
        {
            if (view != null) { ClearWireSelection(); SelectPanelDevice(null); SelectRelay(null); SelectContactor(null); SelectThermalRelay(null); }
            SelectedPlc = view;
            plcProperties?.Show(view == null ? null : plcSessions[view.Runtime.DeviceId]);
        }
        public void ConnectPlc(string id)
        {
            closingPlcs.RemoveAll(t => t.IsCompleted);
            if (closingPlcs.Count > 0) throw new InvalidOperationException("正在结束旧工程的 PLC 连接，请稍后再连接。");
            var config = plcSessions[id].Configuration;
            foreach (var other in plcSessions.Where(p => p.Key != id && p.Value.IsBusy))
            {
                var candidate = other.Value.Configuration;
                if (config.Ip == candidate.Ip && config.Port == candidate.Port && config.Rack == candidate.Rack && config.Slot == candidate.Slot &&
                    config.Inputs.Select(p => p.Address).Intersect(candidate.Inputs.Select(p => p.Address)).Any())
                    throw new InvalidOperationException("两台场景 PLC 不能同时写入同一真实 PLC 的重叠地址。");
            }
            plcSessions[id].Connect();
        }
        private void PreparePlcOutputs()
        {
            foreach (var view in plcViews)
            {
                var session = plcSessions[view.Runtime.DeviceId];
                view.Runtime.SetOutputs(Mode == SimulationMode.Simulate && session.HasFreshSample && !IsFileOperationActive, session.RemoteOutputs);
                if (session.State == PlcConnectionState.Faulted && (!reportedPlcErrors.TryGetValue(view.Runtime.DeviceId, out var previous) || previous != session.Error))
                {
                    reportedPlcErrors[view.Runtime.DeviceId] = session.Error;
                    SetStatus(view.Runtime.DeviceId + "：" + session.Error + "\n场景输出已断开，请手动重新连接。", true);
                }
                else if (session.State != PlcConnectionState.Faulted) reportedPlcErrors.Remove(view.Runtime.DeviceId);
            }
        }
        private void SamplePlcInputs()
        {
            foreach (var view in plcViews)
                plcSessions[view.Runtime.DeviceId].SetSimulation(Mode == SimulationMode.Simulate && !IsFileOperationActive, view.Runtime.InputStates);
        }
        private void PausePlcSimulation()
        {
            foreach (var view in plcViews)
            {
                plcSessions[view.Runtime.DeviceId].SetSimulation(false, null);
                view.Runtime.SetOutputs(false, null);
            }
        }
        private void StopPlcConnections()
        {
            PausePlcSimulation();
            foreach (var session in plcSessions.Values) closingPlcs.Add(session.DisconnectAsync());
        }
        private bool TrySelectPlcFromPointer()
        {
            if (!Input.GetMouseButtonDown(0) || Camera.main == null || EventSystem.current != null && EventSystem.current.IsPointerOverGameObject()) return false;
            if (Physics.Raycast(Camera.main.ScreenPointToRay(Input.mousePosition), out var hit, 100f))
            {
                var view = hit.collider.GetComponentInParent<PlcDeviceView>();
                if (view != null) { SelectPlc(view); return true; }
            }
            if (SelectedPlc != null) SelectPlc(null);
            return false;
        }
        public ElectricalPortView ResolvePlcTerminal(string id, string terminal) => portViews.Values.Single(p => p.PortName == id + "_" + terminal);
        public string PlcSupplyState(string id, string terminal)
        {
            var potential = lastSnapshot?.GetPotential(id + "." + terminal) ?? ElectricalPotential.Floating;
            switch (potential)
            {
                case ElectricalPotential.DcPositive24: return "+24V";
                case ElectricalPotential.DcNegative: return "0V";
                case ElectricalPotential.Conflict: return "冲突";
                case ElectricalPotential.Floating: return "悬空";
                default: return potential == ElectricalPotential.Neutral ? "N/PE" : "AC";
            }
        }
        private string PlcConfigurationSignature() => JsonConvert.SerializeObject(plcSessions.OrderBy(p => p.Key).Select(p => p.Value.Configuration).ToArray());
        private void ExportPlcConfigurations(Cc3dDocument document)
        {
            if (plcSessions.Count > 0) document.Extra["plcConfigurations"] = JArray.FromObject(plcSessions.OrderBy(p => p.Key).Select(p => p.Value.Configuration).ToArray());
        }
        private PlcConfiguration[] ReadPlcConfigurations(Cc3dDocument document)
        {
            if (plcSessions.Count == 0) return Array.Empty<PlcConfiguration>();
            if (!document.Extra.TryGetValue("plcConfigurations", out var token)) return new[] { PlcConfiguration.Create("PLC_1"), PlcConfiguration.Create("PLC_2") };
            var configs = token.ToObject<PlcConfiguration[]>();
            if (configs == null || configs.Length != 2 || configs.Any(c => c == null) || configs.Select(c => c.DeviceId).Distinct().Count() != 2)
                throw new ArgumentException("工程必须包含两台独立的 PLC 配置。");
            foreach (var config in configs) config.Validate();
            return configs;
        }
        private void ReplacePlcConfigurations(PlcConfiguration[] configs)
        {
            StopPlcConnections(); SelectPlc(null);
            foreach (var config in configs) plcSessions[config.DeviceId] = NewPlcSession(config);
            savedPlcConfiguration = PlcConfigurationSignature();
        }
    }
}
