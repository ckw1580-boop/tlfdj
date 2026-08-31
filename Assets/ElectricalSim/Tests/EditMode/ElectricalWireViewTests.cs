using System.Linq;
using NUnit.Framework;
using UnityEngine;

namespace ElectricalSim.Tests
{
    public sealed class ElectricalWireViewTests
    {
        [Test]
        public void DefaultWireAreaUsesReferenceScaleWidth()
        {
            Assert.That(ElectricalWireView.WidthForArea(0.01f), Is.EqualTo(0.0035f).Within(0.00001f));
            Assert.That(ElectricalWireView.WidthForArea(0f), Is.EqualTo(0.0025f).Within(0.00001f));
            Assert.That(ElectricalWireView.WidthForArea(1f), Is.EqualTo(0.025f).Within(0.00001f));
        }

        [Test]
        public void RoutedWireInterpolatesItsControlPointsWithSmoothSpans()
        {
            var anchors = new[]
            {
                new Vector3(0f, 0f, 0f),
                new Vector3(1f, 0.6f, 0f),
                new Vector3(2f, 0.4f, 0f),
                new Vector3(3f, 1f, 0f)
            };

            var points = ElectricalWireView.BuildSmoothedPath(anchors, 8);

            Assert.That(points.Length, Is.EqualTo(25));
            Assert.That(points[0], Is.EqualTo(anchors[0]));
            Assert.That(points[points.Length - 1], Is.EqualTo(anchors[anchors.Length - 1]));
            foreach (var anchor in anchors)
                Assert.That(points.Any(point => Vector3.Distance(point, anchor) < 0.00001f), Is.True);
        }

        [Test]
        public void DirectWireKeepsExactlyTwoEndpoints()
        {
            var anchors = new[] { Vector3.left, Vector3.right };

            var points = ElectricalWireView.BuildSmoothedPath(anchors);

            Assert.That(points, Is.EqualTo(anchors));
        }

        [Test]
        public void RenderedWireUsesOneCameraDepthWithoutMovingItsScreenAnchors()
        {
            var cameraObject = new GameObject("WireTestCamera");
            try
            {
                var camera = cameraObject.AddComponent<Camera>();
                camera.nearClipPlane = 0.3f;

                var start = new Vector3(-1f, 0.25f, 5f);
                var bend = new Vector3(0f, 0.75f, 8f);
                var end = new Vector3(1f, -0.25f, 6f);
                var expectedStartScreen = camera.WorldToViewportPoint(start);
                var expectedBendScreen = camera.WorldToViewportPoint(bend);
                var expectedEndScreen = camera.WorldToViewportPoint(end);
                var rendered = ElectricalWireView.BuildVisiblePath(
                    new[] { start, bend, end },
                    camera,
                    ElectricalWireView.WidthForArea(0.01f));
                var cameraDepths = rendered
                    .Select(point => camera.transform.InverseTransformPoint(point).z)
                    .ToArray();

                Assert.That(cameraDepths.Max() - cameraDepths.Min(), Is.LessThan(0.0001f),
                    "A wire that spans multiple depths can pass behind the cabinet panel and disappear.");
                Assert.That(Vector2.Distance(camera.WorldToViewportPoint(rendered[0]), expectedStartScreen),
                    Is.LessThan(0.0001f));
                Assert.That(Vector2.Distance(camera.WorldToViewportPoint(rendered[10]), expectedBendScreen),
                    Is.LessThan(0.0001f));
                Assert.That(Vector2.Distance(camera.WorldToViewportPoint(rendered[rendered.Length - 1]), expectedEndScreen),
                    Is.LessThan(0.0001f));
            }
            finally
            {
                Object.DestroyImmediate(cameraObject);
            }
        }
    }
}
