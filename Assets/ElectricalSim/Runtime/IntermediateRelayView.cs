using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using UnityEngine;

namespace ElectricalSim
{
    public sealed class IntermediateRelayView : MonoBehaviour
    {
        public ElectricalDeviceRuntime Runtime { get; private set; }
        public IntermediateRelayDefinition Definition { get; private set; }
        public IReadOnlyDictionary<string, ElectricalPortView> Bindings { get; private set; }
        public Collider Picker { get; private set; }

        public void Initialize(IntermediateRelayDefinition definition, ElectricalDeviceRuntime runtime,
            IDictionary<string, ElectricalPortView> bindings)
        {
            Definition = definition;
            Runtime = runtime;
            Bindings = new ReadOnlyDictionary<string, ElectricalPortView>(new Dictionary<string, ElectricalPortView>(bindings));
            // The imported picker follows the relay housing. A box around all mesh bounds
            // also covers the socket and overlaps the neighboring relay's click area.
            Picker = transform.Find("picker")?.GetComponent<Collider>();
            if (Picker != null)
            {
                Picker.gameObject.SetActive(true);
                Picker.enabled = true;
                return;
            }
            var bounds = new Bounds();
            var first = true;
            var terminals = transform.Find("point");
            foreach (var mesh in GetComponentsInChildren<MeshFilter>(true))
            {
                if (mesh.sharedMesh == null || terminals != null && mesh.transform.IsChildOf(terminals)) continue;
                var b = mesh.sharedMesh.bounds;
                for (var x = -1; x <= 1; x += 2)
                for (var y = -1; y <= 1; y += 2)
                for (var z = -1; z <= 1; z += 2)
                {
                    var p = transform.InverseTransformPoint(mesh.transform.TransformPoint(b.center + Vector3.Scale(b.extents, new Vector3(x, y, z))));
                    if (first) { bounds = new Bounds(p, Vector3.zero); first = false; }
                    else bounds.Encapsulate(p);
                }
            }
            if (first) throw new InvalidOperationException("中间继电器模型网格缺失：" + definition.Id);
            var box = gameObject.AddComponent<BoxCollider>();
            box.center = bounds.center;
            box.size = bounds.size;
            Picker = box;
        }
    }
}
