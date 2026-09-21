using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace ElectricalSim
{
    public sealed partial class SimulationController
    {
        private readonly List<PanelDeviceView> panelControls = new List<PanelDeviceView>();
        private PanelDeviceView heldPanelButton;
        public IReadOnlyList<PanelDeviceView> PanelControls => panelControls;
        public PanelPowerState PanelPower { get; private set; }
        public PanelDeviceView SelectedPanelDevice { get; private set; }
        public event Action<PanelDeviceView> PanelSelectionChanged;
        public PowerTerminalBlockView SelectedPowerTerminalBlock { get; private set; }
        public event Action PowerTerminalSelectionChanged;

        public void RegisterPanel(IEnumerable<PanelDeviceView> views, PanelPowerState power)
        {
            panelControls.Clear();
            panelControls.AddRange(views);
            PanelPower = power;
        }

        public void SelectPanelDevice(PanelDeviceView view)
        {
            SelectSceneIo(null);
            SelectedPowerTerminalBlock = null;
            PowerTerminalSelectionChanged?.Invoke();
            if (view != null) { ClearWireSelection(); SelectFrontBreaker(null); SelectPlc(null); SelectRelay(null); SelectContactor(null); SelectThermalRelay(null); }
            SelectedPanelDevice = view;
            PanelSelectionChanged?.Invoke(view);
        }

        public void SelectPowerTerminalBlock(PowerTerminalBlockView view)
        {
            if (view != null) SelectFrontBreaker(null);
            SelectPanelDevice(null);
            if (view != null)
            {
                ClearWireSelection(); SelectPlc(null); SelectRelay(null); SelectContactor(null); SelectThermalRelay(null);
            }
            SelectedPowerTerminalBlock = view;
            PowerTerminalSelectionChanged?.Invoke();
        }

        public string DescribePowerTerminalBlock()
        {
            if (SelectedPowerTerminalBlock == null) return string.Empty;
            return SelectedPowerTerminalBlock.Describe(PanelPower != null && PanelPower.Enabled);
        }

        public void PressPanelDevice(PanelDeviceView view)
        {
            if (view == null || !panelControls.Contains(view) || Mode != SimulationMode.View && Mode != SimulationMode.Simulate && !CanOperateFaultControls) return;
            SelectPanelDevice(view);
            if (Mode != SimulationMode.Simulate && !CanOperateFaultControls || view.Definition.Control == PanelControlKind.Indicator) return;
            ReleasePanelButton();
            if (view.Definition.Momentary)
            {
                heldPanelButton = view;
                view.Runtime.SetControl(true);
            }
            else view.Runtime.SetControl(!view.Runtime.IsPressed);
        }

        public void ReleasePanelButton()
        {
            if (heldPanelButton != null) heldPanelButton.Runtime.SetControl(false);
            heldPanelButton = null;
        }

        private void OnApplicationFocus(bool focused) { if (!focused) { ReleasePanelButton(); Multimeter?.SuspendPointer(); } }
        private void OnApplicationPause(bool paused) { if (paused) { ReleasePanelButton(); Multimeter?.SuspendPointer(); } }
        private void OnDisable() { ReleasePanelButton(); StopPlcConnections(); Multimeter?.Deselect(); }

        public string DescribePanelDevice(PanelDeviceView view)
        {
            if (view == null) return string.Empty;
            var d = view.Definition;
            var runtime = view.Runtime;
            var title = (d.Internal ? d.Label : d.Id + " · " + d.Label) + (d.RatedDcVoltage > 0 ? " · DC 24V" : "");
            var state = d.Control == PanelControlKind.Indicator ? runtime.PanelStatus :
                d.Control == PanelControlKind.EmergencyStop ? runtime.IsPressed ? "锁定（点击旋转复位）" : "已复位" :
                d.Control == PanelControlKind.Key ? runtime.IsPressed ? "开启" : "关闭" :
                d.Control == PanelControlKind.Selector ? runtime.IsPressed ? "工作位 · 保持" : "零位 · 保持" : runtime.IsPressed ? "按下" : "释放";
            var rows = new List<string> { title, "状态：" + state };
            if (view.IsRear) rows[0] += "（柜体背面）";
            if (d.Internal)
                rows.Add("柜内预接 · 总供电" + (PanelPower.Enabled ? "开启" : "关闭"));
            if (d.Control != PanelControlKind.Indicator)
            {
                rows.Add("COM1–NO1：" + (runtime.IsPressed ? "闭合" : "断开"));
                rows.Add("COM2–NC2：" + (runtime.IsPressed ? "断开" : "闭合"));
            }
            if (!d.Internal)
            {
                foreach (var port in d.Ports)
                {
                    var node = d.Id + "." + port;
                    if (view.IsRear)
                    {
                        var anchor = ResolveWireAnchor(node, TrainingViewPreset.FaultBack, false);
                        rows.Add(port + " → " + (anchor != null ? anchor.parent.parent.name + "." + anchor.name : "未绑定"));
                        continue;
                    }
                    var physical = devices.Values.Where(v => v.Kind == ElectricalDeviceKind.Terminal)
                        .SelectMany(v => v.FixedLinks.Where(l => l.B == node).Select(l => l.A));
                    rows.Add(port + " → " + string.Join("、", physical));
                }
            }
            return string.Join("\n", rows);
        }
    }
}
