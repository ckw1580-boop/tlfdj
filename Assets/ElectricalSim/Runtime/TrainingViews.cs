using System;
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

    public sealed class ElectricalWireView : MonoBehaviour
    {
        private LineRenderer line;
        private WireConnection wire;
        private Func<string, Vector3> resolvePort;

        public void Initialize(WireConnection connection, Func<string, Vector3> portResolver, Material material)
        {
            wire = connection;
            resolvePort = portResolver;
            line = gameObject.AddComponent<LineRenderer>();
            if (material != null) line.sharedMaterial = material;
            else
            {
                var shader = Shader.Find("Sprites/Default");
                if (shader != null) line.material = new Material(shader);
            }
            line.startColor = wire.Color;
            line.endColor = wire.Color;
            line.startWidth = Mathf.Clamp(wire.Area * 2.5f, 0.009f, 0.028f);
            line.endWidth = line.startWidth;
            line.numCornerVertices = 4;
            line.numCapVertices = 4;
            line.useWorldSpace = true;
            Refresh();
        }

        public void Refresh()
        {
            if (line == null || wire == null) return;
            var count = wire.Points.Count + 2;
            line.positionCount = count;
            line.SetPosition(0, resolvePort(wire.StartPort));
            for (var i = 0; i < wire.Points.Count; i++) line.SetPosition(i + 1, wire.Points[i]);
            line.SetPosition(count - 1, resolvePort(wire.EndPort));
        }
    }
}
