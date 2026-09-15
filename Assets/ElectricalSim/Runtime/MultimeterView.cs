using UnityEngine;
using UnityEngine.UI;

namespace ElectricalSim
{
    public sealed class MultimeterView : MonoBehaviour
    {
        [SerializeField] private Transform body;
        [SerializeField] private Transform dial;
        [SerializeField] private Transform[] probes;
        [SerializeField] private Transform[] tips;
        [SerializeField] private Transform[] docks;
        [SerializeField] private Transform[] sockets;
        [SerializeField] private LineRenderer[] leads;
        [SerializeField] private Text valueText, modeText, hintText, terminalText;
        [SerializeField] private AudioSource buzzer;
        private AudioClip tone;
        private readonly Vector3[] leadPoints = new Vector3[25];
        public Transform Body => body;
        public string DisplayText => valueText.text;
        public string HintText => hintText.text;
        public bool IsBeeping => buzzer != null && buzzer.isPlaying;
        public Transform Probe(int index) => probes[index];
        public Transform ProbeTip(int index) => tips[index];
        public LineRenderer Lead(int index) => leads[index];

        public void Configure(Transform meterBody, Transform knob, Transform[] probeRoots, Transform[] probeTips,
            Transform[] probeDocks, Transform[] inputSockets, LineRenderer[] cables, Text value, Text mode,
            Text hint, Text terminals, AudioSource audio)
        {
            body = meterBody; dial = knob; probes = probeRoots; tips = probeTips; docks = probeDocks;
            sockets = inputSockets; leads = cables; valueText = value; modeText = mode;
            hintText = hint; terminalText = terminals; buzzer = audio;
        }

        public void SetFont(Font font)
        {
            foreach (var label in GetComponentsInChildren<Text>(true)) label.font = font;
        }

        public void DockProbe(int index)
        {
            probes[index].SetPositionAndRotation(docks[index].position, docks[index].rotation);
            SetProbePickable(index, true);
        }

        public void PlaceProbe(int index, Vector3 point, Quaternion rotation)
        {
            probes[index].rotation = rotation;
            probes[index].position += point - tips[index].position;
        }

        public void SetProbePickable(int index, bool enabled)
        {
            foreach (var collider in probes[index].GetComponentsInChildren<Collider>()) collider.enabled = enabled;
        }

        public void SetReading(MultimeterReading reading, string red, string black)
        {
            valueText.text = reading.DisplayValue + (reading.State == MultimeterReadingState.Valid && reading.Unit.Length > 0 ? " " + reading.Unit : "");
            modeText.text = MultimeterReading.ModeLabel(reading.Mode);
            hintText.text = reading.Hint;
            terminalText.text = "红：" + red + "\n黑：" + black;
            dial.localRotation = Quaternion.Euler(0, 0, -(int)reading.Mode * 90f);
            SetBeep(reading.ShouldBeep);
        }

        public void RefreshLeads()
        {
            for (var i = 0; i < 2; i++)
            {
                var start = sockets[i].position;
                var end = probes[i].position - probes[i].up * 0.036f;
                var sag = Mathf.Clamp(Vector3.Distance(start, end) * 0.18f, 0.035f, 0.22f);
                for (var p = 0; p < leadPoints.Length; p++)
                {
                    var t = p / (float)(leadPoints.Length - 1);
                    leadPoints[p] = Vector3.Lerp(start, end, t) - Vector3.up * (4 * t * (1 - t) * sag)
                        - body.forward * (Mathf.Sin(t * Mathf.PI) * 0.025f);
                }
                leads[i].SetPositions(leadPoints);
            }
        }

        private void SetBeep(bool play)
        {
            if (!play || !isActiveAndEnabled) { if (buzzer != null) buzzer.Stop(); return; }
            if (buzzer.isPlaying) return;
            if (tone == null)
            {
                const int rate = 22050;
                var samples = new float[rate / 10];
                for (var i = 0; i < samples.Length; i++) samples[i] = Mathf.Sin(2 * Mathf.PI * 1800 * i / rate) * 0.22f;
                tone = AudioClip.Create("Multimeter continuity", samples.Length, 1, rate, false);
                tone.SetData(samples, 0);
                buzzer.clip = tone;
            }
            buzzer.Play();
        }
        private void OnDisable() { if (buzzer != null) buzzer.Stop(); }
        private void OnDestroy() { if (tone != null) Destroy(tone); }
    }
}
