using System;
using System.Collections.Generic;
using UnityEngine;

namespace ElectricalSim
{
    // Runtime-only geometry; never serialized into the circuit or CC3D files.
    public sealed class WireBodyGeometry
    {
        private readonly Transform root;
        private readonly Vector3[] localVertices;
        private Matrix4x4 cachedMatrix;
        private Quaternion cachedRotation;
        private Bounds cachedBounds;
        private bool cached;

        public WireBodyGeometry(Transform modelRoot)
        {
            root = modelRoot;
            var vertices = new List<Vector3>();
            foreach (var filter in root.GetComponentsInChildren<MeshFilter>(true))
            {
                var renderer = filter.GetComponent<MeshRenderer>();
                if (filter.sharedMesh == null || renderer == null || !renderer.enabled ||
                    filter.GetComponent<TextMesh>() != null || IsHelper(filter.transform)) continue;
                foreach (var point in filter.sharedMesh.vertices)
                    vertices.Add(root.InverseTransformPoint(filter.transform.TransformPoint(point)));
            }
            localVertices = vertices.ToArray();
        }

        private bool IsHelper(Transform item)
        {
            for (var current = item; current != null; current = current.parent)
            {
                if (current.GetComponent<ElectricalPortView>() != null ||
                    current.name.Equals("point", StringComparison.OrdinalIgnoreCase) ||
                    current.name.Equals("picker", StringComparison.OrdinalIgnoreCase)) return true;
                if (current == root) break;
            }
            return false;
        }

        public bool TryGetBounds(WireSurfacePlane surface, out Bounds bounds)
        {
            bounds = default;
            if (root == null || localVertices.Length == 0) return false;
            var matrix = root.localToWorldMatrix;
            if (!cached || matrix != cachedMatrix || surface.Rotation != cachedRotation)
            {
                var inverse = Quaternion.Inverse(surface.Rotation);
                cachedBounds = new Bounds(inverse * matrix.MultiplyPoint3x4(localVertices[0]), Vector3.zero);
                for (var i = 1; i < localVertices.Length; i++)
                    cachedBounds.Encapsulate(inverse * matrix.MultiplyPoint3x4(localVertices[i]));
                cachedMatrix = matrix;
                cachedRotation = surface.Rotation;
                cached = true;
            }
            bounds = cachedBounds;
            return true;
        }
    }

    public readonly struct WireEndpointGeometry
    {
        public WireEndpointGeometry(Vector3 position, WireBodyGeometry body = null)
        {
            Position = position;
            Body = body;
        }

        public Vector3 Position { get; }
        public WireBodyGeometry Body { get; }

        public Vector3[] BuildLead(WireSurfacePlane surface)
        {
            if (Body == null || !Body.TryGetBounds(surface, out var bounds))
                return new[] { Position, surface.Project(Position) };

            const float clearance = 0.01f;
            var terminal = Quaternion.Inverse(surface.Rotation) * Position;
            var outside = terminal;
            outside.z = Mathf.Max(terminal.z, bounds.max.z) + clearance;
            var exit = outside;
            // Stable tie order: top, bottom, left, right. Keep the other coordinate
            // unchanged so neighbouring terminals never converge on an edge centre.
            var distances = new[] { Mathf.Abs(bounds.max.y - terminal.y), Mathf.Abs(terminal.y - bounds.min.y),
                Mathf.Abs(terminal.x - bounds.min.x), Mathf.Abs(bounds.max.x - terminal.x) };
            var edge = 0;
            for (var i = 1; i < distances.Length; i++)
                if (distances[i] < distances[edge] - 0.00001f) edge = i;
            if (edge == 0) exit.y = bounds.max.y + clearance;
            else if (edge == 1) exit.y = bounds.min.y - clearance;
            else if (edge == 2) exit.x = bounds.min.x - clearance;
            else exit.x = bounds.max.x + clearance;
            var exitWorld = surface.Rotation * exit;
            return new[] { Position, surface.Rotation * outside, exitWorld, surface.Project(exitWorld) };
        }
    }

    public sealed class WireRenderPath
    {
        public Vector3[] Points;
        public Vector3[] Trunk;
        public Vector3[] StartLead;
        public Vector3[] EndLead;
        public int[] InsertionIndices;
        public bool HasSpatialLeads;
        public bool IsSoftJumper;

        public static bool IsMotorJumper(string start, string end, string lineType)
        {
            var jumper = (lineType ?? "").IndexOf("jumper", StringComparison.OrdinalIgnoreCase) >= 0 ||
                         (lineType ?? "").IndexOf("rope", StringComparison.OrdinalIgnoreCase) >= 0;
            return jumper && ((IsMotor(start) && IsMotorSource(end)) || (IsMotor(end) && IsMotorSource(start)));
        }

        private static bool IsMotorSource(string port) => port != null &&
            (port.StartsWith("DuanZiPai_7.", StringComparison.Ordinal) || port == "FR.T1" || port == "FR.T2" || port == "FR.T3");
        private static bool IsMotor(string port) => port != null &&
            (port.StartsWith("M1.", StringComparison.Ordinal) || port.StartsWith("M2.", StringComparison.Ordinal) ||
             port.StartsWith("M_DOUBLE.", StringComparison.Ordinal));

        public static WireRenderPath Build(WireEndpointGeometry start, WireEndpointGeometry end,
            IReadOnlyList<Vector3> bends, WireSurfacePlane surface, bool softJumper = false)
        {
            // Motor leads must never fall back to cabinet projection, including
            // old saves and routes with accidental blank-space clicks.
            if (softJumper)
            {
                const int segments = 32;
                var curve = new Vector3[segments + 1];
                var curveIndices = new int[segments];
                var sag = Mathf.Min(Vector3.Distance(start.Position, end.Position) * 0.15f, 0.15f);
                for (var i = 0; i <= segments; i++)
                {
                    var t = i / (float)segments;
                    curve[i] = Vector3.Lerp(start.Position, end.Position, t) + Vector3.down * (4f * sag * t * (1f - t));
                    if (i < segments) curveIndices[i] = -1;
                }
                return new WireRenderPath { Points = curve, StartLead = curve, EndLead = Array.Empty<Vector3>(),
                    Trunk = Array.Empty<Vector3>(), InsertionIndices = curveIndices, HasSpatialLeads = true, IsSoftJumper = true };
            }
            var result = new WireRenderPath
            {
                StartLead = start.BuildLead(surface), EndLead = end.BuildLead(surface),
                HasSpatialLeads = start.Body != null || end.Body != null
            };
            var count = bends?.Count ?? 0;
            var anchors = new List<Vector3> { result.StartLead[result.StartLead.Length - 1] };
            for (var i = 0; i < count; i++) anchors.Add(surface.Project(bends[i]));
            anchors.Add(result.EndLead[result.EndLead.Length - 1]);
            result.Trunk = ElectricalWireView.BuildSmoothedPath(anchors);
            for (var i = 0; i < result.Trunk.Length; i++) result.Trunk[i] = surface.Project(result.Trunk[i]);
            var points = new List<Vector3>(result.StartLead);
            var indices = new List<int>();
            for (var i = 1; i < result.StartLead.Length; i++) indices.Add(0);
            for (var i = 1; i < result.Trunk.Length; i++)
            {
                points.Add(result.Trunk[i]);
                indices.Add(count == 0 ? 0 : Mathf.Min((i - 1) / 10, count));
            }
            for (var i = result.EndLead.Length - 2; i >= 0; i--)
            {
                points.Add(result.EndLead[i]);
                indices.Add(count);
            }
            result.Points = points.ToArray();
            result.InsertionIndices = indices.ToArray();
            return result;
        }
    }

    public sealed class WireLeadMesh : MonoBehaviour
    {
        private Mesh mesh;
        private MeshRenderer meshRenderer;
        private WireRenderPath previousPath;
        private Matrix4x4 previousMatrix;
        private float previousWidth;
        private Color previousColor;
        public MeshRenderer Renderer => meshRenderer;

        public void Initialize(Material material, int order)
        {
            mesh = new Mesh { name = "Wire Endpoint Tubes" };
            mesh.MarkDynamic();
            gameObject.AddComponent<MeshFilter>().sharedMesh = mesh;
            meshRenderer = gameObject.AddComponent<MeshRenderer>();
            meshRenderer.sharedMaterial = material;
            meshRenderer.sortingOrder = order;
            meshRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            meshRenderer.receiveShadows = false;
        }

        public void Refresh(WireRenderPath path, float width, Color color)
        {
            // The controller refreshes wires each frame. Rebuild tubes only when
            // their geometry/style changes, not when merely looking at the cabinet.
            var matrix = transform.localToWorldMatrix;
            if (previousPath != null && previousWidth == width && previousColor == color && previousMatrix == matrix &&
                SamePoints(previousPath.StartLead, path.StartLead) && SamePoints(previousPath.EndLead, path.EndLead)) return;
            previousPath = path;
            previousWidth = width;
            previousColor = color;
            previousMatrix = matrix;
            var vertices = new List<Vector3>();
            var triangles = new List<int>();
            AddTube(path.StartLead, width * 0.5f, vertices, triangles);
            AddTube(path.EndLead, width * 0.5f, vertices, triangles);
            mesh.Clear();
            mesh.SetVertices(vertices);
            mesh.SetTriangles(triangles, 0);
            var colors = new Color[vertices.Count];
            for (var i = 0; i < colors.Length; i++) colors[i] = color;
            mesh.colors = colors;
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
        }

        private static bool SamePoints(IReadOnlyList<Vector3> a, IReadOnlyList<Vector3> b)
        {
            if (a.Count != b.Count) return false;
            for (var i = 0; i < a.Count; i++) if (a[i] != b[i]) return false;
            return true;
        }

        private void AddTube(IReadOnlyList<Vector3> path, float radius, List<Vector3> vertices, List<int> triangles)
        {
            const int sides = 8;
            var points = new List<Vector3>();
            foreach (var p in path)
                if (points.Count == 0 || Vector3.Distance(points[points.Count - 1], p) > 0.000001f) points.Add(p);
            if (points.Count < 2) return;
            var first = vertices.Count;
            var previousRight = Vector3.zero;
            for (var i = 0; i < points.Count; i++)
            {
                var tangent = (points[Mathf.Min(i + 1, points.Count - 1)] - points[Mathf.Max(i - 1, 0)]).normalized;
                var right = Vector3.ProjectOnPlane(previousRight, tangent).normalized;
                if (right.sqrMagnitude < 0.5f)
                    right = Vector3.Cross(tangent, Mathf.Abs(Vector3.Dot(tangent, Vector3.up)) > 0.95f ? Vector3.right : Vector3.up).normalized;
                var up = Vector3.Cross(tangent, right).normalized;
                previousRight = right;
                for (var side = 0; side < sides; side++)
                {
                    var angle = side * Mathf.PI * 2f / sides;
                    vertices.Add(transform.InverseTransformPoint(points[i] + radius * (right * Mathf.Cos(angle) + up * Mathf.Sin(angle))));
                    if (i == 0) continue;
                    var a = first + (i - 1) * sides + side;
                    var b = first + (i - 1) * sides + (side + 1) % sides;
                    triangles.AddRange(new[] { a, b, b + sides, a, b + sides, a + sides });
                }
            }
            var capStart = vertices.Count;
            vertices.Add(transform.InverseTransformPoint(points[0]));
            var capEnd = vertices.Count;
            vertices.Add(transform.InverseTransformPoint(points[points.Count - 1]));
            var last = first + (points.Count - 1) * sides;
            for (var side = 0; side < sides; side++)
                triangles.AddRange(new[] { capStart, first + (side + 1) % sides, first + side,
                    capEnd, last + side, last + (side + 1) % sides });
        }

        private void OnDestroy()
        {
            if (mesh == null) return;
            if (Application.isPlaying) Destroy(mesh);
            else DestroyImmediate(mesh);
        }
    }
}
