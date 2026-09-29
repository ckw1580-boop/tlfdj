using System;
using System.Collections;
using System.IO;
using System.Linq;
using System.Reflection;
using ElectricalSim.Editor;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace ElectricalSim.Tests
{
    public sealed class PersonalProjectImportTests
    {
        private static object Invoke(string method, params object[] arguments)
            => typeof(OriginalAssetsImporter).GetMethod(method, BindingFlags.Static | BindingFlags.NonPublic)
                .Invoke(null, arguments);

        [Test]
        public void OfflineImportCopiesNestedProjectDataWithoutRestoringDocuments()
        {
            var root = Path.Combine(Path.GetTempPath(), "personal-import-" + Guid.NewGuid().ToString("N"));
            var source = Path.Combine(root, "source");
            var destination = Path.Combine(root, "destination");
            try
            {
                foreach (var relative in new[] { "project/nested/wiring.cc3d", "project/ports.json",
                             "project/manual.pdf", "Instructions/manual.pdf", "Instructions/index.json" })
                {
                    var path = Path.Combine(source, "StreamingAssets", relative);
                    Directory.CreateDirectory(Path.GetDirectoryName(path));
                    File.WriteAllText(path, relative);
                }
                Invoke("CopyOfflineData", source, destination);
                Invoke("CopyOfflineData", source, destination);
                var files = Directory.GetFiles(destination, "*", SearchOption.AllDirectories);
                Assert.That(files.Length, Is.EqualTo(2));
                Assert.That(File.ReadAllText(Path.Combine(destination, "project/nested/wiring.cc3d")),
                    Is.EqualTo("project/nested/wiring.cc3d"));
                Assert.That(File.ReadAllText(Path.Combine(destination, "project/ports.json")),
                    Is.EqualTo("project/ports.json"));
                Assert.That(Directory.Exists(Path.Combine(destination, "Instructions")), Is.False);
            }
            finally
            {
                if (Directory.Exists(root)) Directory.Delete(root, true);
            }
        }

        [TestCase("ElementProperties", "App/Src/UI/UIExperimentElementMsg.prefab")]
        [TestCase("ExperimentToolbar", "App/Src/UI/UIExperimentTop.prefab")]
        public void UiImportSanitizesReintroducedLabelsAndHelpControls(string id, string relative)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/OriginalContent/" + relative);
            Assert.That(prefab, Is.Not.Null);
            var instance = UnityEngine.Object.Instantiate(prefab);
            try
            {
                var transforms = instance.GetComponentsInChildren<Transform>(true);
                var texts = instance.GetComponentsInChildren<Text>(true).Where(t => t.name != "txt_company")
                    .ToDictionary(t => t, t => t.text);
                if (id == "ElementProperties")
                {
                    foreach (var row in transforms.Where(t => t.name == "company")) row.gameObject.SetActive(true);
                    transforms.Single(t => t.name == "txt_company").GetComponent<Text>().text = "Example imported company";
                }
                else
                {
                    foreach (var name in new[] { "btn_help", "split_help" })
                        transforms.Single(t => t.name == name).gameObject.SetActive(true);
                }
                Invoke("SanitizePersonalProjectUi", instance, id);
                Invoke("SanitizePersonalProjectUi", instance, id);
                if (id == "ElementProperties")
                {
                    Assert.That(transforms.Where(t => t.name == "company").All(t => !t.gameObject.activeSelf), Is.True);
                    Assert.That(transforms.Single(t => t.name == "txt_company").GetComponent<Text>().text, Is.Empty);
                }
                else
                    foreach (var name in new[] { "btn_help", "split_help" })
                        Assert.That(transforms.Single(t => t.name == name).gameObject.activeSelf, Is.False);
                foreach (var pair in texts) Assert.That(pair.Key.text, Is.EqualTo(pair.Value));
            }
            finally { UnityEngine.Object.DestroyImmediate(instance); }
        }

        [Test]
        public void ShippedResourcesAndImportRegistryDoNotExposeRemovedManual()
        {
            var registry = AssetDatabase.LoadAssetAtPath<OriginalVisualRegistry>(
                "Assets/OriginalContent/OriginalVisualRegistry.asset");
            Assert.That(registry.ResolveUi("Help"), Is.Null);
            var map = (IDictionary)typeof(OriginalAssetsImporter).GetField("RuntimeUiPrefabs",
                BindingFlags.Static | BindingFlags.NonPublic).GetValue(null);
            Assert.That(map.Contains("Help"), Is.False);
            Assert.That(Directory.Exists("Assets/StreamingAssets/OfflineData/Instructions"), Is.False);
            var properties = registry.ResolveUi("ElementProperties");
            Assert.That(properties.GetComponentsInChildren<Text>(true).Single(t => t.name == "txt_company").text, Is.Empty);
            var rows = properties.GetComponentsInChildren<Transform>(true).Where(t => t.name == "company").ToArray();
            Assert.That(rows, Is.Not.Empty);
            Assert.That(rows.All(t => !t.gameObject.activeSelf), Is.True);
        }
    }
}
