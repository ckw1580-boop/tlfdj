using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;

namespace ElectricalSim.Editor
{
    // Read-only audit. The report never grants permission to delete an asset.
    public static class LegacyResourceAudit
    {
        private const string LegacyRoot = "Assets/OriginalContent/";
        private static readonly Regex GuidPattern = new Regex(@"guid:\s*([0-9a-fA-F]{32})", RegexOptions.Compiled);

        [Serializable] public sealed class Candidate
        {
            public string path;
            public long bytes;
            public string reason;
        }

        [Serializable] public sealed class Report
        {
            public string unityVersion;
            public string[] roots;
            public string[] retained;
            public Candidate[] candidates;
            public string[] referenceIssues;
        }

        // Includes all Resources, StreamingAssets, scripts, tests, scenes and generated
        // assets outside the legacy tree. Literal editor-only inputs need explicit roots.
        public static Report Inspect()
        {
            var all = AssetDatabase.GetAllAssetPaths()
                .Where(p => p.StartsWith("Assets/", StringComparison.Ordinal) && File.Exists(p))
                .OrderBy(p => p, StringComparer.Ordinal).ToArray();
            var roots = new HashSet<string>(all.Where(p => !p.StartsWith(LegacyRoot, StringComparison.Ordinal)
                && p != "Assets/Scenes/SampleScene.unity"), StringComparer.Ordinal);
            foreach (var path in new[] {
                ProjectResourceFiles.RegistryPath,
                ProjectResourceFiles.EnvironmentPath,
                "Assets/OriginalContent/App/Src/Tool/Tachymeter.prefab",
                "Assets/OriginalContent/Texture2D/loading_logo_forground.png" })
            {
                if (!File.Exists(path)) throw new FileNotFoundException("Required audit root is missing", path);
                roots.Add(path);
            }
            // Catch direct asset-path loads added to retained source code in future.
            foreach (var source in all.Where(p => p.EndsWith(".cs", StringComparison.Ordinal)
                && !p.EndsWith("/LegacyResourceAudit.cs", StringComparison.Ordinal)))
                foreach (Match match in Regex.Matches(File.ReadAllText(source), "\"(Assets/[^\"\\r\\n]+)\""))
                    if (File.Exists(match.Groups[1].Value)) roots.Add(match.Groups[1].Value);
            var orderedRoots = roots.OrderBy(p => p, StringComparer.Ordinal).ToArray();
            var retained = new HashSet<string>(AssetDatabase.GetDependencies(orderedRoots, true), StringComparer.Ordinal);
            var issues = new SortedSet<string>(StringComparer.Ordinal);
            foreach (var path in retained.Where(p => p.StartsWith("Assets/", StringComparison.Ordinal)).OrderBy(p => p))
            {
                if (!File.Exists(path)) { issues.Add(path + ": missing file"); continue; }
                InspectGuids(path, issues);
                InspectGuids(path + ".meta", issues);
                if (!path.EndsWith(".prefab", StringComparison.Ordinal)) continue;
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (prefab == null) { issues.Add(path + ": cannot load prefab"); continue; }
                foreach (var transform in prefab.GetComponentsInChildren<Transform>(true))
                {
                    var missing = GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(transform.gameObject);
                    if (missing > 0) issues.Add(path + ": missing scripts " + missing + " at "
                        + AnimationUtility.CalculateTransformPath(transform, prefab.transform));
                }
                foreach (var renderer in prefab.GetComponentsInChildren<Renderer>(true))
                    for (var i = 0; i < renderer.sharedMaterials.Length; i++)
                        if (renderer.sharedMaterials[i] == null) issues.Add(path + ": missing material " + i + " at "
                            + AnimationUtility.CalculateTransformPath(renderer.transform, prefab.transform));
            }
            return new Report {
                unityVersion = Application.unityVersion,
                roots = orderedRoots,
                retained = retained.Where(p => p.StartsWith("Assets/", StringComparison.Ordinal)).OrderBy(p => p).ToArray(),
                candidates = all.Where(p => (p.StartsWith(LegacyRoot, StringComparison.Ordinal)
                        || p == "Assets/Scenes/SampleScene.unity") && !retained.Contains(p))
                    .Select(p => new Candidate { path = p, bytes = new FileInfo(p).Length
                        + (File.Exists(p + ".meta") ? new FileInfo(p + ".meta").Length : 0),
                        reason = "Outside recursive Unity dependencies of retained runtime/editor/test roots" }).ToArray(),
                referenceIssues = issues.ToArray()
            };
        }

        private static void InspectGuids(string path, ISet<string> issues)
        {
            if (!File.Exists(path)) { issues.Add(path + ": missing file"); return; }
            using (var reader = new StreamReader(path))
            {
                var header = new char[5];
                if (reader.Read(header, 0, header.Length) != header.Length) return;
                var prefix = new string(header);
                if (prefix != "%YAML" && !path.EndsWith(".meta", StringComparison.Ordinal)) return;
            }
            foreach (Match match in GuidPattern.Matches(File.ReadAllText(path)))
            {
                var guid = match.Groups[1].Value;
                // Unity built-in meshes, materials and scripts use reserved GUIDs.
                if (guid.StartsWith("0000000000000000", StringComparison.Ordinal)) continue;
                if (string.IsNullOrEmpty(AssetDatabase.GUIDToAssetPath(guid)))
                    issues.Add(path + ": unresolved GUID " + guid);
            }
        }

        public static void WriteReport()
        {
            var args = Environment.GetCommandLineArgs();
            var index = Array.IndexOf(args, "-legacyAuditReport");
            var output = index >= 0 && index + 1 < args.Length ? args[index + 1]
                : "Build/Reports/legacy-cleanup/audit.json";
            var report = Inspect();
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(output)));
            File.WriteAllText(output, JsonUtility.ToJson(report, true));
            Debug.Log($"[LegacyResourceAudit] retained={report.retained.Length}, candidates={report.candidates.Length}, "
                + $"candidateBytes={report.candidates.Sum(c => c.bytes)}, referenceIssues={report.referenceIssues.Length}; {output}");
        }
    }
}
