using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace ElectricalSim
{
    [Serializable]
    public sealed class Cc3dDocument
    {
        [JsonProperty("element")]
        public Dictionary<string, Cc3dElement> Elements = new Dictionary<string, Cc3dElement>();

        [JsonProperty("customPoints")]
        public Dictionary<string, Cc3dPoint> CustomPoints = new Dictionary<string, Cc3dPoint>();

        [JsonProperty("line")]
        public Dictionary<string, Cc3dLine> Lines = new Dictionary<string, Cc3dLine>();

        [JsonProperty("ropeLine")]
        public Dictionary<string, Cc3dRopeLine> RopeLines = new Dictionary<string, Cc3dRopeLine>();

        [JsonExtensionData]
        public IDictionary<string, JToken> Extra = new Dictionary<string, JToken>();
    }

    [Serializable]
    public sealed class Cc3dElement
    {
        [JsonProperty("id")] public string Id = string.Empty;
        [JsonProperty("_path")] public string Path = "model";
        [JsonProperty("type")] public string Type = string.Empty;
        [JsonProperty("Name")] public string Name;
        [JsonProperty("position", NullValueHandling = NullValueHandling.Ignore)] public float[] Position;
        [JsonProperty("rotation")] public float[] Rotation = { 0f, 0f, 0f };
        [JsonExtensionData] public IDictionary<string, JToken> Extra = new Dictionary<string, JToken>();
    }

    [Serializable]
    public sealed class Cc3dPoint
    {
        [JsonProperty("rotation")] public float[] Rotation = { 0f, 0f, 0f };
        [JsonProperty("position")] public float[] Position = { 0f, 0f, 0f };
        [JsonProperty("Name")] public string Name = string.Empty;
        [JsonProperty("elementId")] public string ElementId = "0";
        [JsonProperty("boardsId")] public string BoardsId = "0";
        [JsonProperty("boardName")] public string BoardName = string.Empty;
        [JsonExtensionData] public IDictionary<string, JToken> Extra = new Dictionary<string, JToken>();
    }

    [Serializable]
    public sealed class Cc3dLine
    {
        [JsonProperty("startDeviceId")] public string StartDeviceId = string.Empty;
        [JsonProperty("startPortName")] public string StartPortName = string.Empty;
        [JsonProperty("endDeviceId")] public string EndDeviceId = string.Empty;
        [JsonProperty("endPortName")] public string EndPortName = string.Empty;
        [JsonProperty("points")] public List<string> Points = new List<string>();
        [JsonProperty("color")] public float[] Color = { 1f, 0f, 0f, 1f };
        [JsonProperty("type")] public string Type = "Elec";
        [JsonProperty("area")] public float Area = 0.01f;
        [JsonProperty("tag")] public string Tag;
        [JsonExtensionData] public IDictionary<string, JToken> Extra = new Dictionary<string, JToken>();
    }

    [Serializable]
    public sealed class Cc3dRopeLine
    {
        [JsonProperty("lineColor")] public float[] LineColor = { 1f, 0f, 0f, 1f };
        [JsonProperty("lineType")] public string LineType = "JumperLine";
        [JsonProperty("lineArea")] public float LineArea = 0.01f;
        [JsonProperty("startDeviceId")] public string StartDeviceId = string.Empty;
        [JsonProperty("startPortName")] public string StartPortName = string.Empty;
        [JsonProperty("endDeviceId")] public string EndDeviceId = string.Empty;
        [JsonProperty("endPortName")] public string EndPortName = string.Empty;
        [JsonExtensionData] public IDictionary<string, JToken> Extra = new Dictionary<string, JToken>();
    }

    public static class Cc3dSerializer
    {
        private static readonly JsonSerializerSettings Settings = new JsonSerializerSettings
        {
            Culture = CultureInfo.InvariantCulture,
            Formatting = Formatting.None,
            NullValueHandling = NullValueHandling.Include,
            FloatParseHandling = FloatParseHandling.Double,
            MissingMemberHandling = MissingMemberHandling.Ignore
        };

        public static Cc3dDocument Deserialize(string json)
        {
            if (string.IsNullOrWhiteSpace(json)) throw new InvalidDataException("The CC3D document is empty.");
            var document = JsonConvert.DeserializeObject<Cc3dDocument>(json, Settings);
            if (document == null) throw new InvalidDataException("The CC3D document could not be parsed.");
            document.Elements ??= new Dictionary<string, Cc3dElement>();
            document.CustomPoints ??= new Dictionary<string, Cc3dPoint>();
            document.Lines ??= new Dictionary<string, Cc3dLine>();
            document.RopeLines ??= new Dictionary<string, Cc3dRopeLine>();
            return document;
        }

        public static string Serialize(Cc3dDocument document)
        {
            if (document == null) throw new ArgumentNullException(nameof(document));
            return JsonConvert.SerializeObject(document, Settings);
        }

        public static Cc3dDocument Load(string path)
        {
            return Deserialize(File.ReadAllText(path));
        }

        public static void Save(string path, Cc3dDocument document)
        {
            var directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
            File.WriteAllText(path, Serialize(document), new System.Text.UTF8Encoding(false));
        }
    }

    public static class Cc3dCircuitAdapter
    {
        public static void ImportWires(Cc3dDocument document, CircuitGraph graph)
        {
            graph.ClearWires();
            foreach (var entry in document.Lines)
            {
                var source = entry.Value;
                graph.AddWire(new WireConnection
                {
                    Id = entry.Key,
                    StartPort = CircuitGraph.Port(source.StartDeviceId, source.StartPortName),
                    EndPort = CircuitGraph.Port(source.EndDeviceId, source.EndPortName),
                    Color = ToColor(source.Color),
                    Area = source.Area,
                    LineType = source.Type,
                    Points = ResolvePoints(source.Points, document.CustomPoints)
                });
            }

            foreach (var entry in document.RopeLines)
            {
                var source = entry.Value;
                graph.AddWire(new WireConnection
                {
                    Id = entry.Key,
                    StartPort = CircuitGraph.Port(source.StartDeviceId, source.StartPortName),
                    EndPort = CircuitGraph.Port(source.EndDeviceId, source.EndPortName),
                    Color = ToColor(source.LineColor),
                    Area = source.LineArea,
                    LineType = source.LineType
                });
            }
        }

        public static Cc3dDocument Export(CircuitGraph graph, IEnumerable<DeviceSceneState> devices, Cc3dDocument baseDocument = null)
        {
            var document = baseDocument ?? new Cc3dDocument();
            document.Elements.Clear();
            document.CustomPoints.Clear();
            document.Lines.Clear();
            document.RopeLines.Clear();

            var elementIndex = 1;
            foreach (var device in devices)
            {
                document.Elements[elementIndex.ToString(CultureInfo.InvariantCulture)] = new Cc3dElement
                {
                    Id = device.Id,
                    Path = "model",
                    Type = device.Type,
                    Name = device.Name,
                    Position = ToArray(device.Position),
                    Rotation = ToArray(device.Rotation.eulerAngles)
                };
                elementIndex++;
            }

            foreach (var wire in graph.Wires)
            {
                SplitPort(wire.StartPort, out var startDevice, out var startPort);
                SplitPort(wire.EndPort, out var endDevice, out var endPort);
                if (wire.Points.Count == 0 || string.Equals(wire.LineType, "JumperLine", StringComparison.OrdinalIgnoreCase))
                {
                    document.RopeLines[wire.Id] = new Cc3dRopeLine
                    {
                        StartDeviceId = startDevice,
                        StartPortName = startPort,
                        EndDeviceId = endDevice,
                        EndPortName = endPort,
                        LineColor = ToArray(wire.Color),
                        LineArea = wire.Area,
                        LineType = wire.LineType
                    };
                    continue;
                }

                var pointIds = new List<string>();
                foreach (var point in wire.Points)
                {
                    var pointId = Guid.NewGuid().ToString();
                    pointIds.Add(pointId);
                    document.CustomPoints[pointId] = new Cc3dPoint
                    {
                        Position = ToArray(point),
                        Name = $"Elec_line_{wire.Id}_point"
                    };
                }

                document.Lines[wire.Id] = new Cc3dLine
                {
                    StartDeviceId = startDevice,
                    StartPortName = startPort,
                    EndDeviceId = endDevice,
                    EndPortName = endPort,
                    Points = pointIds,
                    Color = ToArray(wire.Color),
                    Area = wire.Area,
                    Type = wire.LineType
                };
            }

            return document;
        }

        private static List<Vector3> ResolvePoints(IEnumerable<string> ids, IReadOnlyDictionary<string, Cc3dPoint> points)
        {
            var result = new List<Vector3>();
            foreach (var id in ids)
                if (points.TryGetValue(id, out var point)) result.Add(ToVector3(point.Position));
            return result;
        }

        private static Color ToColor(IReadOnlyList<float> values)
        {
            if (values == null || values.Count < 3) return UnityEngine.Color.red;
            return new Color(values[0], values[1], values[2], values.Count > 3 ? values[3] : 1f);
        }

        private static Vector3 ToVector3(IReadOnlyList<float> values)
        {
            if (values == null || values.Count < 3) return Vector3.zero;
            return new Vector3(values[0], values[1], values[2]);
        }

        private static float[] ToArray(Vector3 value) => new[] { value.x, value.y, value.z };
        private static float[] ToArray(Color value) => new[] { value.r, value.g, value.b, value.a };

        private static void SplitPort(string qualifiedPort, out string device, out string port)
        {
            var split = qualifiedPort.IndexOf('.');
            if (split < 0)
            {
                device = "0";
                port = qualifiedPort;
                return;
            }
            device = qualifiedPort.Substring(0, split);
            port = qualifiedPort.Substring(split + 1);
        }
    }

    public readonly struct DeviceSceneState
    {
        public DeviceSceneState(string id, string type, string name, Vector3 position, Quaternion rotation)
        {
            Id = id;
            Type = type;
            Name = name;
            Position = position;
            Rotation = rotation;
        }

        public string Id { get; }
        public string Type { get; }
        public string Name { get; }
        public Vector3 Position { get; }
        public Quaternion Rotation { get; }
    }
}
