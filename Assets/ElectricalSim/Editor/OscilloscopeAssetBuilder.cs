using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace ElectricalSim.Editor
{
    public static class OscilloscopeAssetBuilder
    {
        public const string OutputPath = "Assets/ElectricalSim/Resources/Oscilloscope.prefab";
        private const string Folder = "Assets/ElectricalSim/Generated/Oscilloscope";
        [MenuItem("Electrical Sim/Build Oscilloscope Model")]
        public static void Build()
        {
            if (!AssetDatabase.IsValidFolder(Folder)) AssetDatabase.CreateFolder("Assets/ElectricalSim/Generated", "Oscilloscope");
            var grey = Material("Warm Grey", new Color(.72f, .74f, .73f));
            var dark = Material("Graphite", new Color(.055f, .075f, .085f));
            var steel = Material("Steel", new Color(.65f, .7f, .74f));
            var yellow = Material("Channel Yellow", OscilloscopePlot.ChannelColors[0]);
            var cyan = Material("Channel Cyan", OscilloscopePlot.ChannelColors[1]);
            var root = new GameObject("Oscilloscope");
            try
            {
                var body = Empty("Body", root.transform, Vector3.zero);
                Shape("Enclosure", PrimitiveType.Cube, body, grey, Vector3.zero, new Vector3(.46f, .30f, .16f));
                Shape("FrontPanel", PrimitiveType.Cube, body, dark, new Vector3(0, 0, -.084f), new Vector3(.44f, .28f, .012f));
                Picker("BodyPicker", body, Vector3.zero, new Vector3(.46f, .30f, .16f), OscilloscopeAction.MoveBody);
                for (var side = -1; side <= 1; side += 2)
                {
                    var foot = Shape("SupportFoot", PrimitiveType.Cube, body, dark, new Vector3(side * .17f, -.159f, 0), new Vector3(.04f, .025f, .15f)); foot.localRotation = Quaternion.Euler(-8, 0, 0);
                    for (var i = 0; i < 8; i++) Shape("Vent", PrimitiveType.Cube, body, dark, new Vector3(side * .231f, -.055f + i * .014f, .035f), new Vector3(.002f, .005f, .06f));
                }
                Label(body, "DUAL DIFFERENTIAL OSCILLOSCOPE", new Vector3(-.058f, .127f, -.095f), new Vector2(.30f, .014f), 18, Color.white);
                var screen = new GameObject("Screen", typeof(RectTransform), typeof(Canvas), typeof(Image)); screen.transform.SetParent(body, false);
                screen.transform.localPosition = new Vector3(-.055f, -.002f, -.094f); screen.transform.localScale = Vector3.one * .00028f;
                ((RectTransform)screen.transform).sizeDelta = new Vector2(1000, 800); screen.GetComponent<Canvas>().renderMode = RenderMode.WorldSpace;
                screen.GetComponent<Image>().color = new Color(.009f, .019f, .025f); screen.GetComponent<Image>().raycastTarget = false;
                var plotObject = new GameObject("Waveform", typeof(RectTransform), typeof(CanvasRenderer), typeof(OscilloscopePlot)); plotObject.transform.SetParent(screen.transform, false);
                var pr = (RectTransform)plotObject.transform; pr.anchorMin = new Vector2(.04f, .48f); pr.anchorMax = new Vector2(.96f, .96f); pr.offsetMin = pr.offsetMax = Vector2.zero;
                var summary = Text(screen.transform, "Readings", 32, Color.white); var tr = summary.rectTransform; tr.anchorMin = new Vector2(.04f, .02f); tr.anchorMax = new Vector2(.96f, .45f); tr.offsetMin = tr.offsetMax = Vector2.zero;
                summary.text = "当前工况波形\nCH1 ＋/－：未连接\nCH2 ＋/－：未连接";
                Key(body, "RUN / STOP", .156f, .092f, OscilloscopeAction.RunStop, grey);
                Key(body, "AUTO", .156f, .051f, OscilloscopeAction.AutoScale, grey);
                Knob(body, "TIME / DIV", .15f, -.002f, OscilloscopeAction.Timebase, steel);
                Knob(body, "CH1 V/div", .13f, -.075f, OscilloscopeAction.Voltage1, yellow);
                Knob(body, "CH2 V/div", .188f, -.075f, OscilloscopeAction.Voltage2, cyan);
                var probes = new Transform[4]; var tips = new Transform[4]; var docks = new Transform[4]; var sockets = new Transform[4]; var leads = new LineRenderer[4];
                for (var i = 0; i < 4; i++)
                {
                    var material = i < 2 ? yellow : cyan; var label = "CH" + (i / 2 + 1) + (i % 2 == 0 ? " +" : " -"); var x = -.17f + i * .075f;
                    sockets[i] = Empty(label + " Socket", body, new Vector3(x, -.131f, -.102f));
                    var socket = Shape("InputRing", PrimitiveType.Cylinder, sockets[i], material, Vector3.zero, new Vector3(.018f, .004f, .018f)); socket.localRotation = Quaternion.Euler(90, 0, 0);
                    Picker("ProbeSelect", sockets[i], Vector3.zero, new Vector3(.028f, .025f, .022f), (OscilloscopeAction)i);
                    Label(body, label, new Vector3(x, -.111f, -.10f), new Vector2(.065f, .012f), 18, OscilloscopePlot.ChannelColors[i / 2]);
                    docks[i] = Empty("ProbeDock" + i, body, new Vector3(-.27f - i * .025f, -.035f, -.015f));
                    probes[i] = Empty(label + " Probe", root.transform, docks[i].position);
                    Shape("Handle", PrimitiveType.Cylinder, probes[i], material, Vector3.zero, new Vector3(.011f, .033f, .011f));
                    Shape("PolarityBand", PrimitiveType.Cylinder, probes[i], i % 2 == 0 ? grey : dark, new Vector3(0, -.022f, 0), new Vector3(.012f, .006f, .012f));
                    Shape("Guard", PrimitiveType.Cylinder, probes[i], material, new Vector3(0, .029f, 0), new Vector3(.021f, .003f, .021f));
                    Shape("Insulation", PrimitiveType.Cylinder, probes[i], dark, new Vector3(0, .043f, 0), new Vector3(.006f, .012f, .006f));
                    Shape("Tip", PrimitiveType.Cylinder, probes[i], steel, new Vector3(0, .064f, 0), new Vector3(.002f, .009f, .002f));
                    tips[i] = Empty("Contact", probes[i], new Vector3(0, .073f, 0));
                    Label(probes[i], label, new Vector3(0, 0, -.008f), new Vector2(.036f, .009f), 18, Color.white);
                    Picker("ProbePicker", probes[i], new Vector3(0, .008f, 0), new Vector3(.019f, .09f, .019f), (OscilloscopeAction)i);
                    leads[i] = Empty("Lead" + i, root.transform, Vector3.zero).gameObject.AddComponent<LineRenderer>();
                    leads[i].sharedMaterial = material; leads[i].useWorldSpace = true; leads[i].positionCount = 25; leads[i].widthMultiplier = .0028f;
                    leads[i].numCapVertices = 4; leads[i].numCornerVertices = 3; leads[i].shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                }
                var view = root.AddComponent<OscilloscopeView>(); view.Configure(body, probes, tips, docks, sockets, leads, plotObject.GetComponent<OscilloscopePlot>(), summary);
                view.RefreshLeads(); PrefabUtility.SaveAsPrefabAsset(root, OutputPath); AssetDatabase.SaveAssets(); Debug.Log("Oscilloscope model generated: " + OutputPath);
            }
            finally { Object.DestroyImmediate(root); }
        }
        private static Material Material(string name, Color color)
        {
            var path = Folder + "/" + name + ".mat"; var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m == null) { m = new Material(Shader.Find("Standard")); AssetDatabase.CreateAsset(m, path); }
            m.color = color; m.SetFloat("_Glossiness", .3f); m.EnableKeyword("_EMISSION"); m.SetColor("_EmissionColor", color * .3f); EditorUtility.SetDirty(m); return m;
        }
        private static Transform Empty(string name, Transform parent, Vector3 position) { var t = new GameObject(name).transform; t.SetParent(parent, false); t.localPosition = position; return t; }
        private static Transform Shape(string name, PrimitiveType type, Transform parent, Material m, Vector3 pos, Vector3 size)
        { var go = GameObject.CreatePrimitive(type); go.name = name; go.transform.SetParent(parent, false); go.transform.localPosition = pos; go.transform.localScale = size; go.GetComponent<Renderer>().sharedMaterial = m; Object.DestroyImmediate(go.GetComponent<Collider>()); return go.transform; }
        private static void Picker(string name, Transform parent, Vector3 pos, Vector3 size, OscilloscopeAction action)
        { var t = Empty(name, parent, pos); t.gameObject.AddComponent<BoxCollider>().size = size; t.gameObject.AddComponent<OscilloscopeInteractable>().Action = action; }
        private static Text Text(Transform parent, string name, int size, Color color)
        { var go = new GameObject(name, typeof(RectTransform), typeof(Text)); go.transform.SetParent(parent, false); var t = go.GetComponent<Text>(); t.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf"); t.fontSize = size; t.color = color; t.raycastTarget = false; return t; }
        private static void Label(Transform parent, string value, Vector3 pos, Vector2 size, int fontSize, Color color)
        {
            var go = new GameObject("LabelCanvas", typeof(RectTransform), typeof(Canvas));
            var canvas = go.transform; canvas.SetParent(parent, false); canvas.localPosition = pos;
            go.GetComponent<Canvas>().renderMode = RenderMode.WorldSpace; canvas.localScale = Vector3.one * .0005f;
            var text = Text(canvas, "Label", fontSize, color); text.rectTransform.sizeDelta = size / .0005f; text.alignment = TextAnchor.MiddleCenter; text.text = value;
        }
        private static void Key(Transform parent, string text, float x, float y, OscilloscopeAction action, Material m)
        { Shape(text, PrimitiveType.Cube, parent, m, new Vector3(x, y, -.098f), new Vector3(.078f, .026f, .013f)); Label(parent, text, new Vector3(x, y, -.107f), new Vector2(.075f, .020f), 19, Color.black); Picker(text + "Picker", parent, new Vector3(x, y, -.106f), new Vector3(.079f, .027f, .018f), action); }
        private static void Knob(Transform parent, string text, float x, float y, OscilloscopeAction action, Material m)
        { var k = Shape(text, PrimitiveType.Cylinder, parent, m, new Vector3(x, y, -.103f), new Vector3(.027f, .01f, .027f)); k.localRotation = Quaternion.Euler(90, 0, 0); Label(parent, text, new Vector3(x, y - .025f, -.107f), new Vector2(.055f, .017f), 15, Color.white); Picker(text + "Picker", parent, new Vector3(x, y, -.109f), new Vector3(.034f, .034f, .024f), action); }
    }
}
