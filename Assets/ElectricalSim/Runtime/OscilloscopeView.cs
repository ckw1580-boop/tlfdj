using UnityEngine;
using UnityEngine.UI;

namespace ElectricalSim
{
    public sealed class OscilloscopeView : MonoBehaviour
    {
        [SerializeField] private Transform body;
        [SerializeField] private Transform[] probes, tips, docks, sockets;
        [SerializeField] private LineRenderer[] leads;
        [SerializeField] private OscilloscopePlot plot;
        [SerializeField] private Text summary;
        private readonly Vector3[] leadPoints = new Vector3[25];
        public Transform Body => body;
        public OscilloscopePlot Plot => plot;
        public string DisplayText => summary.text;
        public Transform Probe(int i) => probes[i];
        public Transform ProbeTip(int i) => tips[i];
        public LineRenderer Lead(int i) => leads[i];
        public void Configure(Transform b, Transform[] p, Transform[] t, Transform[] d, Transform[] s, LineRenderer[] l, OscilloscopePlot graph, Text text)
        { body = b; probes = p; tips = t; docks = d; sockets = s; leads = l; plot = graph; summary = text; }
        public void Bind(OscilloscopeFrame frame) => plot.Bind(frame);
        public void SetFont(Font font) { foreach (var text in GetComponentsInChildren<Text>(true)) text.font = font; }
        public void DockProbe(int i) { probes[i].SetPositionAndRotation(docks[i].position, docks[i].rotation); SetProbePickable(i, true); }
        public void PlaceProbe(int i, Vector3 point, Quaternion rotation) { probes[i].rotation = rotation; probes[i].position += point - tips[i].position; }
        public void SetProbePickable(int i, bool value) { foreach (var c in probes[i].GetComponentsInChildren<Collider>()) c.enabled = value; }
        public void RefreshScreen(OscilloscopeFrame frame)
        {
            summary.text = (frame.Frozen ? "已冻结" : "运行") + $" · 当前工况波形 · {frame.MillisecondsPerDivision:0.###} ms/div\n" +
                ScreenChannel(frame.Channels[0], 1) + "\n" + ScreenChannel(frame.Channels[1], 2);
            foreach (var c in frame.Channels)
                if (c.Enabled && c.Signal.IsInverter && !c.Signal.PwmSupported && c.Waveform != OscilloscopeWaveform.Fundamental)
                { summary.text += "\n超出教学 PWM 模型范围"; break; }
        }
        private static string ScreenChannel(OscilloscopeChannel c, int number) =>
            $"CH{number} {c.Coupling} · {c.VoltsPerDivision:0.###} V/div · " +
            (!c.Enabled ? "关闭" : c.Signal.Valid ? (c.Signal.IsInverter ? $"{Mode(c.Waveform)} · 基波 {c.Signal.AcRms:0.##} V · {c.Signal.FrequencyHz:0.##} Hz · 载波4kHz" : $"{c.Signal.Rms(c.Coupling):0.##} V RMS") + (c.Clipped ? " 裁切" : "") : c.Signal.Hint) +
            "\n＋ " + Short(c.PositiveLabel) + "\n－ " + Short(c.NegativeLabel);
        private static string Mode(OscilloscopeWaveform mode) => mode == OscilloscopeWaveform.Fundamental ? "基波" : mode == OscilloscopeWaveform.PWM ? "PWM" : "叠加";
        private static string Short(string label) => label.Length <= 30 ? label : label.Substring(0, 27) + "…";
        public void RefreshLeads()
        {
            for (var i = 0; i < 4; i++)
            {
                var start = sockets[i].position; var end = probes[i].position - probes[i].up * .03f;
                var sag = Mathf.Clamp(Vector3.Distance(start, end) * .18f, .035f, .22f);
                for (var j = 0; j < leadPoints.Length; j++)
                {
                    var t = j / (float)(leadPoints.Length - 1);
                    leadPoints[j] = Vector3.Lerp(start, end, t) - Vector3.up * (4 * t * (1 - t) * sag) - body.forward * (Mathf.Sin(t * Mathf.PI) * .025f);
                }
                leads[i].SetPositions(leadPoints);
            }
        }
    }
}
