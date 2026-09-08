using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace ElectricalSim.Editor
{
    public static class TachometerAssetBuilder
    {
        public const string OutputPath = "Assets/ElectricalSim/Resources/Tachometer.prefab";
        [MenuItem("Electrical Sim/Build Tachometer Model")]
        public static void Build()
        {
            var source = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/OriginalContent/App/Src/Tool/Tachymeter.prefab");
            if (source == null) throw new FileNotFoundException("Original tachometer model is missing.");
            var root = new GameObject("Tachometer");
            var visual = new GameObject("OriginalVisual");
            visual.transform.SetParent(root.transform, false);
            CopyMeshes(source.transform.Find("mesh"), visual.transform);
            const string materialPath = "Assets/ElectricalSim/Generated/Tachometer.mat";
            var sourceMaterial = visual.GetComponentInChildren<MeshRenderer>().sharedMaterial;
            var material = AssetDatabase.LoadAssetAtPath<Material>(materialPath);
            if (material == null)
            {
                material = new Material(sourceMaterial) { name = "Tachometer" };
                AssetDatabase.CreateAsset(material, materialPath);
            }
            material.CopyPropertiesFromMaterial(sourceMaterial);
            material.shader = Shader.Find("Standard");
            material.DisableKeyword("_METALLICGLOSSMAP");
            material.SetFloat("_Metallic", 0f);
            material.SetFloat("_Glossiness", 0.2f);
            material.SetTexture("_MetallicGlossMap", null);
            // Preserve the printed face and orange rubber in the shaded cabinet interior.
            material.EnableKeyword("_EMISSION");
            material.SetTexture("_EmissionMap", sourceMaterial.mainTexture);
            material.SetColor("_EmissionColor", Color.white * 0.65f);
            material.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None;
            foreach (var renderer in visual.GetComponentsInChildren<MeshRenderer>()) renderer.sharedMaterial = material;
            EditorUtility.SetDirty(material);
            var oldScreen = source.transform.Find("Canvas");
            visual.transform.localRotation = Quaternion.Inverse(oldScreen.localRotation);
            var screen = new GameObject("LCD", typeof(RectTransform), typeof(Canvas));
            screen.transform.SetParent(visual.transform, false);
            screen.transform.localPosition = oldScreen.localPosition;
            screen.transform.localRotation = oldScreen.localRotation;
            screen.transform.localScale = oldScreen.localScale;
            var rect = (RectTransform)screen.transform;
            rect.sizeDelta = new Vector2(272f, 207f);
            screen.GetComponent<Canvas>().renderMode = RenderMode.WorldSpace;
            var face = new GameObject("Background", typeof(RectTransform), typeof(Image));
            face.transform.SetParent(screen.transform, false);
            Stretch((RectTransform)face.transform);
            face.GetComponent<Image>().color = new Color(0.71f, 0.73f, 0.7f);
            face.GetComponent<Image>().raycastTarget = false;
            var display = MakeText("Reading", face.transform, 92, TextAnchor.MiddleRight);
            display.rectTransform.offsetMin = new Vector2(8, 5);
            display.rectTransform.offsetMax = new Vector2(-12, -30);
            var unit = MakeText("Unit", face.transform, 28, TextAnchor.UpperRight);
            unit.rectTransform.offsetMax = new Vector2(-12, -8);
            unit.text = "rpm";
            var bounds = BoundsOf(root);
            visual.transform.localScale *= 0.14f / bounds.size.y;
            bounds = BoundsOf(root);
            visual.transform.localPosition = -bounds.center;
            var vertices = root.GetComponentsInChildren<MeshFilter>()
                .SelectMany(filter => filter.sharedMesh.vertices.Select(filter.transform.TransformPoint)).ToArray();
            var tipHeight = vertices.Max(v => v.y);
            var cap = vertices.Where(v => v.y >= tipHeight - 0.00015f).ToArray();
            var tip = new GameObject("ProbeTip").transform;
            tip.SetParent(root.transform, false);
            tip.position = new Vector3(cap.Average(v => v.x), tipHeight, cap.Average(v => v.z));
            bounds = BoundsOf(root);
            var collider = root.AddComponent<BoxCollider>();
            collider.center = bounds.center;
            collider.size = bounds.size;
            root.AddComponent<TachometerView>().Configure(tip, display);
            display.text = "—";
            PrefabUtility.SaveAsPrefabAsset(root, OutputPath);
            Object.DestroyImmediate(root);
            AssetDatabase.SaveAssets();
            Debug.Log("Tachometer model generated: " + OutputPath + " shader=" + material.shader.name + " keywords=" + string.Join(",", material.shaderKeywords));
        }

        private static void CopyMeshes(Transform source, Transform parent)
        {
            var copy = new GameObject(source.name).transform;
            copy.SetParent(parent, false);
            copy.localPosition = source.localPosition;
            copy.localRotation = source.localRotation;
            copy.localScale = source.localScale;
            var mesh = source.GetComponent<MeshFilter>();
            if (mesh != null)
            {
                copy.gameObject.AddComponent<MeshFilter>().sharedMesh = mesh.sharedMesh;
                copy.gameObject.AddComponent<MeshRenderer>().sharedMaterials = source.GetComponent<MeshRenderer>().sharedMaterials;
            }
            foreach (Transform child in source) CopyMeshes(child, copy);
        }
        private static Bounds BoundsOf(GameObject root)
        {
            var renderers = root.GetComponentsInChildren<MeshRenderer>();
            var bounds = renderers[0].bounds;
            foreach (var renderer in renderers) bounds.Encapsulate(renderer.bounds);
            return bounds;
        }
        private static Text MakeText(string name, Transform parent, int size, TextAnchor alignment)
        {
            var text = new GameObject(name, typeof(RectTransform), typeof(Text)).GetComponent<Text>();
            text.transform.SetParent(parent, false);
            Stretch(text.rectTransform);
            text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            text.fontSize = size;
            text.color = Color.black;
            text.alignment = alignment;
            text.raycastTarget = false;
            return text;
        }
        private static void Stretch(RectTransform rect)
        { rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one; rect.offsetMin = rect.offsetMax = Vector2.zero; }
    }
}
