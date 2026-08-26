using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using UnityEditor;
using UnityEngine;

namespace ElectricalSim.Editor
{
    public static class OriginalReferenceGenerator
    {
        private const string RegistryPath = "Assets/OriginalContent/OriginalVisualRegistry.asset";
        private const string PortMapPath = "Assets/ElectricalSim/Generated/OriginalPortMap.asset";
        private const string UiLayoutPath = "Assets/ElectricalSim/Generated/OriginalUiLayoutProfile.asset";

        [MenuItem("Electrical Sim/Generate Original Reference Manifests")]
        public static void Generate()
        {
            Directory.CreateDirectory("Assets/ElectricalSim/Generated");
            var registry = AssetDatabase.LoadAssetAtPath<OriginalVisualRegistry>(RegistryPath);
            if (registry == null) throw new InvalidOperationException("OriginalVisualRegistry is missing. Import original assets first.");
            GeneratePorts(registry);
            GenerateUi(registry);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
        }

        public static void ReportEnvironmentBounds()
        {
            var registry = AssetDatabase.LoadAssetAtPath<OriginalVisualRegistry>(RegistryPath);
            if (registry?.EnvironmentPrefab == null) throw new InvalidOperationException("Original environment is missing.");
            var path = AssetDatabase.GetAssetPath(registry.EnvironmentPrefab);
            var root = PrefabUtility.LoadPrefabContents(path);
            try
            {
                var records = new List<object>();
                AddBoundsRecord(records, root.transform, "<all>");
                foreach (Transform child in root.transform) AddBoundsRecord(records, child, child.name);
                WriteReport("environment-bounds.json", records);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        public static void ReportEnvironmentTerminals()
        {
            var registry = AssetDatabase.LoadAssetAtPath<OriginalVisualRegistry>(RegistryPath);
            if (registry?.EnvironmentPrefab == null) throw new InvalidOperationException("Original environment is missing.");
            var path = AssetDatabase.GetAssetPath(registry.EnvironmentPrefab);
            var root = PrefabUtility.LoadPrefabContents(path);
            try
            {
                var aliases = PortSpecs().SelectMany(spec => spec.Ports.Values.SelectMany(values => values))
                    .Concat(new[] { "A1", "A2", "COM1", "COM2", "NO1", "NO2", "NC1", "NC2", "U1", "V1", "W1" })
                    .Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
                var records = root.GetComponentsInChildren<Transform>(true)
                    .Where(item => aliases.Any(alias => item.name.EndsWith(alias, StringComparison.OrdinalIgnoreCase)))
                    .Select(item => new
                    {
                        item.name,
                        path = AnimationUtility.CalculateTransformPath(item, root.transform),
                        position = new[] { item.position.x, item.position.y, item.position.z },
                        forward = new[] { item.forward.x, item.forward.y, item.forward.z }
                    })
                    .OrderBy(item => item.name, StringComparer.OrdinalIgnoreCase).ThenBy(item => item.position[2])
                    .ToArray();
                WriteReport("environment-terminals.json", records);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        private static void AddBoundsRecord(ICollection<object> records, Transform root, string name)
        {
            var renderers = root.GetComponentsInChildren<Renderer>(true).Where(item => item.enabled).ToArray();
            if (renderers.Length == 0) return;
            var bounds = renderers[0].bounds;
            for (var i = 1; i < renderers.Length; i++) bounds.Encapsulate(renderers[i].bounds);
            records.Add(new
            {
                name,
                rendererCount = renderers.Length,
                center = new[] { bounds.center.x, bounds.center.y, bounds.center.z },
                size = new[] { bounds.size.x, bounds.size.y, bounds.size.z },
                position = new[] { root.position.x, root.position.y, root.position.z }
            });
        }

        private static void GeneratePorts(OriginalVisualRegistry registry)
        {
            var asset = AssetDatabase.LoadAssetAtPath<OriginalPortMap>(PortMapPath);
            if (asset == null)
            {
                asset = ScriptableObject.CreateInstance<OriginalPortMap>();
                AssetDatabase.CreateAsset(asset, PortMapPath);
            }
            asset.Ports.Clear();

            foreach (var spec in PortSpecs())
            {
                var entry = registry.Entries.FirstOrDefault(item => item.DeviceId == spec.DeviceId);
                if (entry?.Prefab == null) continue;
                var root = entry.Prefab.transform;
                foreach (var port in spec.Ports)
                {
                    var terminal = FindTerminal(root, spec.DeviceId, port.Key, port.Value);
                    if (terminal == null) continue;
                    asset.Ports.Add(new OriginalPortRecord
                    {
                        DeviceId = spec.DeviceId,
                        DeviceType = spec.Kind.ToString(),
                        PortName = port.Key,
                        DisplayName = port.Key,
                        TransformPath = AnimationUtility.CalculateTransformPath(terminal, root),
                        LocalPosition = root.InverseTransformPoint(terminal.position),
                        ReferenceWorldPosition = terminal.position
                    });
                }
            }
            EditorUtility.SetDirty(asset);
            WriteReport("port-validation.json", asset.Ports.Select(item => new
            {
                item.DeviceId,
                item.PortName,
                item.TransformPath,
                local = new[] { item.LocalPosition.x, item.LocalPosition.y, item.LocalPosition.z },
                world = new[] { item.ReferenceWorldPosition.x, item.ReferenceWorldPosition.y, item.ReferenceWorldPosition.z }
            }));
        }

        private static void GenerateUi(OriginalVisualRegistry registry)
        {
            var asset = AssetDatabase.LoadAssetAtPath<OriginalUiLayoutProfile>(UiLayoutPath);
            if (asset == null)
            {
                asset = ScriptableObject.CreateInstance<OriginalUiLayoutProfile>();
                AssetDatabase.CreateAsset(asset, UiLayoutPath);
            }
            asset.Elements.Clear();
            foreach (var ui in registry.UiPrefabs.Where(item => item.Prefab != null))
            {
                foreach (var rect in ui.Prefab.GetComponentsInChildren<RectTransform>(true))
                {
                    asset.Elements.Add(new OriginalUiElementLayout
                    {
                        Id = ui.Id + "/" + AnimationUtility.CalculateTransformPath(rect, ui.Prefab.transform),
                        ReferenceRect = new Rect(rect.anchoredPosition, rect.rect.size),
                        AnchorMin = rect.anchorMin,
                        AnchorMax = rect.anchorMax,
                        Pivot = rect.pivot
                    });
                }
            }
            EditorUtility.SetDirty(asset);
            WriteReport("ui-layout-1920x1080.json", asset.Elements.Select(item => new
            {
                item.Id,
                rect = new[] { item.ReferenceRect.x, item.ReferenceRect.y, item.ReferenceRect.width, item.ReferenceRect.height },
                anchorMin = new[] { item.AnchorMin.x, item.AnchorMin.y },
                anchorMax = new[] { item.AnchorMax.x, item.AnchorMax.y },
                pivot = new[] { item.Pivot.x, item.Pivot.y }
            }));
        }

        private static Transform FindTerminal(Transform root, string deviceId, string port, IReadOnlyList<string> aliases)
        {
            var all = root.GetComponentsInChildren<Transform>(true);
            foreach (var alias in aliases.Concat(new[] { port }))
            {
                var match = all.FirstOrDefault(item => string.Equals(item.name, alias, StringComparison.OrdinalIgnoreCase) ||
                                                       string.Equals(item.name, deviceId + "_" + alias, StringComparison.OrdinalIgnoreCase));
                if (match != null) return match;
            }
            return null;
        }

        private static IEnumerable<DevicePortSpec> PortSpecs()
        {
            yield return new DevicePortSpec("QF", ElectricalDeviceKind.Breaker, new Dictionary<string, string[]> {
                { "L1", new[] { "1" } }, { "L2", new[] { "3" } }, { "L3", new[] { "5" } },
                { "T1", new[] { "2" } }, { "T2", new[] { "4" } }, { "T3", new[] { "6" } } });
            foreach (var id in new[] { "KM1", "KM2", "KMF", "KMR", "KMB", "KB" })
                yield return new DevicePortSpec(id, ElectricalDeviceKind.Contactor, new Dictionary<string, string[]> {
                    { "L1", new[] { "1L1" } }, { "L2", new[] { "3L2" } }, { "L3", new[] { "5L3" } },
                    { "T1", new[] { "2T1" } }, { "T2", new[] { "4T2" } }, { "T3", new[] { "6T3" } },
                    { "A1", new[] { "A1" } }, { "A2", new[] { "A2" } }, { "13", new[] { "13NO" } },
                    { "14", new[] { "14NO" } }, { "21", new[] { "21NC" } }, { "22", new[] { "22NC" } } });
            yield return new DevicePortSpec("FR", ElectricalDeviceKind.ThermalRelay, new Dictionary<string, string[]> {
                { "L1", new[] { "1L1" } }, { "L2", new[] { "3L2" } }, { "L3", new[] { "5L3" } },
                { "T1", new[] { "2T1" } }, { "T2", new[] { "4T2" } }, { "T3", new[] { "6T3" } },
                { "95", new[] { "95NC" } }, { "96", new[] { "96NC" } }, { "97", new[] { "97NO" } }, { "98", new[] { "98NO" } } });
            foreach (var id in new[] { "SB0", "SB0A", "SB0B", "SBR", "SBB", "SB1", "SB2", "SBF", "SBE", "SB1A", "SB1B" })
                yield return new DevicePortSpec(id, ElectricalDeviceKind.PushButton, new Dictionary<string, string[]> {
                    { "COM", new[] { "COM1", "COM2" } }, { "NO", new[] { "NO1", "NO2" } }, { "NC", new[] { "NC1", "NC2" } } });
            foreach (var id in new[] { "M1", "M2" })
                yield return new DevicePortSpec(id, ElectricalDeviceKind.Motor, new Dictionary<string, string[]> {
                    { "U", new[] { "U1" } }, { "V", new[] { "V1" } }, { "W", new[] { "W1" } },
                    { "U2", new[] { "U2" } }, { "V2", new[] { "V2" } }, { "W2", new[] { "W2" } } });
        }

        private static void WriteReport(string name, object value)
        {
            var directory = Path.Combine(Directory.GetParent(Application.dataPath).FullName, "Build", "Reports");
            Directory.CreateDirectory(directory);
            File.WriteAllText(Path.Combine(directory, name), JsonConvert.SerializeObject(value, Formatting.Indented));
        }

        private sealed class DevicePortSpec
        {
            public DevicePortSpec(string deviceId, ElectricalDeviceKind kind, Dictionary<string, string[]> ports)
            {
                DeviceId = deviceId;
                Kind = kind;
                Ports = ports;
            }

            public string DeviceId { get; }
            public ElectricalDeviceKind Kind { get; }
            public Dictionary<string, string[]> Ports { get; }
        }
    }
}
