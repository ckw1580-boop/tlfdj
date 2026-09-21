using System.Collections.Generic;
using UnityEngine;

namespace ElectricalSim
{
    // A runtime height field in cabinet coordinates. The imported lids identify
    // each duct opening even while hidden; normally route 10 mm behind their
    // inside face, raising locally where a mounting plate/support requires it.
    // Only rendered samples are added, never user bends or serialized circuit data.
    public sealed class WireDuctRoutingProfile
    {
        private const float LidClearance = 0.01f;
        private const float PlateClearance = 0.003f;
        private const float Transition = 0.01f;
        private readonly List<Bounds> lanes = new List<Bounds>();
        private readonly List<Bounds> regions = new List<Bounds>();
        private readonly Quaternion rotation;
        private readonly Quaternion inverse;
        private readonly float baseDepth;

        public WireDuctRoutingProfile(IEnumerable<MeshFilter> lids, WireSurfacePlane surface,
            IEnumerable<MeshFilter> mountingPlates = null)
        {
            rotation = surface.Rotation;
            inverse = Quaternion.Inverse(rotation);
            baseDepth = Vector3.Dot(surface.Origin, surface.Normal);
            foreach (var lid in lids) AddRegion(lid, true);
            if (mountingPlates != null)
                foreach (var plate in mountingPlates) AddRegion(plate, false);

            void AddRegion(MeshFilter filter, bool isLid)
            {
                if (filter == null || filter.sharedMesh == null) return;
                var vertices = filter.sharedMesh.vertices;
                if (vertices.Length == 0) return;
                var bounds = new Bounds(inverse * filter.transform.TransformPoint(vertices[0]), Vector3.zero);
                foreach (var vertex in vertices)
                    bounds.Encapsulate(inverse * filter.transform.TransformPoint(vertex));
                var depth = isLid ? bounds.min.z - LidClearance : bounds.max.z + PlateClearance;
                if (depth <= baseDepth) return;
                bounds.center = new Vector3(bounds.center.x, bounds.center.y, depth);
                bounds.size = new Vector3(bounds.size.x, bounds.size.y, 0f);
                regions.Add(bounds);
                // Only narrow ducts constrain lateral curve overshoot. Plates
                // affect depth without changing the user's route in the plane.
                if (isLid) lanes.Add(bounds);
            }
        }

        public Vector3 Project(Vector3 point)
        {
            var local = inverse * point;
            local.z = baseDepth;
            foreach (var lane in regions)
            {
                var edgeDistance = Mathf.Min(Mathf.Min(local.x - lane.min.x, lane.max.x - local.x),
                    Mathf.Min(local.y - lane.min.y, lane.max.y - local.y));
                var height = Mathf.Clamp01(1f + edgeDistance / Transition) * (lane.center.z - baseDepth);
                local.z = Mathf.Max(local.z, baseDepth + height);
            }
            return rotation * local;
        }

        // Some imported mounting panels are faces of the cabinet shell, not
        // separate objects. Pick the nearest parallel face behind each device,
        // then use that face's coplanar triangles, never the entire shell AABB.
        public void AddMountingPanelsBehind(MeshFilter shell, IEnumerable<Vector3> anchors)
        {
            if (shell == null || shell.sharedMesh == null) return;
            const float tolerance = 0.00001f;
            var mesh = shell.sharedMesh;
            var vertices = mesh.vertices;
            for (var i = 0; i < vertices.Length; i++)
                vertices[i] = inverse * shell.transform.TransformPoint(vertices[i]);
            var triangles = mesh.triangles;
            var faces = new List<Bounds>();
            var depths = new List<float>();
            foreach (var anchor in anchors)
            {
                var p = inverse * anchor;
                var nearest = baseDepth;
                for (var i = 0; i < triangles.Length; i += 3)
                {
                    var a = vertices[triangles[i]];
                    var b = vertices[triangles[i + 1]];
                    var c = vertices[triangles[i + 2]];
                    if (Mathf.Abs(a.z - b.z) > tolerance || Mathf.Abs(a.z - c.z) > tolerance ||
                        a.z <= nearest || a.z >= p.z) continue;
                    float Cross(Vector3 u, Vector3 v) => u.x * v.y - u.y * v.x;
                    var area = Cross(b - a, c - a);
                    if (Mathf.Abs(area) < 0.00000001f) continue;
                    var u = Cross(p - a, c - a) / area;
                    var v = Cross(b - a, p - a) / area;
                    if (u >= -tolerance && v >= -tolerance && u + v <= 1f + tolerance) nearest = a.z;
                }
                if (nearest <= baseDepth || depths.Exists(depth => Mathf.Abs(depth - nearest) <= tolerance)) continue;
                depths.Add(nearest);
            }
            foreach (var depth in depths)
            {
                faces.Clear();
                for (var i = 0; i < triangles.Length; i += 3)
                {
                    var a = vertices[triangles[i]];
                    var b = vertices[triangles[i + 1]];
                    var c = vertices[triangles[i + 2]];
                    if (Mathf.Abs(a.z - depth) > tolerance || Mathf.Abs(b.z - depth) > tolerance ||
                        Mathf.Abs(c.z - depth) > tolerance) continue;
                    var face = new Bounds(a, Vector3.zero);
                    face.Encapsulate(b);
                    face.Encapsulate(c);
                    faces.Add(face);
                }
                if (faces.Count == 0) continue;
                var panel = faces[0];
                foreach (var face in faces) panel.Encapsulate(face);
                panel.center = new Vector3(panel.center.x, panel.center.y, panel.max.z + PlateClearance);
                panel.size = new Vector3(panel.size.x, panel.size.y, 0f);
                regions.Add(panel);
            }
        }

        public Vector3 ConstrainSpan(Vector3 point, Vector3 start, Vector3 end)
        {
            var a = inverse * start;
            var b = inverse * end;
            var local = inverse * point;
            foreach (var lane in lanes)
            {
                bool Inside(Vector3 p) => p.x >= lane.min.x && p.x <= lane.max.x &&
                    p.y >= lane.min.y && p.y <= lane.max.y;
                if (!Inside(a) || !Inside(b)) continue;
                // Catmull-Rom corners must not bow outside a narrow duct between
                // two bends placed inside it (especially the tall side ducts).
                local.x = Mathf.Clamp(local.x, Mathf.Min(a.x, b.x), Mathf.Max(a.x, b.x));
                local.y = Mathf.Clamp(local.y, Mathf.Min(a.y, b.y), Mathf.Max(a.y, b.y));
                break;
            }
            return rotation * local;
        }

        public bool Raycast(Ray ray, WireSurfacePlane surface, out Vector3 point)
        {
            var localRay = new Ray(inverse * ray.origin, inverse * ray.direction);
            var closest = float.PositiveInfinity;
            var hit = Vector3.zero;
            // Each duct has a flat interior and four planar entrance ramps.
            // Validate against the combined height field where ducts intersect.
            void TryPlane(Vector3 normal, Vector3 anchor)
            {
                var plane = new Plane(normal, anchor);
                if (!plane.Raycast(localRay, out var distance) || distance < 0f || distance >= closest) return;
                var candidate = rotation * localRay.GetPoint(distance);
                if (Vector3.Distance(candidate, surface.Project(candidate)) > 0.0001f) return;
                closest = distance;
                hit = candidate;
            }
            TryPlane(Vector3.forward, Vector3.forward * baseDepth);
            foreach (var lane in regions)
            {
                TryPlane(Vector3.forward, lane.center);
                var slope = (lane.center.z - baseDepth) / Transition;
                TryPlane(new Vector3(-slope, 0f, 1f), new Vector3(lane.min.x, 0f, lane.center.z));
                TryPlane(new Vector3(slope, 0f, 1f), new Vector3(lane.max.x, 0f, lane.center.z));
                TryPlane(new Vector3(0f, -slope, 1f), new Vector3(0f, lane.min.y, lane.center.z));
                TryPlane(new Vector3(0f, slope, 1f), new Vector3(0f, lane.max.y, lane.center.z));
            }
            point = hit;
            return !float.IsPositiveInfinity(closest);
        }

        public void RefinePath(ref Vector3[] points, ref int[] insertionIndices)
        {
            if (points.Length < 2 || regions.Count == 0) return;
            var refined = new List<Vector3> { Project(points[0]) };
            var indices = new List<int>();
            var cuts = new List<float>();
            for (var i = 1; i < points.Length; i++)
            {
                var a = inverse * points[i - 1];
                var b = inverse * points[i];
                cuts.Clear();
                cuts.Add(1f);
                void Cut(float start, float end, float boundary)
                {
                    if (Mathf.Abs(end - start) < 0.000001f) return;
                    var t = (boundary - start) / (end - start);
                    if (t > 0.00001f && t < 0.99999f) cuts.Add(t);
                }
                foreach (var lane in regions)
                {
                    // Reject lanes that cannot affect this segment.
                    if (Mathf.Max(a.x, b.x) < lane.min.x - Transition || Mathf.Min(a.x, b.x) > lane.max.x + Transition ||
                        Mathf.Max(a.y, b.y) < lane.min.y - Transition || Mathf.Min(a.y, b.y) > lane.max.y + Transition) continue;
                    Cut(a.x, b.x, lane.min.x - Transition);
                    Cut(a.x, b.x, lane.min.x);
                    Cut(a.x, b.x, lane.max.x);
                    Cut(a.x, b.x, lane.max.x + Transition);
                    Cut(a.y, b.y, lane.min.y - Transition);
                    Cut(a.y, b.y, lane.min.y);
                    Cut(a.y, b.y, lane.max.y);
                    Cut(a.y, b.y, lane.max.y + Transition);
                }
                cuts.Sort();
                var last = 0f;
                foreach (var t in cuts)
                {
                    if (t - last < 0.00001f) continue;
                    // Include the middle of each interval for corner/intersection ramps.
                    var midpoint = Project(Vector3.Lerp(points[i - 1], points[i], (last + t) * 0.5f));
                    var endpoint = Project(Vector3.Lerp(points[i - 1], points[i], t));
                    if (Vector3.Distance(midpoint, (refined[refined.Count - 1] + endpoint) * 0.5f) > 0.0001f)
                    {
                        refined.Add(midpoint);
                        indices.Add(insertionIndices[i - 1]);
                    }
                    refined.Add(endpoint);
                    indices.Add(insertionIndices[i - 1]);
                    last = t;
                }
            }
            points = refined.ToArray();
            insertionIndices = indices.ToArray();
        }
    }
}
