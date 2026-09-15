using UnityEngine;

namespace ElectricalSim
{
    public sealed class PowerTerminalBlockView : MonoBehaviour
    {
        public ElectricalDeviceRuntime Runtime { get; private set; }
        public Collider Picker { get; private set; }
        private string ratedVoltage;
        private string terminalDescription;
        private TrainingCameraController faultCamera;

        public void Initialize(ElectricalDeviceRuntime runtime,
            string voltage = "DC 24V",
            string terminals = "24V+：1、2、3、4（正极）\n24V-：1、2、3、4（负极）\n同极端子内部连通")
        {
            Runtime = runtime;
            ratedVoltage = voltage;
            terminalDescription = terminals;
            var points = transform.Find("point");
            var bounds = new Bounds();
            var first = true;
            foreach (var mesh in GetComponentsInChildren<MeshFilter>())
            {
                if (mesh.sharedMesh == null || points != null && mesh.transform.IsChildOf(points)) continue;
                var renderer = mesh.GetComponent<Renderer>();
                if (renderer == null || !renderer.enabled) continue;
                var b = mesh.sharedMesh.bounds;
                for (var x = -1; x <= 1; x += 2)
                for (var y = -1; y <= 1; y += 2)
                for (var z = -1; z <= 1; z += 2)
                {
                    var p = transform.InverseTransformPoint(mesh.transform.TransformPoint(
                        b.center + Vector3.Scale(b.extents, new Vector3(x, y, z))));
                    if (first) { bounds = new Bounds(p, Vector3.zero); first = false; }
                    else bounds.Encapsulate(p);
                }
            }
            if (first) throw new System.InvalidOperationException("电源端子区模型网格缺失");
            var box = gameObject.AddComponent<BoxCollider>();
            box.center = bounds.center;
            box.size = bounds.size;
            Picker = box;
        }

        public string Describe(bool powered)
            => "电源端子区 · 属性\n额定电压：" + ratedVoltage + "\n状态：" +
               (powered ? "供电中" : "未供电") + "\n" + terminalDescription + "\n连接点：端子排下方";

        public void ConfigureFaultSide(TrainingCameraController cameraController)
        {
            faultCamera = cameraController;
            LateUpdate();
        }

        private void LateUpdate()
        {
            if (faultCamera != null && Picker != null)
                Picker.enabled = faultCamera.IsViewingFaultSide;
        }
    }
}
