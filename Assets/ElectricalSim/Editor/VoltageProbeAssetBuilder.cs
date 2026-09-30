using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace ElectricalSim.Editor
{
    public static class VoltageProbeAssetBuilder
    {
        public const string OutputPath = "Assets/ElectricalSim/Resources/VoltageProbe.prefab";
        private const string Folder = "Assets/ElectricalSim/Generated/VoltageProbe";

        [MenuItem("Electrical Sim/Build Voltage Probe Model")]
        public static void Build()
        {
            if (!AssetDatabase.IsValidFolder(Folder)) AssetDatabase.CreateFolder("Assets/ElectricalSim/Generated", "VoltageProbe");
            var shell = Material("Safety Amber", new Color(0.98f, 0.58f, 0.06f));
            var rubber = Material("Graphite Grip", new Color(0.065f, 0.085f, 0.10f));
            var steel = Material("Steel", new Color(0.7f, 0.76f, 0.8f), 0.45f);
            var key = Material("Mode Key", new Color(0.16f, 0.36f, 0.40f));
            var mesh = CreateShellMesh();
            var meshPath = Folder + "/RoundedShell.asset";
            var saved = AssetDatabase.LoadAssetAtPath<Mesh>(meshPath);
            if (saved == null) { saved = mesh; AssetDatabase.CreateAsset(saved, meshPath); }
            else { EditorUtility.CopySerialized(mesh, saved); Object.DestroyImmediate(mesh); }
            var root = new GameObject("VoltageProbe");
            try
            {
                Rounded("InsulatedBody", root.transform, saved, shell, new Vector3(0, -0.0025f, 0), new Vector3(0.032f, 0.115f, 0.023f));
                Rounded("FrontGrip", root.transform, saved, rubber, new Vector3(0, -0.003f, -0.008f), new Vector3(0.029f, 0.105f, 0.011f));
                for (var side = -1; side <= 1; side += 2)
                    for (var i = 0; i < 7; i++)
                        Shape("GripRib", PrimitiveType.Cube, root.transform, rubber,
                            new Vector3(side * 0.0154f, -0.042f + i * 0.009f, 0), new Vector3(0.002f, 0.003f, 0.019f));
                Shape("FingerGuard", PrimitiveType.Cylinder, root.transform, shell, new Vector3(0, 0.051f, 0), new Vector3(0.031f, 0.003f, 0.027f));
                Shape("InsulatedShaft", PrimitiveType.Cylinder, root.transform, rubber, new Vector3(0, 0.076f, 0), new Vector3(0.009f, 0.022f, 0.009f));
                Shape("SteelShaft", PrimitiveType.Cylinder, root.transform, steel, new Vector3(0, 0.106f, 0), new Vector3(0.003f, 0.008f, 0.003f));
                Shape("ContactBlade", PrimitiveType.Cube, root.transform, steel, new Vector3(0, 0.1165f, 0), new Vector3(0.003f, 0.007f, 0.0012f));
                Shape("PocketClip", PrimitiveType.Cube, root.transform, steel, new Vector3(0.008f, -0.025f, 0.016f), new Vector3(0.005f, 0.056f, 0.002f));
                Shape("ClipMount", PrimitiveType.Cube, root.transform, steel, new Vector3(0.008f, -0.05f, 0.012f), new Vector3(0.005f, 0.008f, 0.009f));
                var tip = Empty("Tip", root.transform, new Vector3(0, 0.12f, 0));
                Picker("BodyPicker", root.transform, new Vector3(0, -0.0025f, 0), new Vector3(0.032f, 0.115f, 0.027f), VoltageProbeAction.PickUp);
                Rounded("ModeButton", root.transform, saved, key, new Vector3(0, -0.03f, -0.015f), new Vector3(0.021f, 0.013f, 0.005f));
                Picker("ModePicker", root.transform, new Vector3(0, -0.03f, -0.019f), new Vector3(0.023f, 0.015f, 0.006f), VoltageProbeAction.ToggleMode);

                var lcd = new GameObject("BacklitLCD", typeof(RectTransform), typeof(Canvas), typeof(Image));
                lcd.transform.SetParent(root.transform, false);
                lcd.transform.localPosition = new Vector3(0, 0.010f, -0.015f);
                lcd.transform.localScale = Vector3.one * 0.0001f;
                ((RectTransform)lcd.transform).sizeDelta = new Vector2(244, 420);
                lcd.GetComponent<Canvas>().renderMode = RenderMode.WorldSpace;
                lcd.GetComponent<Image>().color = new Color(0.65f, 0.92f, 0.83f);
                lcd.GetComponent<Image>().raycastTarget = false;
                var mode = Text("Mode", lcd.transform, "AC  V~", new Vector2(0, 139), new Vector2(230, 65), 37);
                var value = Text("Voltage", lcd.transform, "—", new Vector2(0, 30), new Vector2(238, 115), 48);
                Text("Reference", lcd.transform, "AUTO REF", new Vector2(0, -123), new Vector2(230, 50), 27);
                Label("ModeLabel", root.transform, "AC / DC", new Vector3(0, -0.03f, -0.0225f), 28, Color.white);
                Label("Brand", root.transform, "DIGITAL", new Vector3(0, -0.047f, -0.014f), 24, new Color(0.8f, 0.87f, 0.9f));
                var view = root.AddComponent<VoltageProbeView>();
                view.Configure(tip, value, mode);
                view.SetReading(new VoltageProbeReading(VoltageProbeMode.AC, VoltageProbeState.MissingContact));
                PrefabUtility.SaveAsPrefabAsset(root, OutputPath);
                AssetDatabase.SaveAssets();
                Debug.Log("Voltage probe model generated: " + OutputPath);
            }
            finally { Object.DestroyImmediate(root); }
        }

        private static Material Material(string name, Color color, float metallic = 0)
        {
            var path = Folder + "/" + name + ".mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null) { material = new Material(Shader.Find("Standard")); AssetDatabase.CreateAsset(material, path); }
            material.color = color; material.SetFloat("_Metallic", metallic); material.SetFloat("_Glossiness", 0.4f);
            material.EnableKeyword("_EMISSION"); material.SetColor("_EmissionColor", color * (metallic > 0 ? 0.65f : 0.45f));
            EditorUtility.SetDirty(material); return material;
        }
        private static Transform Empty(string name, Transform parent, Vector3 position)
        { var t = new GameObject(name).transform; t.SetParent(parent, false); t.localPosition = position; return t; }
        private static void Shape(string name, PrimitiveType type, Transform parent, Material material, Vector3 position, Vector3 scale)
        {
            var go = GameObject.CreatePrimitive(type); go.name = name; go.transform.SetParent(parent, false);
            go.transform.localPosition = position; go.transform.localScale = scale; go.GetComponent<Renderer>().sharedMaterial = material;
            Object.DestroyImmediate(go.GetComponent<Collider>());
        }
        private static void Rounded(string name, Transform parent, Mesh mesh, Material material, Vector3 position, Vector3 scale)
        {
            var t = Empty(name, parent, position); t.localScale = scale;
            t.gameObject.AddComponent<MeshFilter>().sharedMesh = mesh; t.gameObject.AddComponent<MeshRenderer>().sharedMaterial = material;
        }
        private static void Picker(string name, Transform parent, Vector3 position, Vector3 size, VoltageProbeAction action)
        {
            var t = Empty(name, parent, position); t.gameObject.AddComponent<BoxCollider>().size = size;
            t.gameObject.AddComponent<VoltageProbeInteractable>().Action = action;
        }
        private static Text Text(string name, Transform parent, string content, Vector2 position, Vector2 size, int fontSize)
        {
            var text = new GameObject(name, typeof(RectTransform), typeof(Text)).GetComponent<Text>();
            text.transform.SetParent(parent, false); text.rectTransform.sizeDelta = size; text.rectTransform.anchoredPosition = position;
            text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf"); text.fontSize = fontSize;
            text.text = content; text.alignment = TextAnchor.MiddleCenter; text.color = new Color(0.035f, 0.12f, 0.12f);
            text.raycastTarget = false; text.horizontalOverflow = HorizontalWrapMode.Overflow; text.verticalOverflow = VerticalWrapMode.Overflow;
            return text;
        }
        private static void Label(string name, Transform parent, string content, Vector3 position, int fontSize, Color color)
        {
            var go = new GameObject(name + "Canvas", typeof(RectTransform), typeof(Canvas));
            go.transform.SetParent(parent, false); go.transform.localPosition = position; go.transform.localScale = Vector3.one * 0.0001f;
            go.GetComponent<Canvas>().renderMode = RenderMode.WorldSpace;
            ((RectTransform)go.transform).sizeDelta = new Vector2(230, 70);
            Text(name, go.transform, content, Vector2.zero, new Vector2(230, 70), fontSize).color = color;
        }
        private static Mesh CreateShellMesh()
        {
            var vertices = new List<Vector3>(); var triangles = new List<int>();
            const int count = 32;
            for (var layer = 0; layer < 4; layer++)
            {
                var radius = layer == 0 || layer == 3 ? 0.085f : 0.12f;
                var z = layer == 0 ? -0.5f : layer == 1 ? -0.36f : layer == 2 ? 0.36f : 0.5f;
                for (var i = 0; i < count; i++)
                {
                    var quadrant = i / 8; var angle = (quadrant * 90 + i % 8 * 90f / 7) * Mathf.Deg2Rad;
                    vertices.Add(new Vector3((quadrant == 0 || quadrant == 3 ? 0.38f : -0.38f) + Mathf.Cos(angle) * radius,
                        (quadrant < 2 ? 0.38f : -0.38f) + Mathf.Sin(angle) * radius, z));
                }
            }
            for (var layer = 0; layer < 3; layer++)
                for (var i = 0; i < count; i++)
                {
                    var a = layer * count + i; var b = layer * count + (i + 1) % count;
                    triangles.AddRange(new[] { a, b, a + count, b, b + count, a + count });
                }
            for (var i = 1; i < count - 1; i++)
                triangles.AddRange(new[] { 0, i + 1, i, 96, 96 + i, 96 + i + 1 });
            var mesh = new Mesh { name = "Rounded probe shell" };
            mesh.SetVertices(vertices); mesh.SetTriangles(triangles, 0); mesh.RecalculateNormals(); mesh.RecalculateBounds(); return mesh;
        }
    }
}
