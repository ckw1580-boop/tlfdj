using System;
using System.Collections.Generic;
using System.Linq;

namespace ElectricalSim
{
    public sealed partial class CircuitGraph
    {
        private bool AdvanceThermalRelays(SimulationSnapshot snapshot, float seconds)
        {
            var relays = allDevices.OfType<ElectricalDeviceRuntime>().Where(d => d.Kind == ElectricalDeviceKind.ThermalRelay).ToArray();
            if (relays.Length == 0) return false;
            var edges = new List<(string a, string b, string heater)>();
            foreach (var wire in wires) edges.Add((wire.StartPort, wire.EndPort, ""));
            foreach (var device in allDevices)
                foreach (var pair in device.GetConductiveLinks())
                {
                    var a = Qualify(device.DeviceId, pair.A); var b = Qualify(device.DeviceId, pair.B);
                    var heater = device.Kind == ElectricalDeviceKind.ThermalRelay && pair.A.StartsWith("L") && pair.B.StartsWith("T") ? device.DeviceId + "." + pair.A : "";
                    edges.Add((a, b, heater));
                }
            var sourcePorts = new Dictionary<int, List<string>> { [0] = new List<string>(), [1] = new List<string>(), [2] = new List<string>() };
            foreach (var source in sources)
                foreach (var output in source.GetSourcePotentials())
                {
                    var index = output.Value == ElectricalPotential.PhaseL1 ? 0 : output.Value == ElectricalPotential.PhaseL2 ? 1 : output.Value == ElectricalPotential.PhaseL3 ? 2 : -1;
                    if (index >= 0) sourcePorts[index].Add(output.Key);
                }
            var changed = false;
            foreach (var relay in relays)
            {
                var currents = new float[3]; var warnings = new List<string>();
                for (var phase = 0; phase < 3; phase++)
                {
                    var input = Port(relay.DeviceId, "L" + (phase + 1)); var output = Port(relay.DeviceId, "T" + (phase + 1));
                    var adjacency = new Dictionary<string, List<string>>();
                    void Add(string a, string b) { if (!adjacency.TryGetValue(a, out var list)) adjacency[a] = list = new List<string>(); list.Add(b); }
                    foreach (var edge in edges)
                    {
                        if (edge.heater == input) continue;
                        Add(edge.a, edge.b); Add(edge.b, edge.a);
                    }
                    HashSet<string> Reach(string start)
                    {
                        var seen = new HashSet<string> { start }; var pending = new Queue<string>(); pending.Enqueue(start);
                        while (pending.Count > 0)
                        {
                            var node = pending.Dequeue();
                            if (adjacency.TryGetValue(node, out var neighbours))
                                foreach (var next in neighbours) if (seen.Add(next)) pending.Enqueue(next);
                        }
                        return seen;
                    }
                    var left = Reach(input); var right = Reach(output);
                    if (left.Contains(output))
                    {
                        currents[phase] = float.NaN;
                        warnings.Add("热元件" + (phase + 1) + "存在旁路／并联，支路电流无法唯一计算");
                        continue;
                    }
                    foreach (var motor in runtimeMotors)
                    {
                        var state = motor.MotorState; var connection = state.Connection;
                        if (connection == null || connection.SupplyPorts == null || connection.SupplyPorts.Length < 3 || connection.PhaseIndices.Length < 3) continue;
                        if (TryGetWindingBranchCurrent(motor, snapshot, input, left, right, sourcePorts,
                            out var windingCurrent, out var windingWarning))
                        {
                            currents[phase] += windingCurrent;
                            if (!string.IsNullOrEmpty(windingWarning)) warnings.Add("热元件" + (phase + 1) + "：" + windingWarning);
                            continue;
                        }
                        // Several motor terminals on a delta node represent ONE supply current.
                        var counted = new HashSet<int>();
                        for (var i = 0; i < 3; i++)
                        {
                            var terminal = connection.SupplyPorts[i]; var sourcePhase = connection.PhaseIndices[i];
                            if (string.IsNullOrEmpty(terminal) || sourcePhase < 0 || !counted.Add(sourcePhase)) continue;
                            var sourcesForPhase = string.IsNullOrEmpty(connection.DriveId) ? sourcePorts[sourcePhase] :
                                new[] { "U2", "V2", "W2" }.Select(p => Port(connection.DriveId, p))
                                    .Where(p => snapshot.SameNet(p, terminal)).ToList();
                            if (sourcesForPhase.Any(left.Contains) && sourcesForPhase.Any(right.Contains))
                            {
                                currents[phase] = float.NaN;
                                warnings.Add("热元件" + (phase + 1) + "两侧并联电源，电流分配无法唯一计算");
                                continue;
                            }
                            if (sourcesForPhase.Any(s => left.Contains(s)) && right.Contains(terminal) ||
                                sourcesForPhase.Any(s => right.Contains(s)) && left.Contains(terminal))
                                currents[phase] += state.PhaseCurrentsAmps[i];
                        }
                    }
                    // Teaching input-current estimate assumes equal input/output power factor.
                    // This keeps a relay upstream of the DC link attached to its actual input branch.
                    foreach (var drive in drives.Where(d => d.IsActive))
                    {
                        var loadAmps = runtimeMotors.Where(m => m.MotorState.Connection.DriveId == drive.DeviceId)
                            .Sum(m => m.MotorState.CurrentAmps) * drive.OutputLineVoltage / 380f;
                        foreach (var inputName in new[] { "L1", "L2", "L3" })
                        {
                            var terminal = Port(drive.DeviceId, inputName);
                            var potential = snapshot.GetPotential(terminal);
                            var index = potential == ElectricalPotential.PhaseL1 ? 0 : potential == ElectricalPotential.PhaseL2 ? 1 : potential == ElectricalPotential.PhaseL3 ? 2 : -1;
                            if (index < 0) continue;
                            if (sourcePorts[index].Any(left.Contains) && right.Contains(terminal) || sourcePorts[index].Any(right.Contains) && left.Contains(terminal))
                                currents[phase] += loadAmps;
                        }
                    }
                }
                changed |= relay.AdvanceThermal(currents, string.Join("；", warnings), seconds);
            }
            return changed;
        }

        // A heater can sit inside a delta bridge or between a winding and the star point.
        // In those positions the head terminal alone does not identify the protected load.
        private static bool TryGetWindingBranchCurrent(ElectricalDeviceRuntime motor, SimulationSnapshot snapshot,
            string heaterInput, HashSet<string> left, HashSet<string> right, Dictionary<int, List<string>> sourcePorts,
            out float current, out string warning)
        {
            current = 0f; warning = "";
            var state = motor.MotorState; var connection = state.Connection;
            if (motor.MotorConfiguration.IsTwoSpeed)
            {
                var commonPorts = new[] { "U", "V", "W" }.Select(p => Port(motor.DeviceId, p)).ToArray();
                if (connection.Kind != MotorConnectionKind.DoubleHigh || !snapshot.SameNet(heaterInput, commonPorts[0]) ||
                    !commonPorts.Any(left.Contains) || !commonPorts.Any(right.Contains)) return false;
                // The dedicated YY model exposes line currents, not the split currents of its two stars.
                // Do not report a fictitious zero for a heater inserted inside that parallel winding.
                if (state.CurrentAmps > 0)
                {
                    current = float.NaN;
                    warning = "双速YY公共点内部并联支路电流无法由线电流唯一计算";
                }
                return true;
            }
            if (connection.Kind != MotorConnectionKind.Star && connection.Kind != MotorConnectionKind.Delta) return false;
            var terminals = new[] { "U", "V", "W", "U2", "V2", "W2" }.Select(p => Port(motor.DeviceId, p)).ToArray();
            var onLeft = terminals.Count(left.Contains); var onRight = terminals.Count(right.Contains);
            if (connection.Kind == MotorConnectionKind.Star)
            {
                // Only the common point is new here; a supply-side heater is already a line-current measurement.
                var commonStart = connection.SupplyPorts[0] == terminals[0] ? 3 : 0;
                if (!snapshot.SameNet(heaterInput, terminals[commonStart]) || onLeft == 0 || onRight == 0) return false;
            }
            else
            {
                // Each healthy delta node contains one head and the adjacent winding's tail.
                // Splitting those two terminals isolates a winding; keeping both together is a line feeder.
                if (onLeft != 1 || onRight != 1) return false;
                var supplyIndex = Array.FindIndex(connection.SupplyPorts, p => snapshot.SameNet(heaterInput, p));
                if (supplyIndex < 0) return false;
                var sourcePhase = connection.PhaseIndices[supplyIndex];
                if (sourcePhase >= 0)
                {
                    var phaseSources = string.IsNullOrEmpty(connection.DriveId) ? sourcePorts[sourcePhase] :
                        new[] { "U2", "V2", "W2" }.Select(p => Port(connection.DriveId, p))
                            .Where(p => snapshot.SameNet(p, heaterInput)).ToList();
                    if (phaseSources.Any(left.Contains) && phaseSources.Any(right.Contains))
                    {
                        current = float.NaN; warning = "绕组支路两侧并联电源，电流无法唯一计算"; return true;
                    }
                }
            }
            if (connection.PhaseLoss)
            {
                current = float.NaN; warning = "缺相时绕组支路电流无法由平衡三相模型唯一计算"; return true;
            }
            current = connection.Kind == MotorConnectionKind.Delta ? state.CurrentAmps / (float)Math.Sqrt(3) : state.CurrentAmps;
            return true;
        }
    }
}
