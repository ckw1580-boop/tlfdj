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
            Transform anchor;
            if (preset == TrainingViewPreset.FaultBack)
                anchor = jumper ? backJumperAnchor : backElectricalAnchor;
            else
                anchor = jumper ? frontJumperAnchor : frontElectricalAnchor;
            UsesJumperAnchor = jumper && anchor != null;
            CurrentAnchor = anchor;
            if (anchor != null) transform.position = anchor.position;
            RefreshVisibility();
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
        private Renderer[] visualRenderers = Array.Empty<Renderer>();
        private Color[] baseColors = Array.Empty<Color>();
        private Transform rotor;

        public ElectricalDeviceRuntime Runtime { get; private set; }
        public IReadOnlyList<ElectricalPortView> Ports => ports;
        private readonly List<ElectricalPortView> ports = new List<ElectricalPortView>();

        public void Initialize(ElectricalDeviceRuntime runtime, string displayName)
        {
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
            if (rotor != null && Runtime != null && Runtime.MotorDirection != MotorDirection.Stopped)
            {
                var sign = Runtime.MotorDirection == MotorDirection.Reverse ? -1f : 1f;
                var speed = Runtime.MotorDirection == MotorDirection.Braking ? 120f : 720f;
                rotor.Rotate(Vector3.forward, sign * speed * Time.deltaTime, Space.Self);
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

    public sealed class ElectricalWireView : MonoBehaviour
    {
        private const int CurveSamplesPerSpan = 10;
        private LineRenderer line;
        private WireConnection wire;
        private Func<string, Vector3> resolvePort;

        public void Initialize(WireConnection connection, Func<string, Vector3> portResolver, Material material)
        {
            wire = connection;
            resolvePort = portResolver;
            line = gameObject.AddComponent<LineRenderer>();
            ConfigureLine(line, material, wire.Color, wire.Area);
            Refresh();
        }

        public void Refresh()
        {
            if (line == null || wire == null) return;
            var anchors = new List<Vector3>(wire.Points.Count + 2)
            {
                resolvePort(wire.StartPort)
            };
            anchors.AddRange(wire.Points);
            anchors.Add(resolvePort(wire.EndPort));
            ApplyPath(line, anchors);
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

        public static Vector3[] BuildVisiblePath(
            IReadOnlyList<Vector3> anchors,
            Camera camera,
            float surfaceOffset,
            int samplesPerSpan = CurveSamplesPerSpan)
        {
            if (anchors == null || anchors.Count == 0) return Array.Empty<Vector3>();
            if (camera == null) return BuildSmoothedPath(anchors, samplesPerSpan);

            var viewportAnchors = new Vector3[anchors.Count];
            var nearestDepth = float.PositiveInfinity;
            for (var i = 0; i < anchors.Count; i++)
            {
                viewportAnchors[i] = camera.WorldToViewportPoint(anchors[i]);
                if (viewportAnchors[i].z > camera.nearClipPlane)
                    nearestDepth = Mathf.Min(nearestDepth, viewportAnchors[i].z);
            }

            if (float.IsInfinity(nearestDepth)) return BuildSmoothedPath(anchors, samplesPerSpan);

            var visibleDepth = Mathf.Max(
                camera.nearClipPlane + 0.001f,
                nearestDepth - Mathf.Max(0f, surfaceOffset));
            var visibleAnchors = new Vector3[anchors.Count];
            for (var i = 0; i < viewportAnchors.Length; i++)
            {
                viewportAnchors[i].z = visibleDepth;
                visibleAnchors[i] = camera.ViewportToWorldPoint(viewportAnchors[i]);
            }

            return BuildSmoothedPath(visibleAnchors, samplesPerSpan);
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
            renderer.alignment = LineAlignment.View;
        }

        internal static void ApplyPath(LineRenderer renderer, IReadOnlyList<Vector3> anchors)
        {
            var points = BuildVisiblePath(anchors, Camera.main, Mathf.Max(renderer.startWidth, 0.001f));
            renderer.positionCount = points.Length;
            renderer.SetPositions(points);
        }
    }

    public sealed class ElectricalWireDraftView : MonoBehaviour
    {
        private readonly List<Vector3> anchors = new List<Vector3>();
        private LineRenderer line;
        private Func<Vector3> resolveStart;

        public void Initialize(Func<Vector3> startResolver, Material material, Color color, float area)
        {
            resolveStart = startResolver;
            line = gameObject.AddComponent<LineRenderer>();
            ElectricalWireView.ConfigureLine(line, material, color, area);
        }

        public void Refresh(IReadOnlyList<Vector3> bendPoints, Vector3 cursorPosition)
        {
            if (line == null || resolveStart == null) return;
            anchors.Clear();
            anchors.Add(resolveStart());
            if (bendPoints != null)
                for (var i = 0; i < bendPoints.Count; i++) anchors.Add(bendPoints[i]);
            anchors.Add(cursorPosition);
            ElectricalWireView.ApplyPath(line, anchors);
        }
    }
}
