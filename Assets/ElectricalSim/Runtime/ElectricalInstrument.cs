using System;

namespace ElectricalSim
{
    public sealed class ElectricalInstrument : IElectricalInstrument
    {
        public ElectricalInstrument(InstrumentKind kind)
        {
            Kind = kind;
        }

        public InstrumentKind Kind { get; }

        public VoltageProbeReading MeasureVoltageProbe(VoltageProbeMode mode, string port, SimulationSnapshot snapshot)
        {
            VoltageProbeReading Result(VoltageProbeState state, double value = double.NaN) => new VoltageProbeReading(mode, state, value);
            if (string.IsNullOrEmpty(port)) return Result(VoltageProbeState.MissingContact);
            if (snapshot == null) return Result(VoltageProbeState.Unavailable);
            if (!snapshot.ContainsPort(port)) return Result(VoltageProbeState.MissingContact);
            var reference = mode == VoltageProbeMode.AC ? VoltageProbeReading.AcReference : VoltageProbeReading.DcReference;
            var potential = snapshot.GetPotential(port);
            if (potential == ElectricalPotential.Conflict || snapshot.HasSignalConflict(port) ||
                snapshot.GetPotential(reference) == ElectricalPotential.Conflict || snapshot.HasSignalConflict(reference))
                return Result(VoltageProbeState.Conflict);
            if (!snapshot.IsConverged) return Result(VoltageProbeState.Unavailable);
            if (snapshot.TryReadInverterVoltage(port, reference, out var inverterProbe))
                return Result(inverterProbe.State == OscilloscopeSignalState.Conflict ? VoltageProbeState.Conflict : VoltageProbeState.UndefinedReference);
            if (snapshot.IsVoltageUnsupported(port) || snapshot.IsVoltageUnsupported(reference)) return Result(VoltageProbeState.Unsupported);
            if (mode == VoltageProbeMode.AC && (IsDc(potential) || snapshot.HasControlSignal(port)) ||
                mode == VoltageProbeMode.DC && (IsPhase(potential) || potential == ElectricalPotential.Neutral))
                return Result(VoltageProbeState.ModeMismatch);
            if (!snapshot.ContainsPort(reference) || snapshot.GetPotential(reference) !=
                (mode == VoltageProbeMode.AC ? ElectricalPotential.Neutral : ElectricalPotential.DcNegative))
                return Result(VoltageProbeState.UndefinedReference);
            if (snapshot.SameNet(port, reference)) return Result(VoltageProbeState.Valid, 0);
            if (mode == VoltageProbeMode.AC)
                return IsPhase(potential) || potential == ElectricalPotential.Neutral
                    ? Result(VoltageProbeState.Valid, snapshot.GetAcVoltage(port, reference))
                    : Result(VoltageProbeState.UndefinedReference);
            if (snapshot.TryGetSignalVoltage(port, reference, out var volts))
                return double.IsNaN(volts) || double.IsInfinity(volts) ? Result(VoltageProbeState.Conflict) : Result(VoltageProbeState.Valid, volts);
            if (IsDc(potential)) return Result(VoltageProbeState.Valid, snapshot.GetDcVoltage(port, reference));
            return Result(VoltageProbeState.UndefinedReference);
        }

        // Red is the positive input; black is COM. This API leaves legacy instruments unchanged.
        public MultimeterReading Measure(MultimeterMode mode, string redPort, string blackPort, SimulationSnapshot snapshot)
        {
            if (mode == MultimeterMode.Off) return new MultimeterReading(mode, MultimeterReadingState.Off);
            if (snapshot == null || !snapshot.ContainsPort(redPort) || !snapshot.ContainsPort(blackPort))
                return new MultimeterReading(mode, MultimeterReadingState.MissingProbe);
            var red = snapshot.GetPotential(redPort);
            var black = snapshot.GetPotential(blackPort);
            if (red == ElectricalPotential.Conflict || black == ElectricalPotential.Conflict || snapshot.HasSignalConflict(redPort) || snapshot.HasSignalConflict(blackPort))
                return new MultimeterReading(mode, MultimeterReadingState.Conflict);
            if (mode == MultimeterMode.Continuity)
            {
                if (snapshot.TryReadInverterVoltage(redPort, blackPort, out var fault) && fault.State == OscilloscopeSignalState.Conflict)
                    return new MultimeterReading(mode, MultimeterReadingState.Conflict);
                if (snapshot.HasExternalSupply(redPort) || snapshot.HasExternalSupply(blackPort))
                    return new MultimeterReading(mode, MultimeterReadingState.Energized);
                return snapshot.GetResistance(redPort, blackPort) <= 50
                    ? new MultimeterReading(mode, MultimeterReadingState.Valid, 1)
                    : new MultimeterReading(mode, MultimeterReadingState.OpenCircuit, 0);
            }
            if (snapshot.TryReadInverterVoltage(redPort, blackPort, out var inverter))
                return new MultimeterReading(mode, inverter.Valid ? MultimeterReadingState.Valid :
                    inverter.State == OscilloscopeSignalState.Conflict ? MultimeterReadingState.Conflict :
                    inverter.State == OscilloscopeSignalState.Unavailable ? MultimeterReadingState.Unavailable : MultimeterReadingState.UndefinedReference,
                    inverter.Valid ? (mode == MultimeterMode.AcVoltage ? inverter.AcRms : inverter.Dc) : double.NaN, true);
            if (snapshot.IsVoltageUnsupported(redPort) || snapshot.IsVoltageUnsupported(blackPort))
                return new MultimeterReading(mode, MultimeterReadingState.Unsupported);
            if (snapshot.TryGetSignalVoltage(redPort, blackPort, out var signalVoltage))
                return new MultimeterReading(mode, double.IsNaN(signalVoltage) ? MultimeterReadingState.Conflict : MultimeterReadingState.Valid,
                    mode == MultimeterMode.DcVoltage ? signalVoltage : 0);
            if (snapshot.HasControlSignal(redPort) || snapshot.HasControlSignal(blackPort))
                return new MultimeterReading(mode, MultimeterReadingState.UndefinedReference);
            if ((red == ElectricalPotential.Floating) != (black == ElectricalPotential.Floating) ||
                red != ElectricalPotential.Floating && IsDc(red) != IsDc(black))
                return new MultimeterReading(mode, MultimeterReadingState.UndefinedReference);
            return new MultimeterReading(mode, MultimeterReadingState.Valid,
                mode == MultimeterMode.AcVoltage ? snapshot.GetAcVoltage(redPort, blackPort) : snapshot.GetDcVoltage(redPort, blackPort));
        }

        private static bool IsDc(ElectricalPotential potential)
            => potential == ElectricalPotential.DcPositive24 || potential == ElectricalPotential.DcNegative;

        public double Sample(MeasurementKind measurement, string portA, string portB, SimulationSnapshot snapshot)
        {
            if (snapshot == null) return double.NaN;
            if (measurement == MeasurementKind.DcVoltage) return snapshot.GetDcVoltage(portA, portB);
            if (measurement == MeasurementKind.AcVoltage) return snapshot.GetAcVoltage(portA, portB);
            if (measurement == MeasurementKind.Continuity)
                return snapshot.GetResistance(portA, portB) <= 50 ? 1d : 0d;
            if (measurement == MeasurementKind.Resistance)
                return snapshot.HasExternalSupply(portA) || snapshot.HasExternalSupply(portB) ? double.NaN : snapshot.GetResistance(portA, portB);

            var a = snapshot.GetPotential(portA);
            var b = snapshot.GetPotential(portB);
            if (a == ElectricalPotential.Conflict || b == ElectricalPotential.Conflict) return double.NaN;
            if (a == b) return 0d;
            if (IsPhase(a) && b == ElectricalPotential.Neutral || IsPhase(b) && a == ElectricalPotential.Neutral)
                return measurement == MeasurementKind.AcVoltage ? 220d : 0d;
            if (IsPhase(a) && IsPhase(b)) return measurement == MeasurementKind.AcVoltage ? 380d : 0d;
            return 0d;
        }

        public double SampleMotorSpeed(string motorId, SimulationSnapshot snapshot)
        {
            return snapshot == null ? double.NaN : snapshot.GetMotorSpeedRpm(motorId);
        }

        private static bool IsPhase(ElectricalPotential potential)
        {
            return potential == ElectricalPotential.PhaseL1 || potential == ElectricalPotential.PhaseL2 || potential == ElectricalPotential.PhaseL3;
        }
    }
}
