using System;
using System.Linq;
using UnityEngine;

namespace ElectricalSim
{
    public sealed class LiquidTankView : MonoBehaviour
    {
        private SimulationController controller;
        private Transform liquidRoot, space;
        private Vector3 originalScale, originalPosition;
        private float bottom, height;
        private Renderer[] liquidRenderers;
        private Renderer[][] streams;
        private Material liquidMaterial;
        private MaterialPropertyBlock properties;
        private double flowTime;
        public float BottomWorldY => space.TransformPoint(new Vector3(0, bottom, 0)).y;
        public float TopWorldY => space.TransformPoint(new Vector3(0, bottom + height, 0)).y;
        public void Initialize(Transform environment, SimulationController source)
        {
            controller = source;
            properties = new MaterialPropertyBlock();
            space = environment.Find(SceneIoCatalog.EnvironmentPath);
            // The imported scene encodes its empty tank by collapsing mesh Y to zero.
            // Restore the authored full-volume geometry before measuring its bounds.
            var waterMesh = space.Find("rivet/5/JiaoBanWater/mesh");
            if (waterMesh != null) waterMesh.localScale = Vector3.one;
            liquidRoot = space.Find("rivet/5/JiaoBanWater/mesh/Water");
            if (liquidRoot == null) throw new InvalidOperationException("混合罐液体网格缺失。");
            var bounds = SceneIoView.MeshBounds(liquidRoot, space);
            bottom = bounds.min.y; height = bounds.size.y;
            if (height < 0.01f) throw new InvalidOperationException("混合罐液体网格高度无效。");
            originalScale = liquidRoot.localScale;
            originalPosition = liquidRoot.localPosition;
            liquidRenderers = liquidRoot.GetComponentsInChildren<Renderer>(true);
            var shader = Resources.Load<Shader>("LiquidSurface");
            if (shader == null) throw new InvalidOperationException("液体显示着色器缺失。");
            liquidMaterial = new Material(shader) { name = "Mixed liquid (instance)" };
            foreach (var renderer in liquidRenderers)
                renderer.sharedMaterials = renderer.sharedMaterials.Select(_ => liquidMaterial).ToArray();
            // The source scene also contains an old static fill representation.
            var staticLiquid = space.Find("mesh/View/YeTiHunHe/yeti");
            if (staticLiquid != null)
                foreach (var renderer in staticLiquid.GetComponentsInChildren<Renderer>(true)) renderer.enabled = false;
            var paths = new[]
            {
                new[] { "rivet/6/JinLiaoWater1", "rivet/15/JinLiaoliudong_A" },
                new[] { "rivet/7/JinLiaoWater2", "rivet/16/JinLiaoliudong_B" },
                new[] { "rivet/8/PaiLiaoWater" }
            };
            streams = paths.Select(group => group.SelectMany(path =>
                (space.Find(path) ?? throw new InvalidOperationException("流动模型缺失：" + path)).GetComponentsInChildren<Renderer>(true)).ToArray()).ToArray();
            foreach (var path in paths.SelectMany(p => p))
            {
                var root = space.Find(path);
                foreach (var renderer in root.GetComponentsInChildren<Renderer>(true))
                    for (var node = renderer.transform; node != root.parent; node = node.parent)
                    {
                        node.gameObject.SetActive(true);
                        if (node.name == "RedWaterSuptter" && node.localScale.y == 0)
                            node.localScale = new Vector3(node.localScale.x, 1, node.localScale.z);
                    }
            }
            Refresh();
        }
        public float LevelAtWorldHeight(float worldY) => (worldY - BottomWorldY) / (TopWorldY - BottomWorldY);
        private void OnDestroy() { if (liquidMaterial != null) Destroy(liquidMaterial); }
        private void LateUpdate()
        {
            if (controller.Mode == SimulationMode.Simulate && !controller.IsFileOperationActive) flowTime += Time.deltaTime;
            Refresh();
        }
        public void Refresh()
        {
            if (liquidRoot == null || controller.Liquid == null || streams == null) return;
            var level = (float)controller.Liquid.Level;
            liquidRoot.localScale = new Vector3(originalScale.x, originalScale.y * Mathf.Max(level, 0.00001f), originalScale.z);
            liquidRoot.localPosition = originalPosition;
            // Scaling about the imported pivot is corrected to leave the bottom fixed.
            var currentBottom = SceneIoView.MeshBounds(liquidRoot, space).min.y;
            liquidRoot.position += space.TransformVector(Vector3.up * (bottom - currentBottom));
            foreach (var renderer in liquidRenderers) renderer.enabled = level > 0.00001f;
            var flows = new[] { controller.Liquid.Pump1Flow, controller.Liquid.Pump2Flow, controller.Liquid.DrainFlow };
            for (var i = 0; i < streams.Length; i++)
                for (var j = 0; j < streams[i].Length; j++)
                {
                    var renderer = streams[i][j];
                    renderer.enabled = flows[i] > 1e-9 && controller.Mode == SimulationMode.Simulate && !controller.IsFileOperationActive;
                    if (!renderer.enabled) continue;
                    renderer.GetPropertyBlock(properties);
                    var phase = (float)(flowTime * flows[i] * 30 - j * 0.17);
                    properties.SetColor("_Color", Color.Lerp(new Color(0.08f, 0.45f, 0.65f), new Color(0.4f, 1, 1), 0.5f + 0.5f * Mathf.Sin(phase * Mathf.PI * 2)));
                    renderer.SetPropertyBlock(properties);
                }
        }
    }
}
