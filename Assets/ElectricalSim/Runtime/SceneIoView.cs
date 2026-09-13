using System;
using System.Linq;
using UnityEngine;

namespace ElectricalSim
{
    public sealed class SceneIoView : MonoBehaviour
    {
        private SimulationController controller;
        private Renderer[] lights = Array.Empty<Renderer>();
        private Transform openLight, closeLight;
        private TextMesh pumpLabel;
        private MaterialPropertyBlock lightProperties;
        public string Id { get; private set; }
        public Collider Picker { get; private set; }
        public void Initialize(SimulationController source, string id, Font font)
        {
            controller = source; Id = id;
            lightProperties = new MaterialPropertyBlock();
            Picker = transform.Find("picker")?.GetComponent<Collider>();
            if (Picker == null)
            {
                var bounds = MeshBounds(transform, transform);
                var box = gameObject.AddComponent<BoxCollider>();
                box.center = bounds.center; box.size = bounds.size;
                Picker = box;
            }
            lights = GetComponentsInChildren<Renderer>(true).Where(r => r.name == "Deng1").ToArray();
            openLight = transform.Find("mesh/OpenLight1");
            closeLight = transform.Find("mesh/CloseLight1");
            if (id.StartsWith("泵", StringComparison.Ordinal) && name == "BengSensor")
            {
                var bounds = MeshBounds(transform, transform);
                var label = new GameObject("Pump speed label");
                label.transform.SetParent(transform, false);
                label.transform.localPosition = new Vector3(bounds.center.x, bounds.max.y + 0.04f, bounds.center.z);
                pumpLabel = label.AddComponent<TextMesh>();
                pumpLabel.font = font;
                label.GetComponent<MeshRenderer>().sharedMaterial = pumpLabel.font.material;
                pumpLabel.fontSize = 32; pumpLabel.characterSize = 0.012f;
                pumpLabel.anchor = TextAnchor.LowerCenter; pumpLabel.color = new Color(0.2f, 1, 0.8f);
            }
        }
        private void LateUpdate()
        {
            if (controller.SceneIoDevices.TryGetValue(Id, out var runtime))
            {
                if (openLight != null) openLight.gameObject.SetActive(runtime.IsActive);
                if (closeLight != null) closeLight.gameObject.SetActive(!runtime.IsActive);
                if (closeLight != null && !runtime.IsActive)
                    foreach (var renderer in closeLight.GetComponentsInChildren<Renderer>())
                    {
                        if (runtime.Powered) { renderer.SetPropertyBlock(null); continue; }
                        renderer.GetPropertyBlock(lightProperties);
                        lightProperties.SetColor("_Color", new Color(0.2f, 0.2f, 0.2f));
                        renderer.SetPropertyBlock(lightProperties);
                    }
                foreach (var light in lights)
                {
                    light.GetPropertyBlock(lightProperties);
                    var color = runtime.IsActive ? new Color(0.2f, 1f, 0.3f) : new Color(0.3f, 0.025f, 0.015f);
                    lightProperties.SetColor("_Color", color);
                    lightProperties.SetColor("_BaseColor", color);
                    light.SetPropertyBlock(lightProperties);
                }
            }
            if (pumpLabel != null)
            {
                var binding = SceneIoCatalog.Pumps.Single(p => p.Name == Id);
                var rpm = ((ElectricalDeviceRuntime)controller.Graph.Devices[binding.MotorId]).ActualSpeedRpm;
                pumpLabel.text = Id + "  " + rpm.ToString("F0") + " rpm";
                if (Camera.main != null) pumpLabel.transform.rotation = Camera.main.transform.rotation;
            }
        }
        internal static Bounds MeshBounds(Transform root, Transform relativeTo)
        {
            var first = true; var bounds = new Bounds();
            foreach (var mesh in root.GetComponentsInChildren<MeshFilter>(true))
            {
                if (mesh.sharedMesh == null || mesh.transform == root.Find("picker") ||
                    root.Find("point") != null && mesh.transform.IsChildOf(root.Find("point"))) continue;
                var b = mesh.sharedMesh.bounds;
                for (var x = -1; x <= 1; x += 2)
                for (var y = -1; y <= 1; y += 2)
                for (var z = -1; z <= 1; z += 2)
                {
                    var p = relativeTo.InverseTransformPoint(mesh.transform.TransformPoint(b.center + Vector3.Scale(b.extents, new Vector3(x, y, z))));
                    if (first) { bounds = new Bounds(p, Vector3.zero); first = false; } else bounds.Encapsulate(p);
                }
            }
            if (first) throw new InvalidOperationException("器件网格缺失：" + root.name);
            return bounds;
        }
    }
}
