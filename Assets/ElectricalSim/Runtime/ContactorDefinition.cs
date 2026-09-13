using System.Collections.Generic;
using System.Linq;

namespace ElectricalSim
{
    public sealed class ContactorContact
    {
        public string Input { get; }
        public string Output { get; }
        public bool NormallyClosed { get; }
        public bool Main { get; }
        public ContactorContact(string input, string output, bool normallyClosed = false, bool main = false)
        { Input = input; Output = output; NormallyClosed = normallyClosed; Main = main; }
    }

    public sealed class ContactorDefinition
    {
        public const double RatedAcVoltage = 220d;
        public static readonly IReadOnlyList<ContactorContact> Contacts = System.Array.AsReadOnly(new[]
        {
            new ContactorContact("L1", "T1", main: true), new ContactorContact("L2", "T2", main: true),
            new ContactorContact("L3", "T3", main: true), new ContactorContact("13", "14"),
            new ContactorContact("53", "54"), new ContactorContact("83", "84"),
            new ContactorContact("61", "62", true), new ContactorContact("71", "72", true)
        });
        // Retain port ordering for existing body-terminal layout and saved references.
        public static readonly IReadOnlyList<string> Ports = System.Array.AsReadOnly(new[]
        { "L1", "L2", "L3", "T1", "T2", "T3", "13", "14", "53", "54", "61", "62", "71", "72", "83", "84", "A1", "A2" });
        public static readonly IReadOnlyList<ContactorDefinition> All = System.Array.AsReadOnly(new[]
        {
            new ContactorDefinition(1, "KMF", 29), new ContactorDefinition(2, "KM1", 30),
            new ContactorDefinition(3, "KMR", 31), new ContactorDefinition(4, "KM2", 32)
        });
        public static readonly IReadOnlyList<ContactorDefinition> Rear = System.Array.AsReadOnly(new[]
        {
            new ContactorDefinition(5, "KMBACK1", 111, true),
            new ContactorDefinition(6, "KMBACK2", 112, true),
            new ContactorDefinition(7, "KMBACK3", 113, true)
        });
        public string Id { get; }
        public string RuntimeId { get; }
        public string ModelPath { get; }
        public bool IsRear { get; }
        public string RearModelPath => IsRear ? ModelPath : null;
        private ContactorDefinition(int index, string runtimeId, int nut, bool isRear = false)
        {
            Id = "KM" + index; RuntimeId = runtimeId; IsRear = isRear;
            ModelPath = "Bench/ElectricBench/Nuts/" + nut + "/JiaoLiuJieChuQi_F4-32";
        }
        public static string TerminalLabel(string port)
        {
            switch (port)
            {
                case "L1": return "1L1"; case "L2": return "3L2"; case "L3": return "5L3";
                case "T1": return "2T1"; case "T2": return "4T2"; case "T3": return "6T3";
                default: return port;
            }
        }
        public string BindingName(string port)
        {
            var contact = Contacts.FirstOrDefault(c => c.Input == port || c.Output == port);
            return Id + "_" + TerminalLabel(port) + (contact == null || contact.Main ? "" : contact.NormallyClosed ? "NC" : "NO");
        }
        public static string PortRole(string port)
        {
            var contact = Contacts.FirstOrDefault(c => c.Input == port || c.Output == port);
            return contact == null ? "线圈 AC 220V" : contact.Main ? "主触点 常开" : contact.NormallyClosed ? "辅助触点 常闭" : "辅助触点 常开";
        }
    }
}
