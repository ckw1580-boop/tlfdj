using System;
using UnityEngine;

namespace ElectricalSim
{
    // One clock owns both the pipe front and the falling jet. Rendering never
    // advances this state; the tank receives only the post-contact portion of a tick.
    public sealed class LiquidStreamRuntime
    {
        public PipeFlowState Pipe { get; }
        public float OutletY { get; }
        public float JetFront { get; private set; }

        public LiquidStreamRuntime(LiquidPipeRoute route)
        {
            var distances = new float[route.points.Length];
            for (var i = 1; i < distances.Length; i++)
                distances[i] = distances[i - 1] + Vector3.Distance(route.points[i - 1], route.points[i]);
            var nearest = float.MaxValue; var valveDistance = 0f;
            for (var i = 1; i < route.points.Length; i++)
            {
                var segment = route.points[i] - route.points[i - 1];
                if (segment.sqrMagnitude < 1e-12f) continue;
                var t = Mathf.Clamp01(Vector3.Dot(route.valve - route.points[i - 1], segment) / segment.sqrMagnitude);
                var error = (route.valve - route.points[i - 1] - segment * t).sqrMagnitude;
                if (error >= nearest) continue;
                nearest = error;
                valveDistance = Mathf.Lerp(distances[i - 1], distances[i], t);
            }
            Pipe = new PipeFlowState(distances[distances.Length - 1], valveDistance);
            OutletY = route.points[route.points.Length - 1].y;
        }

        public double Advance(double seconds, float speed, bool valveOpen, bool simulating, float surfaceY)
        {
            if (seconds <= 0) return 0;
            var oldFront = Pipe.Front;
            Pipe.Advance((float)seconds, speed, valveOpen, simulating);
            if (Pipe.DownstreamOpacity == 0) JetFront = 0;
            if (!simulating || !valveOpen || speed <= 0.00001f) return 0;
            var pipeSeconds = Math.Max(0, Pipe.Length - oldFront) / speed;
            if (pipeSeconds >= seconds) return 0;
            var available = seconds - pipeSeconds;
            var gap = Mathf.Max(0, OutletY - surfaceY);
            var fallSeconds = Math.Max(0, gap - JetFront) / speed;
            JetFront = Mathf.Min(Mathf.Max(gap, JetFront), JetFront + (float)(speed * available));
            return Math.Max(0, available - fallSeconds);
        }
        public void Reset() { Pipe.Reset(); JetFront = 0; }
    }
}
