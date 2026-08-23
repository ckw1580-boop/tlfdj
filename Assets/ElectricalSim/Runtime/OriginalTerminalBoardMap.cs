using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace ElectricalSim
{
    public enum OriginalTerminalZone
    {
        ThreePhasePower,
        Indicator,
        SelectorAndButton
    }

    [Serializable]
    public sealed class OriginalTerminalBinding
    {
        public string AnchorId = string.Empty;
        public string DisplayName = string.Empty;
        public string WireTransformPath = string.Empty;
        public string JumperTransformPath = string.Empty;
        public string LogicalNode = string.Empty;
        public OriginalTerminalZone Zone;

        public string JumperAnchorId => string.IsNullOrEmpty(AnchorId)
            ? string.Empty
            : char.ToUpperInvariant(AnchorId[0]) + AnchorId.Substring(1);
    }

    [Serializable]
    public sealed class OriginalTerminalBoardMap
    {
        public const string DeviceId = "DuanZiPai_0";
        public const string RelativeConfigurationPath = "OfflineData/ElementConf/DuanZiPai_0.json";
        public const string BoardTransformPath = "Bench/ElectricBench/Nuts/13/DuanZiPai_0";

        public List<OriginalTerminalBinding> Bindings = new List<OriginalTerminalBinding>();

        public OriginalTerminalBinding Find(string anchorId)
            => Bindings.FirstOrDefault(item => string.Equals(item.AnchorId, anchorId, StringComparison.Ordinal));

        public static OriginalTerminalBoardMap Load(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) throw new ArgumentException("Terminal configuration path is empty.", nameof(path));
            if (!File.Exists(path)) throw new FileNotFoundException("Original terminal configuration is missing.", path);

            var root = JObject.Parse(File.ReadAllText(path));
            var points = root["actorComponents"]?["ElementPoints"]?["data"]?["points"] as JObject;
            if (points == null) throw new InvalidDataException("DuanZiPai_0.json does not contain ElementPoints.data.points.");

            var map = new OriginalTerminalBoardMap();
            foreach (var property in points.Properties())
            {
                var displayName = property.Value.Value<string>("name") ?? string.Empty;
                if (string.IsNullOrWhiteSpace(displayName)) continue;
                var anchorId = property.Name;
                var jumperAnchorId = char.ToUpperInvariant(anchorId[0]) + anchorId.Substring(1);
                map.Bindings.Add(new OriginalTerminalBinding
                {
                    AnchorId = anchorId,
                    DisplayName = displayName,
                    WireTransformPath = BoardTransformPath + "/point/" + anchorId,
                    JumperTransformPath = BoardTransformPath + "/point/" + jumperAnchorId,
                    LogicalNode = ResolveLogicalNode(displayName),
                    Zone = ResolveZone(displayName)
                });
            }

            return map;
        }

        private static OriginalTerminalZone ResolveZone(string displayName)
        {
            if (displayName.StartsWith("HL", StringComparison.OrdinalIgnoreCase)) return OriginalTerminalZone.Indicator;
            if (displayName.StartsWith("SA", StringComparison.OrdinalIgnoreCase) ||
                displayName.StartsWith("SB", StringComparison.OrdinalIgnoreCase)) return OriginalTerminalZone.SelectorAndButton;
            return OriginalTerminalZone.ThreePhasePower;
        }

        private static string ResolveLogicalNode(string displayName)
        {
            if (displayName == "24V+") return "TERMINAL_BUS.DC_POSITIVE";
            if (displayName == "24V-") return "TERMINAL_BUS.DC_NEGATIVE";
            if (displayName.Length >= 2 && char.IsDigit(displayName[displayName.Length - 1]))
            {
                switch (char.ToUpperInvariant(displayName[0]))
                {
                    case 'U': return "POWER.L1";
                    case 'V': return "POWER.L2";
                    case 'W': return "POWER.L3";
                    case 'N': return "POWER.N";
                }
            }

            var separator = displayName.IndexOf('_');
            if (separator <= 0 || separator == displayName.Length - 1)
                return DeviceId + "." + displayName;
            var device = displayName.Substring(0, separator);
            var port = displayName.Substring(separator + 1);
            if ((device == "SB1" || device == "SB2") && port.StartsWith("COM", StringComparison.Ordinal)) port = "COM";
            else if ((device == "SB1" || device == "SB2") && port.StartsWith("NO", StringComparison.Ordinal)) port = "NO";
            else if ((device == "SB1" || device == "SB2") && port.StartsWith("NC", StringComparison.Ordinal)) port = "NC";
            return device + "." + port;
        }
    }
}
