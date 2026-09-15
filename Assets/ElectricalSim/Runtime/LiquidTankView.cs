using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.Rendering;

namespace ElectricalSim
{
    public sealed class LiquidTankView : MonoBehaviour
    {
        public static readonly Color LiquidAColor = new Color(0.39f, 0.45f, 0.54f, 0.94f);
        public static readonly Color LiquidBColor = new Color(0.53f, 0.34f, 0.35f, 0.94f);
        public static readonly Color MixedColor = new Color(0.49f, 0.38f, 0.53f, 0.94f);
        private SimulationController controller;
        private Transform liquidRoot, space;
        private Vector3 originalScale, originalPosition;
        private float bottom, height, receiverY;
        private Renderer[] liquidRenderers;
        private readonly List<Material> materials = new List<Material>();
        private readonly List<Material> surfaceMaterials = new List<Material>();
        private readonly List<LiquidPipeView> pipes = new List<LiquidPipeView>();
        private float flowTime;
        private Material tankMaterial;
        public IReadOnlyList<LiquidPipeView> Pipes => pipes;
        public float BottomWorldY => space.TransformPoint(new Vector3(0, bottom, 0)).y;
        public float TopWorldY => space.TransformPoint(new Vector3(0, bottom + height, 0)).y;

        public void Initialize(Transform environment, SimulationController source)
        {
            controller = source;
            space = environment.Find(SceneIoCatalog.EnvironmentPath);
            var waterMesh = Require("rivet/5/JiaoBanWater/mesh");
            waterMesh.localScale = Vector3.one;
            liquidRoot = Require("rivet/5/JiaoBanWater/mesh/Water");
            var bounds = SceneIoView.MeshBounds(liquidRoot, space);
            bottom = bounds.min.y; height = bounds.size.y;
            if (height < 0.01f) throw new InvalidOperationException("混合罐液体网格高度无效。");
            originalScale = liquidRoot.localScale;
            originalPosition = liquidRoot.localPosition;
            liquidRenderers = liquidRoot.GetComponentsInChildren<Renderer>(true);
            var a = CreateSurfaceMaterial("Unmixed A", LiquidAColor);
            var b = CreateSurfaceMaterial("Unmixed B", LiquidBColor);
            var mixed = CreateSurfaceMaterial("Mixed liquid", MixedColor);
            var tankColor = MixedColor; tankColor.a = 0.78f;
            tankMaterial = CreateSurfaceMaterial("Mixing tank liquid", tankColor);
            Assign(liquidRoot, tankMaterial);

            const string view = "mesh/View/YeTiHunHe/";
            // Only the obsolete cylindrical fill layers are hidden. The three
            // rectangular basins retain their authored surface geometry and level.
            foreach (var path in new[] { "ORANGE02", "RED02", "YELLOW02", "yeti" }) Hide(Require(view + path));
            Assign(Require(view + "YELLOW01"), a);
            Assign(Require(view + "RED01"), b);
            var receiver = Require(view + "xiang01 (1)/RED01 (1)");
            Assign(receiver, mixed);
            receiverY = SceneIoView.MeshBounds(receiver, space).max.y;

            foreach (var path in new[] { "rivet/6/JinLiaoWater1", "rivet/7/JinLiaoWater2", "rivet/8/PaiLiaoWater",
                "rivet/15/JinLiaoliudong_A", "rivet/16/JinLiaoliudong_B", "rivet/17/ShuiLiu_1", "rivet/18/ShuiLiu_2",
                "rivet/19/ShuiLiu_3", "rivet/20/ShuiLiu_4", view + "guangdao" }) Hide(Require(path));
            var wallMaterial = new Material(Resources.Load<Shader>("TransparentPipe")) { name = "Clear pipe wall" };
            materials.Add(wallMaterial);
            foreach (var renderer in Require(view + "guan").GetComponentsInChildren<Renderer>(true))
            {
                var slots = renderer.sharedMaterials;
                // Retain the secondary coupling material on polySurface2007.
                slots[0] = wallMaterial;
                renderer.sharedMaterials = slots;
                renderer.enabled = true;
                renderer.shadowCastingMode = ShadowCastingMode.Off;
            }
            var data = Resources.Load<TextAsset>("LiquidPipeRoutes");
            if (data == null) throw new InvalidOperationException("管路中心线数据缺失。");
            var routes = JsonUtility.FromJson<LiquidPipeRoutes>(data.text).routes;
            controller.Liquid.InitializeTransport(routes, bottom, height, receiverY);
            var colors = new[] { LiquidAColor, LiquidBColor, MixedColor };
            for (var i = 0; i < routes.Length; i++)
            {
                var root = new GameObject(routes[i].name + " pipe animation");
                root.transform.SetParent(space, false);
                var pipe = root.AddComponent<LiquidPipeView>();
                pipe.Initialize(routes[i], colors[i], controller.Liquid.Streams[i]);
                pipes.Add(pipe);
            }
            Refresh();
        }
        private Transform Require(string path) => space.Find(path) ?? throw new InvalidOperationException("液体模型缺失：" + path);
        private Material CreateSurfaceMaterial(string name, Color color)
        {
            var shader = Resources.Load<Shader>("LiquidSurface");
            if (shader == null) throw new InvalidOperationException("液体显示着色器缺失。");
            var material = new Material(shader) { name = name };
            material.SetColor("_Color", color);
            material.SetTexture("_BumpMap", Resources.Load<Texture2D>("WaterRippleNormal"));
            materials.Add(material); surfaceMaterials.Add(material);
            return material;
        }
        private static void Hide(Transform root)
        {
            foreach (var renderer in root.GetComponentsInChildren<Renderer>(true)) renderer.enabled = false;
        }
        private void Assign(Transform root, Material material)
        {
            foreach (var renderer in root.GetComponentsInChildren<Renderer>(true))
            {
                for (var node = renderer.transform; node != space; node = node.parent) node.gameObject.SetActive(true);
                renderer.enabled = true;
                renderer.sharedMaterials = renderer.sharedMaterials.Select(_ => material).ToArray();
                renderer.shadowCastingMode = ShadowCastingMode.Off;
            }
        }
        public float LevelAtWorldHeight(float worldY) => (worldY - BottomWorldY) / (TopWorldY - BottomWorldY);
        private void OnDestroy()
        {
            foreach (var pipe in pipes) if (pipe != null) Destroy(pipe.gameObject);
            foreach (var material in materials) if (material != null) Destroy(material);
        }
        private void LateUpdate()
        {
            if (controller == null) return;
            AdvanceVisuals(Time.deltaTime);
        }
        // Surface ripples use a presentation clock. Transport and mixing are advanced
        // exclusively by SimulationController, including while rendering is disabled.
        public void AdvanceVisuals(float seconds)
        {
            if (controller.IsFileOperationActive) return;
            Refresh();
            flowTime += Mathf.Max(0, seconds);
            foreach (var material in surfaceMaterials) material.SetFloat("_FlowTime", flowTime);
        }
        public void ResetVisuals()
        {
            flowTime = 0;
            Refresh();
            foreach (var material in surfaceMaterials) material.SetFloat("_FlowTime", 0);
        }
        public void Refresh()
        {
            if (liquidRoot == null || controller.Liquid == null) return;
            var level = (float)controller.Liquid.Level;
            var color = controller.Liquid.CurrentColor; color.a = 0.78f;
            tankMaterial.SetColor("_Color", color);
            for (var i = 0; i < pipes.Count; i++)
            {
                if (i == 2) pipes[i].SetColor(controller.Liquid.DischargeColor);
                pipes[i].Refresh(i < 2 ? bottom + height * level : receiverY);
            }
            liquidRoot.localScale = new Vector3(originalScale.x, originalScale.y * Mathf.Max(level, 0.00001f), originalScale.z);
            liquidRoot.localPosition = originalPosition;
            // Correct scaling about the imported pivot so the bottom remains fixed.
            var currentBottom = SceneIoView.MeshBounds(liquidRoot, space).min.y;
            liquidRoot.position += space.TransformVector(Vector3.up * (bottom - currentBottom));
            foreach (var renderer in liquidRenderers) renderer.enabled = level > 0.00001f;
        }
    }
}
