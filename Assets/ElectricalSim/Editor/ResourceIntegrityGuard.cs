using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace ElectricalSim.Editor
{
    [InitializeOnLoad]
    public sealed class ResourceIntegrityGuard : IPreprocessBuildWithReport, IProcessSceneWithReport
    {
        private const string RecoveryHelp = "原始资源不完整，已停止运行或构建。\n"
            + "请重新下载包含 Git LFS 实际资源的 ZIP，或在完整 Git 仓库中执行 git lfs pull。\n"
            + "普通 ZIP 解压目录没有 .git，不能直接执行 git lfs pull。\n"
            + "详见 Docs/project-recovery.md；菜单 Electrical Sim > Validate Project Resources 可重新检查。";

        static ResourceIntegrityGuard()
        {
            EditorApplication.playModeStateChanged += OnPlayModeChanged;
            EditorApplication.delayCall += CheckOnOpen;
        }

        public int callbackOrder { get { return -1000; } }

        private static void CheckOnOpen()
        {
            if (Application.isBatchMode || EditorApplication.isPlayingOrWillChangePlaymode) return;
            if (EditorApplication.isCompiling || EditorApplication.isUpdating)
            {
                EditorApplication.delayCall += CheckOnOpen;
                return;
            }
            var problems = FindProblems();
            if (problems.Count > 0) Report(problems, true);
        }

        public static List<string> FindProblems()
        {
            var problems = ProjectResourceFiles.FindProblems(Path.GetDirectoryName(Application.dataPath));
            // Avoid loading pointer files as Unity assets; give the actionable file report first.
            if (problems.Count > 0) return problems;
            var registry = AssetDatabase.LoadAssetAtPath<OriginalVisualRegistry>(ProjectResourceFiles.RegistryPath);
            if (registry == null) problems.Add("Unity cannot load " + ProjectResourceFiles.RegistryPath);
            else problems.AddRange(registry.FindMissingVisuals());
            try { SchematicCatalog.Load(); }
            catch (Exception exception) { problems.Add(exception.Message); }
            return problems;
        }

        [MenuItem("Electrical Sim/Validate Project Resources")]
        public static void ValidateMenu()
        {
            var problems = FindProblems();
            if (problems.Count > 0) Report(problems, true);
            else Debug.Log("[ResourceIntegrity] PASS: no LFS pointers, required files and original model references are present.");
        }

        // Public entry point for -batchmode -executeMethod.
        public static void ValidateForBuild()
        {
            var problems = FindProblems();
            if (problems.Count > 0) throw new BuildFailedException(Format(problems));
            Debug.Log("[ResourceIntegrity] PASS: project resources validated.");
        }

        private static List<string> SceneProblems(Scene scene)
        {
            var problems = new List<string>();
            foreach (var bootstrap in scene.GetRootGameObjects().SelectMany(root => root.GetComponentsInChildren<TrainingSceneBootstrap>(true)))
            {
                var registry = new SerializedObject(bootstrap).FindProperty("originalVisuals").objectReferenceValue as OriginalVisualRegistry;
                if (registry == null) problems.Add(scene.path + ": TrainingSceneBootstrap.originalVisuals is missing.");
                else problems.AddRange(registry.FindMissingVisuals());
            }
            return problems;
        }

        private static void OnPlayModeChanged(PlayModeStateChange state)
        {
            if (state != PlayModeStateChange.ExitingEditMode) return;
            var problems = FindProblems();
            for (var i = 0; i < SceneManager.sceneCount; i++)
                problems.AddRange(SceneProblems(SceneManager.GetSceneAt(i)));
            if (problems.Count == 0) return;
            EditorApplication.isPlaying = false;
            Report(problems, !Application.isBatchMode);
        }

        public void OnPreprocessBuild(BuildReport report) { ValidateForBuild(); }

        public void OnProcessScene(Scene scene, BuildReport report)
        {
            if (report == null) return;
            var problems = SceneProblems(scene);
            if (problems.Count > 0) throw new BuildFailedException(Format(problems));
        }

        private static string Format(List<string> problems)
        {
            return "[ResourceIntegrity] " + RecoveryHelp + "\nProblems: " + problems.Count + "\n"
                + string.Join("\n", problems.Take(20)) + (problems.Count > 20 ? "\nSee the PowerShell resource report for the complete list." : "");
        }

        private static void Report(List<string> problems, bool dialog)
        {
            var reportPath = Path.Combine(Path.GetDirectoryName(Application.dataPath), "Build/Reports/resource-integrity.txt");
            Directory.CreateDirectory(Path.GetDirectoryName(reportPath));
            File.WriteAllLines(reportPath, problems);
            Debug.LogError(Format(problems) + "\nFull report: " + reportPath);
            if (dialog && !Application.isBatchMode)
                EditorUtility.DisplayDialog("项目资源缺失", RecoveryHelp + "\n\n问题数量：" + problems.Count + "\n" + problems[0], "知道了");
        }
    }
}
