using UnityEngine;
using UnityEngine.UI;

namespace ElectricalSim
{
    public sealed class VoltageProbeView : MonoBehaviour
    {
        [SerializeField] private Transform tip;
        [SerializeField] private Text valueText, modeText;
        public Transform Tip => tip;
        public string DisplayText => valueText.text;
        public string ModeText => modeText.text;

        public void Configure(Transform contactTip, Text value, Text mode)
        { tip = contactTip; valueText = value; modeText = mode; }
        public void SetFont(Font font)
        { foreach (var label in GetComponentsInChildren<Text>(true)) label.font = font; }
        public void Place(Vector3 point, Quaternion rotation)
        {
            transform.rotation = rotation;
            transform.position += point - tip.position;
        }
        public void SetPickable(bool pickable)
        { foreach (var collider in GetComponentsInChildren<Collider>()) collider.enabled = pickable; }
        public void SetReading(VoltageProbeReading reading)
        {
            valueText.text = reading.DisplayText;
            modeText.text = reading.Mode == VoltageProbeMode.AC ? "AC" : "DC";
            valueText.color = reading.State == VoltageProbeState.Conflict ? new Color(0.7f, 0.08f, 0.04f) : new Color(0.035f, 0.12f, 0.12f);
        }
    }
}
