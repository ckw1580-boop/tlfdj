using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace ElectricalSim.Editor
{
    public static class MultimeterAssetBuilder
    {
        public const string OutputPath = "Assets/ElectricalSim/Resources/Multimeter.prefab";
        private const string Folder = "Assets/ElectricalSim/Generated/Multimeter";
        private const float TextResolution = 8f;

        [MenuItem("Electrical Sim/Build Multimeter Model")]
        public static void Build()
        {
            if (!AssetDatabase.IsValidFolder(Folder)) AssetDatabase.CreateFolder("Assets/ElectricalSim/Generated", "Multimeter");
            var orange = Material("Orange Rubber", new Color(0.96f, 0.31f, 0.035f));
            var dark = Material("Graphite", new Color(0.055f, 0.067f, 0.08f));
            var silver = Material("Steel", new Color(0.72f, 0.76f, 0.8f), 0.6f);
            var red = Material("Red Lead", new Color(0.85f, 0.025f, 0.025f));
            var black = Material("Black Lead", new Color(0.035f, 0.035f, 0.04f));
            var cream = Material("Ivory", new Color(0.85f, 0.86f, 0.78f));
            var shell = RoundedMesh();
            var meshPath = Folder + "/RoundedShell.asset";
            var mesh = AssetDatabase.LoadAssetAtPath<Mesh>(meshPath);
            if (mesh == null) { mesh = shell; AssetDatabase.CreateAsset(mesh, meshPath); }
            else { EditorUtility.CopySerialized(shell, mesh); Object.DestroyImmediate(shell); }
            var root = new GameObject("Multimeter");
            try
            {
                var body = Empty("Body", root.transform, Vector3.zero);
                Rounded("ProtectiveShell", body, mesh, orange, Vector3.zero, new Vector3(0.118f, 0.224f, 0.043f));
                Rounded("Face", body, mesh, dark, new Vector3(0, 0, -0.018f), new Vector3(0.102f, 0.204f, 0.018f));
                var bodyPicker = body.gameObject.AddComponent<BoxCollider>();
                bodyPicker.size = new Vector3(0.118f, 0.224f, 0.043f);
                body.gameObject.AddComponent<MultimeterInteractable>().Action = MultimeterAction.MoveBody;
                for (var side = -1; side <= 1; side += 2)
                    for (var i = 0; i < 5; i++)
                        Shape("Grip", PrimitiveType.Cube, body, dark,
                            new Vector3(side * 0.057f, -0.048f + i * 0.018f, 0), new Vector3(0.005f, 0.009f, 0.038f));
                // A small rear stand makes the separate table body recognizable from every angle.
                var stand = Shape("RearStand", PrimitiveType.Cube, body, dark, new Vector3(0, -0.035f, 0.034f), new Vector3(0.071f, 0.10f, 0.008f));
                stand.localRotation = Quaternion.Euler(-18, 0, 0);
                Label("Brand", body, "DIGITAL MULTIMETER", new Vector2(0, 0.096f), new Vector2(0.092f, 0.009f), 5, Color.white);
                Label("Rating", body, "V~  /  V DC  /  CONT", new Vector2(0, 0.084f), new Vector2(0.092f, 0.008f), 4, new Color(0.7f, 0.75f, 0.78f));
                var lcd = new GameObject("LCD", typeof(RectTransform), typeof(Canvas), typeof(Image));
                lcd.transform.SetParent(body, false);
                lcd.transform.localPosition = new Vector3(0, 0.054f, -0.028f);
                lcd.transform.localScale = Vector3.one * (0.001f / TextResolution);
                ((RectTransform)lcd.transform).sizeDelta = new Vector2(87, 52) * TextResolution;
                lcd.GetComponent<Canvas>().renderMode = RenderMode.WorldSpace;
                lcd.GetComponent<Image>().color = new Color(0.68f, 0.78f, 0.62f);
                lcd.GetComponent<Image>().raycastTarget = false;
                var ink = new Color(0.08f, 0.15f, 0.10f);
                var mode = ScreenText("Mode", lcd.transform, new Vector2(0, 18), new Vector2(81, 9), 6, ink);
                var value = ScreenText("Reading", lcd.transform, new Vector2(0, 3), new Vector2(82, 25), 19, ink);
                var hint = ScreenText("Hint", lcd.transform, new Vector2(0, -17), new Vector2(82, 10), 6, ink);
                value.text = "—"; mode.text = "交流 V~"; hint.text = "请连接红黑表笔";
                var dial = Empty("Dial", body, new Vector3(0, -0.025f, -0.032f));
                var knob = Shape("Knob", PrimitiveType.Cylinder, dial, black, Vector3.zero, new Vector3(0.043f, 0.005f, 0.043f));
                knob.localRotation = Quaternion.Euler(90, 0, 0);
                Shape("Pointer", PrimitiveType.Cube, dial, cream, new Vector3(0, 0.011f, -0.006f), new Vector3(0.004f, 0.018f, 0.003f));
                ModeButton(body, "OFF", new Vector2(0, 0.006f), MultimeterAction.Off);
                ModeButton(body, "V~", new Vector2(0.037f, -0.025f), MultimeterAction.AcVoltage);
                ModeButton(body, "V DC", new Vector2(0, -0.056f), MultimeterAction.DcVoltage);
                ModeButton(body, "通断", new Vector2(-0.037f, -0.025f), MultimeterAction.Continuity);
                Picker("DialPicker", dial, Vector3.zero, new Vector3(0.038f, 0.038f, 0.018f), MultimeterAction.CycleMode);
                var sockets = new Transform[2]; var probes = new Transform[2]; var tips = new Transform[2];
                var docks = new Transform[2]; var leads = new LineRenderer[2];
                for (var i = 0; i < 2; i++)
                {
                    var material = i == 0 ? red : black;
                    var action = i == 0 ? MultimeterAction.RedProbe : MultimeterAction.BlackProbe;
                    var x = i == 0 ? 0.03f : -0.03f;
                    sockets[i] = Empty(i == 0 ? "RedSocket" : "COM", body, new Vector3(x, -0.086f, -0.035f));
                    var ring = Shape("SocketRim", PrimitiveType.Cylinder, sockets[i], material, Vector3.zero, new Vector3(0.018f, 0.004f, 0.018f));
                    ring.localRotation = Quaternion.Euler(90, 0, 0);
                    var hole = Shape("Plug", PrimitiveType.Cylinder, sockets[i], silver, new Vector3(0, 0, -0.006f), new Vector3(0.009f, 0.003f, 0.009f));
                    hole.localRotation = Quaternion.Euler(90, 0, 0);
                    Picker("SelectProbe", sockets[i], Vector3.zero, new Vector3(0.025f, 0.022f, 0.022f), action);
                    Label("SocketLabel", body, i == 0 ? "V / CONT" : "COM", new Vector2(x, -0.071f), new Vector2(0.033f, 0.01f), 5, Color.white);
                    docks[i] = Empty("ProbeDock" + i, body, new Vector3(i == 0 ? 0.088f : -0.088f, -0.041f, -0.018f));
                    docks[i].localRotation = Quaternion.Euler(0, 0, i == 0 ? -12 : 12);
                    probes[i] = Empty(i == 0 ? "RedProbe" : "BlackProbe", root.transform, docks[i].position);
                    probes[i].rotation = docks[i].rotation;
                    Shape("Handle", PrimitiveType.Cylinder, probes[i], material, Vector3.zero, new Vector3(0.012f, 0.035f, 0.012f));
                    Shape("FingerGuard", PrimitiveType.Cylinder, probes[i], material, new Vector3(0, 0.033f, 0), new Vector3(0.022f, 0.003f, 0.022f));
                    Shape("InsulatedShaft", PrimitiveType.Cylinder, probes[i], black, new Vector3(0, 0.049f, 0), new Vector3(0.007f, 0.014f, 0.007f));
                    Shape("SteelTip", PrimitiveType.Cylinder, probes[i], silver, new Vector3(0, 0.072f, 0), new Vector3(0.0025f, 0.010f, 0.0025f));
                    tips[i] = Empty("Tip", probes[i], new Vector3(0, 0.082f, 0));
                    Picker("ProbePicker", probes[i], new Vector3(0, 0.018f, 0), new Vector3(0.022f, 0.113f, 0.022f), action);
                    var cable = Empty(i == 0 ? "RedLead" : "BlackLead", root.transform, Vector3.zero);
                    leads[i] = cable.gameObject.AddComponent<LineRenderer>();
                    leads[i].sharedMaterial = material; leads[i].useWorldSpace = true;
                    leads[i].positionCount = 25; leads[i].widthMultiplier = 0.0034f;
                    leads[i].numCapVertices = 5; leads[i].numCornerVertices = 4;
                    leads[i].shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                }
                ModeButton(body, "收笔", new Vector2(-0.024f, -0.102f), MultimeterAction.Retract, 0.035f);
                ModeButton(body, "归位", new Vector2(0.024f, -0.102f), MultimeterAction.Home, 0.035f);
                var terminalText = Label("Terminals", body, "红：未连接\n黑：未连接", new Vector2(0, -0.13f), new Vector2(0.18f, 0.025f), 5, Color.white);
                var audio = body.gameObject.AddComponent<AudioSource>();
                audio.playOnAwake = false; audio.loop = true; audio.volume = 0.25f; audio.spatialBlend = 0;
                var view = root.AddComponent<MultimeterView>();
                view.Configure(body, dial, probes, tips, docks, sockets, leads, value, mode, hint, terminalText, audio);
                view.RefreshLeads();
                PrefabUtility.SaveAsPrefabAsset(root, OutputPath);
                AssetDatabase.SaveAssets();
                Debug.Log("Multimeter model generated: " + OutputPath);
            }
            finally { Object.DestroyImmediate(root); }
        }

        private static Material Material(string name, Color color, float metal = 0)
        {
            var path = Folder + "/" + name + ".mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null) { material = new Material(Shader.Find("Standard")); AssetDatabase.CreateAsset(material, path); }
            material.color = color; material.SetFloat("_Metallic", metal); material.SetFloat("_Glossiness", metal > 0 ? 0.55f : 0.25f);
            material.EnableKeyword("_EMISSION"); material.SetColor("_EmissionColor", color * 0.45f);
            EditorUtility.SetDirty(material);
            return material;
        }
        private static Transform Empty(string name, Transform parent, Vector3 position)
        {
            var t = new GameObject(name).transform; t.SetParent(parent, false); t.localPosition = position; return t;
        }
        private static Transform Shape(string name, PrimitiveType type, Transform parent, Material material, Vector3 position, Vector3 scale)
        {
            var go = GameObject.CreatePrimitive(type); go.name = name; go.transform.SetParent(parent, false);
            go.transform.localPosition = position; go.transform.localScale = scale;
            go.GetComponent<Renderer>().sharedMaterial = material; Object.DestroyImmediate(go.GetComponent<Collider>()); return go.transform;
        }
        private static void Rounded(string name, Transform parent, Mesh mesh, Material material, Vector3 position, Vector3 scale)
        {
            var t = Empty(name, parent, position); t.localScale = scale;
            t.gameObject.AddComponent<MeshFilter>().sharedMesh = mesh; t.gameObject.AddComponent<MeshRenderer>().sharedMaterial = material;
        }
        private static void Picker(string name, Transform parent, Vector3 position, Vector3 size, MultimeterAction action)
        {
            var t = Empty(name, parent, position); t.gameObject.AddComponent<BoxCollider>().size = size;
            t.gameObject.AddComponent<MultimeterInteractable>().Action = action;
        }
        private static void ModeButton(Transform body, string text, Vector2 position, MultimeterAction action, float width = 0.025f)
        {
            Label(action + "Label", body, text, position, new Vector2(width, 0.011f), 5, Color.white);
            Picker(action + "Button", body, new Vector3(position.x, position.y, -0.034f), new Vector3(width, 0.015f, 0.008f), action);
        }
        private static Text Label(string name, Transform parent, string value, Vector2 position, Vector2 size, int fontSize, Color color)
        {
            var canvas = new GameObject(name + "Canvas", typeof(RectTransform), typeof(Canvas));
            canvas.transform.SetParent(parent, false); canvas.transform.localPosition = new Vector3(position.x, position.y, -0.04f);
            canvas.transform.localScale = Vector3.one * (0.001f / TextResolution);
            canvas.GetComponent<Canvas>().renderMode = RenderMode.WorldSpace;
            ((RectTransform)canvas.transform).sizeDelta = size * (1000 * TextResolution);
            var text = ScreenText(name, canvas.transform, Vector2.zero, size * 1000, fontSize, color); text.text = value; return text;
        }
        private static Text ScreenText(string name, Transform parent, Vector2 position, Vector2 size, int fontSize, Color color)
        {
            var text = new GameObject(name, typeof(RectTransform), typeof(Text)).GetComponent<Text>();
            text.transform.SetParent(parent, false); text.rectTransform.sizeDelta = size * TextResolution; text.rectTransform.anchoredPosition = position * TextResolution;
            text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf"); text.fontSize = Mathf.RoundToInt(fontSize * TextResolution);
            text.alignment = TextAnchor.MiddleCenter; text.color = color; text.raycastTarget = false;
            text.horizontalOverflow = HorizontalWrapMode.Wrap; text.verticalOverflow = VerticalWrapMode.Overflow;
            return text;
        }
        private static Mesh RoundedMesh()
        {
            var vertices = new List<Vector3>(); var triangles = new List<int>();
            const int corners = 32;
            for (var layer = 0; layer < 4; layer++)
            {
                var inset = layer == 0 || layer == 3 ? 0.025f : 0;
                var z = layer == 0 ? -0.5f : layer == 1 ? -0.38f : layer == 2 ? 0.38f : 0.5f;
                for (var i = 0; i < corners; i++)
                {
                    var angle = (i / 8 * 90 + (i % 8) * 90f / 7) * Mathf.Deg2Rad;
                    var quadrant = i / 8;
                    var center = new Vector2(quadrant == 0 || quadrant == 3 ? 0.38f : -0.38f, quadrant < 2 ? 0.38f : -0.38f);
                    vertices.Add(new Vector3(center.x + Mathf.Cos(angle) * (0.12f - inset), center.y + Mathf.Sin(angle) * (0.12f - inset), z));
                }
            }
            for (var layer = 0; layer < 3; layer++)
                for (var i = 0; i < corners; i++)
                {
                    var a = layer * corners + i; var b = layer * corners + (i + 1) % corners;
                    triangles.AddRange(new[] { a, b, a + corners, b, b + corners, a + corners });
                }
            vertices.Add(new Vector3(0, 0, -0.5f)); vertices.Add(new Vector3(0, 0, 0.5f));
            for (var i = 0; i < corners; i++)
                triangles.AddRange(new[] { 128, (i + 1) % corners, i, 129, 96 + i, 96 + (i + 1) % corners });
            var mesh = new Mesh { name = "Rounded Multimeter Shell" };
            mesh.SetVertices(vertices); mesh.SetTriangles(triangles, 0); mesh.RecalculateNormals(); mesh.RecalculateBounds(); return mesh;
        }
    }
}
