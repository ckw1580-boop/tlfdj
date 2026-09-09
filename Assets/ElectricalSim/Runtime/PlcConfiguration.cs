using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text.RegularExpressions;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace ElectricalSim
{
    public enum SiemensCpu { S71200, S71500 }
    public enum PlcConnectionState { Disconnected, Connecting, Connected, Disconnecting, Faulted }

    public sealed class PlcBitAddress
    {
        private static readonly Regex Syntax = new Regex(@"^(?:(?<area>[MQ])|DB(?<db>[0-9]+)\.DBX)(?<offset>[0-9]+)\.(?<bit>[0-7])$", RegexOptions.CultureInvariant);
        public char Area { get; private set; }
        public int Db { get; private set; }
        public int Offset { get; private set; }
        public int Bit { get; private set; }
        public static PlcBitAddress Parse(string text, bool writable = false)
        {
            var match = Syntax.Match((text ?? "").Trim().ToUpperInvariant());
            if (!match.Success || !int.TryParse(match.Groups["offset"].Value, out var offset) || offset > 2097151)
                throw new ArgumentException("地址格式应为 M0.0、Q0.0 或 DB1.DBX0.0：" + text);
            var db = 0;
            var area = match.Groups["area"].Success ? match.Groups["area"].Value[0] : 'D';
            if (area == 'D' && (!int.TryParse(match.Groups["db"].Value, out db) || db < 1 || db > 65535))
                throw new ArgumentException("DB 编号必须为 1～65535：" + text);
            if (writable && area == 'Q') throw new ArgumentException("虚拟输入只能写入 M 或 DB 位：" + text);
            return new PlcBitAddress { Area = area, Db = db, Offset = offset, Bit = int.Parse(match.Groups["bit"].Value) };
        }
        public override string ToString() => (Area == 'D' ? "DB" + Db + ".DBX" : Area.ToString()) + Offset + "." + Bit;
    }

    [Serializable]
    public sealed class PlcPointBinding
    {
        public string Terminal;
        public string Address;
        [JsonExtensionData] public IDictionary<string, JToken> Extra = new Dictionary<string, JToken>();
    }

    [Serializable]
    public sealed class PlcConfiguration
    {
        public string DeviceId;
        public string DisplayName;
        public SiemensCpu Cpu = SiemensCpu.S71200;
        public string Ip = "";
        public int Port = 102;
        public short Rack;
        public short Slot;
        public int PollIntervalMs = 100;
        public int TimeoutMs = 2000;
        public List<PlcPointBinding> Inputs = new List<PlcPointBinding>();
        public List<PlcPointBinding> Outputs = new List<PlcPointBinding>();
        [JsonExtensionData] public IDictionary<string, JToken> Extra = new Dictionary<string, JToken>();
        public static readonly string[] InputTerminals = Enumerable.Range(0, 14).Select(i => "M" + i / 8 + "." + i % 8).ToArray();
        public static readonly string[] OutputTerminals = Enumerable.Range(0, 10).Select(i => "Q" + i / 8 + "." + i % 8).ToArray();
        public static readonly string[] SupplyTerminals = { "L+", "M", "PE", "1M", "3L+", "3M-" };
        public static PlcConfiguration Create(string id) => new PlcConfiguration
        {
            DeviceId = id, DisplayName = id,
            Inputs = InputTerminals.Select(t => new PlcPointBinding { Terminal = t, Address = t }).ToList(),
            Outputs = OutputTerminals.Select(t => new PlcPointBinding { Terminal = t, Address = t }).ToList()
        };
        public PlcConfiguration Clone() => JsonConvert.DeserializeObject<PlcConfiguration>(JsonConvert.SerializeObject(this));
        public void Validate(bool requireIp = false)
        {
            if (DeviceId != "PLC_1" && DeviceId != "PLC_2") throw new ArgumentException("未知 PLC 标识。");
            if (string.IsNullOrWhiteSpace(DisplayName) || DisplayName.Length > 64) throw new ArgumentException("设备名称需为 1～64 个字符。");
            if (!Enum.IsDefined(typeof(SiemensCpu), Cpu)) throw new ArgumentException("不支持的 CPU 型号。");
            if ((requireIp || !string.IsNullOrWhiteSpace(Ip)) && (!IPAddress.TryParse(Ip, out var ip) || ip.AddressFamily != System.Net.Sockets.AddressFamily.InterNetwork))
                throw new ArgumentException("请输入真实 PLC 的 IPv4 地址。");
            if (Port < 1 || Port > 65535 || Rack < 0 || Rack > 7 || Slot < 0 || Slot > 31) throw new ArgumentException("端口、Rack 或 Slot 超出范围。");
            if (PollIntervalMs < 20 || PollIntervalMs > 10000 || TimeoutMs < 100 || TimeoutMs > 30000) throw new ArgumentException("轮询周期范围为 20～10000 ms，超时为 100～30000 ms。");
            ValidatePoints(Inputs, InputTerminals, true);
            ValidatePoints(Outputs, OutputTerminals, false);
        }
        private static void ValidatePoints(List<PlcPointBinding> points, string[] terminals, bool writable)
        {
            if (points == null || points.Count != terminals.Length || points.Any(p => p == null) ||
                !points.Select(p => p.Terminal).SequenceEqual(terminals)) throw new ArgumentException("PLC 端子集合或顺序不完整。");
            var addresses = points.Select(p => PlcBitAddress.Parse(p.Address, writable).ToString()).ToArray();
            if (writable && addresses.Distinct().Count() != addresses.Length) throw new ArgumentException("输入写入地址不能重复。");
            for (var i = 0; i < points.Count; i++) points[i].Address = addresses[i];
        }
    }
}
