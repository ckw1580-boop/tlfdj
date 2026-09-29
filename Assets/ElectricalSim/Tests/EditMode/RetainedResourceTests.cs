using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace ElectricalSim.Tests
{
    public sealed class RetainedResourceTests
    {
        [Test]
        public void RuntimeUiRegistryContainsOnlyCompleteActivePanels()
        {
            var registry = AssetDatabase.LoadAssetAtPath<OriginalVisualRegistry>(ProjectResourceFiles.RegistryPath);
            Assert.That(registry, Is.Not.Null);
            Assert.That(registry.FindMissingVisuals(), Is.Empty);
            Assert.That(registry.UiPrefabs.Select(entry => entry.Id), Is.EquivalentTo(new[] {
                "TopNavigation", "ExperimentToolbar", "LineForm", "LineParam", "Inverter" }));
            foreach (var entry in registry.UiPrefabs)
            {
                Assert.That(entry.Prefab, Is.Not.Null, entry.Id);
                foreach (var transform in entry.Prefab.GetComponentsInChildren<Transform>(true))
                    Assert.That(GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(transform.gameObject),
                        Is.Zero, entry.Id + "/" + transform.name);
            }
        }

        [Test]
        public void EditorGeneratorsKeepTheirDirectAssetInputs()
        {
            // These are loaded by path by editor tools, not by scene serialization.
            Assert.That(AssetDatabase.LoadAssetAtPath<GameObject>(
                "Assets/OriginalContent/App/Src/Tool/Tachymeter.prefab"), Is.Not.Null);
            Assert.That(AssetDatabase.LoadAssetAtPath<Texture2D>(
                "Assets/OriginalContent/Texture2D/loading_logo_forground.png"), Is.Not.Null);
        }
    }
}
