using System;
using UnityEngine;

namespace ElectricalSim
{
    /// <summary>Teaching model: piecewise torque/slip curve, rigid inertia and first-order heat.</summary>
    public static class MotorPhysics
    {
        private const float RadiansPerRpm = (float)(2d * Math.PI / 60d);

        public static void Refresh(MotorConfiguration config, MotorRuntimeState state, bool braking = false)
        {
            var connection = state.Connection;
            var high = connection.IsHighSpeed;
            var poles = high ? config.HighPoleCount : config.PoleCount;
            var nominalRpm = high ? config.HighRatedSpeedRpm : config.RatedSpeedRpm;
            var nominalCurrent = high ? config.HighRatedCurrentAmps : config.RatedCurrentAmps;
            var nominalSync = 120f * config.RatedFrequencyHz / poles;
            var ratedSlip = 1f - nominalRpm / nominalSync;
            state.SynchronousSpeedRpm = connection.Energized
                ? connection.DirectionSign * 120f * connection.FrequencyHz / poles : 0f;
            state.Slip = Math.Abs(state.SynchronousSpeedRpm) > 0.01f
                ? (state.SynchronousSpeedRpm - state.SpeedRpm) / state.SynchronousSpeedRpm : 0f;
            state.TorqueNm = 0f;
            Array.Clear(state.PhaseCurrentsAmps, 0, 3);
            if (!connection.IsValid || !connection.Energized || braking) return;

            var nominalWindingVoltage = connection.Kind == MotorConnectionKind.Star
                ? config.RatedVoltageStar / Mathf.Sqrt(3f) : config.RatedVoltageDelta;
            var flux = connection.WindingVoltage / nominalWindingVoltage * config.RatedFrequencyHz /
                       Math.Max(0.05f, connection.FrequencyHz);
            // Saturation caps the teaching model instead of creating unbounded overvoltage torque.
            flux = Mathf.Clamp(flux, 0f, 1.2f);
            var slip = Math.Abs(state.Slip);
            var torque = Curve(slip, ratedSlip, config.BreakdownSlip, 1f,
                config.MaximumTorqueMultiple, config.StartingTorqueMultiple);
            state.TorqueNm = Math.Sign(state.Slip) * connection.DirectionSign * torque *
                config.RatedTorqueNm(high) * flux * flux;
            if (connection.PhaseLoss)
            {
                // A stationary single-phasing motor has no starting torque; a rotating rotor can continue.
                state.TorqueNm = Math.Abs(state.SpeedRpm) < 1f ? 0f : state.TorqueNm * 0.45f;
            }
            var currentMultiple = slip <= ratedSlip
                ? Mathf.Sqrt(0.25f * 0.25f + 0.9375f * slip * slip / (ratedSlip * ratedSlip))
                : Mathf.Lerp(1f, config.StartingCurrentMultiple, Mathf.Clamp01((slip - ratedSlip) / (1f - ratedSlip)));
            var connectionCurrentFactor = connection.Kind == MotorConnectionKind.Star ? 1f / Mathf.Sqrt(3f) : 1f;
            var amps = nominalCurrent * currentMultiple * flux * connectionCurrentFactor * (connection.PhaseLoss ? 1.7f : 1f);
            for (var i = 0; i < 3; i++)
                state.PhaseCurrentsAmps[i] = connection.PhaseIndices.Length == 3 && connection.PhaseIndices[i] >= 0 ? amps : 0f;
        }

        public static void Advance(MotorConfiguration config, MotorRuntimeState state, float deltaTime,
            bool braking = false, float coastStopSeconds = 3f, float brakeStopSeconds = 1f)
        {
            Refresh(config, state, braking);
            if (!(deltaTime > 0f) || float.IsInfinity(deltaTime) || float.IsNaN(deltaTime)) return;
            var high = state.Connection.IsHighSpeed;
            var poles = high ? config.HighPoleCount : config.PoleCount;
            var nominalRpm = high ? config.HighRatedSpeedRpm : config.RatedSpeedRpm;
            var ratedSlip = 1f - nominalRpm / (120f * config.RatedFrequencyHz / poles);
            var syncOmega = Math.Max(0.1f, 120f * state.Connection.FrequencyHz / poles * RadiansPerRpm);
            // Resolve the steep near-synchronous curve without low-frequency Euler oscillation.
            var stableStep = Math.Min(0.005f, 0.4f * config.InertiaKgM2 * syncOmega * ratedSlip /
                (config.RatedTorqueNm(high) * 1.44f));
            if (!state.Connection.Energized || braking || state.IsStalled) stableStep = 0.005f;
            var steps = Math.Max(1, (int)Math.Ceiling(deltaTime / Math.Max(0.00005f, stableStep)));
            var dt = deltaTime / steps;
            var nominalOmega = config.RatedSpeedRpm * RadiansPerRpm;
            var loadTorque = config.LoadFactor * config.RatedTorqueNm();
            for (var step = 0; step < steps; step++)
            {
                Refresh(config, state, braking);
                if (state.IsStalled) state.SpeedRpm = 0f;
                else
                {
                    var omega = state.SpeedRpm * RadiansPerRpm;
                    var sign = Math.Abs(omega) > 0.00001f ? Math.Sign(omega) : Math.Sign(state.TorqueNm);
                    var friction = config.RatedTorqueNm() * 0.005f * omega / nominalOmega;
                    var coastDrag = !state.Connection.Energized
                        ? config.InertiaKgM2 * nominalOmega / Math.Max(0.01f, coastStopSeconds) * sign : 0f;
                    var brakeTorque = braking
                        ? config.InertiaKgM2 * nominalOmega / Math.Max(0.01f, brakeStopSeconds) * sign : 0f;
                    var next = omega + (state.TorqueNm - loadTorque * sign - friction - coastDrag - brakeTorque) /
                               config.InertiaKgM2 * dt;
                    if ((Math.Abs(omega) < 0.00001f || Math.Sign(next) != Math.Sign(omega)) &&
                        (braking || Math.Abs(state.TorqueNm) <= loadTorque + Math.Abs(coastDrag))) next = 0f;
                    state.SpeedRpm = next / RadiansPerRpm;
                }
                var ratedCurrent = high ? config.HighRatedCurrentAmps : config.RatedCurrentAmps;
                var heating = 0.65f * state.CurrentAmps * state.CurrentAmps / (ratedCurrent * ratedCurrent);
                state.ThermalState = heating + (state.ThermalState - heating) *
                    Mathf.Exp(-dt / config.ThermalTimeConstantSeconds);
            }
            Refresh(config, state, braking);
        }

        private static float Curve(float slip, float ratedSlip, float breakdownSlip,
            float rated, float maximum, float starting)
        {
            if (slip <= ratedSlip) return rated * slip / ratedSlip;
            if (slip <= breakdownSlip) return Mathf.Lerp(rated, maximum, (slip - ratedSlip) / (breakdownSlip - ratedSlip));
            if (slip <= 1f) return Mathf.Lerp(maximum, starting, (slip - breakdownSlip) / (1f - breakdownSlip));
            return starting / Mathf.Sqrt(slip); // reverse-field braking remains finite for slip > 1.
        }
    }
}
