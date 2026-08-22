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

        public double Sample(MeasurementKind measurement, string portA, string portB, SimulationSnapshot snapshot)
        {
            if (snapshot == null) return double.NaN;
            if (measurement == MeasurementKind.Continuity)
                return snapshot.SameNet(portA, portB) ? 1d : 0d;
            if (measurement == MeasurementKind.Resistance)
                return snapshot.SameNet(portA, portB) ? 0.2d : double.PositiveInfinity;

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
            var direction = snapshot.GetMotorDirection(motorId);
            if (direction == MotorDirection.Forward) return 1450d;
            if (direction == MotorDirection.Reverse) return -1450d;
            if (direction == MotorDirection.Braking) return 250d;
            return 0d;
        }

        private static bool IsPhase(ElectricalPotential potential)
        {
            return potential == ElectricalPotential.PhaseL1 || potential == ElectricalPotential.PhaseL2 || potential == ElectricalPotential.PhaseL3;
        }
    }
}
