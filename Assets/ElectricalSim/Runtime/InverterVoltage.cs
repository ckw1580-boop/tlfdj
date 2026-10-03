using System;
using System.Collections.Generic;

namespace ElectricalSim
{
    // Captured values only: reading an old snapshot never consults a live drive.
    public readonly struct InverterVoltage
    {
        public const double CarrierHz = 4000;
        public readonly string SourceId;
        public readonly bool Active, Valid;
        public readonly double FrequencyHz, LineRms, DcBus, Phase, Clock;
        public readonly int Direction;
        public bool PwmSupported => FrequencyHz < CarrierHz / 2;
        public InverterVoltage(string id, bool active, bool valid, double hz, double rms, double bus, double phase, double clock, int direction)
        { SourceId = id; Active = active; Valid = valid; FrequencyHz = hz; LineRms = rms; DcBus = bus; Phase = phase; Clock = clock; Direction = direction; }
        public double Fundamental(int a, int b, double seconds)
        {
            if (!Active || a == b) return 0;
            var angle = Phase + Direction * 2 * Math.PI * FrequencyHz * seconds;
            return Math.Sqrt(2.0 / 3) * LineRms * (Math.Sin(angle - a * 2 * Math.PI / 3) - Math.Sin(angle - b * 2 * Math.PI / 3));
        }
        private double Duty(int phase, double cycle)
        {
            var angle = Phase + Direction * 2 * Math.PI * FrequencyHz * ((cycle + .5) / CarrierHz - Clock);
            var amp = Math.Sqrt(2.0 / 3) * LineRms;
            var u = amp * Math.Sin(angle); var v = amp * Math.Sin(angle - 2 * Math.PI / 3); var w = amp * Math.Sin(angle - 4 * Math.PI / 3);
            var offset = -(Math.Max(u, Math.Max(v, w)) + Math.Min(u, Math.Min(v, w))) / 2;
            var value = phase == 0 ? u : phase == 1 ? v : w;
            return Math.Max(0, Math.Min(1, .5 + (value + offset) / DcBus));
        }
        public double Pwm(int a, int b, double seconds)
        {
            if (!Active || a == b || DcBus <= 0) return 0;
            if (!PwmSupported) return double.NaN;
            var t = (Clock + seconds) * CarrierHz; var cycle = Math.Floor(t); var distance = Math.Abs(t - cycle - .5);
            return ((distance < Duty(a, cycle) / 2 ? 1 : 0) - (distance < Duty(b, cycle) / 2 ? 1 : 0)) * DcBus;
        }
        // Enumerate constant-voltage intervals at exact switching edges, not display sampling times.
        public void Segments(int a, int b, double duration, Action<double, double, double> sink)
        {
            if (!Active || a == b || DcBus <= 0) { sink(0, duration, 0); return; }
            if (!PwmSupported) return;
            var first = Math.Floor(Clock * CarrierHz); var last = Math.Floor((Clock + duration) * CarrierHz);
            for (var cycle = first; cycle <= last; cycle++)
            {
                var da = Duty(a, cycle); var db = Duty(b, cycle);
                var e0 = (1 - Math.Max(da, db)) / 2; var e1 = (1 - Math.Min(da, db)) / 2;
                var e2 = 1 - e1; var e3 = 1 - e0;
                var pulse = Math.Sign(da - db) * DcBus;
                Segment(cycle, 0, e0, 0, duration, sink); Segment(cycle, e0, e1, pulse, duration, sink);
                Segment(cycle, e1, e2, 0, duration, sink); Segment(cycle, e2, e3, pulse, duration, sink);
                Segment(cycle, e3, 1, 0, duration, sink);
            }
        }
        private void Segment(double cycle, double from, double to, double voltage, double duration, Action<double, double, double> sink)
        {
            var start = Math.Max(0, (cycle + from) / CarrierHz - Clock); var end = Math.Min(duration, (cycle + to) / CarrierHz - Clock);
            if (end > start) sink(start, end, voltage);
        }
    }

    public sealed partial class SimulationSnapshot
    {
        private readonly Dictionary<string, InverterVoltage> inverterSources = new Dictionary<string, InverterVoltage>();
        private readonly Dictionary<string, InverterTerminal> inverterTerminals = new Dictionary<string, InverterTerminal>();
        private readonly HashSet<string> inverterConflicts = new HashSet<string>();
        private readonly struct InverterTerminal
        {
            public readonly string Source; public readonly int Phase;
            public InverterTerminal(string source, int phase) { Source = source; Phase = phase; }
        }
        public bool HasInverterOutput(string port) => ContainsPort(port) && inverterTerminals.ContainsKey(roots[port]);
        internal bool IsEarthConnection(string port)
        {
            if (!ContainsPort(port)) return false;
            foreach (var candidate in roots)
                if (candidate.Key.EndsWith(".PE", StringComparison.Ordinal) && candidate.Value == roots[port]) return true;
            return false;
        }
        internal void RegisterInverter(InverterDriveRuntime drive)
        {
            inverterSources[drive.DeviceId] = drive.CaptureVoltage();
            var ports = new[] { "U2", "V2", "W2" };
            for (var i = 0; i < ports.Length; i++)
            {
                var port = CircuitGraph.Port(drive.DeviceId, ports[i]); if (!ContainsPort(port)) continue;
                var root = roots[port];
                if (inverterTerminals.TryGetValue(root, out var previous) && (previous.Source != drive.DeviceId || previous.Phase != i)) inverterConflicts.Add(root);
                else inverterTerminals[root] = new InverterTerminal(drive.DeviceId, i);
                if (drive.IsActive) externalSupplyRoots.Add(root);
            }
        }
        internal bool InverterHasSharedSource(string id)
        {
            foreach (var other in inverterSources.Keys)
                if (other != id)
                    foreach (var a in new[] { "U2", "V2", "W2" }) foreach (var b in new[] { "U2", "V2", "W2" })
                        if (SameNet(CircuitGraph.Port(id, a), CircuitGraph.Port(other, b))) return true;
            return false;
        }
        public bool TryReadInverterVoltage(string a, string b, out OscilloscopeSignal signal)
        {
            signal = default;
            var hasA = ContainsPort(a) && inverterTerminals.TryGetValue(roots[a], out _);
            var hasB = ContainsPort(b) && inverterTerminals.TryGetValue(roots[b], out _);
            if (!hasA && !hasB) return false;
            if (!IsConverged) { signal = new OscilloscopeSignal(OscilloscopeSignalState.Unavailable); return true; }
            if (hasA && (inverterConflicts.Contains(roots[a]) || !inverterSources[inverterTerminals[roots[a]].Source].Valid) ||
                hasB && (inverterConflicts.Contains(roots[b]) || !inverterSources[inverterTerminals[roots[b]].Source].Valid))
            { signal = new OscilloscopeSignal(OscilloscopeSignalState.Conflict); return true; }
            if (!hasA || !hasB || inverterTerminals[roots[a]].Source != inverterTerminals[roots[b]].Source)
            { signal = new OscilloscopeSignal(OscilloscopeSignalState.UndefinedReference); return true; }
            var ta = inverterTerminals[roots[a]]; var tb = inverterTerminals[roots[b]];
            signal = new OscilloscopeSignal(inverterSources[ta.Source], ta.Phase, tb.Phase); return true;
        }
        public OscilloscopeSignal ReadVoltage(string positive, string negative) => OscilloscopeMeasurement.Read(positive, negative, this);
    }
}
