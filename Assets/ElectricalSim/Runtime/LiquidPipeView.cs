using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace ElectricalSim
{
    [Serializable]
    public sealed class LiquidPipeRoute
    {
        public string name;
        public float radius;
        public Vector3[] points;
        public Vector3 valve;
    }
    [Serializable]
    public sealed class LiquidPipeRoutes { public LiquidPipeRoute[] routes; }

    public sealed class LiquidPipeView : MonoBehaviour
    {
        private Mesh mesh, jetMesh;
        private Material material;
        private MaterialPropertyBlock properties;
        private MeshRenderer body, jet;
        private Transform jetRoot;
        private Vector3 outlet;
        public LiquidStreamRuntime Stream { get; private set; }
        public PipeFlowState State => Stream.Pipe;
        public Renderer Body => body;
        public Renderer Jet => jet;
        public Vector3 Outlet => outlet;

        public void Initialize(LiquidPipeRoute route, Color color, LiquidStreamRuntime stream)
        {
            // Coordinates are measured in EnvironmentBench space, from the authored
            // Line001..006 mesh rings (including each elbow), not screen positions.
            Stream = stream;
            properties = new MaterialPropertyBlock();
            material = new Material(Resources.Load<Shader>("PipeLiquid")) { name = route.name + " pipe liquid" };
            material.SetColor("_Color", color);
            mesh = MakeTube(route.points, route.radius);
            gameObject.AddComponent<MeshFilter>().sharedMesh = mesh;
            body = gameObject.AddComponent<MeshRenderer>();
            body.sharedMaterial = material;
            body.shadowCastingMode = ShadowCastingMode.Off;
            body.receiveShadows = false;
            outlet = route.points[route.points.Length - 1];
            jetRoot = new GameObject(route.name + " outlet stream").transform;
            jetRoot.SetParent(transform, false);
            jetRoot.localPosition = outlet;
            jetMesh = MakeTube(new[] { Vector3.zero, Vector3.down }, route.radius * 0.82f);
            jetRoot.gameObject.AddComponent<MeshFilter>().sharedMesh = jetMesh;
            jet = jetRoot.gameObject.AddComponent<MeshRenderer>();
            jet.sharedMaterial = material;
            jet.shadowCastingMode = ShadowCastingMode.Off;
            jet.receiveShadows = false;
            Refresh(outlet.y);
        }

        public void SetColor(Color color) => material.SetColor("_Color", color);
        public void Refresh(float surfaceY)
        {
            body.enabled = State.Front > 0 && (State.UpstreamOpacity > 0 || State.DownstreamOpacity > 0);
            properties.SetFloat("_Front", State.Front);
            properties.SetFloat("_Valve", State.ValveDistance);
            properties.SetFloat("_UpOpacity", State.UpstreamOpacity);
            properties.SetFloat("_DownOpacity", State.DownstreamOpacity);
            properties.SetFloat("_UpPhase", State.UpstreamPhase);
            properties.SetFloat("_DownPhase", State.DownstreamPhase);
            body.SetPropertyBlock(properties);
            jet.enabled = State.Front >= State.Length && State.DownstreamOpacity > 0 && outlet.y > surfaceY && Stream.JetFront > 0;
            jetRoot.localScale = new Vector3(1, Mathf.Max(0.001f, Mathf.Min(Stream.JetFront, outlet.y - surfaceY)), 1);
            properties.SetFloat("_Front", 2);
            properties.SetFloat("_Valve", 2);
            properties.SetFloat("_UpOpacity", State.DownstreamOpacity);
            properties.SetFloat("_UpPhase", State.DownstreamPhase);
            jet.SetPropertyBlock(properties);
        }

        private static Mesh MakeTube(Vector3[] source, float radius)
        {
            // Subdivide long straight sections so the clipped front is smooth.
            var points = new List<Vector3> { source[0] };
            for (var i = 1; i < source.Length; i++)
            {
                var steps = Mathf.Max(1, Mathf.CeilToInt(Vector3.Distance(source[i - 1], source[i]) / 0.04f));
                for (var j = 1; j <= steps; j++) points.Add(Vector3.Lerp(source[i - 1], source[i], (float)j / steps));
            }
            const int sides = 16;
            var vertices = new Vector3[points.Count * (sides + 1)];
            var normals = new Vector3[vertices.Length];
            var uv = new Vector2[vertices.Length];
            var triangles = new int[(points.Count - 1) * sides * 6];
            var distance = 0f;
            for (var i = 0; i < points.Count; i++)
            {
                if (i > 0) distance += Vector3.Distance(points[i], points[i - 1]);
                var tangent = (points[Mathf.Min(i + 1, points.Count - 1)] - points[Mathf.Max(i - 1, 0)]).normalized;
                var side = Vector3.Cross(tangent, Vector3.forward).normalized;
                if (side.sqrMagnitude < 0.01f) side = Vector3.right;
                var other = Vector3.Cross(tangent, side).normalized;
                for (var j = 0; j <= sides; j++)
                {
                    var angle = j * Mathf.PI * 2 / sides;
                    var n = side * Mathf.Cos(angle) + other * Mathf.Sin(angle);
                    var k = i * (sides + 1) + j;
                    vertices[k] = points[i] + n * radius; normals[k] = n;
                    uv[k] = new Vector2(distance, (float)j / sides);
                    if (i == points.Count - 1 || j == sides) continue;
                    var t = (i * sides + j) * 6;
                    triangles[t] = k; triangles[t + 1] = k + 1; triangles[t + 2] = k + sides + 1;
                    triangles[t + 3] = k + 1; triangles[t + 4] = k + sides + 2; triangles[t + 5] = k + sides + 1;
                }
            }
            var result = new Mesh { name = "Liquid tube", vertices = vertices, normals = normals, uv = uv, triangles = triangles };
            result.RecalculateBounds();
            return result;
        }

        private void OnDestroy()
        {
            if (mesh != null) Destroy(mesh);
            if (jetMesh != null) Destroy(jetMesh);
            if (material != null) Destroy(material);
        }
    }
}
