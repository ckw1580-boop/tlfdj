using System.Linq;
using NUnit.Framework;
using UnityEngine;

namespace ElectricalSim.Tests
{
    public sealed class ElectricalWireViewTests
    {
        [Test]
        public void MotorSoftJumperUsesWorldSagAndPreservesManualRoutes()
        {
            var a = new WireEndpointGeometry(new Vector3(0f, 2f, 1f));
            var b = new WireEndpointGeometry(new Vector3(2f, 1f, -1f));
            var surface = new WireSurfacePlane(Vector3.zero, Vector3.back, 0.003f);
            var path = WireRenderPath.Build(a, b, null, surface, true);
            Assert.That(path.IsSoftJumper, Is.True);
            Assert.That(path.Points.First(), Is.EqualTo(a.Position));
            Assert.That(path.Points.Last(), Is.EqualTo(b.Position));
            Assert.That(path.Points[16], Is.EqualTo((a.Position + b.Position) * 0.5f + Vector3.down * 0.15f));
            Assert.That(path.InsertionIndices.All(i => i == -1), Is.True);
            var reversed = WireRenderPath.Build(b, a, null, surface, true);
            for (var i = 0; i < path.Points.Length; i++)
                Assert.That(Vector3.Distance(path.Points[i], reversed.Points[32 - i]), Is.LessThan(0.00001f));
            Assert.That(WireRenderPath.Build(a, b, new[] { Vector3.one }, surface, true).Points, Is.EqualTo(path.Points));
            Assert.That(WireRenderPath.Build(a, b, new[] { Vector3.one }, surface, false).IsSoftJumper, Is.False);
            foreach (var motor in new[] { "M1.U", "M2.V", "M_DOUBLE.W" })
            {
                Assert.That(WireRenderPath.IsMotorJumper("DuanZiPai_7.A_u1", motor, "JumperLine"), Is.True);
                Assert.That(WireRenderPath.IsMotorJumper(motor, "DuanZiPai_7.A_u1", "JumperLine"), Is.True);
                Assert.That(WireRenderPath.IsMotorJumper(motor, "DuanZiPai_7.A_u1", "ElectricalWire"), Is.False);
            }
            Assert.That(WireRenderPath.IsMotorJumper("M1.U", "M1.V", "JumperLine"), Is.False);
            foreach (var source in new[] { "FR.T1", "FR.T2", "FR.T3" })
            {
                Assert.That(WireRenderPath.IsMotorJumper(source, "M1.U", "JumperLine"), Is.True);
                Assert.That(WireRenderPath.IsMotorJumper("M1.U", source, "JumperLine"), Is.True);
                Assert.That(WireRenderPath.IsMotorJumper(source, "M1.U", "ElectricalWire"), Is.False);
            }
            Assert.That(WireRenderPath.IsMotorJumper("FR.L1", "M1.U", "JumperLine"), Is.False);
        }

        [Test]
        public void SpatialLeadsClearBodyAndKeepNeighbouringTerminalsSeparate()
        {
            var body = GameObject.CreatePrimitive(PrimitiveType.Cube);
            var helper = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            try
            {
                body.transform.localScale = new Vector3(0.1f, 0.1f, 0.04f);
                helper.name = "point";
                helper.transform.SetParent(body.transform, false);
                helper.transform.localScale = Vector3.one * 100f;
                var geometry = new WireBodyGeometry(body.transform);
                var surface = new WireSurfacePlane(Vector3.zero, Vector3.back, 0.003f);
                var a = new WireEndpointGeometry(new Vector3(-0.02f, 0.04f, -0.03f), geometry);
                var b = new WireEndpointGeometry(new Vector3(0.02f, 0.04f, -0.03f), geometry);
                var leadA = a.BuildLead(surface);
                var leadB = b.BuildLead(surface);
                Assert.That(leadA[0], Is.EqualTo(a.Position));
                Assert.That(leadA[1].z, Is.EqualTo(-0.04f).Within(0.00001f));
                Assert.That(leadA[2].y, Is.EqualTo(0.06f).Within(0.00001f));
                Assert.That(leadA[2].x, Is.EqualTo(a.Position.x).Within(0.00001f));
                Assert.That(leadB[2].x, Is.EqualTo(b.Position.x).Within(0.00001f));
                Assert.That(Mathf.Abs(surface.SignedDistance(leadA.Last())), Is.LessThan(0.00001f));
                Assert.That(a.BuildLead(surface), Is.EqualTo(leadA));
                var rotated = Quaternion.Euler(0f, 25f, 0f);
                body.transform.rotation = rotated;
                var rotatedSurface = new WireSurfacePlane(Vector3.zero, rotated * Vector3.back, 0.003f);
                var rotatedLead = new WireEndpointGeometry(rotated * a.Position, geometry).BuildLead(rotatedSurface);
                for (var i = 0; i < leadA.Length; i++)
                    Assert.That(Vector3.Distance(rotatedLead[i], rotated * leadA[i]), Is.LessThan(0.0001f));
            }
            finally { Object.DestroyImmediate(body); }
        }

        [Test]
        public void SpatialLeadHitTestingMapsToUserBends()
        {
            var body = GameObject.CreatePrimitive(PrimitiveType.Cube);
            var cameraObject = new GameObject("LeadHitCamera");
            var wireObject = new GameObject("LeadHitWire");
            try
            {
                body.transform.position = new Vector3(-1f, 0f, 0f);
                body.transform.localScale = Vector3.one * 0.1f;
                var geometry = new WireBodyGeometry(body.transform);
                var start = new WireEndpointGeometry(new Vector3(-1f, -0.04f, -0.06f), geometry);
                var end = new WireEndpointGeometry(new Vector3(1f, -0.04f, -0.06f));
                var surface = new WireSurfacePlane(Vector3.zero, Vector3.back, 0.003f);
                var connection = new WireConnection { StartPort = "A", EndPort = "B" };
                connection.Points.Add(new Vector3(0f, -0.5f, 0f));
                var wire = wireObject.AddComponent<ElectricalWireView>();
                wire.Initialize(connection, p => p == "A" ? start.Position : end.Position, null, surface,
                    p => p == "A" ? start : end);
                var camera = cameraObject.AddComponent<Camera>();
                camera.transform.position = new Vector3(0f, 0f, -4f);
                camera.pixelRect = new Rect(0f, 0f, 1600f, 900f);
                for (var span = 0; span < 2; span++)
                {
                    var point = wire.RenderPath.Trunk[span * 10 + 5];
                    Assert.That(wire.TryHitLine(camera, camera.WorldToScreenPoint(point), 5f,
                        out _, out var index, out _), Is.True);
                    Assert.That(index, Is.EqualTo(span));
                }
                var leadPoint = (wire.RenderPath.StartLead[1] + wire.RenderPath.StartLead[2]) * 0.5f;
                Assert.That(wire.TryHitLine(camera, camera.WorldToScreenPoint(leadPoint), 5f,
                    out _, out var leadIndex, out _), Is.True);
                Assert.That(leadIndex, Is.Zero);
                wire.SetSelected(true);
                Assert.That(wire.GetComponentsInChildren<WireLeadMesh>().All(m => m.Renderer.enabled), Is.True);
                var mesh = wire.GetComponentInChildren<WireLeadMesh>().GetComponent<MeshFilter>().sharedMesh;
                Assert.That(mesh.vertexCount, Is.GreaterThan(16));
                var vertices = mesh.vertices;
                Assert.That(vertices.All(p => !float.IsNaN(p.x) && !float.IsNaN(p.y) && !float.IsNaN(p.z)), Is.True);
            }
            finally
            {
                Object.DestroyImmediate(wireObject);
                Object.DestroyImmediate(cameraObject);
                Object.DestroyImmediate(body);
            }
        }
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
        public void RoutedWireKeepsTerminalDepthsAndProjectsOnlyBendPoints()
        {
            var surface = new WireSurfacePlane(new Vector3(0f, 0f, 2f), Vector3.forward, 0.003f);
            var anchors = new[]
            {
                new Vector3(-1f, 0.25f, 5f),
                new Vector3(0f, 0.75f, 8f),
                new Vector3(1f, -0.25f, 6f)
            };

            var renderedBeforeCameraMove = ElectricalWireView.BuildPlanarPath(anchors, surface);
            var renderedAfterCameraMove = ElectricalWireView.BuildPlanarPath(anchors, surface);

            Assert.That(renderedAfterCameraMove, Is.EqualTo(renderedBeforeCameraMove));
            Assert.That(renderedBeforeCameraMove[0], Is.EqualTo(anchors[0]));
            Assert.That(Vector3.Distance(renderedBeforeCameraMove[11], surface.Project(anchors[1])), Is.LessThan(0.00001f));
            foreach (var point in renderedBeforeCameraMove.Skip(1).Take(renderedBeforeCameraMove.Length - 2))
                Assert.That(Mathf.Abs(surface.SignedDistance(point)), Is.LessThan(0.0001f));
            Assert.That(renderedBeforeCameraMove[renderedBeforeCameraMove.Length - 1],
                Is.EqualTo(anchors[2]));
        }

        [Test]
        public void DirectWireKeepsOffSurfaceTerminalPositions()
        {
            var surface = new WireSurfacePlane(Vector3.zero, Vector3.back, 0.003f);
            var anchors = new[] { new Vector3(-1f, 0f, 0.2f), new Vector3(1f, 0f, -0.4f) };
            var path = ElectricalWireView.BuildPlanarPath(anchors, surface);
            var expected = new[] { anchors[0], surface.Project(anchors[0]), surface.Project(anchors[1]), anchors[1] };
            Assert.That(path.Length, Is.EqualTo(expected.Length));
            for (var i = 0; i < path.Length; i++)
                Assert.That(Vector3.Distance(path[i], expected[i]), Is.LessThan(0.00001f));
        }

        [Test]
        public void DraftKeepsPhysicalEndpointsBeforeAndAfterChangingSurface()
        {
            var draftObject = new GameObject("WireDraftTest");
            try
            {
                var start = new Vector3(-1f, 0f, 0.2f);
                var end = new Vector3(1f, 0f, -0.4f);
                var bend = new Vector3(0f, 1f, 5f);
                var draft = draftObject.AddComponent<ElectricalWireDraftView>();
                var front = new WireSurfacePlane(Vector3.zero, Vector3.forward, 0.003f);
                var rear = new WireSurfacePlane(Vector3.back, Vector3.back, 0.003f);
                draft.Initialize(() => start, null, Color.red, 0.01f, front);
                foreach (var surface in new[] { front, rear })
                {
                    draft.SetSurface(surface);
                    draft.Refresh(new[] { bend }, end);
                    var line = draft.GetComponent<LineRenderer>();
                    Assert.That(line.GetPosition(0), Is.EqualTo(start));
                    Assert.That(line.GetPosition(line.positionCount - 1), Is.EqualTo(end));
                    Assert.That(Vector3.Distance(line.GetPosition(11), surface.Project(bend)), Is.LessThan(0.00001f));
                    start += Vector3.back * 0.1f;
                }
            }
            finally
            {
                Object.DestroyImmediate(draftObject);
            }
        }

        [Test]
        public void FixedSurfaceRaycastReturnsAPlanarEditPoint()
        {
            var surface = new WireSurfacePlane(new Vector3(0f, 0f, 2f), Vector3.forward, 0.003f);

            Assert.That(surface.Raycast(new Ray(Vector3.zero, Vector3.forward), out var point), Is.True);
            Assert.That(Mathf.Abs(surface.SignedDistance(point)), Is.LessThan(0.0001f));
            Assert.That(point.z, Is.EqualTo(2.003f).Within(0.0001f));
        }

        [Test]
        public void CabinetSurfaceRejectsOutsideAndGrazingRaysAndClampsOldBends()
        {
            var surface = new WireSurfacePlane(Vector3.zero, Vector3.back, 0.003f,
                new Bounds(Vector3.zero, new Vector3(2f, 3f, 0.5f)));
            Assert.That(surface.Raycast(new Ray(new Vector3(0f, 0f, -2f), Vector3.forward), out _), Is.True);
            Assert.That(surface.Raycast(new Ray(new Vector3(3f, 0f, -2f), Vector3.forward), out _), Is.False);
            Assert.That(surface.Raycast(new Ray(new Vector3(0f, 0f, -2f), new Vector3(1f, 0f, 0.001f)), out _), Is.False);
            var path = ElectricalWireView.BuildPlanarPath(new[]
            {
                new Vector3(-0.5f, 0f, -0.2f), new Vector3(100f, 100f, 100f), new Vector3(0.5f, 0f, -0.3f)
            }, surface);
            foreach (var point in path.Skip(1).Take(path.Length - 2))
            {
                Assert.That(Mathf.Abs(point.x), Is.LessThanOrEqualTo(1.0001f));
                Assert.That(Mathf.Abs(point.y), Is.LessThanOrEqualTo(1.5001f));
                Assert.That(Mathf.Abs(surface.SignedDistance(point)), Is.LessThan(0.0001f));
            }
        }

        [Test]
        public void SelectedWireCanHitItsLineAndBendNodeInScreenSpace()
        {
            var cameraObject = new GameObject("WireHitCamera");
            var wireObject = new GameObject("WireHitView");
            try
            {
                var camera = cameraObject.AddComponent<Camera>();
                camera.transform.position = new Vector3(0f, 0f, -10f);
                camera.pixelRect = new Rect(0f, 0f, 800f, 600f);
                var surface = new WireSurfacePlane(Vector3.zero, Vector3.back, 0f);
                var connection = new WireConnection
                {
                    StartPort = "START",
                    EndPort = "END",
                    Points = new System.Collections.Generic.List<Vector3> { Vector3.zero }
                };
                var view = wireObject.AddComponent<ElectricalWireView>();
                view.Initialize(
                    connection,
                    port => port == "START" ? Vector3.left : Vector3.right,
                    null,
                    surface);
                view.SetSelected(true);
                var nodeScreen = (Vector2)camera.WorldToScreenPoint(Vector3.zero);

                Assert.That(view.TryHitNode(camera, nodeScreen, 14f, out var pointIndex), Is.True);
                Assert.That(pointIndex, Is.EqualTo(0));
                Assert.That(view.TryHitLine(
                    camera,
                    (Vector2)camera.WorldToScreenPoint(new Vector3(-0.5f, 0f, 0f)),
                    10f,
                    out var distance,
                    out var insertionIndex,
                    out var surfacePoint), Is.True);
                Assert.That(distance, Is.LessThan(0.01f));
                Assert.That(insertionIndex, Is.EqualTo(0));
                Assert.That(Mathf.Abs(surface.SignedDistance(surfacePoint)), Is.LessThan(0.0001f));
                Assert.That(view.TryHitLine(
                    camera,
                    (Vector2)camera.WorldToScreenPoint(new Vector3(0.5f, 0f, 0f)),
                    10f,
                    out _,
                    out insertionIndex,
                    out _), Is.True);
                Assert.That(insertionIndex, Is.EqualTo(1));
                Assert.That(view.HighlightRenderer.enabled, Is.True);
                Assert.That(view.HighlightRenderer.gameObject, Is.Not.SameAs(view.LineRenderer.gameObject));
            }
            finally
            {
                Object.DestroyImmediate(wireObject);
                Object.DestroyImmediate(cameraObject);
            }
        }
    }
}
