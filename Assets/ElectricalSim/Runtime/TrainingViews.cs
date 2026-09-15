using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace ElectricalSim
{
    public sealed class ElectricalPortView : MonoBehaviour
    {
        private Renderer cachedRenderer;
        private Color baseColor;
        private Transform frontElectricalAnchor;
        private Transform frontJumperAnchor;
        private Transform backElectricalAnchor;
        private Transform backJumperAnchor;
        private bool hasOriginalAnchorConfiguration;
        private bool supportsJumperAnchor = true;
        private bool jumperOnly;
        private bool electricalOnly;
        private bool wiringModeOnly;
        private bool requestedVisible;
        private bool isVisible;

        public string DeviceId { get; private set; }
        public string PortName { get; private set; }
        public string QualifiedPort => CircuitGraph.Port(DeviceId, PortName);
        public string HoverLabel { get; private set; }
        public string PhysicalAnchorId { get; private set; }
        public bool IsVisible => isVisible;
        public bool SupportsJumperAnchor => supportsJumperAnchor;
        public bool JumperOnly => jumperOnly;
        public bool ElectricalOnly => electricalOnly;
        public bool WiringModeOnly => wiringModeOnly;
        public bool UsesJumperAnchor { get; private set; }
        public Transform CurrentAnchor { get; private set; }
        public Vector3 CurrentAnchorPosition => CurrentAnchor != null ? CurrentAnchor.position : transform.position;
        public WireBodyGeometry RearWireBody { get; private set; }
        public void ConfigureRearWireBody(WireBodyGeometry body) => RearWireBody = body;
        public string MotorId { get; private set; }
        private Transform motorRoot;
        private Vector3 motorLocalOutward;
        public Vector3 MotorOutward => motorRoot != null ? motorRoot.TransformDirection(motorLocalOutward).normalized : Vector3.zero;
        public void ConfigureMotorTerminal(string id, Transform root, Vector3 outward)
        { MotorId = id; motorRoot = root; motorLocalOutward = root.InverseTransformDirection(outward); }
        public WireEndpointGeometry EndpointGeometry(Vector3 position, bool rear)
            => new WireEndpointGeometry(position, rear ? RearWireBody : null, MotorId, MotorOutward);

        public void Initialize(string deviceId, string portName, Color color)
        {
            DeviceId = deviceId;
            PortName = portName;
            HoverLabel = portName;
            PhysicalAnchorId = portName;
            gameObject.name = $"Port_{deviceId}_{portName}";
            cachedRenderer = GetComponent<Renderer>();
            baseColor = color;
            if (cachedRenderer != null) cachedRenderer.material.color = color;
        }

        public void ConfigureHover(string hoverLabel, string physicalAnchorId)
        {
            HoverLabel = string.IsNullOrWhiteSpace(hoverLabel) ? PortName : hoverLabel;
            PhysicalAnchorId = string.IsNullOrWhiteSpace(physicalAnchorId) ? PortName : physicalAnchorId;
        }

        public void ConfigureJumperOnly(bool value = true)
        {
            jumperOnly = value;
            if (value) electricalOnly = false;
            RefreshVisibility();
        }

        public void ConfigureElectricalOnly(bool value = true)
        {
            electricalOnly = value;
            if (value) jumperOnly = false;
            RefreshVisibility();
        }

        public void ConfigureWiringModeOnly(bool value = true)
        {
            wiringModeOnly = value;
            RefreshVisibility();
        }

        public void ConfigureOriginalAnchors(
            Transform frontElectrical,
            Transform frontJumper,
            Transform backElectrical,
            Transform backJumper,
            bool supportsJumper = true)
        {
            hasOriginalAnchorConfiguration = frontElectrical != null || frontJumper != null ||
                                             backElectrical != null || backJumper != null;
            supportsJumperAnchor = supportsJumper;
            frontElectricalAnchor = frontElectrical;
            frontJumperAnchor = supportsJumper && frontJumper != null ? frontJumper :
                                supportsJumper ? frontElectrical : null;
            backElectricalAnchor = backElectrical != null ? backElectrical : frontElectricalAnchor;
            backJumperAnchor = supportsJumper && backJumper != null ? backJumper :
                               supportsJumper ? backElectricalAnchor : null;
        }

        public void ApplyOriginalAnchor(TrainingViewPreset preset, bool jumper)
        {
            var anchor = GetOriginalAnchor(preset, jumper);
            UsesJumperAnchor = jumper && anchor != null;
            CurrentAnchor = anchor;
            if (anchor != null) transform.position = anchor.position;
            RefreshVisibility();
        }

        public Transform GetOriginalAnchor(TrainingViewPreset preset, bool jumper)
        {
            if (!hasOriginalAnchorConfiguration) return transform;
            return preset == TrainingViewPreset.FaultBack
                ? (jumper ? backJumperAnchor : backElectricalAnchor)
                : (jumper ? frontJumperAnchor : frontElectricalAnchor);
        }

        public void SetHighlighted(bool highlighted)
        {
            if (cachedRenderer != null)
            {
                cachedRenderer.enabled = isVisible;
                cachedRenderer.material.color = highlighted ? Color.yellow : baseColor;
            }
        }

        public void SetVisible(bool visible)
        {
            requestedVisible = visible;
            RefreshVisibility();
        }

        public void SetVisibleForMode(SimulationMode mode)
        {
            SetVisible(mode == SimulationMode.Wiring ||
                       (!wiringModeOnly && mode == SimulationMode.Fault));
        }

        private void RefreshVisibility()
        {
            var anchorAvailable = !hasOriginalAnchorConfiguration || CurrentAnchor != null;
            var lineTypeAvailable = (!jumperOnly || UsesJumperAnchor) &&
                                    (!electricalOnly || !UsesJumperAnchor);
            isVisible = requestedVisible && anchorAvailable && lineTypeAvailable;
            if (cachedRenderer != null) cachedRenderer.enabled = isVisible;
            var collider = GetComponent<Collider>();
            if (collider != null) collider.enabled = isVisible;
        }
    }

    public sealed class ElectricalDeviceView : MonoBehaviour
    {
        public string DisplayName { get; private set; }
        private Renderer[] visualRenderers = Array.Empty<Renderer>();
        private Color[] baseColors = Array.Empty<Color>();
        private Transform rotor;

        public ElectricalDeviceRuntime Runtime { get; private set; }
        public IReadOnlyList<ElectricalPortView> Ports => ports;
        private readonly List<ElectricalPortView> ports = new List<ElectricalPortView>();

        public void Initialize(ElectricalDeviceRuntime runtime, string displayName)
        {
            DisplayName = displayName;
            Runtime = runtime;
            gameObject.name = runtime.DeviceId + "_" + displayName;
            visualRenderers = GetComponentsInChildren<Renderer>(true);
            baseColors = new Color[visualRenderers.Length];
            for (var i = 0; i < visualRenderers.Length; i++) baseColors[i] = visualRenderers[i].material.color;
            runtime.VisualStateChanged += OnVisualStateChanged;
            rotor = transform.Find("Rotor");
        }

        public void AddPort(ElectricalPortView port) => ports.Add(port);

        private void Update()
        {
            if (rotor != null && Runtime != null)
            {
                rotor.Rotate(Vector3.up, Runtime.ActualSpeedRpm * 6f * Time.deltaTime, Space.Self);
            }
        }

        private void OnDestroy()
        {
            if (Runtime != null) Runtime.VisualStateChanged -= OnVisualStateChanged;
        }

        private void OnVisualStateChanged(ElectricalDeviceRuntime runtime)
        {
            for (var i = 0; i < visualRenderers.Length; i++)
            {
                var color = baseColors[i];
                if (runtime.IsActive) color = Color.Lerp(color, new Color(0.1f, 1f, 0.45f), 0.42f);
                if (runtime.IsTripped) color = Color.Lerp(color, Color.red, 0.65f);
                visualRenderers[i].material.color = color;
            }
        }
    }

    public sealed class CabinetBreakerInteractable : MonoBehaviour
    {
        private Transform handle;
        private Transform pivot;
        private Collider interactionCollider;
        private Vector3 closedLocalPosition;
        private Quaternion closedLocalRotation;
        private Vector3 openLocalPosition;
        private Quaternion openLocalRotation;
        private float animationDuration;
        private float openPositionTravelScale;
        private Coroutine animationRoutine;
        private readonly List<Renderer> highlightRenderers = new List<Renderer>();
        private Material outlineMaterial;

        public string BreakerId { get; private set; } = string.Empty;
        public string DisplayName { get; private set; } = string.Empty;
        public bool IsClosed { get; private set; } = true;
        public Transform Handle => handle;
        public Transform Pivot => pivot;
        public Collider InteractionCollider => interactionCollider;
        public float AnimationDuration => animationDuration;
        public float OpenPositionTravelScale => openPositionTravelScale;
        public bool IsHighlighted { get; private set; }
        public IReadOnlyList<Renderer> HighlightRenderers => highlightRenderers;

        public event Action<CabinetBreakerInteractable, bool> StateChanged;

        public void Initialize(
            string breakerId,
            string displayName,
            Transform switchHandle,
            Transform rotationPivot,
            Collider picker,
            float openAngleDegrees = 45f,
            float transitionSeconds = 0.2f,
            float positionTravelScale = 1f)
        {
            BreakerId = breakerId ?? string.Empty;
            DisplayName = displayName ?? BreakerId;
            handle = switchHandle;
            pivot = rotationPivot;
            interactionCollider = picker;
            animationDuration = Mathf.Max(0.01f, transitionSeconds);
            openPositionTravelScale = Mathf.Clamp01(positionTravelScale);

            if (handle == null || pivot == null)
                throw new ArgumentException("A cabinet breaker requires both a switch handle and a rotation pivot.");

            CreateHighlightOutlines();
            closedLocalPosition = handle.localPosition;
            closedLocalRotation = handle.localRotation;
            CaptureOpenPose(openAngleDegrees);
            SetClosed(true, false);
        }

        public void Toggle() => SetClosed(!IsClosed, true);

        public void ResetClosed() => SetClosed(true, false);

        public void SetHighlighted(bool highlighted)
        {
            IsHighlighted = highlighted;
            foreach (var renderer in highlightRenderers)
                if (renderer != null) renderer.enabled = highlighted;
        }

        public void SetClosed(bool closed, bool animate)
        {
            var stateChanged = IsClosed != closed;
            IsClosed = closed;

            if (animationRoutine != null)
            {
                StopCoroutine(animationRoutine);
                animationRoutine = null;
            }

            var targetPosition = closed ? closedLocalPosition : openLocalPosition;
            var targetRotation = closed ? closedLocalRotation : openLocalRotation;
            if (animate && isActiveAndEnabled)
                animationRoutine = StartCoroutine(AnimateTo(targetPosition, targetRotation));
            else
                ApplyPose(targetPosition, targetRotation);

            if (stateChanged) StateChanged?.Invoke(this, IsClosed);
        }

        private void CaptureOpenPose(float openAngleDegrees)
        {
            var parent = handle.parent;
            if (parent == null)
                throw new ArgumentException("The cabinet breaker switch handle must have a parent transform.");

            var axis = transform.TransformDirection(Vector3.right).normalized;
            var rotation = Quaternion.AngleAxis(openAngleDegrees, axis);
            var pivotPosition = pivot.position;
            var fullOpenWorldPosition = pivotPosition + rotation * (handle.position - pivotPosition);
            var openWorldPosition = Vector3.Lerp(handle.position, fullOpenWorldPosition, openPositionTravelScale);
            var openWorldRotation = rotation * handle.rotation;
            openLocalPosition = parent.InverseTransformPoint(openWorldPosition);
            openLocalRotation = Quaternion.Inverse(parent.rotation) * openWorldRotation;
        }

        private void CreateHighlightOutlines()
        {
            var shader = Resources.Load<Shader>("CabinetBreakerOutline");
            if (shader == null) shader = Shader.Find("ElectricalSim/Cabinet Breaker Outline");
            if (shader == null)
            {
                Debug.LogWarning($"Cabinet breaker {BreakerId} highlight shader is unavailable.");
                return;
            }

            outlineMaterial = new Material(shader)
            {
                name = $"Cabinet Breaker {BreakerId} Yellow Outline",
                hideFlags = HideFlags.DontSave
            };
            outlineMaterial.SetColor("_OutlineColor", new Color(1f, 0.82f, 0.04f, 1f));
            outlineMaterial.SetColor("_GlowColor", new Color(1f, 0.68f, 0.02f, 0.28f));
            outlineMaterial.SetFloat("_OutlineWidth", 0.0009f);
            outlineMaterial.SetFloat("_GlowWidth", 0.0022f);

            var sourceRenderers = handle.GetComponentsInChildren<MeshRenderer>(true);
            foreach (var sourceRenderer in sourceRenderers)
            {
                var sourceFilter = sourceRenderer.GetComponent<MeshFilter>();
                if (sourceFilter == null || sourceFilter.sharedMesh == null) continue;
                var outlineObject = new GameObject("Drag Mode Yellow Outline");
                outlineObject.layer = sourceRenderer.gameObject.layer;
                outlineObject.transform.SetParent(sourceRenderer.transform, false);
                var outlineFilter = outlineObject.AddComponent<MeshFilter>();
                outlineFilter.sharedMesh = sourceFilter.sharedMesh;
                var outlineRenderer = outlineObject.AddComponent<MeshRenderer>();
                outlineRenderer.sharedMaterial = outlineMaterial;
                outlineRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                outlineRenderer.receiveShadows = false;
                outlineRenderer.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
                outlineRenderer.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;
                outlineRenderer.enabled = false;
                highlightRenderers.Add(outlineRenderer);
            }
        }

        private void OnDestroy()
        {
            if (outlineMaterial != null) Destroy(outlineMaterial);
        }

        private IEnumerator AnimateTo(Vector3 targetPosition, Quaternion targetRotation)
        {
            var startPosition = handle.localPosition;
            var startRotation = handle.localRotation;
            var elapsed = 0f;
            while (elapsed < animationDuration)
            {
                elapsed += Time.unscaledDeltaTime;
                var progress = Mathf.Clamp01(elapsed / animationDuration);
                progress = progress * progress * (3f - 2f * progress);
                ApplyPose(
                    Vector3.LerpUnclamped(startPosition, targetPosition, progress),
                    Quaternion.SlerpUnclamped(startRotation, targetRotation, progress));
                yield return null;
            }

            ApplyPose(targetPosition, targetRotation);
            animationRoutine = null;
        }

        private void ApplyPose(Vector3 position, Quaternion rotation)
        {
            if (handle == null) return;
            handle.localPosition = position;
            handle.localRotation = rotation;
        }
    }

    public readonly struct WireSurfacePlane
    {
        public WireSurfacePlane(Vector3 surfacePoint, Vector3 normal, float offset, Bounds? bounds = null,
            WireDuctRoutingProfile ducts = null)
        {
            Normal = normal.sqrMagnitude > 0.000001f ? normal.normalized : Vector3.forward;
            SurfacePoint = surfacePoint;
            SurfaceOffset = Mathf.Max(0f, offset);
            Origin = SurfacePoint + Normal * SurfaceOffset;
            var up = Mathf.Abs(Vector3.Dot(Normal, Vector3.up)) > 0.98f ? Vector3.forward : Vector3.up;
            Rotation = Quaternion.LookRotation(Normal, up);
            SurfaceBounds = bounds;
            Ducts = ducts;
        }

        public Vector3 SurfacePoint { get; }
        public float SurfaceOffset { get; }
        public Vector3 Origin { get; }
        public Vector3 Normal { get; }
        public Quaternion Rotation { get; }
        public Bounds? SurfaceBounds { get; }
        public WireDuctRoutingProfile Ducts { get; }

        public Vector3 Project(Vector3 point)
        {
            var projected = point - Normal * Vector3.Dot(point - Origin, Normal);
            if (!SurfaceBounds.HasValue) return Ducts != null ? Ducts.Project(projected) : projected;
            var bounds = SurfaceBounds.Value;
            var right = Rotation * Vector3.right;
            var up = Rotation * Vector3.up;
            var center = bounds.center - Normal * Vector3.Dot(bounds.center - Origin, Normal);
            var delta = projected - center;
            projected = center + right * Mathf.Clamp(Vector3.Dot(delta, right), -Extent(bounds, right), Extent(bounds, right)) +
                   up * Mathf.Clamp(Vector3.Dot(delta, up), -Extent(bounds, up), Extent(bounds, up));
            return Ducts != null ? Ducts.Project(projected) : projected;
        }

        private static float Extent(Bounds bounds, Vector3 axis) =>
            Vector3.Dot(bounds.extents, new Vector3(Mathf.Abs(axis.x), Mathf.Abs(axis.y), Mathf.Abs(axis.z)));

        public float SignedDistance(Vector3 point)
        {
            return Vector3.Dot(point - Origin, Normal);
        }

        public bool Raycast(Ray ray, out Vector3 point)
        {
            if (Ducts != null && Mathf.Abs(Vector3.Dot(ray.direction.normalized, Normal)) > 0.02f)
                return Ducts.Raycast(ray, this, out point);
            var plane = new Plane(Normal, Origin);
            if (Mathf.Abs(Vector3.Dot(ray.direction.normalized, Normal)) > 0.02f &&
                plane.Raycast(ray, out var distance) && distance >= 0f)
            {
                point = ray.GetPoint(distance);
                return Vector3.Distance(point, Project(point)) < 0.0001f;
            }

            point = Vector3.zero;
            return false;
        }
    }

    public sealed class ElectricalWireView : MonoBehaviour
    {
        private const int CurveSamplesPerSpan = 10;
        private const float SelectionWidthMultiplier = 2.2f;
        private const float HandleSize = 0.014f;
        private LineRenderer line;
        private LineRenderer highlightLine;
        private WireConnection wire;
        private Func<string, Vector3> resolvePort;
        private Func<string, WireEndpointGeometry> resolveEndpoint;
        private WireRenderPath renderPath;
        private WireLeadMesh leadMesh;
        private WireLeadMesh leadHighlight;
        private WireSurfacePlane wireSurface;
        private Material wireMaterial;
        private Vector3[] renderedPoints = Array.Empty<Vector3>();
        private readonly List<GameObject> nodeHandles = new List<GameObject>();
        private bool selected;
        private int selectedPointIndex = -1;

        public WireConnection Connection => wire;
        public bool IsSelected => selected;
        public LineRenderer LineRenderer => line;
        public LineRenderer HighlightRenderer => highlightLine;
        public IReadOnlyList<Vector3> RenderedPoints => renderedPoints;
        public WireSurfacePlane Surface => wireSurface;
        public WireRenderPath RenderPath => renderPath;

        public void Initialize(
            WireConnection connection,
            Func<string, Vector3> portResolver,
            Material material,
            WireSurfacePlane surface,
            Func<string, WireEndpointGeometry> endpointResolver = null)
        {
            wire = connection;
            resolvePort = portResolver;
            resolveEndpoint = endpointResolver;
            wireSurface = surface;
            wireMaterial = material;
            transform.rotation = wireSurface.Rotation;
            line = gameObject.AddComponent<LineRenderer>();
            ConfigureLine(line, material, wire.Color, wire.Area);
            line.sortingOrder = 201;

            var highlightObject = new GameObject("WireSelectionOutline");
            highlightObject.transform.SetParent(transform, false);
            highlightLine = highlightObject.AddComponent<LineRenderer>();
            ConfigureLine(highlightLine, material, new Color(1f, 0.86f, 0.05f, 1f), wire.Area);
            highlightLine.startWidth *= SelectionWidthMultiplier;
            highlightLine.endWidth = highlightLine.startWidth;
            highlightLine.sortingOrder = 200;
            highlightLine.enabled = false;
            Refresh();
        }

        public void Refresh()
        {
            if (line == null || wire == null) return;
            renderPath = WireRenderPath.Build(
                resolveEndpoint != null ? resolveEndpoint(wire.StartPort) : new WireEndpointGeometry(resolvePort(wire.StartPort)),
                resolveEndpoint != null ? resolveEndpoint(wire.EndPort) : new WireEndpointGeometry(resolvePort(wire.EndPort)),
                wire.Points, wireSurface, WireRenderPath.IsMotorJumper(wire.StartPort, wire.EndPort, wire.LineType));
            renderedPoints = renderPath.Points;
            var linePoints = renderPath.HasSpatialLeads ? renderPath.Trunk : renderedPoints;
            ApplyPositions(line, linePoints);
            ApplyPositions(highlightLine, linePoints);
            if (renderPath.HasSpatialLeads)
            {
                if (leadMesh == null) leadMesh = CreateLeadMesh("WireEndpointLeads", 201);
                if (leadHighlight == null) leadHighlight = CreateLeadMesh("WireEndpointLeadHighlight", 200);
                leadMesh.Refresh(renderPath, WidthForArea(wire.Area), wire.Color);
                leadHighlight.Refresh(renderPath, WidthForArea(wire.Area) * SelectionWidthMultiplier, new Color(1f, 0.86f, 0.05f, 1f));
            }
            if (leadMesh != null) leadMesh.Renderer.enabled = renderPath.HasSpatialLeads;
            if (leadHighlight != null) leadHighlight.Renderer.enabled = selected && renderPath.HasSpatialLeads;
            RefreshNodeHandles();
        }

        private WireLeadMesh CreateLeadMesh(string objectName, int order)
        {
            var child = new GameObject(objectName);
            child.transform.SetParent(transform, false);
            child.layer = gameObject.layer;
            var result = child.AddComponent<WireLeadMesh>();
            result.Initialize(line.sharedMaterial, order);
            return result;
        }

        public void SetSurface(WireSurfacePlane surface)
        {
            wireSurface = surface;
            transform.rotation = wireSurface.Rotation;
            Refresh();
        }

        public void SetSelected(bool value, int pointIndex = -1)
        {
            selected = value;
            selectedPointIndex = value && wire != null
                ? Mathf.Clamp(pointIndex, -1, wire.Points.Count - 1)
                : -1;
            if (highlightLine != null) highlightLine.enabled = selected;
            if (leadHighlight != null) leadHighlight.Renderer.enabled = selected && renderPath.HasSpatialLeads;
            RefreshNodeHandles();
        }

        public void SetSelectedPoint(int pointIndex)
        {
            if (!selected || wire == null) return;
            selectedPointIndex = Mathf.Clamp(pointIndex, -1, wire.Points.Count - 1);
            RefreshNodeHandles();
        }

        public bool TryHitNode(Camera camera, Vector2 screenPosition, float maximumDistance, out int pointIndex)
        {
            pointIndex = -1;
            if (!selected || wire == null || camera == null || renderPath.IsSoftJumper) return false;

            var bestDistance = maximumDistance;
            for (var i = 0; i < wire.Points.Count; i++)
            {
                var screenPoint = camera.WorldToScreenPoint(wireSurface.Project(wire.Points[i]));
                if (screenPoint.z <= 0f) continue;
                var distance = Vector2.Distance(screenPosition, screenPoint);
                if (distance > bestDistance) continue;
                bestDistance = distance;
                pointIndex = i;
            }

            return pointIndex >= 0;
        }

        public bool TryHitLine(
            Camera camera,
            Vector2 screenPosition,
            float maximumDistance,
            out float screenDistance,
            out int insertionIndex,
            out Vector3 surfacePoint)
        {
            screenDistance = float.PositiveInfinity;
            insertionIndex = -1;
            surfacePoint = Vector3.zero;
            if (camera == null || renderedPoints == null || renderedPoints.Length < 2) return false;

            var closestSegment = -1;
            for (var i = 0; i < renderedPoints.Length - 1; i++)
            {
                var start = camera.WorldToScreenPoint(renderedPoints[i]);
                var end = camera.WorldToScreenPoint(renderedPoints[i + 1]);
                if (start.z <= 0f || end.z <= 0f) continue;
                var distance = DistanceToScreenSegment(screenPosition, start, end);
                if (distance >= screenDistance) continue;
                screenDistance = distance;
                closestSegment = i;
            }

            if (closestSegment < 0 || screenDistance > maximumDistance)
                return false;

            insertionIndex = renderPath.InsertionIndices[closestSegment];
            if (!wireSurface.Raycast(camera.ScreenPointToRay(screenPosition), out surfacePoint))
                surfacePoint = wireSurface.Project((renderedPoints[closestSegment] + renderedPoints[closestSegment + 1]) * 0.5f);
            surfacePoint = wireSurface.Project(surfacePoint);
            return true;
        }

        public static float WidthForArea(float area)
        {
            // The reference application's cable is visibly narrower than its
            // green terminal marker (roughly one third to one half as wide).
            return Mathf.Clamp(area * 0.35f, 0.0025f, 0.025f);
        }

        public static Vector3[] BuildSmoothedPath(IReadOnlyList<Vector3> anchors, int samplesPerSpan = CurveSamplesPerSpan)
        {
            if (anchors == null || anchors.Count == 0) return Array.Empty<Vector3>();
            if (anchors.Count <= 2)
            {
                var direct = new Vector3[anchors.Count];
                for (var i = 0; i < anchors.Count; i++) direct[i] = anchors[i];
                return direct;
            }

            samplesPerSpan = Mathf.Max(1, samplesPerSpan);
            var points = new List<Vector3>((anchors.Count - 1) * samplesPerSpan + 1);
            for (var span = 0; span < anchors.Count - 1; span++)
            {
                var p0 = anchors[Mathf.Max(0, span - 1)];
                var p1 = anchors[span];
                var p2 = anchors[span + 1];
                var p3 = anchors[Mathf.Min(anchors.Count - 1, span + 2)];
                for (var sample = 0; sample < samplesPerSpan; sample++)
                {
                    var t = sample / (float)samplesPerSpan;
                    var t2 = t * t;
                    var t3 = t2 * t;
                    points.Add(0.5f * ((2f * p1) +
                                       (-p0 + p2) * t +
                                       (2f * p0 - 5f * p1 + 4f * p2 - p3) * t2 +
                                       (-p0 + 3f * p1 - 3f * p2 + p3) * t3));
                }
            }
            points.Add(anchors[anchors.Count - 1]);
            return points.ToArray();
        }

        public static Vector3[] BuildPlanarPath(
            IReadOnlyList<Vector3> anchors,
            WireSurfacePlane surface,
            int samplesPerSpan = CurveSamplesPerSpan)
        {
            if (anchors == null || anchors.Count == 0) return Array.Empty<Vector3>();
            var routedAnchors = new Vector3[anchors.Count];
            for (var i = 0; i < anchors.Count; i++)
            {
                routedAnchors[i] = surface.Project(anchors[i]);
            }
            var planarPath = BuildSmoothedPath(routedAnchors, samplesPerSpan);
            // Keep the trunk on the cabinet face, with short normal leads to the
            // physical terminals. Terminal depth must not bend the trunk into space.
            var result = new Vector3[planarPath.Length + 2];
            result[0] = anchors[0];
            for (var i = 0; i < planarPath.Length; i++) result[i + 1] = surface.Project(planarPath[i]);
            result[result.Length - 1] = anchors[anchors.Count - 1];
            return result;
        }

        internal static void ConfigureLine(LineRenderer renderer, Material material, Color color, float area)
        {
            if (material != null) renderer.sharedMaterial = material;
            else
            {
                var shader = Shader.Find("Sprites/Default");
                if (shader != null) renderer.material = new Material(shader);
            }
            renderer.startColor = color;
            renderer.endColor = color;
            renderer.startWidth = WidthForArea(area);
            renderer.endWidth = renderer.startWidth;
            renderer.numCornerVertices = 6;
            renderer.numCapVertices = 8;
            renderer.useWorldSpace = true;
            renderer.alignment = LineAlignment.TransformZ;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
            renderer.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;
        }

        internal static void ApplyPath(
            LineRenderer renderer,
            IReadOnlyList<Vector3> anchors,
            WireSurfacePlane surface)
        {
            var points = BuildPlanarPath(anchors, surface);
            ApplyPositions(renderer, points);
        }

        private static void ApplyPositions(LineRenderer renderer, IReadOnlyList<Vector3> points)
        {
            if (renderer == null) return;
            renderer.positionCount = points.Count;
            for (var i = 0; i < points.Count; i++) renderer.SetPosition(i, points[i]);
        }

        private void RefreshNodeHandles()
        {
            var required = selected && wire != null && !renderPath.IsSoftJumper ? wire.Points.Count : 0;
            while (nodeHandles.Count < required) nodeHandles.Add(CreateNodeHandle(nodeHandles.Count));

            for (var i = 0; i < nodeHandles.Count; i++)
            {
                var handle = nodeHandles[i];
                var active = i < required;
                handle.SetActive(active);
                if (!active) continue;
                handle.name = $"WireNode_{i}";
                handle.transform.SetPositionAndRotation(
                    wireSurface.Project(wire.Points[i]) + wireSurface.Normal * 0.0003f,
                    wireSurface.Rotation);
                var renderer = handle.GetComponent<MeshRenderer>();
                if (renderer == null) continue;
                var block = new MaterialPropertyBlock();
                renderer.GetPropertyBlock(block);
                block.SetColor("_Color", i == selectedPointIndex ? Color.white : new Color(1f, 0.86f, 0.05f, 1f));
                renderer.SetPropertyBlock(block);
            }
        }

        private GameObject CreateNodeHandle(int index)
        {
            var handle = GameObject.CreatePrimitive(PrimitiveType.Quad);
            handle.name = $"WireNode_{index}";
            handle.layer = 2;
            handle.transform.SetParent(transform, true);
            handle.transform.localScale = Vector3.one * HandleSize;
            var collider = handle.GetComponent<Collider>();
            if (collider != null)
            {
                collider.enabled = false;
                if (Application.isPlaying) Destroy(collider);
                else DestroyImmediate(collider);
            }

            var renderer = handle.GetComponent<MeshRenderer>();
            if (renderer != null)
            {
                if (wireMaterial != null) renderer.sharedMaterial = wireMaterial;
                renderer.sortingOrder = 202;
                renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                renderer.receiveShadows = false;
                renderer.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
                renderer.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;
            }
            return handle;
        }

        private static float DistanceToScreenSegment(Vector2 point, Vector2 start, Vector2 end)
        {
            var segment = end - start;
            if (segment.sqrMagnitude < 0.0001f) return Vector2.Distance(point, start);
            var t = Mathf.Clamp01(Vector2.Dot(point - start, segment) / segment.sqrMagnitude);
            return Vector2.Distance(point, start + segment * t);
        }
    }

    public sealed class ElectricalWireDraftView : MonoBehaviour
    {
        private LineRenderer line;
        private Func<Vector3> resolveStart;
        private WireSurfacePlane wireSurface;
        private Func<WireEndpointGeometry> resolveEndpoint;
        private WireLeadMesh leadMesh;
        private Color color;
        private float area;
        public WireRenderPath RenderPath { get; private set; }

        public void Initialize(
            Func<Vector3> startResolver,
            Material material,
            Color color,
            float area,
            WireSurfacePlane surface,
            Func<WireEndpointGeometry> endpointResolver = null)
        {
            resolveStart = startResolver;
            resolveEndpoint = endpointResolver;
            this.color = color;
            this.area = area;
            wireSurface = surface;
            transform.rotation = wireSurface.Rotation;
            line = gameObject.AddComponent<LineRenderer>();
            ElectricalWireView.ConfigureLine(line, material, color, area);
            line.sortingOrder = 201;
        }

        public void Refresh(IReadOnlyList<Vector3> bendPoints, Vector3 cursorPosition, WireEndpointGeometry? endGeometry = null,
            bool softJumper = false)
        {
            if (line == null || resolveStart == null) return;
            RenderPath = WireRenderPath.Build(resolveEndpoint != null ? resolveEndpoint() : new WireEndpointGeometry(resolveStart()),
                endGeometry ?? new WireEndpointGeometry(cursorPosition), bendPoints, wireSurface, softJumper);
            var points = RenderPath.HasSpatialLeads ? RenderPath.Trunk : RenderPath.Points;
            line.positionCount = points.Length;
            line.SetPositions(points);
            if (RenderPath.HasSpatialLeads)
            {
                if (leadMesh == null)
                {
                    var child = new GameObject("WireDraftEndpointLeads");
                    child.transform.SetParent(transform, false);
                    leadMesh = child.AddComponent<WireLeadMesh>();
                    leadMesh.Initialize(line.sharedMaterial, 201);
                }
                leadMesh.Refresh(RenderPath, ElectricalWireView.WidthForArea(area), color);
            }
            if (leadMesh != null) leadMesh.Renderer.enabled = line.enabled && RenderPath.HasSpatialLeads;
        }

        public void SetVisible(bool visible)
        {
            if (line != null) line.enabled = visible;
            if (leadMesh != null) leadMesh.Renderer.enabled = visible && RenderPath != null && RenderPath.HasSpatialLeads;
        }

        public void SetSurface(WireSurfacePlane surface)
        {
            wireSurface = surface;
            transform.rotation = wireSurface.Rotation;
        }
    }
}
