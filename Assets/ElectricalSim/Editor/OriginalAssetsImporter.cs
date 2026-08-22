using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace ElectricalSim.Editor
{
    public static class OriginalAssetsImporter
    {
        private const string SourceDirectory = "OriginalAssetsSource";
        private const string DestinationDirectory = "Assets/OriginalContent";
        private const string RegistryPath = DestinationDirectory + "/OriginalVisualRegistry.asset";
        private const string ScenePath = "Assets/Scenes/ElectricalTraining.unity";

        private static readonly Dictionary<ElectricalDeviceKind, string[]> Aliases =
            new Dictionary<ElectricalDeviceKind, string[]>
            {
                { ElectricalDeviceKind.Breaker, new[] { "duanlu", "breaker", "qf", "kongqi", "suke" } },
                { ElectricalDeviceKind.Contactor, new[] { "jiechu", "contactor", "jiaoliu", "km" } },
                { ElectricalDeviceKind.ThermalRelay, new[] { "reji", "thermal", "overload", "fr" } },
                { ElectricalDeviceKind.TimeRelay, new[] { "shijian", "timer", "time_relay", "kt" } },
                { ElectricalDeviceKind.PushButton, new[] { "anniu", "button", "push", "sb" } },
                { ElectricalDeviceKind.Motor, new[] { "dianji", "motor", "sanxiang", "m1" } },
                { ElectricalDeviceKind.Indicator, new[] { "zhishideng", "indicator", "lamp" } },
                { ElectricalDeviceKind.BrakeUnit, new[] { "zhidong", "brake" } }
            };

        [MenuItem("Electrical Sim/Import Original Assets")]
        public static void Import()
        {
            var source = Path.GetFullPath(SourceDirectory);
            if (!Directory.Exists(source))
            {
                EditorUtility.DisplayDialog("缺少原始素材", "请先把包含 .meta 的 Assets 子集放到项目根目录 OriginalAssetsSource/。", "确定");
                return;
            }

            if (Directory.Exists(DestinationDirectory))
            {
                EditorUtility.DisplayDialog("目标目录已存在", "Assets/OriginalContent 已存在。为避免覆盖人工调整，请先自行合并或改名后再导入。", "确定");
                return;
            }

            var assetFiles = Directory.GetFiles(source, "*", SearchOption.AllDirectories)
                .Where(path => !path.EndsWith(".meta", StringComparison.OrdinalIgnoreCase))
                .ToArray();
            var metaFiles = Directory.GetFiles(source, "*.meta", SearchOption.AllDirectories);
            if (assetFiles.Length == 0 || metaFiles.Length == 0)
            {
                EditorUtility.DisplayDialog("素材不完整", "暂存目录必须同时包含原始文件及其 .meta 文件。", "确定");
                return;
            }

            FileUtil.CopyFileOrDirectory(SourceDirectory, DestinationDirectory);
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            var registry = BuildRegistry();
            AttachRegistryToScene(registry);
            AssetDatabase.SaveAssets();
            Debug.Log($"Imported {assetFiles.Length} original files and {metaFiles.Length} meta files into {DestinationDirectory}.");
        }

        private static OriginalVisualRegistry BuildRegistry()
        {
            var registry = ScriptableObject.CreateInstance<OriginalVisualRegistry>();
            var prefabPaths = AssetDatabase.FindAssets("t:Prefab", new[] { DestinationDirectory })
                .Select(AssetDatabase.GUIDToAssetPath)
                .ToArray();

            foreach (var pair in Aliases)
            {
                var path = prefabPaths.FirstOrDefault(candidate =>
                    pair.Value.Any(alias => Path.GetFileNameWithoutExtension(candidate)
                        .IndexOf(alias, StringComparison.OrdinalIgnoreCase) >= 0));
                if (string.IsNullOrEmpty(path)) continue;
                registry.Entries.Add(new OriginalVisualEntry
                {
                    TypeId = pair.Key.ToString(),
                    Prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path)
                });
            }

            AssetDatabase.CreateAsset(registry, RegistryPath);
            return registry;
        }

        private static void AttachRegistryToScene(OriginalVisualRegistry registry)
        {
            var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            var bootstrap = UnityEngine.Object.FindObjectOfType<TrainingSceneBootstrap>();
            if (bootstrap == null) throw new InvalidOperationException("ElectricalTraining scene has no TrainingSceneBootstrap.");
            var serialized = new SerializedObject(bootstrap);
            serialized.FindProperty("originalVisuals").objectReferenceValue = registry;
            serialized.FindProperty("showMissingAssetNotice").boolValue = false;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
        }
    }
}
