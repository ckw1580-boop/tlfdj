using System.IO;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace ElectricalSim.Editor
{
    public static class ProjectInstaller
    {
        private const string ScenePath = "Assets/Scenes/ElectricalTraining.unity";
        private const string GeneratedDirectory = "Assets/ElectricalSim/Generated";
        private const string PrimitiveMaterialPath = GeneratedDirectory + "/TrainingPrimitive.mat";
        private const string WireMaterialPath = GeneratedDirectory + "/TrainingWire.mat";

        [MenuItem("Electrical Sim/Install Training Scene")]
        public static void Install()
        {
            Directory.CreateDirectory("Assets/Scenes");
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var root = new GameObject("ElectricalTrainingBootstrap");
            var bootstrap = root.AddComponent<TrainingSceneBootstrap>();
            AssignGeneratedMaterials(bootstrap);
            SceneManager.SetActiveScene(scene);
            EditorSceneManager.SaveScene(scene, ScenePath);

            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };
            PlayerSettings.productName = "电气控制系统仿真软件 · 核心离线版";
            PlayerSettings.companyName = "Offline Electrical Training";
            PlayerSettings.defaultScreenWidth = 1920;
            PlayerSettings.defaultScreenHeight = 1080;
            PlayerSettings.fullScreenMode = FullScreenMode.Windowed;
            PlayerSettings.resizableWindow = true;
            PlayerSettings.runInBackground = true;
            PlayerSettings.colorSpace = ColorSpace.Gamma;
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log("ElectricalTraining scene installed at " + ScenePath);
        }

        [MenuItem("Electrical Sim/Build Windows x64")]
        public static void BuildWindowsPlayer()
        {
            if (!File.Exists(ScenePath)) Install();
            else EnsureSceneResources();
            var outputDirectory = Path.GetFullPath("Build/Windows");
            Directory.CreateDirectory(outputDirectory);
            var options = new BuildPlayerOptions
            {
                scenes = new[] { ScenePath },
                locationPathName = Path.Combine(outputDirectory, "ElectricalTraining.exe"),
                target = BuildTarget.StandaloneWindows64,
                options = BuildOptions.None
            };
            var report = BuildPipeline.BuildPlayer(options);
            if (report.summary.result != UnityEditor.Build.Reporting.BuildResult.Succeeded)
                throw new BuildFailedException($"Windows build failed: {report.summary.result} ({report.summary.totalErrors} errors)");
            Debug.Log($"Windows player built: {options.locationPathName} ({report.summary.totalSize} bytes)");
        }

        private static void EnsureSceneResources()
        {
            var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            var bootstrap = Object.FindObjectOfType<TrainingSceneBootstrap>();
            if (bootstrap == null) throw new BuildFailedException("ElectricalTraining scene has no TrainingSceneBootstrap.");
            AssignGeneratedMaterials(bootstrap);
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            AssetDatabase.SaveAssets();
        }

        private static void AssignGeneratedMaterials(TrainingSceneBootstrap bootstrap)
        {
            Directory.CreateDirectory(GeneratedDirectory);
            var primitive = LoadOrCreateMaterial(PrimitiveMaterialPath, "Standard");
            var wire = LoadOrCreateMaterial(WireMaterialPath, "Sprites/Default");
            var serialized = new SerializedObject(bootstrap);
            serialized.FindProperty("primitiveMaterial").objectReferenceValue = primitive;
            serialized.FindProperty("wireMaterial").objectReferenceValue = wire;
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        private static Material LoadOrCreateMaterial(string path, string shaderName)
        {
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material != null) return material;
            var shader = Shader.Find(shaderName);
            if (shader == null) throw new BuildFailedException("Required shader is unavailable: " + shaderName);
            material = new Material(shader) { name = Path.GetFileNameWithoutExtension(path) };
            AssetDatabase.CreateAsset(material, path);
            return material;
        }
    }
}
