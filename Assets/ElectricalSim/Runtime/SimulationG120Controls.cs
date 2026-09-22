using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace ElectricalSim
{
    public sealed partial class SimulationController
    {
        public InverterDriveRuntime InverterDrive { get; private set; }
        public G120ControlRuntime InverterControls => InverterDrive?.Control;
        public G120PropertiesPresenter InverterProperties { get; private set; }
        public void CreateInverterProperties(Canvas canvas, Font font)
        {
            InverterProperties = new GameObject("G120 Properties", typeof(RectTransform)).AddComponent<G120PropertiesPresenter>();
            InverterProperties.Initialize(this, canvas, font);
        }
        public void SelectInverter(bool selected)
        {
            if (selected && (InverterControls == null || Mode != SimulationMode.View && Mode != SimulationMode.Simulate)) return;
            if (selected)
            {
                ClearWireSelection(); SelectFrontBreaker(null); SelectThermalRelay(null); SelectRelay(null);
                SelectContactor(null); SelectPanelDevice(null); SelectPlc(null); SelectSceneIo(null); SelectPowerTerminalBlock(null);
            }
            InverterProperties?.Show(selected);
        }
        public void OpenInverterBop() => setInverterPanelVisible?.Invoke(true);
        private bool IsInverterHit(RaycastHit hit, ElectricalPortView port)
        {
            if (port != null) return port.PortName.StartsWith("G120_", StringComparison.Ordinal);
            if (inverterModel != null && (hit.transform == inverterModel || hit.transform.IsChildOf(inverterModel))) return true;
            for (var parent = hit.transform; parent != null; parent = parent.parent)
            {
                if (parent.name != "DuanZiPai_3" && parent.name != "DuanZiPai_4") continue;
                var pointRoot = parent.Find("point");
                if (pointRoot == null) return false;
                var anchors = pointRoot.Cast<Transform>().Where(t => t.name.StartsWith("G120_", StringComparison.Ordinal)).ToArray();
                if (anchors.Length == 0) return false;
                var x = pointRoot.InverseTransformPoint(hit.point).x;
                return x >= anchors.Min(t => t.localPosition.x) - 0.008f && x <= anchors.Max(t => t.localPosition.x) + 0.008f;
            }
            return false;
        }
        public string DescribeInverter()
        {
            var control = InverterControls;
            if (control == null) return "G120 未初始化";
            var rows = new List<string>
            {
                "G120 · CU240E-2 控制端子", "宏 " + inverterPanel.Macro + "：" + inverterPanel.ActiveMacroName,
                "控制电源：" + (control.Powered ? "已供电" : "未供电") + "  三相主电源：" + (control.MainSupply ? "正常" : "未接齐"),
                "实际转速：" + inverterPanel.ActualSpeedRpm.ToString("F1") + " rpm",
                "故障：" + (inverterPanel.HasFault ? inverterPanel.FaultNumber.ToString() : "无") + "  报警：" + (inverterPanel.HasAlarm ? inverterPanel.AlarmNumber.ToString() : "无"),
                "PTC：" + control.PtcStatus, "模拟电机电流：" + control.SimulatedMotorCurrent.ToString("F2") + " A（模拟值）",
                "模拟调速来源：AI" + control.SelectedAnalogInput
            };
            var snapshot = control.LastSnapshot;
            if (snapshot != null)
            {
                foreach (var number in new[] { 1, 9, 31 })
                {
                    var terminal = G120TerminalCatalog.Find(number);
                    var reading = snapshot.ReadControlSignal(terminal.Node, G120ControlRuntime.Terminal(terminal.Reference), ControlSignalUnit.Volts);
                    if (number == 31 && reading.Valid && reading.Value < 0) reading = new ControlSignalReading(ControlSignalState.Reversed, reading.Value);
                    else if (number == 31 && reading.Valid && (reading.Value < 18 || reading.Value > 30)) reading = new ControlSignalReading(ControlSignalState.OutOfRange, reading.Value);
                    rows.Add(terminal.Label + " / " + terminal.Reference + "：" + reading.Value.ToString("F2") + " V · " + reading.Description);
                }
            }
            rows.Add("DO0 故障：COM–" + (control.DigitalOutputs[0] ? "NO" : "NC") +
                "  ·  DO1 报警：" + (control.DigitalOutputs[1] ? "导通" : "截止") +
                "  ·  DO2 运行：COM–" + (control.DigitalOutputs[2] ? "NO" : "NC"));
            for (var i = 0; i < 2; i++)
            {
                var input = control.Inputs[i]; var output = control.Outputs[i];
                rows.Add("AI" + i + " · " + (input.Simulated ? "模拟输入" : "实际接线") + "：" + input.Reading.Value.ToString("F2") +
                    (control.InputUnit(i) == ControlSignalUnit.Volts ? " V" : " mA") + " · " + input.Reading.Description + " · " + input.Percent.ToString("F1") + "%");
                rows.Add("AO" + i + " · 设定 " + output.Setpoint.ToString("F2") + " / 有效 " + output.Effective.ToString("F2") +
                    (output.Unit == ControlSignalUnit.Volts ? " V" : " mA") + " · " + output.Reading.Description);
            }
            rows.Add("\n端子与当前宏功能（接线数不含内部连接）");
            foreach (var terminal in G120TerminalCatalog.All)
            {
                var i = Array.IndexOf(G120TerminalCatalog.DigitalNumbers, terminal.Number);
                var bindings = portViews.Values.Where(p => G120TerminalCatalog.FromBoardPort(p.PortName)?.Number == terminal.Number).ToArray();
                var ids = bindings.Select(p => p.QualifiedPort).Concat(new[] { terminal.Node }).ToArray();
                var wires = graph.Wires.Count(w => ids.Contains(w.StartPort) || ids.Contains(w.EndPort));
                var role = i >= 0 ? G120TerminalCatalog.DigitalRole(inverterPanel.Macro, i) + " · " + (control.DigitalInputs[i] ? "ON" : "OFF") : terminal.Specification;
                rows.Add(terminal.Label + "  " + role);
                rows.Add("  " + (terminal.Reference > 0 ? "参考端 " + terminal.Reference + " · " : "") + "接线 " + wires + " 条 · " + string.Join(" / ", bindings.Select(p => p.QualifiedPort)));
            }
            rows.Add("\n旧 DI1_COM1 / DI1_COM2 分别兼容 69 / 34。");
            rows.Add("G120_A1：旧模拟量端子，请改接 AI0+/AI0−。原接线保留，端子独立。");
            rows.Add("PTC 使用状态模拟；模拟电流为人工设定，不计算负载及热积累。测试输入不写入接线存档。");
            return string.Join("\n", rows);
        }
    }
}
