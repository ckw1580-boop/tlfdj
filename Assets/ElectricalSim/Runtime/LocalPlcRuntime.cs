using System;
using System.Collections.Generic;

namespace ElectricalSim
{
    [Serializable]
    public sealed class PlcContact
    {
        public string Address = string.Empty;
        public bool NormallyClosed;
    }

    [Serializable]
    public sealed class PlcRung
    {
        public List<PlcContact> Contacts = new List<PlcContact>();
        public string CoilAddress = string.Empty;
    }

    public sealed class LocalPlcRuntime
    {
        private readonly Dictionary<string, bool> memory = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
        private readonly List<PlcRung> program = new List<PlcRung>();
        private double accumulator;

        public double ScanPeriodSeconds { get; set; } = 0.02d;
        public IReadOnlyList<PlcRung> Program => program;

        public bool Read(string address) => memory.TryGetValue(Normalize(address), out var value) && value;

        public void Write(string address, bool value) => memory[Normalize(address)] = value;

        public void LoadProgram(IEnumerable<PlcRung> rungs)
        {
            program.Clear();
            if (rungs != null) program.AddRange(rungs);
        }

        public int Tick(double deltaSeconds)
        {
            accumulator += Math.Max(0d, deltaSeconds);
            var scans = 0;
            while (accumulator >= ScanPeriodSeconds && scans < 64)
            {
                ScanOnce();
                accumulator -= ScanPeriodSeconds;
                scans++;
            }
            return scans;
        }

        public void ScanOnce()
        {
            foreach (var rung in program)
            {
                var energized = true;
                foreach (var contact in rung.Contacts)
                {
                    var state = Read(contact.Address);
                    energized &= contact.NormallyClosed ? !state : state;
                }
                Write(rung.CoilAddress, energized);
            }
        }

        private static string Normalize(string address)
            => (address ?? string.Empty).Trim().Replace(" ", string.Empty).ToUpperInvariant();
    }
}
