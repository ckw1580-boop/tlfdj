using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace ElectricalSim
{
    public sealed partial class TrainingSceneBootstrap
    {
        private void CreateTachometer()
        {
            var prefab = Resources.Load<TachometerView>("Tachometer");
            if (prefab == null)
            {
                Debug.LogError("Tachometer model is missing. Run Electrical Sim > Build Tachometer Model.");
                return;
            }
            var targets = new List<MotorSpeedTarget>();
            for (var i = 0; i < MotorBindingDefinition.All.Count; i++)
            {
                var binding = MotorBindingDefinition.All[i];
                var model = originalEnvironment != null
                    ? originalEnvironment.Find(binding.ModelPath) : null;
                if (model == null) continue;
                var marker = motorFaultBlocks.transform.Find("MotorFaultBlock_" + (i + 1));
                if (marker == null)
                {
                    Debug.LogWarning("Motor shaft marker is missing: " + binding.Nut);
                    continue;
                }
                var position = marker.position;
                // Legacy green cubes were only visual markers, not electrical terminals.
                marker.gameObject.SetActive(false);
                var target = CreateSpeedTarget(model, binding.Id, position, model.TransformDirection(Vector3.right));
                targets.Add(target);
                var discs = model.GetComponentsInChildren<Transform>(true)
                    .Where(t => t.name == "zhuanpan" || t.name == "zhuanpan (1)").ToArray();
                if (discs.Length > 0)
                {
                    var shaft = new GameObject("RotationAxis").transform;
                    shaft.SetParent(model, false);
                    shaft.position = discs[0].GetComponent<Renderer>().bounds.center;
                    shaft.rotation = target.transform.rotation;
                    model.gameObject.AddComponent<MotorRotorView>().Initialize(
                        deviceViews.Single(v => v.Runtime.DeviceId == binding.Id).Runtime, shaft, discs);
                }
            }
            if (originalEnvironment == null)
                foreach (var motor in deviceViews.Where(v => v.Runtime.Kind == ElectricalDeviceKind.Motor))
                    targets.Add(CreateSpeedTarget(motor.transform, motor.Runtime.DeviceId,
                        motor.transform.TransformPoint(new Vector3(0, 0, -0.45f)), -motor.transform.forward));

            var tachometer = gameObject.AddComponent<TachometerController>();
            tachometer.Initialize(Instantiate(prefab), targets);
            controller.RegisterTachometer(tachometer);
        }

        private static MotorSpeedTarget CreateSpeedTarget(Transform parent, string id, Vector3 position, Vector3 outward)
        {
            var point = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            point.name = "SpeedTarget_" + id;
            point.transform.position = position;
            point.transform.rotation = Quaternion.LookRotation(outward, Vector3.up);
            point.transform.localScale = Vector3.one * 0.028f;
            point.transform.SetParent(parent, true);
            var target = point.AddComponent<MotorSpeedTarget>();
            target.Initialize(id);
            return target;
        }
    }
}
