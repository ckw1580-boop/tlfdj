using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace ElectricalSim
{
    // No Unity dependencies: the PowerShell recovery tools use this same scanner.
    public static class ProjectResourceFiles
    {
        public const string RegistryPath = "Assets/OriginalContent/OriginalVisualRegistry.asset";
        public const string EnvironmentPath = "Assets/OriginalContent/GeneratedVisuals/OriginalLabEnvironment.prefab";
        public const string ScenePath = "Assets/Scenes/ElectricalTraining.unity";
        private static readonly byte[] PointerHeader = Encoding.ASCII.GetBytes("version https://git-lfs.github.com/spec/v1");

        public static bool IsLfsPointer(string path)
        {
            using (var stream = File.OpenRead(path))
            {
                // Read only the signature, even for large textures and meshes.
                for (var i = 0; i < PointerHeader.Length; i++)
                    if (stream.ReadByte() != PointerHeader[i]) return false;
                return true;
            }
        }

        public static List<string> FindProblems(string projectRoot)
        {
            var problems = new List<string>();
            var root = Path.GetFullPath(projectRoot);
            foreach (var relative in new[] { RegistryPath, EnvironmentPath, ScenePath,
                         "Packages/manifest.json", "ProjectSettings/ProjectVersion.txt", "ProjectSettings/ProjectSettings.asset" })
            {
                var path = Path.Combine(root, relative);
                if (!File.Exists(path)) problems.Add("Missing required file: " + relative);
                else if (new FileInfo(path).Length == 0) problems.Add("Empty required file: " + relative);
            }

            foreach (var directory in new[] { "Assets", "Packages", "ProjectSettings" })
            {
                var fullDirectory = Path.Combine(root, directory);
                if (!Directory.Exists(fullDirectory))
                {
                    problems.Add("Missing directory: " + directory);
                    continue;
                }
                foreach (var path in Directory.EnumerateFiles(fullDirectory, "*", SearchOption.AllDirectories))
                {
                    var relative = path.Substring(root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar).Length + 1).Replace('\\', '/');
                    try
                    {
                        if (IsLfsPointer(path)) problems.Add("Git LFS pointer (real file not downloaded): " + relative);
                        if (directory == "Assets" && !path.EndsWith(".meta", StringComparison.OrdinalIgnoreCase)
                            && !Path.GetFileName(path).StartsWith(".", StringComparison.Ordinal)
                            && !File.Exists(path + ".meta"))
                            problems.Add("Missing .meta (asset references may break): " + relative);
                    }
                    catch (IOException ex) { problems.Add("Cannot read " + relative + ": " + ex.Message); }
                    catch (UnauthorizedAccessException ex) { problems.Add("Cannot read " + relative + ": " + ex.Message); }
                }
            }
            return problems;
        }
    }
}
