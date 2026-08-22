using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace ElectricalSim.Editor
{
    public static class OriginalAssetsImporter
    {
        private const string SourceDirectory = "OriginalAssetsSource";
        private const string DestinationDirectory = "Assets/OriginalContent";
        private const string RegistryPath = DestinationDirectory + "/OriginalVisualRegistry.asset";
        private const string GeneratedVisualDirectory = DestinationDirectory + "/GeneratedVisuals";
        private const string EnvironmentPrefabPath = GeneratedVisualDirectory + "/OriginalLabEnvironment.prefab";
        private const string TrainingScenePath = "Assets/Scenes/ElectricalTraining.unity";
        private static readonly Regex GuidRegex = new Regex(@"guid:\s*([0-9a-fA-F]{32})", RegexOptions.Compiled);

        private static readonly string[] DevicePrefabNames =
        {
            "GuiZi", "DianYuan", "JiaoLiuDianYuan", "SuKeDuanLuQi", "KongQiKaiGuan_3PK",
            "JiaoLiuJieChuQiK", "ReJiDianQiK", "ShiJianJiDianQiK", "AnNiu_Red", "AnNiu_Green",
            "SanXiangShuLongDianJiK", "ZhiDongDianZu", "DuanZiPai", "DianJiDuanZiPai",
            "ZhiShiDeng_Red", "ZhiShiDeng_Green"
        };

        private static readonly Dictionary<string, string> TaskSchematics = new Dictionary<string, string>
        {
            { "point", "三相异步电动机点动控制" },
            { "single-start", "三相异步电动机单点启动控制" },
            { "self-lock", "三相异步电动机自锁控制" },
            { "overload", "三相异步电动机过载保护自锁控制" },
            { "forward-reverse", "三相异步电动机联锁正反转控制" },
            { "multi-location", "三相异步电动机两地与多地控制" },
            { "timed", "三相异步电动机时间电路控制" },
            { "sequence", "三相异步电动机顺序启动控制" },
            { "reverse-brake", "三相异步电动机反接制动" },
            { "energy-brake", "三相异步电动机能耗制动" }
        };

        private static readonly string[] UiPrefabPaths =
        {
            "App/Src/UI/UIExperimentTop.prefab",
            "App/Src/UI/UIExperimentTask.prefab",
            "App/Src/UI/UIExperimentLineMap.prefab",
            "App/Src/UI/UIMultimeterForm.prefab",
            "App/Src/UI/UIExperimentShiBoQi.prefab",
            "App/Src/UI/UIPaiGu.prefab",
            "App/Src/UI/NewUI/UITopBar.prefab"
        };

        [MenuItem("Electrical Sim/Import Original Assets")]
        public static void Import()
        {
            try
            {
                ImportCore();
                EditorUtility.DisplayDialog("原始素材导入完成", "已迁移原实训室、器件、原理图和 UI 依赖。", "确定");
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                EditorUtility.DisplayDialog("导入失败", exception.Message, "确定");
            }
        }

        public static void ImportBatch() => ImportCore();

        private static void ImportCore()
        {
            var sourceRoot = ResolveSourceAssetsRoot();
            var selectedCount = 0;
            if (!Directory.Exists(DestinationDirectory))
            {
                var guidIndex = BuildGuidIndex(sourceRoot);
                var selected = ResolveDependencyClosure(sourceRoot, guidIndex, BuildSeedPaths(sourceRoot));
                selectedCount = selected.Count;
                CopySelectedAssets(sourceRoot, selected);
            }
            else
            {
                Debug.Log("Resuming original asset import from the existing dependency copy.");
            }
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);

            Directory.CreateDirectory(GeneratedVisualDirectory);
            var registry = AssetDatabase.LoadAssetAtPath<OriginalVisualRegistry>(RegistryPath);
            if (registry == null)
            {
                registry = ScriptableObject.CreateInstance<OriginalVisualRegistry>();
                AssetDatabase.CreateAsset(registry, RegistryPath);
            }
            registry.Entries.Clear();
            registry.Schematics.Clear();
            PopulateDeviceRegistry(registry);
            PopulateSchematics(registry);
            EditorUtility.SetDirty(registry);
            AssetDatabase.SaveAssets();

            var environmentPrefab = BuildEnvironmentPrefab();
            registry = AssetDatabase.LoadAssetAtPath<OriginalVisualRegistry>(RegistryPath);
            if (registry == null) throw new InvalidOperationException("OriginalVisualRegistry could not be reloaded after scene conversion.");
            registry.EnvironmentPrefab = environmentPrefab;
            var cabinetSource = FindPrefab("GuiZi");
            registry.CabinetPrefab = cabinetSource != null ? CreateCleanVisualPrefab(cabinetSource) : null;
            EditorUtility.SetDirty(registry);
            AttachRegistryToScene(registry);
            AssetDatabase.SaveAssets();
            Debug.Log($"Imported {selectedCount} original assets and generated clean visual prefabs.");
        }

        private static string ResolveSourceAssetsRoot()
        {
            var exported = Path.GetFullPath(Path.Combine(SourceDirectory, "ExportedProject", "Assets"));
            if (Directory.Exists(exported)) return exported;
            var direct = Path.GetFullPath(SourceDirectory);
            if (Directory.Exists(direct) && Directory.GetFiles(direct, "*.meta", SearchOption.AllDirectories).Length > 0)
                return direct;
            throw new DirectoryNotFoundException("未找到 OriginalAssetsSource/ExportedProject/Assets 或带 .meta 的直接素材目录。");
        }

        private static Dictionary<string, string> BuildGuidIndex(string sourceRoot)
        {
            var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var metaPath in Directory.GetFiles(sourceRoot, "*.meta", SearchOption.AllDirectories))
            {
                var match = GuidRegex.Match(File.ReadAllText(metaPath));
                if (!match.Success) continue;
                var assetPath = metaPath.Substring(0, metaPath.Length - 5);
                result[match.Groups[1].Value] = Relative(sourceRoot, assetPath);
            }
            return result;
        }

        private static IEnumerable<string> BuildSeedPaths(string sourceRoot)
        {
            var seeds = new List<string> { "App/Src/Scene/Experiment.unity" };
            foreach (var name in DevicePrefabNames)
                seeds.Add("App/Src/Element/Prefab/" + name + ".prefab");
            seeds.AddRange(UiPrefabPaths);
            foreach (var schematic in TaskSchematics.Values)
            {
                seeds.Add("App/Src/UI/LineDrawing/" + schematic + ".png");
                seeds.Add("App/Src/UI/LineDrawing/" + schematic + ".asset");
            }
            return seeds.Where(relative => File.Exists(Full(sourceRoot, relative)));
        }

        private static HashSet<string> ResolveDependencyClosure(
            string sourceRoot,
            IReadOnlyDictionary<string, string> guidIndex,
            IEnumerable<string> seeds)
        {
            var selected = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var queue = new Queue<string>(seeds);
            while (queue.Count > 0)
            {
                var relative = Normalize(queue.Dequeue());
                if (!selected.Add(relative) || ShouldSkip(relative)) continue;
                var path = Full(sourceRoot, relative);
                foreach (var guid in ReadReferencedGuids(path))
                    if (guidIndex.TryGetValue(guid, out var dependency) && !selected.Contains(dependency))
                        queue.Enqueue(dependency);
                foreach (var guid in ReadReferencedGuids(path + ".meta"))
                    if (guidIndex.TryGetValue(guid, out var dependency) && !selected.Contains(dependency))
                        queue.Enqueue(dependency);
            }
            selected.RemoveWhere(ShouldSkip);
            return selected;
        }

        private static IEnumerable<string> ReadReferencedGuids(string path)
        {
            if (!File.Exists(path) || !IsTextAsset(path)) yield break;
            foreach (Match match in GuidRegex.Matches(File.ReadAllText(path)))
                yield return match.Groups[1].Value;
        }

        private static bool IsTextAsset(string path)
        {
            var extension = Path.GetExtension(path).ToLowerInvariant();
            return extension == ".meta" || extension == ".prefab" || extension == ".unity" ||
                   extension == ".asset" || extension == ".mat" || extension == ".controller" ||
                   extension == ".anim" || extension == ".shader" || extension == ".spriteatlas";
        }

        private static bool ShouldSkip(string relative)
        {
            var extension = Path.GetExtension(relative).ToLowerInvariant();
            return relative.StartsWith("StreamingAssets/", StringComparison.OrdinalIgnoreCase) ||
                   extension == ".cs" || extension == ".dll" || extension == ".bundle" ||
                   extension == ".asmdef" || extension == ".unitypackage";
        }

        private static void CopySelectedAssets(string sourceRoot, IEnumerable<string> selected)
        {
            foreach (var relative in selected.OrderBy(item => item, StringComparer.OrdinalIgnoreCase))
            {
                var source = Full(sourceRoot, relative);
                if (!File.Exists(source)) continue;
                var destination = Full(Path.GetFullPath(DestinationDirectory), relative);
                Directory.CreateDirectory(Path.GetDirectoryName(destination));
                File.Copy(source, destination, false);
                if (File.Exists(source + ".meta")) File.Copy(source + ".meta", destination + ".meta", false);
            }
        }

        private static void PopulateDeviceRegistry(OriginalVisualRegistry registry)
        {
            AddDevice(registry, "POWER", "JiaoLiuDianYuan", "DianYuan");
            AddDevice(registry, "QF", "SuKeDuanLuQi", "KongQiKaiGuan_3PK");
            foreach (var id in new[] { "KM1", "KM2", "KMF", "KMR", "KMB", "KB" })
                AddDevice(registry, id, "JiaoLiuJieChuQiK");
            AddDevice(registry, "FR", "ReJiDianQiK");
            AddDevice(registry, "KT", "ShiJianJiDianQiK");
            foreach (var id in new[] { "SB0", "SB0A", "SB0B", "SBR", "SBB" })
                AddDevice(registry, id, "AnNiu_Red");
            foreach (var id in new[] { "SB1", "SB2", "SBF", "SBE", "SB1A", "SB1B" })
                AddDevice(registry, id, "AnNiu_Green");
            AddDevice(registry, "M1", "SanXiangShuLongDianJiK");
            AddDevice(registry, "M2", "SanXiangShuLongDianJiK");
            AddDevice(registry, "BRAKE", "ZhiDongDianZu");
        }

        private static void AddDevice(OriginalVisualRegistry registry, string deviceId, params string[] prefabNames)
        {
            var source = prefabNames.Select(FindPrefab).FirstOrDefault(item => item != null);
            if (source == null)
            {
                Debug.LogWarning("Original prefab was not found for " + deviceId);
                return;
            }
            var clean = CreateCleanVisualPrefab(source);
            registry.Entries.Add(new OriginalVisualEntry { DeviceId = deviceId, Prefab = clean });
        }

        private static GameObject FindPrefab(string name)
        {
            return AssetDatabase.FindAssets(name + " t:Prefab", new[] { DestinationDirectory })
                .Select(AssetDatabase.GUIDToAssetPath)
                .Where(path => Path.GetFileNameWithoutExtension(path).Equals(name, StringComparison.OrdinalIgnoreCase))
                .Select(path => AssetDatabase.LoadAssetAtPath<GameObject>(path))
                .FirstOrDefault(item => item != null);
        }

        private static GameObject CreateCleanVisualPrefab(GameObject source)
        {
            var destination = GeneratedVisualDirectory + "/" + source.name + "_Visual.prefab";
            var existing = AssetDatabase.LoadAssetAtPath<GameObject>(destination);
            if (existing != null) return existing;
            var instance = UnityEngine.Object.Instantiate(source);
            instance.name = source.name + "_Visual";
            StripRuntimeScripts(instance);
            var prefab = PrefabUtility.SaveAsPrefabAsset(instance, destination);
            UnityEngine.Object.DestroyImmediate(instance);
            return prefab;
        }

        private static void PopulateSchematics(OriginalVisualRegistry registry)
        {
            foreach (var pair in TaskSchematics)
            {
                var path = AssetDatabase.FindAssets(pair.Value + " t:Sprite", new[] { DestinationDirectory })
                    .Select(AssetDatabase.GUIDToAssetPath)
                    .FirstOrDefault(candidate => Path.GetFileNameWithoutExtension(candidate) == pair.Value);
                if (string.IsNullOrEmpty(path)) continue;
                var sprite = AssetDatabase.LoadAssetAtPath<Sprite>(path) ??
                             AssetDatabase.LoadAllAssetsAtPath(path).OfType<Sprite>().FirstOrDefault();
                if (sprite != null) registry.Schematics.Add(new OriginalSchematicEntry { TaskId = pair.Key, Sprite = sprite });
            }
        }

        private static GameObject BuildEnvironmentPrefab()
        {
            var sourceScenePath = DestinationDirectory + "/App/Src/Scene/Experiment.unity";
            if (!File.Exists(sourceScenePath)) return null;
            var trainingScene = EditorSceneManager.OpenScene(TrainingScenePath, OpenSceneMode.Single);
            var sourceScene = EditorSceneManager.OpenScene(sourceScenePath, OpenSceneMode.Additive);
            var container = new GameObject("OriginalLabEnvironment");
            SceneManager.MoveGameObjectToScene(container, trainingScene);
            foreach (var root in sourceScene.GetRootGameObjects())
            {
                if (!ContainsVisuals(root) || IsUiOrRuntimeRoot(root.name)) continue;
                var clone = UnityEngine.Object.Instantiate(root);
                clone.name = root.name;
                SceneManager.MoveGameObjectToScene(clone, trainingScene);
                clone.transform.SetParent(container.transform, true);
                StripRuntimeScripts(clone);
            }
            EditorSceneManager.CloseScene(sourceScene, true);
            var prefab = PrefabUtility.SaveAsPrefabAsset(container, EnvironmentPrefabPath);
            UnityEngine.Object.DestroyImmediate(container);
            return prefab;
        }

        private static bool ContainsVisuals(GameObject root)
            => root.GetComponentInChildren<Renderer>(true) != null || root.GetComponentInChildren<Light>(true) != null;

        private static bool IsUiOrRuntimeRoot(string name)
        {
            var lower = name.ToLowerInvariant();
            return lower.Contains("canvas") || lower.Contains("eventsystem") || lower.Contains("camera") ||
                   lower.Contains("manager") || lower.StartsWith("ui") || lower.Contains("post-process");
        }

        private static void StripRuntimeScripts(GameObject root)
        {
            foreach (var transform in root.GetComponentsInChildren<Transform>(true))
                GameObjectUtility.RemoveMonoBehavioursWithMissingScript(transform.gameObject);
            foreach (var component in root.GetComponentsInChildren<MonoBehaviour>(true))
                if (component != null) UnityEngine.Object.DestroyImmediate(component, true);
            foreach (var camera in root.GetComponentsInChildren<Camera>(true)) UnityEngine.Object.DestroyImmediate(camera, true);
            foreach (var listener in root.GetComponentsInChildren<AudioListener>(true)) UnityEngine.Object.DestroyImmediate(listener, true);
            foreach (var canvas in root.GetComponentsInChildren<Canvas>(true)) UnityEngine.Object.DestroyImmediate(canvas, true);
        }

        private static void AttachRegistryToScene(OriginalVisualRegistry registry)
        {
            var scene = EditorSceneManager.OpenScene(TrainingScenePath, OpenSceneMode.Single);
            var bootstrap = UnityEngine.Object.FindObjectOfType<TrainingSceneBootstrap>();
            if (bootstrap == null) throw new InvalidOperationException("ElectricalTraining scene has no TrainingSceneBootstrap.");
            var serialized = new SerializedObject(bootstrap);
            serialized.FindProperty("originalVisuals").objectReferenceValue = registry;
            serialized.FindProperty("showMissingAssetNotice").boolValue = false;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
        }

        private static string Relative(string root, string path)
        {
            var normalizedRoot = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            return Normalize(Path.GetFullPath(path).Substring(normalizedRoot.Length));
        }

        private static string Full(string root, string relative)
            => Path.Combine(root, Normalize(relative).Replace('/', Path.DirectorySeparatorChar));

        private static string Normalize(string path) => path.Replace('\\', '/');
    }
}
