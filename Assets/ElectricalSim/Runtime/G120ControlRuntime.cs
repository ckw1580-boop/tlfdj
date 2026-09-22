using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace ElectricalSim
{
    public enum G120PtcState { Normal, Overheated, Open, Shorted }
    public enum G120AnalogOutputMode { Current20, Voltage10, Current4To20 }
    public sealed class G120AnalogInputState
    {
        public bool Simulated;
        public float SimulatedValue;
        public ControlSignalReading Reading { get; internal set; }
        public float Percent { get; internal set; }
    }
    public sealed class G120AnalogOutputState
    {
        public G120AnalogOutputMode Mode;
        // Zero means use the configured motor rated current for AO1.
        public float FullScale;
        public float Setpoint { get; internal set; }
        public float Effective { get; internal set; }
        public ControlSignalReading Reading { get; internal set; }
        public ControlSignalUnit Unit => Mode == G120AnalogOutputMode.Voltage10 ? ControlSignalUnit.Volts : ControlSignalUnit.Milliamps;
    }

    public sealed class G120ControlRuntime
    {
        private readonly InverterPanelController panel;
        private readonly bool[] digital = new bool[6];
        private readonly bool[] outputs = new bool[3];
        private int lastMacro;
        private int ptcAlarmNumber;
        private bool do1ForwardSupply;
        private bool ptcResetBlocked;
        public bool Powered { get; private set; }
        public bool MainSupply { get; private set; }
        public bool PtcEnabled { get; set; }
        public G120PtcState PtcState { get; set; }
        public string PtcStatus { get; private set; } = "监测关闭";
        public bool PtcFaultActive { get; private set; }
        public int SelectedAnalogInput { get; set; }
        public float SimulatedMotorCurrent { get; set; }
        public bool HasControlFault { get; private set; }
        public IReadOnlyList<bool> DigitalInputs => digital;
        public IReadOnlyList<bool> DigitalOutputs => outputs;
        public readonly G120AnalogInputState[] Inputs = { new G120AnalogInputState(), new G120AnalogInputState() };
        public readonly G120AnalogOutputState[] Outputs = { new G120AnalogOutputState { FullScale = 1500 }, new G120AnalogOutputState() };
        public SimulationSnapshot LastSnapshot { get; private set; }
        public G120ControlRuntime(InverterPanelController panel)
        {
            this.panel = panel; lastMacro = panel.Macro;
            panel.UseExternalClock = true;
            panel.CanResetFault = () => !(PtcEnabled && (PtcState != G120PtcState.Normal || ptcResetBlocked)) && !HasControlFault;
            panel.FactorySettingsReset += Reset;
        }
        public static string Terminal(int n) => "G120.T" + n.ToString("00");
        public float Parameter(string key, float fallback = 0) => panel.TryGetParameter(key, out var value) ? value : fallback;
        public int InputType(int i) => Mathf.RoundToInt(Parameter("P756." + i, 4));
        public ControlSignalUnit InputUnit(int i) => InputType(i) == 2 || InputType(i) == 3 ? ControlSignalUnit.Milliamps : ControlSignalUnit.Volts;
        public void SetInputType(int i, int type)
        {
            if (i < 0 || i > 1 || type < 0 || type > 4) throw new ArgumentOutOfRangeException();
            panel.TrySetParameter("P756." + i, type);
            panel.TrySetParameter("P757." + i, type == 3 ? 4 : type == 1 ? 2 : 0);
            panel.TrySetParameter("P759." + i, type == 2 || type == 3 ? 20 : 10);
            panel.TrySetParameter("P758." + i, 0); panel.TrySetParameter("P760." + i, 100);
        }
        public IEnumerable<PortPair> CurrentReceivers
        {
            get
            {
                for (var i = 0; i < 2; i++)
                    if (!Inputs[i].Simulated && InputUnit(i) == ControlSignalUnit.Milliamps)
                        yield return new PortPair(Terminal(i == 0 ? 3 : 10), Terminal(i == 0 ? 4 : 11));
            }
        }
        public IEnumerable<ControlSignal> GetSignals(SimulationSnapshot topology)
        {
            var phases = new[] { "L1", "L2", "L3" }.Select(p => topology.GetPotential("G120." + p)).ToArray();
            MainSupply = phases.All(p => p == ElectricalPotential.PhaseL1 || p == ElectricalPotential.PhaseL2 || p == ElectricalPotential.PhaseL3) && phases.Distinct().Count() == 3;
            // The topology here has only external legacy supply potentials. Never let our
            // own 24V output, wired back to T31, latch the controller on after power loss.
            var external = topology.GetDcVoltage(Terminal(31), Terminal(32));
            Powered = MainSupply || external >= 18 && external <= 30;
            do1ForwardSupply = Powered && IsDo1ForwardSupply(topology);
            if (!Powered) yield break;
            yield return new ControlSignal(Terminal(1), Terminal(2), 10);
            yield return new ControlSignal(Terminal(9), Terminal(28), 24);
            for (var i = 0; i < 2; i++)
            {
                var output = Outputs[i];
                var scale = output.FullScale > 0 ? output.FullScale : Parameter("P305", 3.1f);
                var feedback = i == 0 ? Mathf.Abs(panel.ActualSpeedRpm) : Mathf.Max(0, SimulatedMotorCurrent);
                var normalized = Mathf.Clamp01(feedback / Mathf.Max(0.001f, scale));
                output.Setpoint = output.Mode == G120AnalogOutputMode.Voltage10 ? normalized * 10 :
                    output.Mode == G120AnalogOutputMode.Current4To20 ? 4 + normalized * 16 : normalized * 20;
                yield return new ControlSignal(Terminal(i == 0 ? 12 : 26), Terminal(i == 0 ? 13 : 27), output.Setpoint, output.Unit);
            }
            // A transistor's closed-state voltage is zero, but its terminals must
            // stay distinct so an external supply can still be checked for polarity.
            if (outputs[1] && do1ForwardSupply) yield return new ControlSignal(Terminal(21), Terminal(22), 0);
        }
        private bool IsDo1ForwardSupply(SimulationSnapshot topology)
        {
            bool Positive(int terminal) => topology.GetPotential(Terminal(terminal)) == ElectricalPotential.DcPositive24 ||
                topology.SameNet(Terminal(terminal), Terminal(9)) || topology.SameNet(Terminal(terminal), Terminal(1));
            bool Negative(int terminal) => topology.GetPotential(Terminal(terminal)) == ElectricalPotential.DcNegative ||
                topology.SameNet(Terminal(terminal), Terminal(28));
            // Supports both high-side and low-side loads. A load's unpowered end
            // is floating in the discrete model; the other terminal must establish
            // the direction. Neither a floating pair nor reverse supply is valid.
            return !Negative(21) && !Positive(22) && (Positive(21) || Negative(22));
        }
        public IEnumerable<PortPair> Links()
        {
            foreach (var link in G120TerminalCatalog.LegacyLinks()) yield return link;
            // Common GND terminals; DI commons, AI differential returns, PTC and dry
            // contacts are deliberately excluded and require explicit wiring.
            foreach (var n in new[] { 13, 27, 28, 32 }) yield return new PortPair("T02", "T" + n.ToString("00"));
            yield return new PortPair("T20", outputs[0] ? "T19" : "T18");
            yield return new PortPair("T25", outputs[2] ? "T24" : "T23");
        }
        public bool Evaluate(SimulationSnapshot snapshot)
        {
            var before = outputs.ToArray();
            HasControlFault = G120TerminalCatalog.All.Any(t => snapshot.HasSignalConflict(t.Node)) ||
                G120TerminalCatalog.All.Any(t => IsAc(snapshot.GetPotential(t.Node)));
            var ptc = snapshot.SameNet(Terminal(14), Terminal(15)) ? G120PtcState.Shorted : PtcState;
            ptcResetBlocked = PtcEnabled && ptc != G120PtcState.Normal;
            PtcFaultActive = Powered && PtcEnabled && ptc != G120PtcState.Normal;
            PtcStatus = !PtcEnabled ? "监测关闭" : !Powered ? "未供电" : ptc == G120PtcState.Normal ? "正常（模拟传感器）" :
                ptc == G120PtcState.Overheated ? "PTC 过热" : ptc == G120PtcState.Open ? "PTC 断线" : "PTC 短路";
            if (PtcFaultActive)
            {
                ptcAlarmNumber = ptc == G120PtcState.Overheated ? 7910 : 7015;
                if (!panel.HasAlarm || panel.AlarmNumber != ptcAlarmNumber) panel.SetAlarm(true, ptcAlarmNumber);
                if (!panel.HasFault) panel.SetFault(true, ptc == G120PtcState.Overheated ? 7011 : 7016);
            }
            else if (ptcAlarmNumber != 0)
            {
                if (panel.AlarmNumber == ptcAlarmNumber) panel.SetAlarm(false);
                ptcAlarmNumber = 0;
            }
            if (HasControlFault && !panel.HasFault) panel.SetFault(true, 7001);
            outputs[0] = Powered && panel.HasFault;
            outputs[1] = Powered && panel.HasAlarm && do1ForwardSupply && !HasControlFault;
            outputs[2] = Powered && MainSupply && !panel.HasFault && !HasControlFault && panel.OperationEnabled;
            LastSnapshot = snapshot;
            return !before.SequenceEqual(outputs);
        }
        public void SampleAndAdvance(SimulationSnapshot snapshot, float dt)
        {
            if (lastMacro != panel.Macro) { SelectedAnalogInput = 0; lastMacro = panel.Macro; }
            Evaluate(snapshot);
            for (var i = 0; i < digital.Length; i++)
            {
                var volts = snapshot.GetDcVoltage(Terminal(G120TerminalCatalog.DigitalNumbers[i]), Terminal(i % 2 == 0 ? 69 : 34));
                digital[i] = Powered && !HasControlFault && volts >= 18 && volts <= 30;
            }
            for (var i = 0; i < 2; i++) ReadInput(i, snapshot);
            panel.SetDigitalInputs(digital);
            panel.SetAnalogPercentage(Inputs[Mathf.Clamp(SelectedAnalogInput, 0, 1)].Percent);
            panel.Advance(dt, Powered && MainSupply && !PtcFaultActive && !HasControlFault);
        }
        private void ReadInput(int i, SimulationSnapshot snapshot)
        {
            var input = Inputs[i]; var type = InputType(i);
            input.Reading = !Powered ? new ControlSignalReading(ControlSignalState.Floating) : input.Simulated
                ? new ControlSignalReading(ControlSignalState.Valid, input.SimulatedValue)
                : snapshot.ReadControlSignal(Terminal(i == 0 ? 3 : 10), Terminal(i == 0 ? 4 : 11), InputUnit(i));
            var min = type == 4 ? -10 : type == 3 ? 4 : type == 1 ? 2 : 0;
            var max = type == 2 || type == 3 ? 20 : 10;
            if (input.Reading.Valid && (double.IsNaN(input.Reading.Value) || double.IsInfinity(input.Reading.Value) || input.Reading.Value < min || input.Reading.Value > max))
                input.Reading = new ControlSignalReading(ControlSignalState.OutOfRange, input.Reading.Value);
            var x1 = Parameter("P757." + i); var x2 = Parameter("P759." + i, max);
            if (Mathf.Abs(x2 - x1) < 0.0001f) input.Reading = new ControlSignalReading(ControlSignalState.OutOfRange);
            input.Percent = input.Reading.Valid ? Parameter("P758." + i) + ((float)input.Reading.Value - x1) / (x2 - x1) *
                (Parameter("P760." + i, 100) - Parameter("P758." + i)) : 0;
        }
        public void RefreshReadings(SimulationSnapshot snapshot)
        {
            LastSnapshot = snapshot;
            for (var i = 0; i < 2; i++)
            {
                var output = Outputs[i]; var positive = Terminal(i == 0 ? 12 : 26); var negative = Terminal(i == 0 ? 13 : 27);
                var receiver = Enumerable.Range(0, 2).Where(j => !Inputs[j].Simulated &&
                    (snapshot.SameNet(positive, Terminal(j == 0 ? 3 : 10)) && snapshot.SameNet(negative, Terminal(j == 0 ? 4 : 11)) ||
                     snapshot.SameNet(positive, Terminal(j == 0 ? 4 : 11)) && snapshot.SameNet(negative, Terminal(j == 0 ? 3 : 10)))).ToArray();
                var state = !Powered ? ControlSignalState.Floating : snapshot.HasSignalConflict(positive) || snapshot.HasSignalConflict(negative) ? ControlSignalState.Conflict :
                    snapshot.SameNet(positive, negative) ? ControlSignalState.ShortCircuit :
                    receiver.Any(j => InputUnit(j) != output.Unit) ? ControlSignalState.TypeMismatch :
                    receiver.Any(j => snapshot.SameNet(positive, Terminal(j == 0 ? 4 : 11))) ? ControlSignalState.Reversed :
                    receiver.Length == 0 && output.Unit == ControlSignalUnit.Milliamps ? ControlSignalState.Floating : ControlSignalState.Valid;
                if (!Powered) output.Setpoint = 0;
                output.Effective = state == ControlSignalState.Valid ? output.Setpoint : 0;
                output.Reading = new ControlSignalReading(state, output.Effective);
            }
        }
        public void Reset()
        {
            ptcAlarmNumber = 0; PtcEnabled = false; PtcState = G120PtcState.Normal; PtcFaultActive = HasControlFault = false;
            ptcResetBlocked = do1ForwardSupply = false;
            SelectedAnalogInput = 0; SimulatedMotorCurrent = 0; lastMacro = panel.Macro;
            Array.Clear(digital, 0, digital.Length); Array.Clear(outputs, 0, outputs.Length);
            for (var i = 0; i < 2; i++)
            { Inputs[i].Simulated = false; Inputs[i].SimulatedValue = Inputs[i].Percent = 0; Outputs[i].Mode = G120AnalogOutputMode.Current20; Outputs[i].FullScale = i == 0 ? 1500 : 0; }
        }
        private static bool IsAc(ElectricalPotential p) => p == ElectricalPotential.PhaseL1 || p == ElectricalPotential.PhaseL2 || p == ElectricalPotential.PhaseL3 || p == ElectricalPotential.Neutral || p == ElectricalPotential.Conflict;
    }
}
