using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace ElectricalSim
{
    public interface IElectricalSource
    {
        IEnumerable<KeyValuePair<string, ElectricalPotential>> GetSourcePotentials();
    }

    public enum PanelControlKind { Button, Selector, Indicator, Key, EmergencyStop, PowerStart, PowerStop }

    [Serializable]
    public sealed class PanelDeviceDefinition
    {
        public string Id;
        public string Label;
        public string ModelPath;
        public string[] MovingParts;
        public PanelControlKind Control;
        public Color Color;
        public float RatedDcVoltage;
        public float TravelMetres = 0.003f;
        public float PressSeconds = 0.1f;
        public float TurnSeconds = 0.15f;
        public float TurnDegrees = 90f;
        public float LampSeconds = 0.1f;
        public bool Internal => Control == PanelControlKind.Key || Control == PanelControlKind.EmergencyStop ||
                                Control == PanelControlKind.PowerStart || Control == PanelControlKind.PowerStop;
        public bool Momentary => Control == PanelControlKind.Button || Control == PanelControlKind.PowerStart || Control == PanelControlKind.PowerStop;
        public string[] Ports => Control == PanelControlKind.Indicator ? new[] { "L", "N" } : new[] { "COM1", "NO1", "COM2", "NC2" };
        public IEnumerable<PortPair> Contacts(bool operated)
        {
            if (Control != PanelControlKind.Indicator)
                yield return operated ? new PortPair("COM1", "NO1") : new PortPair("COM2", "NC2");
        }
    }

    public static class PanelDeviceCatalog
    {
        public static IReadOnlyList<PanelDeviceDefinition> Create()
        {
            var list = new List<PanelDeviceDefinition>();
            for (var i = 1; i <= 6; i++)
                list.Add(Make("HL" + i, "指示灯", i.ToString(), "ZhiShiDeng_" + new[] { "Green", "Red", "Yellow" }[(i - 1) % 3],
                    PanelControlKind.Indicator, new[] { "mesh/Deng01" }, new[] { Color.green, Color.red, Color.yellow }[(i - 1) % 3]));
            list.Add(Make("SA1", "两档旋钮", "7", "XuanNiu", PanelControlKind.Selector, new[] { "mesh/XuanNiu01" }, Color.white));
            list.Add(Make("SA2", "两档旋钮", "10", "XuanNiu", PanelControlKind.Selector, new[] { "mesh/XuanNiu01" }, Color.white));
            var nuts = new[] { "8", "9", "11", "12", "39", "40", "41", "42" };
            for (var i = 1; i <= 8; i++)
                list.Add(Make("SB" + i, "瞬时按钮", nuts[i - 1], "AnNiu_" + (i % 2 == 1 ? "Green" : "Red"),
                    PanelControlKind.Button, new[] { "mesh/Box" }, i % 2 == 1 ? Color.green : Color.red));
            // The imported mesh names describe the housings poorly: Box is the colored cap,
            // AnNiu*/JiTing02 are fixed mounts, and the actual key is nested inside the lock face.
            list.Add(Make("PANEL_KEY", "钥匙开关", "105", "XuanNiu_PowerStart", PanelControlKind.Key, new[] { "mesh/box/YaoShi01" }, Color.white));
            list.Add(Make("PANEL_ESTOP", "急停", "104", "AnNiu_Scram", PanelControlKind.EmergencyStop, new[] { "mesh/Box" }, Color.red));
            list.Add(Make("PANEL_START", "总启动", "102", "AnNiu_PowerStart", PanelControlKind.PowerStart, new[] { "mesh/Box" }, Color.green));
            list.Add(Make("PANEL_STOP", "总停止", "103", "AnNiu_PowerStop", PanelControlKind.PowerStop, new[] { "mesh/Box" }, Color.red));
            return list;
        }

        private static PanelDeviceDefinition Make(string id, string label, string nut, string model, PanelControlKind control, string[] parts, Color color)
            => new PanelDeviceDefinition { Id = id, Label = label, ModelPath = "Bench/ElectricBench/Nuts/" + nut + "/" + model,
                Control = control, MovingParts = parts, Color = color, RatedDcVoltage = control == PanelControlKind.Indicator ? 24f : 0f };

        // Historical task names refer to these physical installation positions.
        public static string LegacyTarget(string id)
        {
            switch (id)
            {
                case "SB0": return "SB2";
                case "SBF": return "SB5";
                case "SBR": return "SB6";
                case "SBB": return "SB8";
                case "SBE": return "SB7";
                case "SB0A": return "SB4";
                case "SB0B": return "SA2";
                case "SB1A": return "SA1";
                case "SB1B": return "SB3";
                default: return id;
            }
        }
    }

    public sealed class PanelPowerState
    {
        private readonly IDictionary<string, ElectricalDeviceRuntime> controls;
        public bool Enabled { get; private set; }
        public PanelPowerState(IDictionary<string, ElectricalDeviceRuntime> controls)
        {
            this.controls = controls;
            foreach (var id in new[] { "PANEL_KEY", "PANEL_ESTOP", "PANEL_START", "PANEL_STOP" })
                controls[id].PanelControlChanged += OnControl;
        }
        private void OnControl(ElectricalDeviceRuntime device)
        {
            if (!controls["PANEL_KEY"].IsPressed || controls["PANEL_ESTOP"].IsPressed || controls["PANEL_STOP"].IsPressed)
                Enabled = false;
            else if (device.DeviceId == "PANEL_START" && device.IsPressed) Enabled = true;
        }
        public void Reset()
        {
            Enabled = false;
            foreach (var device in controls.Values) device.SetControl(false);
        }
        public void StartForAssessment()
        {
            Reset();
            controls["PANEL_KEY"].SetControl(true);
            controls["PANEL_START"].SetControl(true);
            controls["PANEL_START"].SetControl(false);
        }
    }

    public sealed partial class ElectricalDeviceRuntime
    {
        public PanelDeviceDefinition PanelDefinition { get; private set; }
        public ElectricalDeviceRuntime ControlTarget { get; set; }
        public Func<bool> SupplyEnabled { get; set; }
        public bool IsDcSource { get; set; }
        public string PanelStatus { get; private set; } = "";
        public event Action<ElectricalDeviceRuntime> PanelControlChanged;

        public static ElectricalDeviceRuntime CreatePanel(PanelDeviceDefinition definition)
        {
            var kind = definition.Control == PanelControlKind.Indicator ? ElectricalDeviceKind.Indicator :
                definition.Control == PanelControlKind.Selector || definition.Control == PanelControlKind.Key ? ElectricalDeviceKind.SelectorSwitch : ElectricalDeviceKind.PushButton;
            var device = new ElectricalDeviceRuntime(definition.Id, kind, definition.Ports) { PanelDefinition = definition };
            // Legacy logical endpoints only alias the first (NO) contact block.
            if (definition.Id == "SB1" || definition.Id == "SB2")
            {
                device.AddFixedLink("COM", definition.Id + ".COM1");
                device.AddFixedLink("NO", definition.Id + ".NO1");
            }
            device.SetPanelControl(false);
            return device;
        }

        private void SetPanelControl(bool active)
        {
            if (PanelDefinition.Control == PanelControlKind.Indicator) return;
            var changed = IsPressed != active;
            IsPressed = active;
            IsActive = active;
            if (changed) PanelControlChanged?.Invoke(this);
        }
        private IEnumerable<PortPair> PanelContacts() => PanelDefinition.Contacts(IsPressed);
        private void EvaluatePanel(SimulationSnapshot snapshot)
        {
            if (PanelDefinition.Control != PanelControlKind.Indicator)
            {
                IsActive = IsPressed;
                PanelStatus = IsPressed ? "已操作" : "复位";
                return;
            }
            var voltage = snapshot.GetDcVoltage(Port("L"), Port("N"));
            IsActive = voltage == PanelDefinition.RatedDcVoltage;
            var a = snapshot.GetPotential(Port("L"));
            var b = snapshot.GetPotential(Port("N"));
            PanelStatus = IsActive ? "点亮 · DC +24V" : double.IsNaN(voltage) ? "熄灭 · 电势冲突" : voltage < 0 ? "熄灭 · 极性反接" :
                IsAc(a) || IsAc(b) ? "熄灭 · 交流误接" : a == ElectricalPotential.Floating || b == ElectricalPotential.Floating ? "熄灭 · 未供电或断线" : "熄灭 · 无有效压差";
        }
        private static bool IsAc(ElectricalPotential p) => p == ElectricalPotential.PhaseL1 || p == ElectricalPotential.PhaseL2 || p == ElectricalPotential.PhaseL3;

        public IEnumerable<KeyValuePair<string, ElectricalPotential>> GetSourcePotentials()
        {
            if (Kind != ElectricalDeviceKind.PowerSource || SupplyEnabled != null && !SupplyEnabled()) yield break;
            if (IsDcSource)
            {
                yield return new KeyValuePair<string, ElectricalPotential>(Port("DC_POSITIVE"), ElectricalPotential.DcPositive24);
                yield return new KeyValuePair<string, ElectricalPotential>(Port("DC_NEGATIVE"), ElectricalPotential.DcNegative);
            }
            else
            {
                yield return new KeyValuePair<string, ElectricalPotential>(Port("L1"), ElectricalPotential.PhaseL1);
                yield return new KeyValuePair<string, ElectricalPotential>(Port("L2"), ElectricalPotential.PhaseL2);
                yield return new KeyValuePair<string, ElectricalPotential>(Port("L3"), ElectricalPotential.PhaseL3);
                yield return new KeyValuePair<string, ElectricalPotential>(Port("N"), ElectricalPotential.Neutral);
            }
        }
    }
}
