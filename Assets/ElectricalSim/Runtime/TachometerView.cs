using UnityEngine;
using UnityEngine.UI;

namespace ElectricalSim
{
    public sealed class TachometerView : MonoBehaviour
    {
        [SerializeField] private Transform probeTip;
        [SerializeField] private Text readout;
        public Transform ProbeTip => probeTip;
        public string DisplayText => readout != null ? readout.text : string.Empty;

        public void Configure(Transform tip, Text display) { probeTip = tip; readout = display; }
        public void SetReading(float? rpm)
        {
            if (readout != null) readout.text = rpm.HasValue && !float.IsNaN(rpm.Value)
                ? Mathf.Abs(rpm.Value).ToString("0", System.Globalization.CultureInfo.InvariantCulture) : "—";
        }

        public void SetPickable(bool pickable)
        {
            foreach (var collider in GetComponentsInChildren<Collider>()) collider.enabled = pickable;
        }

        public void PlaceProbe(Vector3 position, Quaternion rotation)
        {
            transform.rotation = rotation;
            transform.position += position - probeTip.position;
        }
    }
}
