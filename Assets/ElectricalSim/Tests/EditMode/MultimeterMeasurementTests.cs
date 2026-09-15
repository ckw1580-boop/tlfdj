using NUnit.Framework;
using UnityEngine;

namespace ElectricalSim.Tests
{
    public sealed class MultimeterMeasurementTests
    {
        private CircuitGraph graph;
        private bool supply;
        private readonly ElectricalInstrument meter = new ElectricalInstrument(InstrumentKind.Multimeter);
        [SetUp]
        public void SetUp()
        {
            graph = new CircuitGraph(); supply = true;
            var source = ElectricalDeviceRuntime.CreatePowerSource(); source.SupplyEnabled = () => supply;
            graph.RegisterDevice(source);
            graph.RegisterDevice(new ElectricalDeviceRuntime("DC", ElectricalDeviceKind.PowerSource, new[] { "DC_POSITIVE", "DC_NEGATIVE" })
                { IsDcSource = true, SupplyEnabled = () => supply });
            var board = new ElectricalDeviceRuntime("TB", ElectricalDeviceKind.Terminal, new[] { "A", "B", "C", "D" });
            board.AddFixedLink("A", "B"); graph.RegisterDevice(board);
        }
        private MultimeterReading Read(MultimeterMode mode, string red, string black) => meter.Measure(mode, red, black, graph.Solve(0));
        [TestCase(MultimeterMode.AcVoltage, "POWER.L1", "POWER.N", 220)]
        [TestCase(MultimeterMode.AcVoltage, "POWER.L2", "POWER.L3", 380)]
        [TestCase(MultimeterMode.AcVoltage, "POWER.L1", "POWER.L1", 0)]
        [TestCase(MultimeterMode.AcVoltage, "POWER.N", "POWER.L1", 220)]
        [TestCase(MultimeterMode.DcVoltage, "DC.DC_POSITIVE", "DC.DC_NEGATIVE", 24)]
        [TestCase(MultimeterMode.DcVoltage, "DC.DC_NEGATIVE", "DC.DC_POSITIVE", -24)]
        [TestCase(MultimeterMode.DcVoltage, "POWER.L1", "POWER.N", 0)]
        [TestCase(MultimeterMode.AcVoltage, "DC.DC_POSITIVE", "DC.DC_NEGATIVE", 0)]
        public void VoltageUsesSelectedRangeAndRedMinusBlackPolarity(MultimeterMode mode, string red, string black, double expected)
        {
            var result = Read(mode, red, black);
            Assert.That(result.State, Is.EqualTo(MultimeterReadingState.Valid));
            Assert.That(result.Value, Is.EqualTo(expected));
            Assert.That(result.ShouldBeep, Is.False);
        }
        [Test]
        public void ContinuityTraversesWiresTerminalLinksAndClosedContactsOnly()
        {
            var button = ElectricalDeviceRuntime.CreatePushButton("SB", false);
            graph.RegisterDevice(button);
            graph.AddWire("TB.B", "SB.COM", Color.red);
            graph.AddWire("SB.NO", "TB.C", Color.red);
            Assert.That(Read(MultimeterMode.Continuity, "TB.A", "TB.C").State, Is.EqualTo(MultimeterReadingState.OpenCircuit));
            button.SetControl(true);
            Assert.That(Read(MultimeterMode.Continuity, "TB.A", "TB.C").ShouldBeep, Is.True);
            graph.RemoveWire(graph.Wires[1].Id);
            Assert.That(Read(MultimeterMode.Continuity, "TB.A", "TB.C").DisplayValue, Is.EqualTo("OL"));
            Assert.That(Read(MultimeterMode.Continuity, "TB.A", "TB.A").ShouldBeep, Is.True);
            Assert.That(Read(MultimeterMode.Continuity, "TB.C", "TB.D").ShouldBeep, Is.False);
        }
        [Test]
        public void ConnectedLiveSamePotentialIsBlockedAndIsolatedBranchRemainsMeasurable()
        {
            graph.AddWire("POWER.L1", "TB.C", Color.red);
            var blocked = Read(MultimeterMode.Continuity, "POWER.L1", "TB.C");
            Assert.That(blocked.State, Is.EqualTo(MultimeterReadingState.Energized));
            Assert.That(blocked.ShouldBeep, Is.False);
            Assert.That(Read(MultimeterMode.Continuity, "TB.A", "TB.B").ShouldBeep, Is.True);
            Assert.That(Read(MultimeterMode.Continuity, "DC.DC_NEGATIVE", "DC.DC_NEGATIVE").State, Is.EqualTo(MultimeterReadingState.Energized));
            supply = false;
            Assert.That(Read(MultimeterMode.Continuity, "POWER.L1", "TB.C").ShouldBeep, Is.True);
            Assert.That(Read(MultimeterMode.AcVoltage, "POWER.L1", "POWER.N").Value, Is.Zero);
        }
        [Test]
        public void InvalidFloatingAndCrossDomainReferencesHaveExplicitStates()
        {
            Assert.That(Read(MultimeterMode.AcVoltage, null, "POWER.N").State, Is.EqualTo(MultimeterReadingState.MissingProbe));
            Assert.That(Read(MultimeterMode.AcVoltage, "missing", "POWER.N").State, Is.EqualTo(MultimeterReadingState.MissingProbe));
            Assert.That(meter.Measure(MultimeterMode.AcVoltage, "TB.A", "TB.B", null).State, Is.EqualTo(MultimeterReadingState.MissingProbe));
            Assert.That(Read(MultimeterMode.AcVoltage, "POWER.L1", "TB.A").State, Is.EqualTo(MultimeterReadingState.UndefinedReference));
            Assert.That(Read(MultimeterMode.DcVoltage, "POWER.N", "DC.DC_NEGATIVE").State, Is.EqualTo(MultimeterReadingState.UndefinedReference));
            Assert.That(Read(MultimeterMode.AcVoltage, "TB.A", "TB.D").Value, Is.Zero);
            Assert.That(Read(MultimeterMode.Off, null, null).DisplayValue, Is.EqualTo("OFF"));
        }
        [Test]
        public void ConflictIsReportedWithoutPoisoningUnrelatedMeasurements()
        {
            graph.AddWire("POWER.L1", "POWER.N", Color.red);
            Assert.That(Read(MultimeterMode.AcVoltage, "POWER.L1", "POWER.N").DisplayValue, Is.EqualTo("Err"));
            Assert.That(Read(MultimeterMode.Continuity, "POWER.L1", "POWER.N").ShouldBeep, Is.False);
            Assert.That(Read(MultimeterMode.Continuity, "TB.A", "TB.B").ShouldBeep, Is.True);
        }
        [Test]
        public void InverterOutputMetadataFollowsConnectedWiresAndDriveState()
        {
            var speed = 600f;
            graph.RegisterDevice(new InverterDriveRuntime("G120", () => speed, () => false));
            foreach (var phase in new[] { "L1", "L2", "L3" }) graph.AddWire("POWER." + phase, "G120." + phase, Color.red);
            graph.AddWire("G120.U2", "TB.A", Color.red);
            Assert.That(Read(MultimeterMode.AcVoltage, "TB.B", "G120.V2").State, Is.EqualTo(MultimeterReadingState.Unsupported));
            Assert.That(Read(MultimeterMode.Continuity, "TB.A", "TB.B").State, Is.EqualTo(MultimeterReadingState.Energized));
            speed = 0;
            Assert.That(Read(MultimeterMode.Continuity, "TB.A", "TB.B").ShouldBeep, Is.True);
            Assert.That(Read(MultimeterMode.AcVoltage, "TB.B", "G120.V2").State, Is.EqualTo(MultimeterReadingState.Unsupported));
            Assert.That(Read(MultimeterMode.AcVoltage, "G120.L1", "G120.L2").Value, Is.EqualTo(380));
        }
        [Test]
        public void CoilAndMotorWindingsAreNotInventedConductiveLinks()
        {
            graph.RegisterDevice(ElectricalDeviceRuntime.CreateContactor("KM"));
            graph.RegisterDevice(ElectricalDeviceRuntime.CreateMotor("M"));
            Assert.That(Read(MultimeterMode.Continuity, "KM.A1", "KM.A2").State, Is.EqualTo(MultimeterReadingState.OpenCircuit));
            Assert.That(Read(MultimeterMode.Continuity, "M.U", "M.V").State, Is.EqualTo(MultimeterReadingState.OpenCircuit));
        }
    }
}
