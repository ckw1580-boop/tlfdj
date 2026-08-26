using System;
using System.Collections.Generic;

namespace ElectricalSim
{
    public enum OriginalCabinetTerminalBoardKind
    {
        PlcRelay,
        DriveContactorUpper,
        DriveContactorLower,
        PowerDistribution,
        Motor,
        SceneIo
    }

    public sealed class OriginalCabinetTerminalBoardDefinition
    {
        public string DeviceId = string.Empty;
        public string DisplayName = string.Empty;
        public OriginalCabinetTerminalBoardKind Kind;
        public int ExpectedPortCount;
        public bool UsesSeparateJumperAnchors;
        public bool AlwaysUsesElectricalAnchor;
        public bool AlwaysUsesJumperAnchor;
    }

    public static class OriginalCabinetTerminalBoardMap
    {
        private static readonly string[] TimerPorts = { "A1", "A2", "15", "16", "18" };

        public static readonly IReadOnlyList<OriginalCabinetTerminalBoardDefinition> Boards =
            new[]
            {
                new OriginalCabinetTerminalBoardDefinition
                {
                    DeviceId = "DuanZiPai_1",
                    DisplayName = "PLC输入及中间继电器端子排",
                    Kind = OriginalCabinetTerminalBoardKind.PlcRelay,
                    ExpectedPortCount = 78
                },
                new OriginalCabinetTerminalBoardDefinition
                {
                    DeviceId = "DuanZiPai_2",
                    DisplayName = "PLC输出及中间继电器端子排",
                    Kind = OriginalCabinetTerminalBoardKind.PlcRelay,
                    ExpectedPortCount = 66
                },
                new OriginalCabinetTerminalBoardDefinition
                {
                    DeviceId = "DuanZiPai_3",
                    DisplayName = "G120、交流接触器、FR及KT上端子排",
                    Kind = OriginalCabinetTerminalBoardKind.DriveContactorUpper,
                    ExpectedPortCount = 64,
                    UsesSeparateJumperAnchors = true,
                    AlwaysUsesElectricalAnchor = true
                },
                new OriginalCabinetTerminalBoardDefinition
                {
                    DeviceId = "DuanZiPai_4",
                    DisplayName = "G120、交流接触器及FR下端子排",
                    Kind = OriginalCabinetTerminalBoardKind.DriveContactorLower,
                    ExpectedPortCount = 48,
                    UsesSeparateJumperAnchors = true,
                    AlwaysUsesJumperAnchor = true
                },
                new OriginalCabinetTerminalBoardDefinition
                {
                    DeviceId = "DuanZiPai_6",
                    DisplayName = "电源端子区",
                    Kind = OriginalCabinetTerminalBoardKind.PowerDistribution,
                    ExpectedPortCount = 8,
                    UsesSeparateJumperAnchors = true
                },
                new OriginalCabinetTerminalBoardDefinition
                {
                    DeviceId = "DuanZiPai_7",
                    DisplayName = "电机端子区",
                    Kind = OriginalCabinetTerminalBoardKind.Motor,
                    ExpectedPortCount = 18,
                    UsesSeparateJumperAnchors = true
                },
                new OriginalCabinetTerminalBoardDefinition
                {
                    DeviceId = "DuanZiPai_8",
                    DisplayName = "场景中传感器、电磁阀端子",
                    Kind = OriginalCabinetTerminalBoardKind.SceneIo,
                    ExpectedPortCount = 18,
                    UsesSeparateJumperAnchors = true,
                    AlwaysUsesElectricalAnchor = true
                }
            };

        // Kept for callers and saved data created before the lower cabinet boards were mapped.
        public static bool IsTerminalName(string name) => IsPlcRelayTerminalName(name);

        public static bool IsTerminalName(OriginalCabinetTerminalBoardDefinition board, string name)
        {
            if (board == null || string.IsNullOrWhiteSpace(name)) return false;
            if (board.Kind == OriginalCabinetTerminalBoardKind.PlcRelay)
                return IsPlcRelayTerminalName(name);
            if (board.Kind == OriginalCabinetTerminalBoardKind.PowerDistribution)
                return IsPowerDistributionElectricalAnchorName(name);
            if (board.Kind == OriginalCabinetTerminalBoardKind.Motor)
                return IsMotorElectricalAnchorName(name);
            if (board.Kind == OriginalCabinetTerminalBoardKind.SceneIo)
                return IsSceneIoElectricalAnchorName(name);

            if (board.Kind == OriginalCabinetTerminalBoardKind.DriveContactorUpper &&
                TryGetGenericAnchorNumber(name, out var number))
                return name[0] == 'a' && number >= 60 && number <= 64;

            if (!ContainsLowercase(name)) return false;
            return name.StartsWith("G120_", StringComparison.Ordinal) ||
                   name.StartsWith("KM1_", StringComparison.Ordinal) ||
                   name.StartsWith("KM2_", StringComparison.Ordinal) ||
                   name.StartsWith("KM3_", StringComparison.Ordinal) ||
                   name.StartsWith("KM4_", StringComparison.Ordinal) ||
                   name.StartsWith("FR1_", StringComparison.Ordinal) ||
                   name.StartsWith("FR2_", StringComparison.Ordinal);
        }

        public static string GetPortName(OriginalCabinetTerminalBoardDefinition board, string electricalAnchorName)
        {
            if (board == null || string.IsNullOrWhiteSpace(electricalAnchorName)) return string.Empty;
            if (board.Kind == OriginalCabinetTerminalBoardKind.DriveContactorUpper &&
                TryGetGenericAnchorNumber(electricalAnchorName, out var number) && number >= 60 && number <= 64)
                return "KT_" + TimerPorts[number - 60];
            if (board.Kind == OriginalCabinetTerminalBoardKind.PowerDistribution ||
                board.Kind == OriginalCabinetTerminalBoardKind.Motor ||
                board.Kind == OriginalCabinetTerminalBoardKind.SceneIo)
                return UppercaseFirstCharacter(electricalAnchorName);
            return board.UsesSeparateJumperAnchors
                ? electricalAnchorName.ToUpperInvariant()
                : electricalAnchorName;
        }

        public static string GetJumperAnchorName(
            OriginalCabinetTerminalBoardDefinition board,
            string electricalAnchorName)
        {
            if (board == null || !board.UsesSeparateJumperAnchors) return electricalAnchorName;
            if (board.Kind == OriginalCabinetTerminalBoardKind.PowerDistribution ||
                board.Kind == OriginalCabinetTerminalBoardKind.Motor ||
                board.Kind == OriginalCabinetTerminalBoardKind.SceneIo)
                return UppercaseFirstCharacter(electricalAnchorName);
            return electricalAnchorName.ToUpperInvariant();
        }

        public static string ResolveLogicalNode(string terminalName)
        {
            if (!IsPlcRelayTerminalName(terminalName)) return string.Empty;
            return ResolvePlcRelayLogicalNode(terminalName);
        }

        public static string ResolveLogicalNode(
            OriginalCabinetTerminalBoardDefinition board,
            string terminalName)
        {
            if (board == null || string.IsNullOrWhiteSpace(terminalName)) return string.Empty;
            if (board.Kind == OriginalCabinetTerminalBoardKind.PlcRelay)
                return ResolvePlcRelayLogicalNode(terminalName);
            if (board.Kind == OriginalCabinetTerminalBoardKind.PowerDistribution)
                return ResolvePowerDistributionLogicalNode(terminalName);
            if (board.Kind == OriginalCabinetTerminalBoardKind.Motor)
                return ResolveMotorLogicalNode(terminalName);
            if (board.Kind == OriginalCabinetTerminalBoardKind.SceneIo)
                return ResolveSceneIoLogicalNode(terminalName);
            if (terminalName.StartsWith("KT_", StringComparison.Ordinal))
                return "KT." + terminalName.Substring(3);
            if (terminalName.StartsWith("G120_", StringComparison.Ordinal))
                return "G120." + terminalName.Substring(5);
            if (terminalName.StartsWith("FR1_", StringComparison.Ordinal) ||
                terminalName.StartsWith("FR2_", StringComparison.Ordinal))
                return "FR." + NormalizePowerTerminal(terminalName.Substring(4));
            if (terminalName.StartsWith("KM", StringComparison.Ordinal) && terminalName.Length > 4 &&
                char.IsDigit(terminalName[2]) && terminalName[3] == '_')
            {
                var runtimeDevice = RuntimeContactorId(terminalName[2]);
                return string.IsNullOrEmpty(runtimeDevice)
                    ? string.Empty
                    : runtimeDevice + "." + NormalizeContactorTerminal(terminalName.Substring(4));
            }
            return string.Empty;
        }

        private static string ResolvePlcRelayLogicalNode(string terminalName)
        {
            if (!IsPlcRelayTerminalName(terminalName)) return string.Empty;
            if (terminalName.StartsWith("PLC_1_", StringComparison.Ordinal) ||
                terminalName.StartsWith("PLC_2_", StringComparison.Ordinal))
            {
                var deviceLength = "PLC_1".Length;
                return terminalName.Substring(0, deviceLength) + "." + terminalName.Substring(deviceLength + 1);
            }

            var separator = terminalName.IndexOf('_');
            return separator > 0
                ? terminalName.Substring(0, separator) + "." + terminalName.Substring(separator + 1)
                : terminalName;
        }

        private static string ResolveMotorLogicalNode(string terminalName)
        {
            if (string.IsNullOrWhiteSpace(terminalName) || terminalName.Length != 4 || terminalName[1] != '_')
                return string.Empty;

            string runtimeDevice;
            switch (char.ToUpperInvariant(terminalName[0]))
            {
                case 'A': runtimeDevice = "M1"; break;
                case 'B': runtimeDevice = "M_DOUBLE"; break;
                case 'C': runtimeDevice = "M2"; break;
                default: return string.Empty;
            }

            var winding = terminalName.Substring(2).ToUpperInvariant();
            if (winding == "U1") return runtimeDevice + ".U";
            if (winding == "V1") return runtimeDevice + ".V";
            if (winding == "W1") return runtimeDevice + ".W";
            return runtimeDevice + "." + winding;
        }

        private static string ResolvePowerDistributionLogicalNode(string terminalName)
        {
            if (string.IsNullOrWhiteSpace(terminalName) || terminalName.Length != 3 ||
                terminalName[1] != '_' || terminalName[2] < '1' || terminalName[2] > '4')
                return string.Empty;
            if (terminalName[0] == 'V') return "POWER.L1";
            if (terminalName[0] == 'N') return "POWER.N";
            return string.Empty;
        }

        private static string ResolveSceneIoLogicalNode(string terminalName)
        {
            if (string.IsNullOrWhiteSpace(terminalName)) return string.Empty;
            var separator = terminalName.IndexOf('_');
            if (separator <= 0 || separator == terminalName.Length - 1) return string.Empty;
            var group = terminalName.Substring(0, separator);
            var signal = terminalName.Substring(separator + 1).ToUpperInvariant();
            if (group.Length == 1 && group[0] >= 'A' && group[0] <= 'D')
                return "SENSOR_" + group + "." + signal;
            if (group.StartsWith("Diancifa", StringComparison.Ordinal) && group.Length == 9 &&
                group[8] >= '1' && group[8] <= '3')
                return "SOLENOID" + group[8] + "." + signal;
            return string.Empty;
        }

        private static string RuntimeContactorId(char physicalIndex)
        {
            switch (physicalIndex)
            {
                case '1': return "KMF";
                case '2': return "KM1";
                case '3': return "KMR";
                case '4': return "KM2";
                default: return string.Empty;
            }
        }

        private static string NormalizePowerTerminal(string terminal)
        {
            switch (terminal)
            {
                case "1L1": return "L1";
                case "3L2": return "L2";
                case "5L3": return "L3";
                case "2T1": return "T1";
                case "4T2": return "T2";
                case "6T3": return "T3";
                case "95NC": return "95";
                case "96NC": return "96";
                case "97NO": return "97";
                case "98NO": return "98";
                default: return terminal;
            }
        }

        private static string NormalizeContactorTerminal(string terminal)
        {
            switch (terminal)
            {
                case "53NO":
                case "83NO":
                case "13NO": return "13";
                case "54NO":
                case "84NO":
                case "14NO": return "14";
                case "61NC":
                case "71NC": return "21";
                case "62NC":
                case "72NC": return "22";
                default: return NormalizePowerTerminal(terminal);
            }
        }

        private static bool IsPlcRelayTerminalName(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return false;
            return name.StartsWith("PLC_1_", StringComparison.Ordinal) ||
                   name.StartsWith("PLC_2_", StringComparison.Ordinal) ||
                   IsRelayTerminalName(name);
        }

        private static bool IsMotorElectricalAnchorName(string name)
        {
            if (string.IsNullOrWhiteSpace(name) || name.Length != 4 || name[1] != '_') return false;
            if (name[0] != 'a' && name[0] != 'b' && name[0] != 'c') return false;
            var phase = name[2];
            return (phase == 'u' || phase == 'v' || phase == 'w') &&
                   (name[3] == '1' || name[3] == '2');
        }

        private static bool IsPowerDistributionElectricalAnchorName(string name)
        {
            return !string.IsNullOrWhiteSpace(name) && name.Length == 3 &&
                   (name[0] == 'v' || name[0] == 'n') && name[1] == '_' &&
                   name[2] >= '1' && name[2] <= '4';
        }

        private static bool IsSceneIoElectricalAnchorName(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return false;
            if (name.Length >= 5 && name[0] >= 'a' && name[0] <= 'd' && name[1] == '_')
            {
                var signal = name.Substring(2);
                return signal == "VCC" || signal == "SIGNAL" || signal == "GND";
            }
            if (!name.StartsWith("diancifa", StringComparison.Ordinal) || name.Length < 12) return false;
            var separator = name.IndexOf('_');
            if (separator != 9 || name[8] < '1' || name[8] > '3') return false;
            var solenoidSignal = name.Substring(separator + 1);
            return solenoidSignal == "VCC" || solenoidSignal == "GND";
        }

        private static string UppercaseFirstCharacter(string value)
        {
            if (string.IsNullOrEmpty(value)) return value;
            return char.ToUpperInvariant(value[0]) + value.Substring(1);
        }

        private static bool IsRelayTerminalName(string name)
        {
            if (!name.StartsWith("KA", StringComparison.Ordinal) || name.Length < 5) return false;
            var separator = name.IndexOf('_');
            if (separator != 3 || !char.IsDigit(name[2])) return false;
            for (var index = separator + 1; index < name.Length; index++)
                if (!char.IsDigit(name[index])) return false;
            return true;
        }

        private static bool ContainsLowercase(string value)
        {
            foreach (var character in value)
                if (char.IsLower(character)) return true;
            return false;
        }

        private static bool TryGetGenericAnchorNumber(string name, out int number)
        {
            number = 0;
            return !string.IsNullOrWhiteSpace(name) && name.Length > 1 &&
                   (name[0] == 'a' || name[0] == 'A') &&
                   int.TryParse(name.Substring(1), out number);
        }
    }
}
