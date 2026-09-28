using System;
using System.Linq;

namespace ElectricalSim
{
    /// <summary>Recognizes external bridges without treating a winding as an ideal wire.</summary>
    public static class MotorConnectionResolver
    {
        private static readonly string[] Terminals = { "U", "V", "W", "U2", "V2", "W2" };

        public static MotorConnectionResult Resolve(string deviceId, MotorConfiguration config, SimulationSnapshot snapshot)
        {
            var result = new MotorConnectionResult();
            var ports = Terminals.Select(p => CircuitGraph.Port(deviceId, p)).ToArray();
            var potentials = ports.Select(snapshot.GetPotential).ToArray();
            snapshot.MotorDrives.TryGetValue(deviceId, out var drive);
            var phases = potentials.Select(PhaseIndex).ToArray();
            var mainsConnected = phases.Any(p => p >= 0);
            if (ports.Any(p => snapshot.SameNet(p, "POWER.PE")))
                return Invalid(result, "绕组端子误接保护地PE，请断开接地线；保护地不得用作绕组工作端。");
            if (potentials.Any(p => p == ElectricalPotential.DcPositive24 || p == ElectricalPotential.DcNegative))
                return Invalid(result, "交流电机绕组误接直流控制电源。");
            if (potentials.Any(p => p == ElectricalPotential.Conflict))
                return Invalid(result, "电机端子存在电源短接，请检查桥接和供电接线。");
            if (drive.Connected)
            {
                result.DriveId = drive.SourceId ?? string.Empty;
                if (mainsConnected) return Invalid(result, "电机绕组同时接入工频与变频电源。");
                if (!string.IsNullOrEmpty(drive.Diagnostic)) return Invalid(result, drive.Diagnostic);
                phases = drive.TerminalPhases == null || drive.TerminalPhases.Length != 6
                    ? Enumerable.Repeat(-1, 6).ToArray() : (int[])drive.TerminalPhases.Clone();
            }
            bool Same(int a, int b) => snapshot.SameNet(ports[a], ports[b]);
            bool Common(int first) => Same(first, first + 1) && Same(first, first + 2);
            var headsCommon = Common(0);
            var tailsCommon = Common(3);
            var anySupply = phases.Any(p => p >= 0);
            var supplyStart = 0;

            if (config.IsTwoSpeed)
            {
                if (potentials.Any(p => p == ElectricalPotential.Neutral))
                    return Invalid(result, "双速电机绕组接入N线，不符合本机Δ/YY接法。");
                var headsPowered = phases.Take(3).Any(p => p >= 0);
                var tailsPowered = phases.Skip(3).Any(p => p >= 0);
                if (headsPowered && tailsPowered)
                    return Invalid(result, "双速电机高低速同时接通或存在错误桥接。");
                if (tailsPowered)
                {
                    if (!headsCommon) return Invalid(result, "高速YY缺少U1/V1/W1公共短接线。");
                    result.Kind = MotorConnectionKind.DoubleHigh;
                    result.IsHighSpeed = true;
                    supplyStart = 3;
                }
                else if (headsPowered)
                {
                    for (var tail = 3; tail < 6; tail++)
                        for (var other = 0; other < 6; other++)
                            if (tail != other && Same(tail, other))
                                return Invalid(result, "低速Δ要求U2/V2/W2外部不供电、不桥接。");
                    result.Kind = MotorConnectionKind.DoubleLow;
                }
                else if (headsCommon)
                {
                    result.Kind = MotorConnectionKind.DoubleHigh;
                    result.IsHighSpeed = true;
                    supplyStart = 3;
                }
                else return result;
            }
            else
            {
                if (tailsCommon && !headsCommon)
                {
                    if (phases.Skip(3).Any(p => p >= 0))
                        return Invalid(result, "星形公共端U2/V2/W2不可接入相线。");
                    result.Kind = MotorConnectionKind.Star;
                }
                else if (headsCommon && !tailsCommon)
                {
                    if (phases.Take(3).Any(p => p >= 0))
                        return Invalid(result, "星形公共端U1/V1/W1不可接入相线。");
                    result.Kind = MotorConnectionKind.Star;
                    supplyStart = 3;
                }
                else if ((Same(3, 1) && Same(4, 2) && Same(5, 0)) ||
                         (Same(3, 2) && Same(4, 0) && Same(5, 1)))
                {
                    if (Same(0, 1) || Same(1, 2) || Same(0, 2))
                        return Invalid(result, "三角形桥接将相线短接。");
                    result.Kind = MotorConnectionKind.Delta;
                }
                else
                {
                    if (!anySupply) return result;
                    return Invalid(result, "U1–U2、V1–V2、W1–W2绕组未形成正确星形或三角形接法；请检查六端子桥接。");
                }
            }

            result.SupplyPorts = ports.Skip(supplyStart).Take(3).ToArray();
            result.PhaseIndices = phases.Skip(supplyStart).Take(3).ToArray();
            var supplied = result.PhaseIndices.Count(p => p >= 0);
            var distinct = result.PhaseIndices.Where(p => p >= 0).Distinct().Count();
            if (supplied != distinct)
                return Invalid(result, "电机供电端存在重复相线，不能形成三相旋转磁场。");
            result.PhaseLoss = supplied > 0 && supplied < 3;
            result.DirectionSign = SequenceSign(result.PhaseIndices);
            result.FrequencyHz = drive.Connected ? drive.FrequencyHz : 50f;
            result.LineVoltage = drive.Connected ? drive.LineVoltage : 380f;
            result.Energized = supplied >= 2 && (!drive.Connected || drive.HasDrive) &&
                result.FrequencyHz > 0.05f && result.LineVoltage > 0f;
            result.WindingVoltage = result.Kind == MotorConnectionKind.Star
                ? result.LineVoltage / (float)Math.Sqrt(3d) : result.LineVoltage;
            if (result.PhaseLoss)
                result.Diagnostic = "缺相：" + string.Join("、", result.SupplyPorts.Where((p, i) => result.PhaseIndices[i] < 0)
                    .Select(p => p.EndsWith(".U") || p.EndsWith(".V") || p.EndsWith(".W") ? p + "1" : p)) + "未连接有效电源；静止不能自启动。";
            else if (!result.Energized && drive.Connected)
                result.Diagnostic = "变频输出未使能、频率为零或输入电源不完整。";
            return result;
        }

        /// <summary>All cyclic permutations keep direction; any two-phase exchange reverses it.</summary>
        public static int SequenceSign(int[] phaseIndices)
        {
            if (phaseIndices == null || phaseIndices.Length != 3) return 0;
            var phases = (int[])phaseIndices.Clone();
            if (phases.Count(p => p < 0) == 1 && phases.Where(p => p >= 0).Distinct().Count() == 2)
            {
                var missing = Enumerable.Range(0, 3).First(p => !phases.Contains(p));
                phases[Array.FindIndex(phases, p => p < 0)] = missing;
            }
            if (phases.Any(p => p < 0 || p > 2) || phases.Distinct().Count() != 3) return 0;
            var inversions = 0;
            for (var i = 0; i < 3; i++)
                for (var j = i + 1; j < 3; j++) if (phases[i] > phases[j]) inversions++;
            return inversions % 2 == 0 ? 1 : -1;
        }

        private static int PhaseIndex(ElectricalPotential potential)
        {
            switch (potential)
            {
                case ElectricalPotential.PhaseL1: return 0;
                case ElectricalPotential.PhaseL2: return 1;
                case ElectricalPotential.PhaseL3: return 2;
                default: return -1;
            }
        }
        private static MotorConnectionResult Invalid(MotorConnectionResult result, string diagnostic)
        {
            result.Kind = MotorConnectionKind.Invalid;
            result.Energized = false;
            result.Diagnostic = diagnostic;
            return result;
        }
    }
}
