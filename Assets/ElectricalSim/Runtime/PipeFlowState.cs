using System;
using UnityEngine;

namespace ElectricalSim
{
    // Visual transport only. Tank volume continues to use LiquidSimulationRuntime.
    public sealed class PipeFlowState
    {
        public const float FadeSeconds = 0.5f;
        public float Length { get; }
        public float ValveDistance { get; }
        public float Front { get; private set; }
        public float UpstreamOpacity { get; private set; }
        public float DownstreamOpacity { get; private set; }
        public float UpstreamPhase { get; private set; }
        public float DownstreamPhase { get; private set; }
        public bool IsBlocked { get; private set; }

        public PipeFlowState(float length, float valveDistance)
        {
            if (length <= 0 || valveDistance <= 0 || valveDistance >= length)
                throw new ArgumentException("管路长度或阀门位置无效。");
            Length = length; ValveDistance = valveDistance;
        }

        public void Reset()
        {
            Front = UpstreamOpacity = DownstreamOpacity = UpstreamPhase = DownstreamPhase = 0;
            IsBlocked = false;
        }

        public void Advance(float seconds, float speed, bool valveOpen, bool simulating, bool frozen = false)
        {
            if (frozen || seconds <= 0) return;
            IsBlocked = false;
            if (!simulating || speed <= 0.00001f)
            {
                UpstreamOpacity = Mathf.Max(0, UpstreamOpacity - seconds / FadeSeconds);
                DownstreamOpacity = Mathf.Max(0, DownstreamOpacity - seconds / FadeSeconds);
                if (UpstreamOpacity == 0 && DownstreamOpacity == 0) Front = 0;
                return;
            }
            UpstreamOpacity = 1;
            if (!valveOpen)
            {
                DownstreamOpacity = Mathf.Max(0, DownstreamOpacity - seconds / FadeSeconds);
                if (DownstreamOpacity == 0) Front = Mathf.Min(Front, ValveDistance);
                var advance = Mathf.Min(speed * seconds, Mathf.Max(0, ValveDistance - Front));
                Front += advance;
                UpstreamPhase += advance;
                IsBlocked = Front >= ValveDistance;
                return;
            }
            var oldFront = Front;
            Front = Mathf.Min(Length, Front + speed * seconds);
            UpstreamPhase += speed * seconds;
            if (Front > ValveDistance)
            {
                DownstreamOpacity = 1;
                DownstreamPhase += oldFront >= ValveDistance ? speed * seconds : Front - ValveDistance;
            }
        }
    }
}
