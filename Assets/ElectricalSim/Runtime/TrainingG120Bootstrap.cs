using System;
using System.Linq;
using UnityEngine;

namespace ElectricalSim
{
    public sealed partial class TrainingSceneBootstrap
    {
        private void CreateG120ControlAnchors()
        {
            if (originalEnvironment == null) return;
            AddG120Anchors("DuanZiPai_3", "G120_l1", G120TerminalCatalog.AddedUpper);
            AddG120Anchors("DuanZiPai_4", "G120_U2", G120TerminalCatalog.AddedLower);
            CacheOriginalEnvironmentTransforms();
        }

        private void AddG120Anchors(string boardName, string referenceName, int[] numbers)
        {
            var board = originalEnvironmentTransforms.Single(t => t.name == boardName && t.Find("point") != null);
            var points = board.Find("point");
            var reference = points.Find(referenceName);
            var mesh = board.Find("mesh");
            if (reference == null || mesh == null) throw new InvalidOperationException("G120 端子参考模型缺失：" + boardName);
            var modules = mesh.Cast<Transform>().Where(t => t.gameObject.activeInHierarchy && t.name.StartsWith("DuanZiPai", StringComparison.Ordinal))
                .OrderBy(t => points.InverseTransformPoint(t.position).x).ToArray();
            var referenceX = points.InverseTransformPoint(reference.position).x;
            var nearest = modules.OrderBy(t => Mathf.Abs(points.InverseTransformPoint(t.position).x - referenceX)).First();
            var referenceIndex = Array.IndexOf(modules, nearest);
            var first = referenceIndex - numbers.Length - 1; // one unused module separates old and new groups
            if (first < 0) throw new InvalidOperationException("G120 左侧空置端子不足：" + boardName);
            var offset = reference.position - nearest.position;
            for (var i = 0; i < numbers.Length; i++)
            {
                var definition = G120TerminalCatalog.Find(numbers[i]);
                var position = modules[first + i].position + offset;
                foreach (var name in new[] { "G120_t" + numbers[i].ToString("00"), definition.BoardPort })
                {
                    if (points.Find(name) != null) continue;
                    var anchor = new GameObject(name).transform;
                    anchor.SetParent(points, false); anchor.position = position; anchor.rotation = reference.rotation;
                }
            }
        }
    }
}
