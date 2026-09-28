using System.Reflection;
using UnityEngine;

namespace ElectricalSim.Tests
{
    // Explicit wiring for physical-wire regression tests, independent of any task or scoring system.
    internal static class ManualCircuitFixture
    {
        public static void CompleteMotor(CircuitGraph graph, string id, bool ratedLoad = true)
        {
            var motor = (ElectricalDeviceRuntime)graph.Devices[id];
            if (ratedLoad) { var config = motor.MotorConfiguration.Clone(); config.LoadFactor = 1; motor.ConfigureMotor(config); }
            if (id == "M_DOUBLE") return;
            graph.AddWire(id + ".U", id + ".W2", Color.red);
            graph.AddWire(id + ".V", id + ".U2", Color.red);
            graph.AddWire(id + ".W", id + ".V2", Color.red);
        }
        public static void WireMotor(SimulationController controller)
        {
            var ports = new[]
            {
                "POWER.L1", "QF.L1", "POWER.L2", "QF.L2", "POWER.L3", "QF.L3",
                "QF.T1", "KM1.L1", "QF.T2", "KM1.L2", "QF.T3", "KM1.L3",
                "KM1.T1", "FR1.L1", "KM1.T2", "FR1.L2", "KM1.T3", "FR1.L3",
                "FR1.T1", "M1.U", "FR1.T2", "M1.V", "FR1.T3", "M1.W",
                "POWER.L1", "SB1.COM", "SB1.NO", "KM1.A1", "KM1.A2", "POWER.N"
            };
            for (var i = 0; i < ports.Length; i += 2)
                controller.Graph.AddWire(ports[i], ports[i + 1], Color.red, "JumperLine");
            CompleteMotor(controller.Graph, "M1");
            typeof(SimulationController).GetMethod("RefreshWireViews", BindingFlags.Instance | BindingFlags.NonPublic)
                .Invoke(controller, null);
        }
    }
}
