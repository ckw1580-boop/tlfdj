using System;
using System.Collections.Generic;
using System.Linq;

namespace ElectricalSim
{
    public sealed class G120TerminalDefinition
    {
        public int Number { get; }
        public string Signal { get; }
        public string Specification { get; }
        public int Reference { get; }
        public bool Upper { get; }
        public string Port => "T" + Number.ToString("00");
        public string Node => "G120." + Port;
        public string BoardPort => "G120_" + Port;
        public string Label => Number + " · " + Signal;
        public G120TerminalDefinition(int number, string signal, string specification, int reference, bool upper)
        { Number = number; Signal = signal; Specification = specification; Reference = reference; Upper = upper; }
    }

    // Numbered terminals are stable save-file identities. Display text and macro roles may change.
    public static class G120TerminalCatalog
    {
        public static readonly int[] DigitalNumbers = { 5, 6, 7, 8, 16, 17 };
        public static readonly int[] AddedUpper = { 1, 2, 3, 4, 10, 11, 9, 28, 69, 34, 31, 32 };
        public static readonly int[] AddedLower = { 14, 15, 12, 13, 26, 27, 20, 19, 18, 22, 21, 25, 24, 23 };
        public static readonly IReadOnlyList<G120TerminalDefinition> All = new[]
        {
            T(1,"+10V","DC 10V 输出，最大 10mA",2), T(2,"0V","模拟量公共参考端"),
            T(3,"AI0+","模拟输入 0：±10V / 0–20mA / 4–20mA",4), T(4,"AI0−","模拟输入 0 参考端"),
            T(10,"AI1+","模拟输入 1：±10V / 0–20mA / 4–20mA",11), T(11,"AI1−","模拟输入 1 参考端"),
            T(5,"DI0","DC 24V 数字输入",69), T(6,"DI1","DC 24V 数字输入",34),
            T(7,"DI2","DC 24V 数字输入",69), T(8,"DI3","DC 24V 数字输入",34),
            T(16,"DI4","DC 24V 数字输入",69), T(17,"DI5","DC 24V 数字输入",34),
            T(9,"U24V","DC 24V 控制电源输出",28), T(28,"U0V","控制电源公共参考端"),
            T(69,"COM1","DI0 / DI2 / DI4 公共端，需外部接线"), T(34,"COM2","DI1 / DI3 / DI5 公共端，需外部接线"),
            T(31,"外部24V+","外部控制电源输入：DC 18–30V",32), T(32,"外部0V","外部电源参考端"),
            T(14,"PTCA","PTC 电机温度传感器输入",15,false), T(15,"PTCB","PTC 返回端",0,false),
            T(12,"AO0+","转速监测：0–20mA / 4–20mA / 0–10V",13,false), T(13,"AO0−","模拟输出公共参考端",0,false),
            T(26,"AO1+","模拟电机电流监测：0–20mA / 4–20mA / 0–10V",27,false), T(27,"AO1−","模拟输出公共参考端",0,false),
            T(20,"DO0 COM","故障输出公共端：DC 30V / 0.5A",0,false),
            T(19,"DO0 NO","故障输出常开触点",20,false), T(18,"DO0 NC","故障输出常闭触点",20,false),
            T(22,"DO1−","报警晶体管输出负端",21,false), T(21,"DO1+","报警晶体管输出正端：DC 30V / 0.5A",22,false),
            T(25,"DO2 COM","运行使能输出公共端：DC 30V / 0.5A",0,false),
            T(24,"DO2 NO","运行使能输出常开触点",25,false), T(23,"DO2 NC","运行使能输出常闭触点",25,false)
        };
        public static G120TerminalDefinition Find(int number) => All.FirstOrDefault(t => t.Number == number);
        public static G120TerminalDefinition FromBoardPort(string name)
        {
            if (string.IsNullOrEmpty(name) || !name.StartsWith("G120_", StringComparison.Ordinal)) return null;
            var suffix = name.Substring(5).ToUpperInvariant();
            if (suffix.StartsWith("T") && int.TryParse(suffix.Substring(1), out var n)) return Find(n);
            if (suffix.StartsWith("DI") && int.TryParse(suffix.Substring(2), out var i) && i >= 0 && i < 6) return Find(DigitalNumbers[i]);
            if (suffix == "DI1_COM1") return Find(69);
            if (suffix == "DI1_COM2") return Find(34);
            return null;
        }
        public static IEnumerable<PortPair> LegacyLinks()
        {
            for (var i = 0; i < 6; i++) yield return new PortPair("DI" + i, Find(DigitalNumbers[i]).Port);
            yield return new PortPair("DI1_COM1", "T69"); yield return new PortPair("DI1_COM2", "T34");
        }
        public static string DigitalRole(int macro, int index)
        {
            string[] roles;
            switch (macro)
            {
                case 1: roles = new[] { "正转启动", "反转启动", "故障复位", "未分配", "固定转速1（P1003）", "固定转速2（P1004）" }; break;
                case 2: roles = new[] { "启动＋固定转速1", "固定转速2", "故障复位", "未分配", "安全功能预留", "安全功能预留" }; break;
                case 3: roles = new[] { "启动＋固定转速1", "固定转速2", "故障复位", "未分配", "固定转速3", "固定转速4" }; break;
                case 7: roles = new[] { "本地点动正转", "本地点动反转", "本地故障复位", "总线／本地点动切换", "未分配", "未分配" }; break;
                case 8: case 9: roles = new[] { "启动", "MOP 升速", "MOP 降速", "故障复位", macro == 8 ? "安全功能预留" : "未分配", macro == 8 ? "安全功能预留" : "未分配" }; break;
                case 12: case 13: roles = new[] { "启动", "反向", "故障复位", "未分配", macro == 13 ? "安全功能预留" : "未分配", macro == 13 ? "安全功能预留" : "未分配" }; break;
                case 14: case 15: roles = new[] { "启动", "外部故障（断开触发）", "故障复位", macro == 15 ? "模拟量／MOP 切换" : "未分配", "本地 MOP 升速", "本地 MOP 降速" }; break;
                case 17: case 18: roles = new[] { "正转启动", "反转启动", "故障复位", "未分配", "未分配", "未分配" }; break;
                case 19: case 20: roles = new[] { "停止许可（断开停止）", "脉冲正转启动", macro == 19 ? "脉冲反转启动" : "反向", "未分配", "故障复位", "未分配" }; break;
                case 21: roles = new[] { "未分配", "未分配", "故障复位", "未分配", "未分配", "未分配" }; break;
                default: roles = new[] { "总线控制", "总线控制", "总线控制", "总线控制", "安全功能预留", "安全功能预留" }; break;
            }
            return index >= 0 && index < roles.Length ? roles[index] : "";
        }
        private static G120TerminalDefinition T(int n, string s, string spec, int reference = 0, bool upper = true)
            => new G120TerminalDefinition(n, s, spec, reference, upper);
    }
}
