using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace ElectricalSim
{
    [Flags]
    public enum PortConnectionKind
    {
        Electrical = 1,
        Jumper = 2,
        Probe = 4,
        Network = 8,
        Pneumatic = 16
    }

    [Serializable]
    public sealed class OriginalPortRecord
    {
        public string DeviceId = string.Empty;
        public string DeviceType = string.Empty;
        public string PortName = string.Empty;
        public string DisplayName = string.Empty;
        public string TransformPath = string.Empty;
        public Vector3 LocalPosition;
        public Vector3 ReferenceWorldPosition;
        public Vector2 ReferenceScreenPosition;
        public PortConnectionKind ConnectionKinds = PortConnectionKind.Electrical | PortConnectionKind.Jumper | PortConnectionKind.Probe;

        public string QualifiedPort => CircuitGraph.Port(DeviceId, PortName);
    }

    [CreateAssetMenu(menuName = "Electrical Sim/Original Port Map", fileName = "OriginalPortMap")]
    public sealed class OriginalPortMap : ScriptableObject
    {
        public Vector2Int ReferenceResolution = new Vector2Int(1920, 1080);
        public List<OriginalPortRecord> Ports = new List<OriginalPortRecord>();

        public OriginalPortRecord Find(string deviceId, string portName)
            => Ports.FirstOrDefault(item => item.DeviceId == deviceId && item.PortName == portName);
    }

    [Serializable]
    public sealed class OriginalUiElementLayout
    {
        public string Id = string.Empty;
        public Rect ReferenceRect;
        public Vector2 AnchorMin;
        public Vector2 AnchorMax;
        public Vector2 Pivot = new Vector2(0.5f, 0.5f);
        public int FontSize;
        public Color Color = Color.white;
    }

    [CreateAssetMenu(menuName = "Electrical Sim/Original UI Layout", fileName = "OriginalUiLayoutProfile")]
    public sealed class OriginalUiLayoutProfile : ScriptableObject
    {
        public Vector2Int ReferenceResolution = new Vector2Int(1920, 1080);
        public List<OriginalUiElementLayout> Elements = new List<OriginalUiElementLayout>();

        public OriginalUiElementLayout Find(string id) => Elements.FirstOrDefault(item => item.Id == id);
    }

    [Serializable]
    public sealed class ScoringRule
    {
        public string Id = string.Empty;
        public string Description = string.Empty;
        public float Score;
        public bool RequireInOrder;
        public List<PortPair> RequiredConnections = new List<PortPair>();
        public List<TaskActionStep> Actions = new List<TaskActionStep>();
    }

    [Serializable]
    public sealed class FaultDefinition
    {
        public string Id = string.Empty;
        public string Description = string.Empty;
        public string DeviceId = string.Empty;
        public string PortA = string.Empty;
        public string PortB = string.Empty;
        public bool OpenCircuit;
        public bool ShortCircuit;
        public float Score;
        [NonSerialized] public JObject ExtensionData = new JObject();
    }

    [Serializable]
    public sealed class ActionSequence
    {
        public string Id = string.Empty;
        public bool Ordered;
        public List<TaskActionStep> Steps = new List<TaskActionStep>();
    }

    public sealed class ExamPackageDefinition
    {
        public string PackageId = string.Empty;
        public TimeSpan Duration = TimeSpan.FromHours(2);
        public readonly List<ScoringRule> WiringRules = new List<ScoringRule>();
        public readonly List<ScoringRule> DebugRules = new List<ScoringRule>();
        public readonly List<FaultDefinition> Faults = new List<FaultDefinition>();
        public JObject WiringRaw = new JObject();
        public JObject DebugRaw = new JObject();
        public JObject FaultRaw = new JObject();
    }

    [Serializable]
    public sealed class ExamSessionRecord
    {
        public string PackageId = string.Empty;
        public string StartedUtc = string.Empty;
        public string CompletedUtc = string.Empty;
        public double RemainingSeconds;
        public float Score;
        public bool Submitted;
        public List<string> CompletedRuleIds = new List<string>();
        public List<string> ClearedFaultIds = new List<string>();
    }

    public static class OfflineExamCatalog
    {
        public static string RootDirectory => Path.Combine(Application.streamingAssetsPath, "OfflineData", "Examine");

        public static IReadOnlyList<ExamPackageDefinition> LoadAll(string root = null)
        {
            root = string.IsNullOrEmpty(root) ? RootDirectory : root;
            if (!Directory.Exists(root)) return Array.Empty<ExamPackageDefinition>();
            return Directory.GetDirectories(root)
                .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                .Select(LoadPackage)
                .ToList();
        }

        public static ExamPackageDefinition LoadPackage(string directory)
        {
            var package = new ExamPackageDefinition { PackageId = Path.GetFileName(directory) };
            package.WiringRaw = LoadObject(Path.Combine(directory, "WiringSubject.json"));
            package.DebugRaw = LoadObject(Path.Combine(directory, "DebugSubject.json"));
            package.FaultRaw = LoadObject(Path.Combine(directory, "FaultSubject.json"));
            PopulateRules(package.WiringRaw["wiring"], package.WiringRules);
            PopulateRules(package.DebugRaw.SelectToken("debug.array"), package.DebugRules);
            PopulateFaults(package.FaultRaw["fault"], package.Faults);

            var time = LoadObject(Path.Combine(directory, "ExamineTime.json"));
            var hours = time.Value<double?>("totalDuration") ?? 2d;
            package.Duration = TimeSpan.FromHours(Math.Max(0.05d, hours));
            return package;
        }

        public static JObject LoadObject(string path)
        {
            if (!File.Exists(path)) return new JObject();
            using var reader = new JsonTextReader(File.OpenText(path)) { DateParseHandling = DateParseHandling.None };
            return JObject.Load(reader, new JsonLoadSettings { CommentHandling = CommentHandling.Ignore, LineInfoHandling = LineInfoHandling.Ignore });
        }

        private static void PopulateRules(JToken token, ICollection<ScoringRule> target)
        {
            if (token is not JArray array) return;
            foreach (var item in array.OfType<JObject>())
            {
                var children = item["wiring"] as JArray ?? item["smallSubject"] as JArray;
                if (children != null)
                {
                    foreach (var child in children.OfType<JObject>()) AddRule(child, target, item.Value<bool?>("isOrder") ?? false);
                }
                else AddRule(item, target, item.Value<bool?>("isOrder") ?? false);
            }
        }

        private static void AddRule(JObject item, ICollection<ScoringRule> target, bool inheritedOrder)
        {
            var rule = new ScoringRule
            {
                Id = item.Value<string>("id") ?? Guid.NewGuid().ToString("N"),
                Description = item.Value<string>("des") ?? string.Empty,
                Score = item.Value<float?>("score") ?? 0f,
                RequireInOrder = item.Value<bool?>("isOrder") ?? inheritedOrder
            };
            foreach (var condition in item.SelectTokens("conditions[*]").OfType<JObject>())
            {
                var device1 = condition.Value<string>("device1");
                var port1 = condition.Value<string>("device1_Prot");
                var device2 = condition.Value<string>("device2");
                var port2 = condition.Value<string>("device2_Prot");
                if (!string.IsNullOrEmpty(device1) && !string.IsNullOrEmpty(port1) && !string.IsNullOrEmpty(device2) && !string.IsNullOrEmpty(port2))
                    rule.RequiredConnections.Add(new PortPair(CircuitGraph.Port(device1, port1), CircuitGraph.Port(device2, port2)));
            }
            target.Add(rule);
        }

        private static void PopulateFaults(JToken token, ICollection<FaultDefinition> target)
        {
            if (token is not JArray array) return;
            foreach (var item in array.OfType<JObject>())
            {
                var children = item["faultPortSmallSubject"] as JArray;
                if (children == null) continue;
                foreach (var child in children.OfType<JObject>())
                {
                    var condition = child.SelectToken("conditions[0]") as JObject;
                    target.Add(new FaultDefinition
                    {
                        Id = child.Value<string>("id") ?? Guid.NewGuid().ToString("N"),
                        Description = child.Value<string>("des") ?? string.Empty,
                        DeviceId = condition?.Value<string>("device1") ?? string.Empty,
                        PortA = condition == null ? string.Empty : CircuitGraph.Port(condition.Value<string>("device1") ?? string.Empty, condition.Value<string>("device1_Prot") ?? string.Empty),
                        PortB = condition == null ? string.Empty : CircuitGraph.Port(condition.Value<string>("device2") ?? string.Empty, condition.Value<string>("device2_Prot") ?? string.Empty),
                        OpenCircuit = true,
                        Score = child.Value<float?>("score") ?? 0f,
                        ExtensionData = (JObject)child.DeepClone()
                    });
                }
            }
        }
    }

    public sealed class LocalSessionStore
    {
        public string RootDirectory { get; }

        public LocalSessionStore(string root = null)
        {
            RootDirectory = string.IsNullOrEmpty(root)
                ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "电气控制系统仿真软件")
                : root;
        }

        public string ProjectsDirectory => Ensure("存档");
        public string CapturesDirectory => Ensure("截图");
        public string RecordingsDirectory => Ensure("录像");
        public string ResultsDirectory => Ensure("成绩");

        public string SaveExam(ExamSessionRecord record)
        {
            if (record == null) throw new ArgumentNullException(nameof(record));
            var path = Path.Combine(ResultsDirectory, $"{SafeName(record.PackageId)}_{DateTime.Now:yyyyMMdd_HHmmss}.json");
            File.WriteAllText(path, JsonConvert.SerializeObject(record, Formatting.Indented));
            return path;
        }

        public ExamSessionRecord LoadExam(string path)
            => JsonConvert.DeserializeObject<ExamSessionRecord>(File.ReadAllText(path));

        private string Ensure(string name)
        {
            var path = Path.Combine(RootDirectory, name);
            Directory.CreateDirectory(path);
            return path;
        }

        private static string SafeName(string value)
        {
            foreach (var invalid in Path.GetInvalidFileNameChars()) value = value.Replace(invalid, '_');
            return string.IsNullOrWhiteSpace(value) ? "session" : value;
        }
    }
}
