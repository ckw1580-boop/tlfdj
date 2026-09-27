using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace ElectricalSim
{
    public sealed partial class TrainingSceneBootstrap
    {
        private void AddCabinetBranding(GameObject root, string cabinetObjectName = null)
        {
            var logoTexture = ResolveCabinetBrandLogo();
            if (root == null || logoTexture == null) return;

            var target = string.IsNullOrWhiteSpace(cabinetObjectName)
                ? root.transform
                : root.GetComponentsInChildren<Transform>(true).FirstOrDefault(item =>
                    string.Equals(item.name, cabinetObjectName, StringComparison.OrdinalIgnoreCase));
            if (target == null) return;

            var meshFilter = target.GetComponent<MeshFilter>();
            if (meshFilter == null || meshFilter.sharedMesh == null) return;
            var cabinetRenderer = target.GetComponent<MeshRenderer>();
            if (cabinetRenderer == null) return;

            var bounds = meshFilter.sharedMesh.bounds;
            if (string.Equals(target.name, "DQG01", StringComparison.OrdinalIgnoreCase))
            {
                RemoveAddedCabinetLogo(cabinetRenderer, meshFilter.sharedMesh.subMeshCount);
                return;
            }

            var logoAspect = logoTexture.width / (float)Mathf.Max(1, logoTexture.height);
            var logoWidth = bounds.size.x * 0.265f;
            var logoHeight = logoWidth / Mathf.Max(0.01f, logoAspect);
            var sideMargin = bounds.size.x * 0.035f;
            var topMargin = bounds.size.y * 0.035f;
            var centerY = bounds.max.y - topMargin - logoHeight * 0.5f;
            var surfaceOffset = Mathf.Max(0.012f, bounds.size.z * 0.02f);

            var frontX = bounds.min.x + sideMargin + logoWidth * 0.5f;
            var backX = bounds.max.x - sideMargin - logoWidth * 0.5f;

            ApplyCabinetBrandingToMesh(
                meshFilter,
                cabinetRenderer,
                logoTexture,
                bounds,
                frontX,
                backX,
                centerY,
                logoWidth,
                logoHeight,
                surfaceOffset);

            CreateCabinetBrandPanel(
                "Cabinet WCK Logo Front",
                target,
                new Vector3(frontX, centerY, bounds.max.z + surfaceOffset * 1.5f),
                Quaternion.identity,
                logoTexture,
                logoWidth,
                logoHeight);
            CreateCabinetBrandPanel(
                "Cabinet WCK Logo Back",
                target,
                new Vector3(backX, centerY, bounds.min.z - surfaceOffset * 1.5f),
                Quaternion.Euler(0f, 180f, 0f),
                logoTexture,
                logoWidth,
                logoHeight);
        }

        private void RefreshCabinetBranding()
        {
            var cabinetMeshes = FindObjectsOfType<MeshFilter>(true)
                .Where(item => string.Equals(item.name, "DQG01", StringComparison.OrdinalIgnoreCase))
                .ToArray();
            foreach (var cabinetMesh in cabinetMeshes)
            {
                if (cabinetMesh.GetComponent<MeshRenderer>() != null)
                    AddCabinetBranding(cabinetMesh.gameObject);
            }
        }

        private static void RemoveAddedCabinetLogo(
            MeshRenderer cabinetRenderer,
            int originalSubMeshCount)
        {
            var materials = new List<Material>(cabinetRenderer.materials.Take(originalSubMeshCount));
            RemoveEmbeddedCabinetLogo(materials);
            cabinetRenderer.materials = materials.ToArray();
        }

        private static void RemoveEmbeddedCabinetLogo(IEnumerable<Material> materials)
        {
            foreach (var material in materials)
            {
                if (material == null || material.mainTexture == null ||
                    material.name.IndexOf("bq", StringComparison.OrdinalIgnoreCase) < 0)
                    continue;

                var source = material.mainTexture;
                var temporary = RenderTexture.GetTemporary(
                    source.width,
                    source.height,
                    0,
                    RenderTextureFormat.ARGB32,
                    RenderTextureReadWrite.sRGB);
                var previous = RenderTexture.active;

                try
                {
                    Graphics.Blit(source, temporary);
                    RenderTexture.active = temporary;
                    var cleaned = new Texture2D(source.width, source.height, TextureFormat.RGBA32, false)
                    {
                        name = source.name + " (Logo Removed)",
                        filterMode = source.filterMode,
                        wrapMode = source.wrapMode
                    };
                    cleaned.ReadPixels(new Rect(0f, 0f, source.width, source.height), 0, 0, false);

                    // The original 同立方/CUBE SPACE mark occupies only the upper-left
                    // portion of bq_0.png. Unity pixel coordinates start at the bottom.
                    var clearX = Mathf.RoundToInt(source.width * 0.015f);
                    var clearY = Mathf.RoundToInt(source.height * 0.915f);
                    // Stop before the title begins at roughly 15.7% of the texture
                    // width. The previous 16% mask clipped the upper-left corner of
                    // the first “电” glyph on both cabinet faces.
                    var clearWidth = Mathf.RoundToInt(source.width * 0.135f);
                    var clearHeight = Mathf.RoundToInt(source.height * 0.075f);
                    cleaned.SetPixels(
                        clearX,
                        clearY,
                        clearWidth,
                        clearHeight,
                        Enumerable.Repeat(Color.clear, clearWidth * clearHeight).ToArray());
                    cleaned.Apply(false, false);
                    material.mainTexture = cleaned;
                }
                finally
                {
                    RenderTexture.active = previous;
                    RenderTexture.ReleaseTemporary(temporary);
                }
            }
        }

        private static void CreateCabinetBrandPanel(
            string name,
            Transform parent,
            Vector3 localPosition,
            Quaternion localRotation,
            Texture2D logoTexture,
            float logoWidth,
            float logoHeight)
        {
            var panel = GameObject.CreatePrimitive(PrimitiveType.Cube);
            panel.name = name;
            panel.transform.SetParent(parent, false);
            panel.transform.localPosition = localPosition;
            panel.transform.localRotation = localRotation;
            panel.transform.localScale = new Vector3(logoWidth * 1.22f, logoHeight * 1.68f, 0.002f);
            var collider = panel.GetComponent<Collider>();
            if (collider != null) Destroy(collider);

            var logoShader = Shader.Find("Standard") ?? Shader.Find("Legacy Shaders/Diffuse");
            var logoMaterial = new Material(logoShader) { name = name + " Material" };
            logoMaterial.mainTexture = logoTexture;
            logoMaterial.color = Color.white;
            if (logoMaterial.HasProperty("_Metallic")) logoMaterial.SetFloat("_Metallic", 0.05f);
            if (logoMaterial.HasProperty("_Glossiness")) logoMaterial.SetFloat("_Glossiness", 0.28f);
            panel.GetComponent<MeshRenderer>().material = logoMaterial;
        }

        private static void ApplyCabinetBrandingToMesh(
            MeshFilter meshFilter,
            MeshRenderer cabinetRenderer,
            Texture2D logoTexture,
            Bounds bounds,
            float frontX,
            float backX,
            float centerY,
            float logoWidth,
            float logoHeight,
            float surfaceOffset)
        {
            var sourceMesh = meshFilter.sharedMesh;
            var brandedMesh = Instantiate(sourceMesh);
            brandedMesh.name = sourceMesh.name + " WCK Branded";

            var vertices = new List<Vector3>(sourceMesh.vertices);
            var normals = new List<Vector3>(sourceMesh.normals);
            var tangents = new List<Vector4>(sourceMesh.tangents);
            var uv0 = new List<Vector2>(sourceMesh.uv);
            var uv1 = new List<Vector2>(sourceMesh.uv2);
            var backingTriangles = new List<int>();
            var logoTriangles = new List<int>();

            var backingWidth = logoWidth * 1.18f;
            var backingHeight = logoHeight * 1.58f;
            var backingOffset = surfaceOffset;
            var logoOffset = surfaceOffset + Mathf.Max(0.002f, surfaceOffset * 0.25f);

            // The cabinet's local -X is screen-left on the front, and local +X is
            // screen-left on the back. Supplying the vertices in visual left-to-right
            // order keeps WCK readable on both faces.
            AppendBrandQuad(
                vertices, normals, tangents, uv0, uv1, backingTriangles,
                frontX - backingWidth * 0.5f, frontX + backingWidth * 0.5f,
                centerY, backingHeight, bounds.max.z + backingOffset, Vector3.forward,
                false);
            AppendBrandQuad(
                vertices, normals, tangents, uv0, uv1, backingTriangles,
                backX + backingWidth * 0.5f, backX - backingWidth * 0.5f,
                centerY, backingHeight, bounds.min.z - backingOffset, Vector3.back,
                false);
            AppendBrandQuad(
                vertices, normals, tangents, uv0, uv1, logoTriangles,
                frontX - logoWidth * 0.5f, frontX + logoWidth * 0.5f,
                centerY, logoHeight, bounds.max.z + logoOffset, Vector3.forward,
                true);
            AppendBrandQuad(
                vertices, normals, tangents, uv0, uv1, logoTriangles,
                backX + logoWidth * 0.5f, backX - logoWidth * 0.5f,
                centerY, logoHeight, bounds.min.z - logoOffset, Vector3.back,
                true);

            brandedMesh.SetVertices(vertices);
            if (normals.Count == vertices.Count) brandedMesh.SetNormals(normals);
            if (tangents.Count == vertices.Count) brandedMesh.SetTangents(tangents);
            if (uv0.Count == vertices.Count) brandedMesh.SetUVs(0, uv0);
            if (uv1.Count == vertices.Count) brandedMesh.SetUVs(1, uv1);

            var originalSubMeshCount = sourceMesh.subMeshCount;
            brandedMesh.subMeshCount = originalSubMeshCount + 2;
            brandedMesh.SetTriangles(backingTriangles, originalSubMeshCount, false);
            brandedMesh.SetTriangles(logoTriangles, originalSubMeshCount + 1, false);
            brandedMesh.RecalculateBounds();
            meshFilter.sharedMesh = brandedMesh;

            var backingShader = Shader.Find("Standard") ?? Shader.Find("Unlit/Color");
            var backingMaterial = new Material(backingShader) { name = "Cabinet WCK Backing Material" };
            backingMaterial.color = new Color(0.035f, 0.043f, 0.052f, 1f);
            if (backingMaterial.HasProperty("_Metallic")) backingMaterial.SetFloat("_Metallic", 0.32f);
            if (backingMaterial.HasProperty("_Glossiness")) backingMaterial.SetFloat("_Glossiness", 0.42f);

            var logoShader = Shader.Find("UI/Default") ?? Shader.Find("Unlit/Transparent") ?? Shader.Find("Standard");
            var logoMaterial = new Material(logoShader) { name = "Cabinet WCK Logo Material" };
            logoMaterial.mainTexture = logoTexture;
            logoMaterial.color = Color.white;
            logoMaterial.renderQueue = 3000;

            var materials = new List<Material>(cabinetRenderer.materials.Take(originalSubMeshCount))
            {
                backingMaterial,
                logoMaterial
            };
            cabinetRenderer.materials = materials.ToArray();
        }

        private static void AppendBrandQuad(
            List<Vector3> vertices,
            List<Vector3> normals,
            List<Vector4> tangents,
            List<Vector2> uv0,
            List<Vector2> uv1,
            List<int> triangles,
            float screenLeftX,
            float screenRightX,
            float centerY,
            float height,
            float z,
            Vector3 normal,
            bool mapLogoTexture)
        {
            var firstVertex = vertices.Count;
            var bottom = centerY - height * 0.5f;
            var top = centerY + height * 0.5f;
            vertices.Add(new Vector3(screenLeftX, bottom, z));
            vertices.Add(new Vector3(screenRightX, bottom, z));
            vertices.Add(new Vector3(screenRightX, top, z));
            vertices.Add(new Vector3(screenLeftX, top, z));

            for (var index = 0; index < 4; index++)
            {
                normals.Add(normal);
                tangents.Add(new Vector4(1f, 0f, 0f, 1f));
                uv1.Add(Vector2.zero);
            }

            if (mapLogoTexture)
            {
                uv0.Add(new Vector2(0f, 0f));
                uv0.Add(new Vector2(1f, 0f));
                uv0.Add(new Vector2(1f, 1f));
                uv0.Add(new Vector2(0f, 1f));
            }
            else
            {
                for (var index = 0; index < 4; index++) uv0.Add(Vector2.zero);
            }

            triangles.Add(firstVertex);
            triangles.Add(firstVertex + 1);
            triangles.Add(firstVertex + 2);
            triangles.Add(firstVertex);
            triangles.Add(firstVertex + 2);
            triangles.Add(firstVertex + 3);
            triangles.Add(firstVertex + 2);
            triangles.Add(firstVertex + 1);
            triangles.Add(firstVertex);
            triangles.Add(firstVertex + 3);
            triangles.Add(firstVertex + 2);
            triangles.Add(firstVertex);
        }

        private Texture2D ResolveCabinetBrandLogo()
        {
            if (cabinetBrandLogo != null) return cabinetBrandLogo;

            var externalLogoPath = Path.Combine(Application.streamingAssetsPath, "WCKLogo.png");
            if (!File.Exists(externalLogoPath)) return null;

            try
            {
                var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false)
                {
                    name = "WCK Cabinet Logo"
                };
                if (!texture.LoadImage(File.ReadAllBytes(externalLogoPath), false))
                {
                    Destroy(texture);
                    return null;
                }

                texture.wrapMode = TextureWrapMode.Clamp;
                texture.filterMode = FilterMode.Bilinear;
                cabinetBrandLogo = texture;
                return cabinetBrandLogo;
            }
            catch (Exception exception)
            {
                Debug.LogWarning("Unable to load external WCK cabinet logo: " + exception.Message);
                return null;
            }
        }

    }
}
