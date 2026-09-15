using System;
using System.Linq;
using UnityEngine;

namespace ElectricalSim
{
    public sealed partial class TrainingSceneBootstrap
    {
        private void CreateFaultPowerTerminalBlock(TrainingCameraController cameraController)
        {
            if (originalEnvironment == null) return;
            var source = originalEnvironmentTransforms.Single(item =>
                item.name == "DuanZiPai_5" && item.Find("point") != null);
            var sourceMesh = source.Find("mesh");
            var sourceLabel = originalEnvironment.Find("Terminal Annotation - Fault Buttons SB");
            var sourceAnchors = faultButtonTerminalAnchors.Values.ToArray();
            if (sourceMesh == null || sourceLabel == null || sourceAnchors.Length != 12)
                throw new InvalidOperationException("排故电源端子排缺少按钮端子排参考模型或连接点。");

            // Orient the strip's own axis left-to-right as seen from the fault camera.
            var cameraRight = Vector3.Cross(Vector3.up,
                cameraController.CurrentFaultTarget - cameraController.FaultPosition).normalized;
            var right = sourceMesh.TransformVector(Vector3.right).normalized;
            if (Vector3.Dot(right, cameraRight) < 0f) right = -right;
            var sortedAnchors = sourceAnchors.OrderBy(a => Vector3.Dot(a.position, right)).ToArray();
            var pitch = Vector3.Distance(sortedAnchors[0].position, sortedAnchors[11].position) / 11f;
            var modules = sourceMesh.Cast<Transform>()
                .Where(t => t.gameObject.activeInHierarchy && t.name.StartsWith("DuanZiPai", StringComparison.Ordinal))
                .OrderBy(t => Vector3.Dot(t.position, right)).ToArray();
            if (modules.Length != 12)
                throw new InvalidOperationException("按钮端子排应包含 12 个可见端子模块。");
            var template = modules[modules.Length / 2];
            var templateAnchor = sortedAnchors.OrderBy(a =>
                Mathf.Abs(Vector3.Dot(a.position - template.position, right))).First();

            var board = new GameObject(FaultPowerTerminalBlock.DeviceId).transform;
            board.SetParent(source.parent, false);
            board.localPosition = source.localPosition;
            board.localRotation = source.localRotation;
            board.localScale = source.localScale;
            var meshRoot = new GameObject("mesh").transform;
            meshRoot.SetParent(board, false);
            meshRoot.localPosition = sourceMesh.localPosition;
            meshRoot.localRotation = sourceMesh.localRotation;
            meshRoot.localScale = sourceMesh.localScale;
            var points = new GameObject("point").transform;
            points.SetParent(board, false);

            for (var index = 0; index < FaultPowerTerminalBlock.PortNames.Count; index++)
            {
                var portName = FaultPowerTerminalBlock.PortNames[index];
                var module = Instantiate(template.gameObject, meshRoot).transform;
                module.name = "Terminal " + portName;
                module.position = template.position + right * (pitch * index);
                foreach (var collider in module.GetComponentsInChildren<Collider>(true))
                {
                    collider.enabled = false;
                    Destroy(collider);
                }
                var anchor = new GameObject(portName).transform;
                anchor.SetParent(points, false);
                anchor.position = templateAnchor.position + right * (pitch * index);
            }

            var originalLeft = ProjectTerminalMeshes(sourceMesh, right).x;
            var newRight = ProjectTerminalMeshes(meshRoot, right).y;
            board.position += right * (originalLeft - pitch - newRight);

            var runtime = FaultPowerTerminalBlock.CreateRuntime();
            var block = board.gameObject.AddComponent<PowerTerminalBlockView>();
            block.Initialize(runtime, "AC 380V（线电压）／220V（相电压）；DC 24V",
                "三相第1组：U1、V1、W1、N1\n三相第2组：U2、V2、W2、N2\n" +
                "直流正极：24V+_1、24V+_2、24V+_3\n直流负极：24V-_1、24V-_2、24V-_3\n" +
                "共用电源；同相、同名零线、同极内部连通");
            // Keep the body target above the lower connection row. A full mesh AABB
            // includes empty space under the screws and can intercept oblique port rays.
            var picker = (BoxCollider)block.Picker;
            var min = picker.center - picker.size * 0.5f;
            var max = picker.center + picker.size * 0.5f;
            min.y = Mathf.Max(min.y, points.Cast<Transform>().Max(a => board.InverseTransformPoint(a.position).y)
                + 0.0075f / board.lossyScale.y);
            picker.center = (min + max) * 0.5f;
            picker.size = max - min;
            block.ConfigureFaultSide(cameraController);

            var portRoot = new GameObject(FaultPowerTerminalBlock.DeviceId + " Connection Points").transform;
            portRoot.SetParent(originalEnvironment, false);
            var view = portRoot.gameObject.AddComponent<ElectricalDeviceView>();
            view.Initialize(runtime, "排故电源端子区");
            foreach (var portName in FaultPowerTerminalBlock.PortNames)
            {
                var anchor = points.Find(portName);
                var color = new Color(0.12f, 0.86f, 0.36f);
                // This compact strip needs separate hit targets even from an oblique view.
                var markerSize = Mathf.Min(0.0075f, pitch * 0.6f);
                var marker = CreatePrimitive(PrimitiveType.Sphere, "Port", portRoot,
                    portRoot.InverseTransformPoint(anchor.position), Vector3.one * markerSize, color);
                marker.GetComponent<SphereCollider>().radius = 0.5f;
                var port = marker.AddComponent<ElectricalPortView>();
                port.Initialize(runtime.DeviceId, portName, color);
                port.ConfigureHover(portName, portName);
                port.ConfigureOriginalAnchors(null, null, anchor, null, false);
                port.ConfigureElectricalOnly();
                view.AddPort(port);
            }
            deviceViews.Add(view);

            var label = new GameObject("Terminal Annotation - Fault Power").transform;
            label.SetParent(originalEnvironment, false);
            var center = points.Cast<Transform>().Aggregate(Vector3.zero, (sum, a) => sum + a.position) / points.childCount;
            var sourceCenter = sourceAnchors.Aggregate(Vector3.zero, (sum, a) => sum + a.position) / sourceAnchors.Length;
            label.SetPositionAndRotation(sourceLabel.position + center - sourceCenter, sourceLabel.rotation);
            label.localScale = sourceLabel.localScale;
            var referenceText = sourceLabel.GetComponent<TextMesh>();
            var text = label.gameObject.AddComponent<TextMesh>();
            text.text = "电源端子区";
            text.font = referenceText.font;
            text.fontSize = referenceText.fontSize;
            text.fontStyle = referenceText.fontStyle;
            text.characterSize = referenceText.characterSize;
            text.anchor = referenceText.anchor;
            text.alignment = referenceText.alignment;
            text.color = referenceText.color;
            var renderer = label.GetComponent<MeshRenderer>();
            renderer.sharedMaterial = sourceLabel.GetComponent<MeshRenderer>().sharedMaterial;
            renderer.sortingOrder = 101;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
            renderer.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;
            label.gameObject.AddComponent<BackViewPersistentRendererVisibility>().Configure(new[] { renderer }, cameraController);
        }

        // Mesh corners give the actual width along a rotated strip, unlike world AABBs.
        private static Vector2 ProjectTerminalMeshes(Transform root, Vector3 axis)
        {
            var min = float.PositiveInfinity;
            var max = float.NegativeInfinity;
            foreach (var mesh in root.GetComponentsInChildren<MeshFilter>())
            {
                var renderer = mesh.GetComponent<Renderer>();
                if (mesh.sharedMesh == null || renderer == null || !renderer.enabled) continue;
                var bounds = mesh.sharedMesh.bounds;
                for (var x = -1; x <= 1; x += 2)
                for (var y = -1; y <= 1; y += 2)
                for (var z = -1; z <= 1; z += 2)
                {
                    var position = mesh.transform.TransformPoint(bounds.center + Vector3.Scale(bounds.extents, new Vector3(x, y, z)));
                    var value = Vector3.Dot(position, axis);
                    min = Mathf.Min(min, value);
                    max = Mathf.Max(max, value);
                }
            }
            if (float.IsInfinity(min)) throw new InvalidOperationException("端子排模型网格缺失。");
            return new Vector2(min, max);
        }
    }
}
