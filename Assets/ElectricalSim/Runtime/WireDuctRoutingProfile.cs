using System.Collections.Generic;
using UnityEngine;

namespace ElectricalSim
{
    // A runtime height field in cabinet coordinates. The imported lids identify
    // each duct opening even while hidden; route 10 mm behind their inside face.
    // Only rendered samples are added, never user bends or serialized circuit data.
    public sealed class WireDuctRoutingProfile
    {
        private const float LidClearance = 0.01f;
        private const float Transition = 0.01f;
        private readonly List<Bounds> lanes = new List<Bounds>();
        private readonly Quaternion rotation;
        private readonly Quaternion inverse;
        private readonly float baseDepth;

        public WireDuctRoutingProfile(IEnumerable<MeshFilter> lids, WireSurfacePlane surface)
        {
            rotation = surface.Rotation;
            inverse = Quaternion.Inverse(rotation);
            baseDepth = Vector3.Dot(surface.Origin, surface.Normal);
            foreach (var lid in lids)
            {
                if (lid == null || lid.sharedMesh == null) continue;
                var vertices = lid.sharedMesh.vertices;
                if (vertices.Length == 0) continue;
                var bounds = new Bounds(inverse * lid.transform.TransformPoint(vertices[0]), Vector3.zero);
                foreach (var vertex in vertices)
                    bounds.Encapsulate(inverse * lid.transform.TransformPoint(vertex));
                var depth = bounds.min.z - LidClearance;
                if (depth <= baseDepth) continue;
                bounds.center = new Vector3(bounds.center.x, bounds.center.y, depth);
                bounds.size = new Vector3(bounds.size.x, bounds.size.y, 0f);
                lanes.Add(bounds);
            }
        }

        public Vector3 Project(Vector3 point)
        {
            var local = inverse * point;
            local.z = baseDepth;
            foreach (var lane in lanes)
            {
                var edgeDistance = Mathf.Min(Mathf.Min(local.x - lane.min.x, lane.max.x - local.x),
                    Mathf.Min(local.y - lane.min.y, lane.max.y - local.y));
                var height = Mathf.Clamp01(1f + edgeDistance / Transition) * (lane.center.z - baseDepth);
                local.z = Mathf.Max(local.z, baseDepth + height);
            }
            return rotation * local;
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
            foreach (var lane in lanes)
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
            if (points.Length < 2 || lanes.Count == 0) return;
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
                foreach (var lane in lanes)
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
