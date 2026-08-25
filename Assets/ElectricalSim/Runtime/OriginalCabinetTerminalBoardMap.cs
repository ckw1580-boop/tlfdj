using System;
using System.Collections.Generic;

namespace ElectricalSim
{
    public sealed class OriginalCabinetTerminalBoardDefinition
    {
        public string DeviceId = string.Empty;
        public string DisplayName = string.Empty;
    }

    public static class OriginalCabinetTerminalBoardMap
    {
        public static readonly IReadOnlyList<OriginalCabinetTerminalBoardDefinition> Boards =
            new[]
            {
                new OriginalCabinetTerminalBoardDefinition
                {
                    DeviceId = "DuanZiPai_1",
                    DisplayName = "PLC输入及中间继电器端子排"
                },
                new OriginalCabinetTerminalBoardDefinition
                {
                    DeviceId = "DuanZiPai_2",
                    DisplayName = "PLC输出及中间继电器端子排"
                }
            };

        public static bool IsTerminalName(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return false;
            return name.StartsWith("PLC_1_", StringComparison.Ordinal) ||
                   name.StartsWith("PLC_2_", StringComparison.Ordinal) ||
                   IsRelayTerminalName(name);
        }

        public static string ResolveLogicalNode(string terminalName)
        {
            if (!IsTerminalName(terminalName)) return string.Empty;
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

        private static bool IsRelayTerminalName(string name)
        {
            if (!name.StartsWith("KA", StringComparison.Ordinal) || name.Length < 5) return false;
            var separator = name.IndexOf('_');
            if (separator != 3 || !char.IsDigit(name[2])) return false;
            for (var index = separator + 1; index < name.Length; index++)
                if (!char.IsDigit(name[index])) return false;
            return true;
        }
    }
}
