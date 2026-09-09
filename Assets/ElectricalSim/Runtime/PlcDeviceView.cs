using System;
using UnityEngine;

namespace ElectricalSim
{
    public sealed class PlcDeviceView : MonoBehaviour
    {
        public PlcDeviceRuntime Runtime { get; private set; }
        public Collider Picker { get; private set; }
        public void Initialize(PlcDeviceRuntime runtime)
        {
            Runtime = runtime;
            var bounds = new Bounds(); var first = true;
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
                    if (first) { bounds = new Bounds(p, Vector3.zero); first = false; } else bounds.Encapsulate(p);
                }
            }
            if (first) throw new InvalidOperationException("PLC 模型网格缺失：" + runtime.DeviceId);
            var box = gameObject.AddComponent<BoxCollider>(); box.center = bounds.center; box.size = bounds.size;
            Picker = box;
        }
    }
}
