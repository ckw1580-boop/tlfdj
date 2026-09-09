using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace ElectricalSim
{
    public sealed class PanelDeviceView : MonoBehaviour
    {
        public ElectricalDeviceRuntime Runtime { get; private set; }
        public PanelDeviceDefinition Definition => Runtime.PanelDefinition;
        public Transform[] MovingParts { get; private set; }
        public Collider Picker { get; private set; }
        public float AnimationAmount { get; private set; }
        private Vector3[] restPositions;
        private Quaternion[] restRotations;
        private Material[] lampMaterials = Array.Empty<Material>();
        private float resetTurn;
        private bool previousPressed;

        public static Transform FindPart(Transform root, string path)
        {
            foreach (var segment in path.Split('/'))
            {
                root = root.Cast<Transform>().FirstOrDefault(t => string.Equals(t.name, segment, StringComparison.OrdinalIgnoreCase));
                if (root == null) return null;
            }
            return root;
        }

        public void Initialize(ElectricalDeviceRuntime runtime)
        {
            enabled = false;
            Runtime = runtime;
            MovingParts = Definition.MovingParts.Select(p => FindPart(transform, p)).ToArray();
            if (MovingParts.Any(p => p == null)) throw new InvalidOperationException("面板可动部件缺失：" + Definition.Id);
            restPositions = MovingParts.Select(t => t.localPosition).ToArray();
            restRotations = MovingParts.Select(t => t.localRotation).ToArray();
            // Hit the original model, with a fixed local collider independent of the animated cap.
            var picker = transform.Find("picker");
            Picker = picker != null ? picker.GetComponent<Collider>() : null;
            if (Picker == null)
            {
                var bounds = new Bounds(Vector3.zero, Vector3.zero);
                var first = true;
                foreach (var filter in GetComponentsInChildren<MeshFilter>(true))
                {
                    var terminals = transform.Find("point");
                    if (filter.sharedMesh == null || terminals != null && filter.transform.IsChildOf(terminals)) continue;
                    var b = filter.sharedMesh.bounds;
                    for (var x = -1; x <= 1; x += 2)
                    for (var y = -1; y <= 1; y += 2)
                    for (var z = -1; z <= 1; z += 2)
                    {
                        var point = transform.InverseTransformPoint(filter.transform.TransformPoint(b.center + Vector3.Scale(b.extents, new Vector3(x, y, z))));
                        if (first) { bounds = new Bounds(point, Vector3.zero); first = false; } else bounds.Encapsulate(point);
                    }
                }
                var box = gameObject.AddComponent<BoxCollider>();
                box.center = bounds.center;
                box.size = Vector3.Max(bounds.size, Vector3.one * 0.01f);
                Picker = box;
            }
            Picker.gameObject.SetActive(true);
            Picker.enabled = true;
            if (Definition.Control == PanelControlKind.Indicator)
            {
                lampMaterials = MovingParts.SelectMany(t => t.GetComponentsInChildren<Renderer>()).SelectMany(r => r.materials).ToArray();
                foreach (var material in lampMaterials) material.EnableKeyword("_EMISSION");
            }
            ApplyPose(0f);
            enabled = true;
        }

        private void Update() => AdvanceAnimation(Time.unscaledDeltaTime);
        public void AdvanceAnimation(float seconds)
        {
            var lamp = Definition.Control == PanelControlKind.Indicator;
            var target = (lamp ? Runtime.IsActive : Runtime.IsPressed) ? 1f : 0f;
            var turn = Definition.Control == PanelControlKind.Selector || Definition.Control == PanelControlKind.Key;
            if (Definition.Control == PanelControlKind.EmergencyStop && previousPressed && !Runtime.IsPressed) resetTurn = 1f;
            previousPressed = Runtime.IsPressed;
            resetTurn = Mathf.MoveTowards(resetTurn, 0, seconds / Definition.TurnSeconds);
            AnimationAmount = Mathf.MoveTowards(AnimationAmount, target, seconds / (lamp ? Definition.LampSeconds : turn ? Definition.TurnSeconds : Definition.PressSeconds));
            ApplyPose(AnimationAmount);
        }

        private void ApplyPose(float amount)
        {
            if (Definition.Control == PanelControlKind.Indicator)
            {
                foreach (var material in lampMaterials)
                {
                    if (material.HasProperty("_Color")) material.color = Definition.Color * Mathf.Lerp(0.18f, 1f, amount);
                    if (material.HasProperty("_EmissionColor")) material.SetColor("_EmissionColor", Definition.Color * amount * 1.5f);
                }
                return;
            }
            var turn = Definition.Control == PanelControlKind.Selector || Definition.Control == PanelControlKind.Key;
            for (var i = 0; i < MovingParts.Length; i++)
            {
                var part = MovingParts[i];
                var localAxis = part.parent.InverseTransformDirection(transform.forward).normalized;
                part.localPosition = restPositions[i] + (turn ? Vector3.zero : part.parent.InverseTransformVector(-transform.forward * Definition.TravelMetres * amount));
                part.localRotation = Quaternion.AngleAxis(turn ? amount * Definition.TurnDegrees : resetTurn * 45f, localAxis) * restRotations[i];
            }
        }

        private void OnDestroy()
        {
            foreach (var material in lampMaterials) if (material != null) Destroy(material);
        }
    }
}
