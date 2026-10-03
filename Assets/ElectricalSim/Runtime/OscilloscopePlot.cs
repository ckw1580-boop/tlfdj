using UnityEngine;
using UnityEngine.UI;

namespace ElectricalSim
{
    [RequireComponent(typeof(CanvasRenderer))]
    public sealed class OscilloscopePlot : MaskableGraphic
    {
        public static readonly Color[] ChannelColors = { new Color(1, .84f, .18f), new Color(.15f, .86f, 1) };
        public OscilloscopeFrame Frame { get; private set; }
        private int revision = -1;
        public void Bind(OscilloscopeFrame frame) { Frame = frame; raycastTarget = false; SetVerticesDirty(); }
        private void LateUpdate() { if (Frame != null && revision != Frame.Revision) { revision = Frame.Revision; SetVerticesDirty(); } }
        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear(); var r = rectTransform.rect;
            for (var x = 0; x <= 10; x++) Line(vh, new Vector2(r.xMin + r.width * x / 10, r.yMin), new Vector2(r.xMin + r.width * x / 10, r.yMax), new Color(.16f, .24f, .28f), .6f);
            for (var y = 0; y <= 8; y++) Line(vh, new Vector2(r.xMin, r.yMin + r.height * y / 8), new Vector2(r.xMax, r.yMin + r.height * y / 8), new Color(.16f, .24f, .28f), .6f);
            if (Frame == null) return;
            for (var channel = 0; channel < 2; channel++)
            {
                var c = Frame.Channels[channel]; if (!c.Enabled) continue;
                var color = ChannelColors[channel];
                var zero = r.center.y + c.Position * r.height / 8;
                Line(vh, new Vector2(r.xMin, zero), new Vector2(r.xMin + 8, zero), color, 2);
                if (!c.Signal.Valid) continue;
                for (var i = 1; i < c.Samples.Length; i++)
                {
                    if (!c.DrawFundamental || c.DrawPwm && i % 12 >= 7) continue;
                    var ya = c.Samples[i - 1] / c.VoltsPerDivision + c.Position;
                    var yb = c.Samples[i] / c.VoltsPerDivision + c.Position;
                    if (ya > 4 && yb > 4 || ya < -4 && yb < -4) continue;
                    var xa = (i - 1f) / (c.Samples.Length - 1); var xb = i / (float)(c.Samples.Length - 1);
                    if (ya < -4 || ya > 4) { var bound = Mathf.Clamp(ya, -4, 4); xa = Mathf.Lerp(xa, xb, (bound - ya) / (yb - ya)); ya = bound; }
                    if (yb < -4 || yb > 4) { var bound = Mathf.Clamp(yb, -4, 4); xb = Mathf.Lerp(xa, xb, (bound - ya) / (yb - ya)); yb = bound; }
                    Line(vh, new Vector2(r.xMin + xa * r.width, r.center.y + ya * r.height / 8), new Vector2(r.xMin + xb * r.width, r.center.y + yb * r.height / 8), color, 1.2f);
                }
                if (!c.DrawPwm) continue;
                if (c.PwmIntervals.Count <= 1600)
                {
                    float previous = 0; var first = true;
                    foreach (var segment in c.PwmIntervals)
                    {
                        var y = segment.Voltage / c.VoltsPerDivision + c.Position;
                        if (!first) Trace(vh, r, c, segment.Start, previous, segment.Start, y, color);
                        Trace(vh, r, c, segment.Start, y, segment.End, y, color);
                        previous = y; first = false;
                    }
                }
                else for (var i = 0; i < c.PwmMin.Length; i++)
                {
                    if (float.IsInfinity(c.PwmMin[i])) continue;
                    var x = (i + .5f) / c.PwmMin.Length;
                    Trace(vh, r, c, x, c.PwmMin[i] / c.VoltsPerDivision + c.Position, x,
                        c.PwmMax[i] / c.VoltsPerDivision + c.Position, new Color(color.r, color.g, color.b, .5f));
                }
            }
        }
        private static void Trace(VertexHelper vh, Rect r, OscilloscopeChannel c, float xa, float ya, float xb, float yb, Color color)
        {
            if (ya > 4 && yb > 4 || ya < -4 && yb < -4) return;
            Line(vh, new Vector2(r.xMin + xa * r.width, r.center.y + Mathf.Clamp(ya, -4, 4) * r.height / 8),
                new Vector2(r.xMin + xb * r.width, r.center.y + Mathf.Clamp(yb, -4, 4) * r.height / 8), color, 1);
        }
        private static void Line(VertexHelper vh, Vector2 a, Vector2 b, Color color, float width)
        {
            var normal = new Vector2(-(b - a).y, (b - a).x).normalized * width / 2; var n = vh.currentVertCount;
            vh.AddVert(a - normal, color, Vector2.zero); vh.AddVert(a + normal, color, Vector2.zero);
            vh.AddVert(b + normal, color, Vector2.zero); vh.AddVert(b - normal, color, Vector2.zero);
            vh.AddTriangle(n, n + 1, n + 2); vh.AddTriangle(n, n + 2, n + 3);
        }
    }
}
