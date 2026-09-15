using System;
using System.Collections.Generic;
using UnityEngine;

namespace ElectricalSim
{
    [Serializable]
    public sealed class SchematicPage
    {
        public int Number;
        public string Title;
        public Sprite Sprite;
    }

    [CreateAssetMenu(menuName = "Electrical Sim/Schematic Catalog", fileName = "SchematicCatalog")]
    public sealed class SchematicCatalog : ScriptableObject
    {
        public List<SchematicPage> Pages = new List<SchematicPage>();

        public static SchematicCatalog Load()
        {
            var catalog = Resources.Load<SchematicCatalog>("SchematicCatalog");
            if (catalog == null) throw new InvalidOperationException("原理图图册资源缺失：SchematicCatalog");
            catalog.Validate();
            return catalog;
        }

        public void Validate()
        {
            if (Pages == null || Pages.Count != 6)
                throw new InvalidOperationException("原理图图册必须包含按附件顺序排列的六张图。");
            for (var i = 0; i < Pages.Count; i++)
            {
                var page = Pages[i];
                if (page == null || page.Number != i + 1 || string.IsNullOrWhiteSpace(page.Title) || page.Sprite == null)
                    throw new InvalidOperationException($"原理图 {i + 1:00} 的序号、名称或图片缺失。");
            }
        }
    }
}
