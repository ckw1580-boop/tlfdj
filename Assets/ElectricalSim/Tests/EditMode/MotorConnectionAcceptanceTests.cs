using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine;

namespace ElectricalSim.Tests
{
    public sealed class MotorConnectionAcceptanceTests
    {
        private CircuitGraph graph;
        private ElectricalDeviceRuntime motor;
        private InverterDriveRuntime drive;
        private static readonly string[] MotorHeads = { "U", "V", "W" };

        private void CreateCircuit(string id = "M1", bool inverter = false, float commandRpm = 1450)
        {
            graph = new CircuitGraph();
            graph.RegisterDevice(ElectricalDeviceRuntime.CreatePowerSource());
            motor = ElectricalDeviceRuntime.CreateMotor(id);
            graph.RegisterDevice(motor);
            drive = null;
            if (!inverter) return;
            // A steady ramp output isolates the winding/phase contract from panel command timing.
            drive = new InverterDriveRuntime("G120", () => commandRpm, () => false);
            graph.RegisterDevice(drive);
            foreach (var phase in new[] { "L1", "L2", "L3" }) Wire("POWER." + phase, "G120." + phase);
        }

        private void Wire(string from, string to) => graph.AddWire(from, to, Color.red);
        private string Port(string terminal) => motor.DeviceId + "." + terminal;

        private void Supply(bool tails = false, string outputOrder = "UVW")
        {
            for (var phase = 0; phase < 3; phase++)
                Wire(drive == null ? "POWER.L" + (phase + 1) : "G120." + outputOrder[phase] + "2",
                    Port(MotorHeads[phase] + (tails ? "2" : "")));
        }

        private void CloseOrdinaryWindings(bool star)
        {
            if (star)
            {
                Wire(Port("U2"), Port("V2"));
                Wire(Port("V2"), Port("W2"));
            }
            else
            {
                Wire(Port("U"), Port("W2"));
                Wire(Port("V"), Port("U2"));
                Wire(Port("W"), Port("V2"));
            }
        }

        private static IEnumerable<TestCaseData> InverterPhaseCases
        {
            get
            {
                var orders = new[] { "UVW", "VWU", "WUV", "VUW", "UWV", "WVU" };
                var directions = new[] { 1, 1, 1, -1, -1, -1 };
                foreach (var star in new[] { false, true })
                    foreach (var commandSign in new[] { 1, -1 })
                        for (var i = 0; i < orders.Length; i++)
                            yield return new TestCaseData(star, orders[i], commandSign, directions[i] * commandSign)
                                .SetName("InverterPhase_" + (star ? "Star_" : "Delta_") + orders[i] + "_Command" + commandSign);
            }
        }

        [TestCaseSource(nameof(InverterPhaseCases))]
        public void EveryInverterPhaseOrderDrivesAClosedWindingInTheExpectedDirection(
            bool star, string outputOrder, int commandSign, int expectedDirection)
        {
            CreateCircuit(inverter: true, commandRpm: commandSign * 1450);
            Supply(outputOrder: outputOrder);
            CloseOrdinaryWindings(star);
            var snapshot = graph.Solve(.1f);
            var state = snapshot.GetMotorState("M1");

            Assert.That(snapshot.HasShortCircuit, Is.False);
            Assert.That(state.Connection.Kind, Is.EqualTo(star ? MotorConnectionKind.Star : MotorConnectionKind.Delta));
            Assert.That(state.Connection.DirectionSign, Is.EqualTo(expectedDirection));
            Assert.That(state.SpeedRpm * expectedDirection, Is.GreaterThan(0), "Actual shaft motion must follow the winding phase order.");
            Assert.That(state.SourceId, Is.EqualTo("G120"));
            Assert.That(state.Connection.FrequencyHz, Is.EqualTo(50).Within(.01));
            Assert.That(state.Connection.WindingVoltage, Is.EqualTo(star ? 380 / Mathf.Sqrt(3) : 380).Within(.01));
            Assert.That(state.CurrentAmps, Is.GreaterThan(0));
        }

        [TestCase(false)]
        [TestCase(true)]
        public void DoubleHighCanStartDirectlyFromRestUsingMainsOrInverter(bool inverter)
        {
            CreateCircuit("M_DOUBLE", inverter);
            Supply(tails: true);
            Wire(Port("U"), Port("V"));
            Wire(Port("V"), Port("W"));
            var starting = graph.Solve(.02f);
            var early = starting.GetMotorState("M_DOUBLE");

            Assert.That(starting.HasShortCircuit, Is.False);
            Assert.That(early.Connection.Kind, Is.EqualTo(MotorConnectionKind.DoubleHigh));
            Assert.That(early.SynchronousSpeedRpm, Is.EqualTo(3000));
            Assert.That(early.SpeedRpm, Is.GreaterThan(0).And.LessThan(2800), "Direct high-speed starting must still preserve inertia.");
            Assert.That(early.CurrentAmps, Is.GreaterThan(motor.MotorConfiguration.HighRatedCurrentAmps));
            Assert.That(graph.Solve(2).GetMotorSpeedRpm("M_DOUBLE"), Is.GreaterThan(2900).And.LessThan(3000));
        }

        [TestCase(false)]
        [TestCase(true)]
        public void DoubleHighRequiresAllThreeHeadsToBeShorted(bool onlyTwoHeadsShorted)
        {
            CreateCircuit("M_DOUBLE");
            Supply(tails: true);
            if (onlyTwoHeadsShorted) Wire(Port("U"), Port("V"));
            AssertConnectionDiagnosticWithoutShort(graph.Solve(.2f), "U1/V1/W1");
        }

        [Test]
        public void DoubleLowRejectsExternalTailBridgesEvenWhenTheyDoNotShortTheSupply()
        {
            CreateCircuit("M_DOUBLE");
            Supply();
            Wire(Port("U2"), Port("V2"));
            AssertConnectionDiagnosticWithoutShort(graph.Solve(.2f), "U2/V2/W2");
        }

        [Test]
        public void DoubleRejectsBothSpeedRowsBeingPoweredWithoutInventingAPhaseShort()
        {
            CreateCircuit("M_DOUBLE");
            Supply();
            Supply(tails: true);
            AssertConnectionDiagnosticWithoutShort(graph.Solve(.2f), "高低速同时接通");
        }

        [TestCase("M1")]
        [TestCase("M2")]
        [TestCase("M3")]
        public void ReversingOneWindingIsAWindingDiagnosticRatherThanAnIdealWireShort(string id)
        {
            CreateCircuit(id);
            // U alone is reversed: U2 takes L1, and U1 joins the V2/W2 star point.
            Wire("POWER.L1", Port("U2"));
            Wire("POWER.L2", Port("V"));
            Wire("POWER.L3", Port("W"));
            Wire(Port("U"), Port("V2"));
            Wire(Port("V2"), Port("W2"));
            AssertConnectionDiagnosticWithoutShort(graph.Solve(.2f), "U1–U2");
        }

        [Test]
        public void ProtectiveEarthCannotBeUsedAsTheStarWorkingPoint()
        {
            CreateCircuit();
            Supply();
            CloseOrdinaryWindings(star: true);
            Wire(Port("U2"), "POWER.PE");
            AssertConnectionDiagnosticWithoutShort(graph.Solve(.2f), "保护地PE");
        }

        [Test]
        public void MainsConnectedToAnInverterOutputIsReportedAsAnElectricalSourceConflict()
        {
            CreateCircuit(inverter: true);
            Supply();
            CloseOrdinaryWindings(star: false);
            Wire("POWER.L1", Port("U"));
            var snapshot = graph.Solve(.2f);

            Assert.That(drive.OutputValid, Is.False);
            Assert.That(snapshot.HasShortCircuit, Is.True, "Directly joining an inverter output to mains is an electrical supply fault.");
            Assert.That(snapshot.GetMotorState("M1").Connection.Kind, Is.EqualTo(MotorConnectionKind.Invalid));
            Assert.That(snapshot.Diagnostics.Any(d => d.Contains("工频与变频")), Is.True);
            Assert.That(snapshot.GetMotorSpeedRpm("M1"), Is.Zero);
        }

        [TestCase(false)]
        [TestCase(true)]
        public void ABridgeBetweenLivePhasesIsAnActualShortOnEitherSupply(bool inverter)
        {
            CreateCircuit(inverter: inverter);
            Supply();
            CloseOrdinaryWindings(star: false);
            Wire(Port("U"), Port("V"));
            var snapshot = graph.Solve(.2f);

            Assert.That(snapshot.HasShortCircuit, Is.True);
            Assert.That(snapshot.Errors, Is.Not.Empty);
            Assert.That(snapshot.GetMotorSpeedRpm("M1"), Is.Zero);
            Assert.That(snapshot.GetMotorState("M1").Connection.Energized, Is.False);
            if (inverter) Assert.That(drive.OutputValid, Is.False);
        }

        private void AssertConnectionDiagnosticWithoutShort(SimulationSnapshot snapshot, string terminalExplanation)
        {
            var state = snapshot.GetMotorState(motor.DeviceId);
            Assert.That(state.Connection.Kind, Is.EqualTo(MotorConnectionKind.Invalid));
            Assert.That(state.Connection.Energized, Is.False);
            Assert.That(state.SpeedRpm, Is.Zero);
            Assert.That(state.CurrentAmps, Is.Zero);
            Assert.That(snapshot.HasShortCircuit, Is.False, "Impedance windings must not be merged into ideal wire nets.");
            Assert.That(snapshot.Errors, Is.Empty);
            Assert.That(snapshot.Diagnostics.Any(d => d.Contains(terminalExplanation)), Is.True,
                "The diagnostic must identify the invalid winding connection.");
        }
    }
}
