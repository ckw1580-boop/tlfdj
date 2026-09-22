using System;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using Object = UnityEngine.Object;

namespace ElectricalSim.Tests
{
    public sealed class G120ControlTests
    {
        private GameObject host;
        private InverterPanelController panel;
        private InverterDriveRuntime drive;
        private CircuitGraph graph;
        private bool mains;
        private G120ControlRuntime Control => drive.Control;
        [SetUp]
        public void Setup()
        {
            host = new GameObject("G120 Test Panel"); panel = host.AddComponent<InverterPanelController>(); panel.Initialize(null, null);
            panel.TrySetParameter("P1120", 0.01f); panel.TrySetParameter("P1121", 0.01f);
            panel.TrySetParameter("P311", 1000f); panel.TrySetParameter("P1082", 1500f);
            graph = new CircuitGraph(); mains = true;
            var supply = ElectricalDeviceRuntime.CreatePowerSource(); supply.SupplyEnabled = () => mains; graph.RegisterDevice(supply);
            graph.RegisterDevice(new ElectricalDeviceRuntime("DC", ElectricalDeviceKind.PowerSource, new[] { "DC_POSITIVE", "DC_NEGATIVE" }) { IsDcSource = true });
            drive = new InverterDriveRuntime(panel); graph.RegisterDevice(drive);
            foreach (var phase in new[] { "L1", "L2", "L3" }) Wire("POWER." + phase, "G120." + phase);
        }
        [TearDown] public void Teardown() => Object.DestroyImmediate(host);
        private WireConnection Wire(string a, string b) => graph.AddWire(a, b, Color.red);
        private WireConnection Link(int a, int b) => Wire(G120ControlRuntime.Terminal(a), G120ControlRuntime.Terminal(b));
        private WireConnection On(int i) => Link(9, G120TerminalCatalog.DigitalNumbers[i]);
        private void Commons() { Link(28, 69); Link(28, 34); }
        private void Macro(int n) { Assert.That(panel.TrySetParameter("P0010", 1), Is.True); Assert.That(panel.TrySetParameter("P0015", n), Is.True); panel.TrySetParameter("P0010", 0); }

        [Test] public void CatalogIncludesEveryNumberExactlyOnceAndTwentySixNewPoints()
        {
            Assert.That(G120TerminalCatalog.All.Count, Is.EqualTo(32));
            Assert.That(G120TerminalCatalog.All.Select(t => t.Number).Distinct().Count(), Is.EqualTo(32));
            Assert.That(G120TerminalCatalog.AddedUpper.Length + G120TerminalCatalog.AddedLower.Length, Is.EqualTo(26));
            Assert.That(panel.Macro, Is.EqualTo(1));
            Assert.That(panel.TrySetParameter("P0015", 7), Is.False);
        }
        [Test] public void CanonicalNodesPreserveAliasesButDoNotShortDifferentialInputsOrCommons()
        {
            var s = graph.Solve(0);
            Assert.That(s.SameNet("G120.DI0", "G120.T05"), Is.True);
            Assert.That(s.SameNet("G120.DI1_COM1", "G120.T69"), Is.True);
            Assert.That(s.SameNet("G120.DI1_COM2", "G120.T34"), Is.True);
            Assert.That(s.SameNet("G120.A1", "G120.T03"), Is.False);
            Assert.That(s.SameNet("G120.T03", "G120.T04"), Is.False);
            Assert.That(s.SameNet("G120.T69", "G120.T34"), Is.False);
            Assert.That(s.SameNet("G120.T69", "G120.T28"), Is.False);
        }
        [Test] public void MacroOneUsesWiredCommonsDirectionsSummedSpeedsAndResetEdge()
        {
            On(0); On(4); graph.Solve(.1f); Assert.That(panel.ActualSpeedRpm, Is.Zero);
            Commons(); graph.Solve(.1f); Assert.That(panel.ActualSpeedRpm, Is.EqualTo(300));
            On(5); graph.Solve(.1f); Assert.That(panel.ActualSpeedRpm, Is.EqualTo(700));
            var forward = graph.Wires.Single(w => w.EndPort == "G120.T05"); graph.RemoveWire(forward.Id); On(1);
            graph.Solve(.1f); Assert.That(panel.ActualSpeedRpm, Is.Zero); graph.Solve(.1f); Assert.That(panel.ActualSpeedRpm, Is.EqualTo(-700));
            panel.SetFault(true, 123); graph.Solve(.1f); Assert.That(panel.ActualSpeedRpm, Is.Zero);
            On(2); graph.Solve(.1f); Assert.That(panel.HasFault, Is.False);
        }
        [Test] public void CommonsUseEvenAndOddGroupsAndNeverImplicitGround()
        {
            for (var i = 0; i < 6; i++) On(i);
            Link(28, 69); graph.Solve(0);
            Assert.That(Control.DigitalInputs, Is.EqualTo(new[] { true, false, true, false, true, false }));
            Link(28, 34); graph.Solve(0); Assert.That(Control.DigitalInputs.All(x => x), Is.True);
        }
        [Test] public void ExternalControlPowerCannotDriveMotorOrBackfeedItsInput()
        {
            mains = false; Commons(); On(0); On(4);
            Wire("DC.DC_POSITIVE", "G120.T31"); Wire("DC.DC_NEGATIVE", "G120.T32");
            var s = graph.Solve(.1f);
            Assert.That(Control.Powered, Is.True); Assert.That(panel.ActualSpeedRpm, Is.Zero);
            Assert.That(panel.OperationEnabled, Is.False);
            Assert.That(panel.FieldbusStatusWord & 7, Is.Zero, "Control power alone cannot report drive readiness or enable.");
            Assert.That(s.GetDcVoltage("G120.T01", "G120.T02"), Is.EqualTo(10));
            graph.RemoveWire(graph.Wires.Single(w => w.StartPort == "DC.DC_POSITIVE").Id);
            Link(9, 31); graph.Solve(.1f); Assert.That(Control.Powered, Is.False);
        }
        [Test] public void LosingMainSupplyWithOutputLoopbackDoesNotLatchPower()
        {
            Link(9, 31); Link(28, 32); graph.Solve(0); Assert.That(Control.Powered, Is.True);
            mains = false; graph.Solve(0); Assert.That(Control.Powered, Is.False);
            Assert.That(panel.OperationEnabled, Is.False); Assert.That(panel.FieldbusStatusWord & 7, Is.Zero);
        }
        [Test] public void DcMeterReadsControlVoltagesAndContinuityNeverUnionsSignalPair()
        {
            var s = graph.Solve(0); var meter = new ElectricalInstrument(InstrumentKind.Multimeter);
            Assert.That(meter.Measure(MultimeterMode.DcVoltage, "G120.T01", "G120.T02", s).Value, Is.EqualTo(10));
            Assert.That(meter.Measure(MultimeterMode.DcVoltage, "G120.T28", "G120.T09", s).Value, Is.EqualTo(-24));
            Assert.That(meter.Measure(MultimeterMode.Continuity, "G120.T01", "G120.T02", s).State, Is.EqualTo(MultimeterReadingState.Energized));
            Assert.That(s.SameNet("G120.T01", "G120.T02"), Is.False);
            Assert.That(meter.Measure(MultimeterMode.DcVoltage, "G120.T01", "G120.T69", s).State, Is.EqualTo(MultimeterReadingState.UndefinedReference));
        }
        [Test] public void ControlPowerShortOrMixedVoltageSourcesFaultAndCannotBeResetWhilePresent()
        {
            var bad = Link(1, 9); var s = graph.Solve(0);
            Assert.That(s.Errors, Is.Not.Empty); Assert.That(panel.HasFault, Is.True);
            panel.SetFault(false); Assert.That(panel.HasFault, Is.True);
            graph.RemoveWire(bad.Id); graph.Solve(0); panel.SetFault(false); Assert.That(panel.HasFault, Is.False);
        }
        [Test] public void RelayOutputsAreDryContactsAndSwitchForFaultAndRunEnable()
        {
            var s = graph.Solve(0); Assert.That(s.SameNet("G120.T20", "G120.T18"), Is.True);
            Assert.That(s.SameNet("G120.T25", "G120.T23"), Is.True);
            Assert.That(s.GetPotential("G120.T20"), Is.EqualTo(ElectricalPotential.Floating));
            Commons(); On(0); On(4); s = graph.Solve(.1f);
            Assert.That(s.SameNet("G120.T25", "G120.T24"), Is.True);
            panel.SetFault(true); s = graph.Solve(0);
            Assert.That(s.SameNet("G120.T20", "G120.T19"), Is.True); Assert.That(s.SameNet("G120.T20", "G120.T18"), Is.False);
        }
        [Test] public void AlarmTransistorRejectsReversePolarity()
        {
            Wire("DC.DC_NEGATIVE", "G120.T21"); Wire("DC.DC_POSITIVE", "G120.T22"); panel.SetAlarm(true);
            Assert.That(graph.Solve(0).SameNet("G120.T21", "G120.T22"), Is.False);
            Assert.That(Control.DigitalOutputs[1], Is.False);
        }
        private WireConnection[] WireAlarmRelay(bool highSide, bool reverse)
        {
            var entry = reverse ? "G120.T22" : "G120.T21";
            var exit = reverse ? "G120.T21" : "G120.T22";
            return highSide ? new[] { Wire("DC.DC_POSITIVE", entry), Wire(exit, "KA.13"), Wire("KA.14", "DC.DC_NEGATIVE") } :
                new[] { Wire("DC.DC_POSITIVE", "KA.13"), Wire("KA.14", entry), Wire(exit, "DC.DC_NEGATIVE") };
        }
        [TestCase(true, false)] [TestCase(false, false)] [TestCase(true, true)] [TestCase(false, true)]
        public void AlarmTransistorPreservesPolarityWithARealRelayLoad(bool highSide, bool reverse)
        {
            var relay = new ElectricalDeviceRuntime("KA", ElectricalDeviceKind.IntermediateRelay, IntermediateRelayDefinition.Ports);
            graph.RegisterDevice(relay); WireAlarmRelay(highSide, reverse); panel.SetAlarm(true);
            var snapshot = graph.Solve(0);
            Assert.That(relay.IsActive, Is.EqualTo(!reverse));
            Assert.That(Control.DigitalOutputs[1], Is.EqualTo(!reverse));
            Assert.That(relay.RelayCoilVoltage, Is.EqualTo(reverse ? 0 : 24));
            // A directional transistor is a numerical zero-volt connection, not a
            // wire union; SameNet must not be used to infer its conducting state.
            Assert.That(snapshot.SameNet("G120.T21", "G120.T22"), Is.False);
            panel.SetAlarm(false); graph.Solve(0); Assert.That(relay.IsActive, Is.False);
        }
        [Test] public void AlarmTransistorRechecksPolarityWhenAnEnergizedLoadIsRewired()
        {
            var relay = new ElectricalDeviceRuntime("KA", ElectricalDeviceKind.IntermediateRelay, IntermediateRelayDefinition.Ports);
            graph.RegisterDevice(relay); var wires = WireAlarmRelay(false, false); panel.SetAlarm(true);
            graph.Solve(0); Assert.That(relay.IsActive, Is.True);
            foreach (var wire in wires) graph.RemoveWire(wire.Id);
            WireAlarmRelay(false, true); graph.Solve(0);
            Assert.That(Control.DigitalOutputs[1], Is.False); Assert.That(relay.IsActive, Is.False);
        }
        [Test] public void AlarmTransistorDoesNotInventAForwardSupplyForFloatingTerminals()
        {
            panel.SetAlarm(true); var snapshot = graph.Solve(0);
            Assert.That(Control.DigitalOutputs[1], Is.False);
            Assert.That(snapshot.TryGetSignalVoltage("G120.T21", "G120.T22", out _), Is.False);
        }
        [Test] public void AnalogWiringUsesBothLeadsAndSimulationIsExplicitAndExclusive()
        {
            Macro(12); Commons(); On(0); Link(1, 3); graph.Solve(.1f); Assert.That(panel.ActualSpeedRpm, Is.Zero);
            Link(2, 4); graph.Solve(.1f); Assert.That(panel.ActualSpeedRpm, Is.EqualTo(1000));
            Control.Inputs[0].Simulated = true; Control.Inputs[0].SimulatedValue = 5;
            graph.Solve(.1f); Assert.That(panel.ActualSpeedRpm, Is.EqualTo(500));
            Control.Inputs[0].Simulated = false; graph.Solve(.1f); Assert.That(panel.ActualSpeedRpm, Is.EqualTo(1000));
        }
        [Test] public void AiOneSupportsNegativeVoltageAndMacroSwitchRestoresAiZero()
        {
            Macro(12); Commons(); On(0); graph.Solve(0); Control.SelectedAnalogInput = 1;
            Control.Inputs[1].Simulated = true; Control.Inputs[1].SimulatedValue = -5;
            graph.Solve(.1f); Assert.That(panel.ActualSpeedRpm, Is.EqualTo(-500));
            Macro(17); graph.Solve(0); Assert.That(Control.SelectedAnalogInput, Is.Zero);
        }
        [Test] public void AoCurrentFeedsMatchingAiWithCalibrationAndExposesOpenAndReversedLoops()
        {
            Macro(12); Commons(); On(0); Control.SetInputType(0, 2); Control.SimulatedMotorCurrent = 1.55f;
            graph.Solve(0); Assert.That(Control.Outputs[1].Reading.State, Is.EqualTo(ControlSignalState.Floating));
            var a = Link(26, 3); var b = Link(27, 4); graph.Solve(.1f);
            Assert.That(Control.Inputs[0].Reading.Value, Is.EqualTo(10).Within(.001));
            Assert.That(panel.ActualSpeedRpm, Is.EqualTo(500).Within(.1)); Assert.That(Control.Outputs[1].Effective, Is.EqualTo(10).Within(.001));
            graph.RemoveWire(a.Id); graph.RemoveWire(b.Id); Link(26, 4); Link(27, 3); graph.Solve(.1f);
            Assert.That(Control.Inputs[0].Reading.State, Is.EqualTo(ControlSignalState.Reversed)); Assert.That(panel.ActualSpeedRpm, Is.Zero);
        }
        [Test] public void AnalogUnitMismatchAndMultipleSourcesAreRejected()
        {
            Link(26, 3); Link(27, 4); graph.Solve(0);
            Assert.That(Control.Inputs[0].Reading.State, Is.EqualTo(ControlSignalState.TypeMismatch));
            Link(12, 3); Link(13, 4); Control.SetInputType(0, 2);
            Assert.That(graph.Solve(0).Errors, Is.Not.Empty);
        }
        [Test] public void ParallelCurrentInputsShareTheSourceCurrentAndRestoreWhenOneIsDisconnected()
        {
            Control.SetInputType(0, 2); Control.SetInputType(1, 2); Control.SimulatedMotorCurrent = 1.55f;
            Link(26, 3); Link(27, 4); var secondPositive = Link(26, 10); Link(27, 11);
            var snapshot = graph.Solve(0);
            Assert.That(Control.Outputs[1].Setpoint, Is.EqualTo(10).Within(.001));
            Assert.That(Control.Inputs[0].Reading.Value, Is.EqualTo(5).Within(.001));
            Assert.That(Control.Inputs[1].Reading.Value, Is.EqualTo(5).Within(.001));
            Assert.That(snapshot.GetDcVoltage("G120.T03", "G120.T04"), Is.EqualTo(1.25).Within(.001));
            graph.RemoveWire(secondPositive.Id); graph.Solve(0);
            Assert.That(Control.Inputs[0].Reading.Value, Is.EqualTo(10).Within(.001));
            Assert.That(Control.Inputs[1].Reading.State, Is.EqualTo(ControlSignalState.Floating));
        }
        [TestCase(G120AnalogOutputMode.Voltage10, 5)]
        [TestCase(G120AnalogOutputMode.Current20, 10)]
        [TestCase(G120AnalogOutputMode.Current4To20, 12)]
        public void OutputModesScaleSimulatedCurrent(G120AnalogOutputMode mode, double expected)
        {
            Control.Outputs[1].Mode = mode; Control.SimulatedMotorCurrent = 2; Control.Outputs[1].FullScale = 4;
            graph.Solve(0); Assert.That(Control.Outputs[1].Setpoint, Is.EqualTo(expected).Within(.001));
        }
        [TestCase(G120PtcState.Overheated)] [TestCase(G120PtcState.Open)] [TestCase(G120PtcState.Shorted)]
        public void PtcRequiresRecoveryAndANewResetEdge(G120PtcState state)
        {
            Commons(); On(0); On(4); Control.PtcEnabled = true; Control.PtcState = state;
            graph.Solve(.1f); Assert.That(panel.HasFault, Is.True); Assert.That(panel.ActualSpeedRpm, Is.Zero);
            Assert.That(panel.FaultNumber, Is.EqualTo(state == G120PtcState.Overheated ? 7011 : 7016));
            Assert.That(panel.AlarmNumber, Is.EqualTo(state == G120PtcState.Overheated ? 7910 : 7015));
            var reset = On(2); graph.Solve(.1f); Assert.That(panel.HasFault, Is.True);
            Control.PtcState = G120PtcState.Normal; graph.Solve(.1f); Assert.That(panel.HasFault, Is.True); Assert.That(panel.HasAlarm, Is.False);
            graph.RemoveWire(reset.Id); graph.Solve(0); On(2); graph.Solve(.1f); Assert.That(panel.HasFault, Is.False);
        }
        [Test] public void PersistentPtcFaultCannotBeAcknowledgedAfterControlPowerIsLost()
        {
            Control.PtcEnabled = true; Control.PtcState = G120PtcState.Overheated; graph.Solve(0);
            Assert.That(panel.HasFault, Is.True);
            mains = false; graph.Solve(0); panel.SetFault(false);
            Assert.That(panel.HasFault, Is.True);
            Control.PtcState = G120PtcState.Normal; graph.Solve(0); panel.SetFault(false);
            Assert.That(panel.HasFault, Is.False);
        }
        [TestCase(0)] [TestCase(1)]
        public void FractionalCalibrationPreservesDistinctPoints(int channel)
        {
            panel.TrySetParameter("P757." + channel, 2.11f); panel.TrySetParameter("P759." + channel, 2.14f);
            Control.Inputs[channel].Simulated = true; Control.Inputs[channel].SimulatedValue = 2.125f;
            graph.Solve(0);
            Assert.That(Control.Inputs[channel].Reading.Valid, Is.True);
            Assert.That(Control.Inputs[channel].Percent, Is.EqualTo(50).Within(.001));
        }
        [Test] public void AtomicThreeWireInputSnapshotGivesStopPrecedence()
        {
            Macro(19); panel.SetAnalogInputVolts(5);
            panel.SetDigitalInputs(new[] { false, true, false, false, false, false }); panel.Advance(.1f);
            Assert.That(panel.ActualSpeedRpm, Is.Zero);
            panel.SetDigitalInputs(new[] { true, true, false, false, false, false }); panel.Advance(.1f);
            Assert.That(panel.ActualSpeedRpm, Is.Zero, "held start is not a new pulse");
            panel.SetDigitalInputs(new[] { true, false, false, false, false, false });
            panel.SetDigitalInputs(new[] { true, true, false, false, false, false }); panel.Advance(.1f);
            Assert.That(panel.ActualSpeedRpm, Is.EqualTo(500));
        }
        [Test] public void ZeroTimeSolveNeverAdvancesRampsOrMopAndFactoryResetClearsInjection()
        {
            Macro(9); Commons(); On(0); On(1); panel.TrySetParameter("P1120", 10);
            graph.Solve(.1f); var speed = panel.ActualSpeedRpm; var mop = panel.MotorizedPotentiometerRpm;
            for (var i = 0; i < 5; i++) graph.Solve(0);
            Assert.That(panel.ActualSpeedRpm, Is.EqualTo(speed)); Assert.That(panel.MotorizedPotentiometerRpm, Is.EqualTo(mop));
            Control.Inputs[0].Simulated = true; Control.SimulatedMotorCurrent = 5; Control.PtcEnabled = true;
            panel.ResetFactorySettings(); Assert.That(panel.Macro, Is.EqualTo(1)); Assert.That(Control.Inputs[0].Simulated, Is.False);
            Assert.That(Control.PtcEnabled, Is.False); Assert.That(Control.SimulatedMotorCurrent, Is.Zero);
        }
    }
}
