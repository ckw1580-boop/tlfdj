using System.Collections.Generic;
using System.Linq;

namespace ElectricalSim
{
    public sealed class RelayChangeoverContact
    {
        public string Common { get; }
        public string NormallyClosed { get; }
        public string NormallyOpen { get; }

        public RelayChangeoverContact(string common, string normallyClosed, string normallyOpen)
        { Common = common; NormallyClosed = normallyClosed; NormallyOpen = normallyOpen; }
    }

    public sealed class IntermediateRelayDefinition
    {
        public const double RatedDcVoltage = 24d;
        public const string CoilPositive = "13";
        public const string CoilNegative = "14";
        public static readonly IReadOnlyList<string> Ports =
            System.Array.AsReadOnly(Enumerable.Range(1, 14).Select(i => i.ToString()).ToArray());
        public static readonly IReadOnlyList<RelayChangeoverContact> Contacts = System.Array.AsReadOnly(new[]
        {
            new RelayChangeoverContact("9", "1", "5"),
            new RelayChangeoverContact("10", "2", "6"),
            new RelayChangeoverContact("11", "3", "7"),
            new RelayChangeoverContact("12", "4", "8")
        });
        public static readonly IReadOnlyList<IntermediateRelayDefinition> All = System.Array.AsReadOnly(
            Enumerable.Range(1, 6).Select(i => new IntermediateRelayDefinition(i)).ToArray());

        public string Id { get; }
        public string ModelPath { get; }
        private IntermediateRelayDefinition(int index)
        {
            Id = "KA" + index;
            ModelPath = "Bench/ElectricBench/Nuts/" + (17 + index) + "/ZhongJianJiDianQi";
        }

        public static string PortRole(string port)
        {
            if (port == CoilPositive) return "线圈 +24V / PLC 输出";
            if (port == CoilNegative) return "线圈 0V";
            for (var i = 0; i < Contacts.Count; i++)
            {
                var c = Contacts[i];
                var group = "第 " + (i + 1) + " 组 · ";
                if (port == c.Common) return group + "公共端 COM";
                if (port == c.NormallyClosed) return group + "常闭端 NC";
                if (port == c.NormallyOpen) return group + "常开端 NO";
            }
            return string.Empty;
        }
    }
}
