using System;
using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEngine;

namespace ElectricalSim.Tests
{
    public sealed class ResourceIntegrityTests
    {
        private string root;

        [SetUp]
        public void SetUp()
        {
            root = Path.Combine(Path.GetTempPath(), "tlfdj-resource-test-" + Guid.NewGuid().ToString("N"));
            foreach (var path in new[] { ProjectResourceFiles.RegistryPath, ProjectResourceFiles.EnvironmentPath,
                         ProjectResourceFiles.ScenePath, "Packages/manifest.json", "ProjectSettings/ProjectVersion.txt",
                         "ProjectSettings/ProjectSettings.asset" }) Write(path, "real file contents");
        }

        [TearDown]
        public void TearDown()
        {
            if (root != null && Directory.Exists(root)) Directory.Delete(root, true);
        }

        private void Write(string relative, string contents)
        {
            var path = Path.Combine(root, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllText(path, contents);
            if (relative.StartsWith("Assets/", StringComparison.Ordinal)) File.WriteAllText(path + ".meta", "guid: test");
        }

        [Test]
        public void CompleteFilesPassWithoutGitOrLibrary()
        {
            Assert.That(ProjectResourceFiles.FindProblems(root), Is.Empty);
        }

        [TestCase(ProjectResourceFiles.RegistryPath)]
        [TestCase("Assets/OriginalContent/Texture2D/测试.png")]
        [TestCase("ProjectSettings/ProjectSettings.asset")]
        public void PointerFilesAreRejectedIncludingBinaryExtensions(string path)
        {
            Write(path, "version https://git-lfs.github.com/spec/v1\noid sha256:" + new string('a', 64) + "\nsize 36642137\n");
            Assert.That(ProjectResourceFiles.FindProblems(root).Any(problem => problem.Contains("Git LFS pointer") && problem.Contains(path)), Is.True);
        }

        [Test]
        public void MissingEnvironmentAndMetadataAreReported()
        {
            File.Delete(Path.Combine(root, ProjectResourceFiles.EnvironmentPath));
            File.Delete(Path.Combine(root, ProjectResourceFiles.RegistryPath + ".meta"));
            var problems = ProjectResourceFiles.FindProblems(root);
            Assert.That(problems.Any(problem => problem.Contains("Missing required file") && problem.Contains(ProjectResourceFiles.EnvironmentPath)), Is.True);
            Assert.That(problems.Any(problem => problem.Contains("Missing .meta") && problem.Contains(ProjectResourceFiles.RegistryPath)), Is.True);
        }

        [Test]
        public void ShortBinaryAndIncidentalPointerTextAreNotPointers()
        {
            Write("Assets/tiny.bytes", "v");
            Write("Assets/help.txt", "Example only:\nversion https://git-lfs.github.com/spec/v1");
            Assert.That(ProjectResourceFiles.FindProblems(root), Is.Empty);
        }

        [Test]
        public void MissingDeviceReferenceFailsEvenWithAnEnvironment()
        {
            var registry = ScriptableObject.CreateInstance<OriginalVisualRegistry>();
            var environment = new GameObject("Test environment");
            try
            {
                registry.EnvironmentPrefab = environment;
                registry.CabinetPrefab = environment;
                registry.Entries.Add(new OriginalVisualEntry { DeviceId = "M1" });
                Assert.That(registry.FindMissingVisuals().Single(), Does.Contain("M1"));
                registry.Entries[0].Prefab = environment;
                Assert.That(registry.FindMissingVisuals(), Is.Empty);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(environment);
                UnityEngine.Object.DestroyImmediate(registry);
            }
        }
    }
}
