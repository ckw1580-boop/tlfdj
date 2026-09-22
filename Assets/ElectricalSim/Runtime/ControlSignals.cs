using System;
using System.Collections.Generic;
using System.Linq;

namespace ElectricalSim
{
    public enum ControlSignalUnit { Volts, Milliamps }
    public enum ControlSignalState { Valid, Floating, Reversed, TypeMismatch, Conflict, ShortCircuit, OutOfRange }
    public readonly struct ControlSignalReading
    {
        public readonly ControlSignalState State;
        public readonly double Value;
        public bool Valid => State == ControlSignalState.Valid;
        public ControlSignalReading(ControlSignalState state, double value = 0) { State = state; Value = value; }
        public string Description => State == ControlSignalState.Valid ? "有效" : State == ControlSignalState.Floating ? "开路／参考端悬空" :
            State == ControlSignalState.Reversed ? "极性反接" : State == ControlSignalState.TypeMismatch ? "信号类型不匹配" :
            State == ControlSignalState.ShortCircuit ? "正负端短接" : State == ControlSignalState.OutOfRange ? "超出量程" : "信号源冲突";
    }
    public readonly struct ControlSignal
    {
        public readonly string Positive, Negative;
        public readonly ControlSignalUnit Unit;
        public readonly double Value;
        public ControlSignal(string positive, string negative, double value, ControlSignalUnit unit = ControlSignalUnit.Volts)
        { Positive = positive; Negative = negative; Value = value; Unit = unit; }
    }
    public interface IControlSignalSource
    {
        IEnumerable<ControlSignal> GetControlSignals(SimulationSnapshot topology);
        IEnumerable<PortPair> CurrentReceivers { get; }
    }

    // Numerical DC signals share wire roots with the discrete circuit, but a source is
    // a voltage difference, never a conductive link between its positive and negative terminals.
    public sealed partial class SimulationSnapshot
    {
        private readonly Dictionary<string, double> signalVolts = new Dictionary<string, double>();
        private readonly Dictionary<string, int> signalGroups = new Dictionary<string, int>();
        private readonly HashSet<string> signalConflicts = new HashSet<string>();
        private readonly List<ControlSignal> controlSignals = new List<ControlSignal>();
        private PortPair[] currentReceivers = Array.Empty<PortPair>();
        private readonly HashSet<string> signalEnergized = new HashSet<string>();
        public bool HasSignalConflict(string port) => port != null && roots.TryGetValue(port, out var root) && signalConflicts.Contains(root);
        public bool HasControlSignal(string port) => port != null && roots.TryGetValue(port, out var root) && signalGroups.ContainsKey(root);
        internal bool SignalEnergized(string port) => port != null && roots.TryGetValue(port, out var root) && signalEnergized.Contains(root);

        internal void ResolveControlSignals(IEnumerable<ControlSignal> signals, IEnumerable<PortPair> receivers, List<string> errors)
        {
            controlSignals.AddRange(signals);
            currentReceivers = receivers.ToArray();
            var edges = new Dictionary<string, List<KeyValuePair<string, double>>>();
            void Edge(string a, string b, double voltage)
            {
                if (!edges.TryGetValue(a, out var list)) edges[a] = list = new List<KeyValuePair<string, double>>();
                list.Add(new KeyValuePair<string, double>(b, voltage));
            }
            void Pair(string a, string b, double voltage) { Edge(a, b, -voltage); Edge(b, a, voltage); }
            foreach (var pair in potentials)
            {
                if (pair.Value == ElectricalPotential.DcPositive24) Pair(pair.Key, "$existingDC", 24);
                if (pair.Value == ElectricalPotential.DcNegative) Pair(pair.Key, "$existingDC", 0);
            }
            foreach (var signal in controlSignals)
            {
                if (!roots.TryGetValue(signal.Positive, out var positive) || !roots.TryGetValue(signal.Negative, out var negative)) continue;
                signalEnergized.Add(positive); signalEnergized.Add(negative);
                var voltage = signal.Value;
                if (signal.Unit == ControlSignalUnit.Milliamps)
                {
                    var loads = CurrentReceiverCount(signal);
                    if (loads == 0) continue; // an open current loop has no defined terminal voltage
                    voltage = signal.Value * 0.25 / loads; // each current input models a 250-ohm burden
                }
                Pair(positive, negative, voltage);
                if (IsAcRoot(positive) || IsAcRoot(negative)) { signalConflicts.Add(positive); signalConflicts.Add(negative); }
            }
            // Parallel current sources are ambiguous even when their setpoints happen to match.
            foreach (var signal in controlSignals.Where(s => s.Unit == ControlSignalUnit.Milliamps))
                if (controlSignals.Count(s => SamePair(signal, s)) > 1)
                { signalConflicts.Add(roots[signal.Positive]); signalConflicts.Add(roots[signal.Negative]); }
            var group = 0;
            foreach (var start in edges.Keys)
            {
                if (signalGroups.ContainsKey(start)) continue;
                group++;
                var members = new List<string>(); var queue = new Queue<string>(); var conflict = false;
                signalGroups[start] = group; signalVolts[start] = 0; queue.Enqueue(start);
                while (queue.Count > 0)
                {
                    var node = queue.Dequeue(); members.Add(node); conflict |= signalConflicts.Contains(node);
                    foreach (var edge in edges[node])
                    {
                        var expected = signalVolts[node] + edge.Value;
                        if (signalGroups.ContainsKey(edge.Key)) { conflict |= Math.Abs(signalVolts[edge.Key] - expected) > 0.001; continue; }
                        signalGroups[edge.Key] = group; signalVolts[edge.Key] = expected; queue.Enqueue(edge.Key);
                    }
                }
                if (conflict) foreach (var member in members) signalConflicts.Add(member);
            }
            if (signalConflicts.Count > 0) errors.Add("G120 控制信号冲突：检查短接、交流误接或多个信号源。");
        }
        private bool IsAcRoot(string root) => potentials.TryGetValue(root, out var p) &&
            (p == ElectricalPotential.Conflict || p == ElectricalPotential.Neutral || p == ElectricalPotential.PhaseL1 || p == ElectricalPotential.PhaseL2 || p == ElectricalPotential.PhaseL3);
        private bool SamePair(ControlSignal a, ControlSignal b) =>
            SameNet(a.Positive, b.Positive) && SameNet(a.Negative, b.Negative) || SameNet(a.Positive, b.Negative) && SameNet(a.Negative, b.Positive);
        private int CurrentReceiverCount(ControlSignal signal) => currentReceivers.Count(r =>
            SameNet(r.A, signal.Positive) && SameNet(r.B, signal.Negative) ||
            SameNet(r.B, signal.Positive) && SameNet(r.A, signal.Negative));

        public bool TryGetSignalVoltage(string positive, string negative, out double value)
        {
            value = double.NaN;
            if (!ContainsPort(positive) || !ContainsPort(negative)) return false;
            var a = roots[positive]; var b = roots[negative];
            if (signalConflicts.Contains(a) || signalConflicts.Contains(b)) return true;
            if (!signalGroups.TryGetValue(a, out var ga) || !signalGroups.TryGetValue(b, out var gb) || ga != gb) return false;
            value = signalVolts[a] - signalVolts[b]; return true;
        }
        public ControlSignalReading ReadControlSignal(string positive, string negative, ControlSignalUnit unit)
        {
            if (HasSignalConflict(positive) || HasSignalConflict(negative) || GetPotential(positive) == ElectricalPotential.Conflict || GetPotential(negative) == ElectricalPotential.Conflict)
                return new ControlSignalReading(ControlSignalState.Conflict);
            if (SameNet(positive, negative)) return new ControlSignalReading(ControlSignalState.ShortCircuit);
            var direct = controlSignals.Where(s => SameNet(positive, s.Positive) && SameNet(negative, s.Negative) ||
                SameNet(positive, s.Negative) && SameNet(negative, s.Positive)).ToArray();
            if (direct.Any(s => s.Unit != unit)) return new ControlSignalReading(ControlSignalState.TypeMismatch);
            if (unit == ControlSignalUnit.Milliamps)
            {
                if (direct.Length > 1) return new ControlSignalReading(ControlSignalState.Conflict);
                if (direct.Length == 1)
                {
                    var loads = CurrentReceiverCount(direct[0]);
                    if (loads == 0) return new ControlSignalReading(ControlSignalState.Floating);
                    // Each enabled current input is a 250-ohm burden. Parallel inputs
                    // divide the source current, matching the voltage solved above.
                    var current = direct[0].Value / loads;
                    return SameNet(positive, direct[0].Positive)
                        ? new ControlSignalReading(ControlSignalState.Valid, current)
                        : new ControlSignalReading(ControlSignalState.Reversed, -current);
                }
                return new ControlSignalReading(TryGetSignalVoltage(positive, negative, out _) ? ControlSignalState.TypeMismatch : ControlSignalState.Floating);
            }
            if (TryGetSignalVoltage(positive, negative, out var volts)) return new ControlSignalReading(double.IsNaN(volts) ? ControlSignalState.Conflict : ControlSignalState.Valid, volts);
            return new ControlSignalReading(ControlSignalState.Floating);
        }
    }
}
