using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace ElectricalSim
{
    public sealed partial class TrainingSceneBootstrap
    {
        private readonly List<FrontBreakerView> frontBreakerViews = new List<FrontBreakerView>();
        public const string FrontBreakerStripPath = "Bench/ElectricBench/mesh/model/GameObject/DuanZiPai_group (5)";

        private void CreateFrontBreakers()
        {
            var strip = originalEnvironment.Find(FrontBreakerStripPath);
            var source = originalEnvironment.Find("Bench/ElectricBench/Nuts/122/KongQiKaiGuan_4PK");
            if (strip == null || source == null)
                throw new InvalidOperationException("正面断路器缺少参考模型或电脑旁端子排。");
            var modules = strip.Cast<Transform>().Where(t => t.name == "DuanZiPai" ||
                t.name.StartsWith("DuanZiPai (", StringComparison.Ordinal)).ToArray();
            if (modules.Length < 4) throw new InvalidOperationException("电脑旁端子排模块不完整。");
            var centers = modules.Select(t => FrontMeshBounds(t, originalEnvironment).center)
                .OrderByDescending(p => p.x).ToArray();
            var screenRight = (centers.Last() - centers.First()).normalized;
            var outward = Vector3.Cross(Vector3.up, screenRight).normalized;
            var layout = new GameObject("Front Independent Breakers").transform;
            layout.SetParent(originalEnvironment, false);
            layout.rotation = Quaternion.LookRotation(outward, Vector3.up);
            var stripBounds = FrontMeshBounds(strip, layout);

            // This strip is decorative; its modules and their wires are one subtree per terminal.
            // Keep the right-hand portion intact and use the original front mounting plane.
            var gap = 0.008f;
            var availableWidth = stripBounds.size.x * 0.66f;
            var maxHeight = 0.070f; // Between the original cable ducts at 1.495 m and 1.627 m.
            var removedEdge = stripBounds.max.x;
            for (var index = 0; index < 3; index++)
            {
                var id = "QFFRONT" + (index + 1);
                var root = new GameObject(id).transform;
                root.SetParent(layout, false);
                var device = root.gameObject.AddComponent<ElectricalDeviceView>();
                device.Initialize(ElectricalDeviceRuntime.CreateBreaker(id, FrontBreakerView.Contacts), "正面四极断路器 " + (index + 1));
                // Initialize the electrical view before attaching meshes: retain the original materials.
                var model = Instantiate(source, root, true);
                model.name = "KongQiKaiGuan_4PK";
                model.rotation = Quaternion.AngleAxis(180, Vector3.up) * source.rotation;
                foreach (var c in model.GetComponentsInChildren<Collider>(true)) c.enabled = false;
                var pointRoot = model.Find("point");
                if (pointRoot == null) throw new InvalidOperationException(id + " 缺少连接点。");
                foreach (var r in pointRoot.GetComponentsInChildren<Renderer>(true)) r.enabled = false;
                var body = FrontMeshBounds(model, layout, pointRoot);
                var scale = Mathf.Min(1f, maxHeight / body.size.y, (availableWidth - gap * 4) / (body.size.x * 3));
                model.localScale *= scale;
                body = FrontMeshBounds(model, layout, pointRoot);
                var x = stripBounds.max.x - gap - body.size.x * 0.5f - index * (body.size.x + gap);
                var target = new Vector3(x, stripBounds.center.y - 0.005f, stripBounds.min.z + body.extents.z);
                model.position += layout.TransformVector(target - body.center);
                removedEdge = x - body.size.x * 0.5f - gap;

                var handle = model.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name == "switch");
                var pivot = model.Find("RotateCenter");
                if (handle == null || pivot == null) throw new InvalidOperationException(id + " 缺少手柄或支点。");
                foreach (var filter in model.GetComponentsInChildren<MeshFilter>(true))
                {
                    var renderer = filter.GetComponent<Renderer>();
                    if (filter.transform.IsChildOf(pointRoot) || renderer == null || !renderer.enabled ||
                        filter.sharedMesh == null || filter.name == "picker") continue;
                    var collider = filter.gameObject.AddComponent<MeshCollider>();
                    collider.sharedMesh = filter.sharedMesh;
                }
                var picker = handle.GetComponentsInChildren<Collider>().FirstOrDefault(c => c.enabled);
                if (picker == null) throw new InvalidOperationException(id + " 缺少手柄碰撞体。");
                var animation = model.gameObject.AddComponent<CabinetBreakerInteractable>();
                animation.Initialize(id, "正面四极断路器 " + (index + 1), handle, pivot, picker, -45f, 0.2f, 0.25f);
                var view = root.gameObject.AddComponent<FrontBreakerView>();
                view.Initialize(device, animation);
                foreach (var terminal in FrontBreakerView.Terminals)
                {
                    var anchor = pointRoot.Find(terminal);
                    if (anchor == null) throw new InvalidOperationException(id + " 缺少端子 " + terminal);
                    var obj = CreatePrimitive(PrimitiveType.Sphere, "Port", root,
                        root.InverseTransformPoint(anchor.position), Vector3.one * 0.0085f * scale,
                        new Color(0.12f, 0.86f, 0.36f));
                    obj.GetComponent<SphereCollider>().radius = 0.9f;
                    var port = obj.AddComponent<ElectricalPortView>();
                    port.Initialize(id, terminal, new Color(0.12f, 0.86f, 0.36f));
                    port.ConfigureHover(id + "." + terminal, terminal);
                    port.ConfigureOriginalAnchors(anchor, anchor, anchor, anchor);
                    port.ConfigureElectricalOnly(); port.ConfigureWiringModeOnly();
                    device.AddPort(port);
                }
                deviceViews.Add(device); frontBreakerViews.Add(view);
            }
            foreach (var module in modules)
                if (FrontMeshBounds(module, layout).max.x > removedEdge) module.gameObject.SetActive(false);
            CacheOriginalEnvironmentTransforms();
        }

        internal static Bounds FrontMeshBounds(Transform root, Transform frame, Transform excluded = null)
        {
            var bounds = new Bounds();
            var initialized = false;
            foreach (var filter in root.GetComponentsInChildren<MeshFilter>())
            {
                if (excluded != null && (filter.transform == excluded || filter.transform.IsChildOf(excluded))) continue;
                var renderer = filter.GetComponent<Renderer>();
                if (filter.sharedMesh == null || renderer == null || !renderer.enabled || filter.name == "picker") continue;
                // Imported meshes can contain combined geometry. Renderer bounds describe
                // the visible instance, whereas sharedMesh.bounds can include other terminals.
                var box = renderer.bounds;
                for (var i = 0; i < 8; i++)
                {
                    var corner = box.center + Vector3.Scale(box.extents,
                        new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
                    var point = frame.InverseTransformPoint(corner);
                    if (!initialized) { bounds = new Bounds(point, Vector3.zero); initialized = true; }
                    else bounds.Encapsulate(point);
                }
            }
            if (!initialized) throw new InvalidOperationException("断路器布局网格缺失：" + root.name);
            return bounds;
        }
    }
}
