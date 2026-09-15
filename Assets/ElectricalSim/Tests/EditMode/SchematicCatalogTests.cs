using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace ElectricalSim.Tests
{
    public sealed class SchematicCatalogTests
    {
        [Test]
        public void SixOriginalImagesHaveCorrectOrderDimensionsAndLosslessSettings()
        {
            var catalog = SchematicCatalog.Load();
            var names = new[] { "电机1控制原理图", "双速电机控制原理图", "变频器与电机3原理图",
                "虚拟PLC_1原理图", "虚拟PLC_2原理图", "故障考核—正反转电路原理图" };
            var sizes = new[] { new Vector2(1207, 731), new Vector2(1329, 743), new Vector2(864, 736),
                new Vector2(1403, 749), new Vector2(1396, 747), new Vector2(1164, 717) };
            Assert.That(catalog.Pages.Select(p => p.Number), Is.EqualTo(Enumerable.Range(1, 6)));
            Assert.That(catalog.Pages.Select(p => p.Title), Is.EqualTo(names));
            for (var i = 0; i < catalog.Pages.Count; i++)
            {
                var sprite = catalog.Pages[i].Sprite;
                var path = AssetDatabase.GetAssetPath(sprite);
                Assert.That(Path.GetFileName(path), Is.EqualTo($"{i + 1:00}.png"));
                Assert.That(sprite.rect.size, Is.EqualTo(sizes[i]));
                var importer = (TextureImporter)AssetImporter.GetAtPath(path);
                Assert.That(importer.textureType, Is.EqualTo(TextureImporterType.Sprite));
                Assert.That(importer.spriteImportMode, Is.EqualTo(SpriteImportMode.Single));
                Assert.That(importer.mipmapEnabled, Is.False);
                Assert.That(importer.maxTextureSize, Is.EqualTo(2048));
                Assert.That(importer.textureCompression, Is.EqualTo(TextureImporterCompression.Uncompressed));
            }
            Assert.That(Directory.Exists("Assets/OriginalContent/App/Src/UI/LineDrawing"), Is.False);
            Assert.That(Directory.Exists("Assets/StreamingAssets/OfflineData/assess"), Is.False);
            Assert.That(Directory.Exists("Assets/StreamingAssets/OfflineData/Examine"), Is.False);
        }

        [Test]
        public void IncompleteCatalogFailsWithAnActionableMessage()
        {
            var invalid = ScriptableObject.CreateInstance<SchematicCatalog>();
            try
            {
                Assert.That(() => invalid.Validate(), Throws.InvalidOperationException.With.Message.Contains("六张图"));
                invalid.Pages.AddRange(SchematicCatalog.Load().Pages);
                invalid.Pages[2] = new SchematicPage { Number = 3, Title = "图片缺失" };
                Assert.That(() => invalid.Validate(), Throws.InvalidOperationException.With.Message.Contains("03"));
            }
            finally { Object.DestroyImmediate(invalid); }
        }
    }
}
