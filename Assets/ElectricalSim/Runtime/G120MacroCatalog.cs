using System;
using System.Collections.Generic;
using System.Linq;

namespace ElectricalSim
{
    /// <summary>
    /// CU240E-2 V4.4 terminal macro catalogue, based on Siemens document A0633.
    /// The strings intentionally use the drive parameter notation from the manual.
    /// </summary>
    internal sealed class G120MacroDefinition
    {
        public G120MacroDefinition(int number, string name, int telegram, params string[] automaticSettings)
        {
            Number = number;
            Name = name;
            Telegram = telegram;
            AutomaticSettings = automaticSettings ?? Array.Empty<string>();
        }

        public int Number { get; }
        public string Name { get; }
        public int Telegram { get; }
        public IReadOnlyList<string> AutomaticSettings { get; }
    }

    internal static class G120MacroCatalog
    {
        private static readonly G120MacroDefinition[] Definitions =
        {
            Macro(1, "双方向两线制控制，两个固定转速", 0,
                "P840[0]=r3333.0", "P1113[0]=r3333.1", "P3330[0]=r722.0", "P3331[0]=r722.1",
                "P2103[0]=r722.2", "P1022[0]=r722.4", "P1023[0]=r722.5", "P1070[0]=r1024"),
            Macro(2, "单方向两个固定转速，预留安全功能", 0,
                "P840[0]=r722.0", "P1020[0]=r722.0", "P1021[0]=r722.1", "P2103[0]=r722.2",
                "P1070[0]=r1024"),
            Macro(3, "单方向四个固定转速", 0,
                "P840[0]=r722.0", "P1020[0]=r722.0", "P1021[0]=r722.1", "P1022[0]=r722.4",
                "P1023[0]=r722.5", "P2103[0]=r722.2", "P1070[0]=r1024"),
            Macro(4, "现场总线 PROFIBUS 控制", 352,
                "P922=352", "P1070[0]=r2050.1", "P2051[0]=r2089.0", "P2051[1]=r63.1",
                "P2051[2]=r68.1", "P2051[3]=r80.1", "P2051[4]=r2132", "P2051[5]=r2131"),
            Macro(5, "现场总线 PROFIBUS 控制，预留安全功能", 352,
                "P922=352", "P1070[0]=r2050.1", "P2051[0]=r2089.0", "P2051[1]=r63.1",
                "P2051[2]=r68.1", "P2051[3]=r80.1", "P2051[4]=r2132", "P2051[5]=r2131"),
            Macro(6, "现场总线 PROFIBUS 控制，预留两项安全功能", 1,
                "P922=1", "P1070[0]=r2050.1", "P2051[0]=r2089.0", "P2051[1]=r63.0"),
            Macro(7, "PROFIBUS 控制和点动切换", 1,
                "P922=1", "P1070[0]=r2050.1", "P1070[1]=0", "P2103[0]=r2090.7",
                "P2103[1]=r722.2", "P2014[0]=r722.2", "P2014[1]=0", "P1055[0]=0",
                "P1055[1]=r722.0", "P1056[0]=0", "P1056[1]=r722.1", "P810=r722.3"),
            Macro(8, "电动电位器 MOP，预留安全功能", 0,
                "P840[0]=r722.0", "P1035[0]=r722.1", "P1036[0]=r722.2", "P2103[0]=r722.3",
                "P1070[0]=r1050"),
            Macro(9, "电动电位器 MOP", 0,
                "P840[0]=r722.0", "P1035[0]=r722.1", "P1036[0]=r722.2", "P2103[0]=r722.3",
                "P1070[0]=r1050"),
            Macro(12, "端子启动模拟量调速", 0,
                "P840[0]=r722.0", "P1113[0]=r722.1", "P2103[0]=r722.2", "P1070[0]=r755.0"),
            Macro(13, "端子启动模拟量调速，预留安全功能", 0,
                "P840[0]=r722.0", "P1113[0]=r722.1", "P2103[0]=r722.2", "P1070[0]=r755.0"),
            Macro(14, "PROFIBUS 控制和 MOP 切换", 20,
                "P922=20", "P1070[0]=r2050.1", "P1070[1]=r1050", "P840[0]=r2090.0",
                "P840[1]=r722.0", "P2106[0]=r722.1", "P2106[1]=r722.1", "P2103[0]=r2090.7",
                "P2103[1]=r722.2", "P1035[0]=0", "P1035[1]=r722.4", "P1036[0]=0",
                "P1036[1]=r722.5", "P810=r2090.15", "P2051[0]=r2089.0", "P2051[1]=r63.1",
                "P2051[2]=r68.1", "P2051[3]=r80.1", "P2051[4]=r82.1", "P2051[5]=r3113"),
            Macro(15, "模拟量给定和 MOP 给定切换", 0,
                "P840[0]=r722.0", "P840[1]=r722.0", "P2106[0]=r722.1", "P2106[1]=r722.1",
                "P2103[0]=r722.2", "P2103[1]=r722.2", "P1035[0]=0", "P1035[1]=r722.4",
                "P1036[0]=0", "P1036[1]=r722.5", "P810=r722.3", "P1070[0]=r755.0",
                "P1070[1]=r1050"),
            Macro(17, "双方向两线制控制，模拟量调速（方法 2）", 0,
                "P840[0]=r3333.0", "P1113[0]=r3333.1", "P3330[0]=r722.0", "P3331[0]=r722.1",
                "P2103[0]=r722.2", "P1070[0]=r755.0"),
            Macro(18, "双方向两线制控制，模拟量调速（方法 3）", 0,
                "P840[0]=r3333.0", "P1113[0]=r3333.1", "P3330[0]=r722.0", "P3331[0]=r722.1",
                "P2103[0]=r722.2", "P1070[0]=r755.0"),
            Macro(19, "双方向三线制控制，模拟量调速（方法 1）", 0,
                "P840[0]=r3333.0", "P1113[0]=r3333.1", "P3330[0]=r722.0", "P3331[0]=r722.1",
                "P3332[0]=r722.2", "P2103[0]=r722.4", "P1070[0]=r755.0"),
            Macro(20, "双方向三线制控制，模拟量调速（方法 2）", 0,
                "P840[0]=r3333.0", "P1113[0]=r3333.1", "P3330[0]=r722.0", "P3331[0]=r722.1",
                "P3332[0]=r722.2", "P2103[0]=r722.4", "P1070[0]=r755.0"),
            Macro(21, "现场总线 USS 控制", 0,
                "P2104[0]=r722.2", "P1070[0]=r2050.1", "P2051[0]=r2089.0", "P2051[1]=r63.0")
        };

        public static IReadOnlyList<int> SupportedNumbers { get; } =
            Definitions.Select(item => item.Number).ToArray();

        public static G120MacroDefinition Get(int number)
            => Definitions.FirstOrDefault(item => item.Number == number) ?? Definitions[0];

        private static G120MacroDefinition Macro(int number, string name, int telegram, params string[] settings)
            => new G120MacroDefinition(number, name, telegram, settings);
    }
}
