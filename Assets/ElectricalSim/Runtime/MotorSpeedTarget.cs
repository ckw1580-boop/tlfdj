using UnityEngine;

namespace ElectricalSim
{
    public sealed class MotorSpeedTarget : MonoBehaviour
    {
        public string MotorId { get; private set; }
        public Vector3 Outward => transform.forward;
        public Quaternion MeterRotation => Quaternion.LookRotation(-Outward, Vector3.up) * Quaternion.Euler(25f, 0f, 0f);
        private Renderer marker;
        private Collider pickCollider;
        private Material material;
        private static readonly Color IdleColor = new Color(0.08f, 0.95f, 0.15f);

        public void Initialize(string motorId)
        {
            MotorId = motorId;
            marker = GetComponent<Renderer>();
            pickCollider = GetComponent<Collider>();
            material = new Material(Shader.Find("Standard")) { color = IdleColor };
            material.EnableKeyword("_EMISSION");
            material.SetColor("_EmissionColor", new Color(0.02f, 0.3f, 0.02f));
            marker.sharedMaterial = material;
            SetVisible(false);
            SetAvailable(false);
        }
        public void SetVisible(bool visible) => marker.enabled = visible;
        public void SetAvailable(bool available) => pickCollider.enabled = available;
        public void Highlight(bool value)
        {
            material.color = value ? new Color(0.6f, 1f, 0.1f) : IdleColor;
            material.SetColor("_EmissionColor", value
                ? new Color(0.15f, 0.55f, 0.02f) : new Color(0.02f, 0.3f, 0.02f));
        }
        private void OnDestroy() { if (material != null) Destroy(material); }
    }
}
