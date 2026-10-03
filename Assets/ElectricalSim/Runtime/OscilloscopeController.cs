using System;
using UnityEngine;
using UnityEngine.EventSystems;

namespace ElectricalSim
{
    public sealed class OscilloscopeController : MonoBehaviour
    {
        private sealed class Contact { public string Port, Label; public Transform Anchor; public Vector3 Point; public Quaternion Rotation; }
        private readonly Contact[] contacts = new Contact[4];
        private TrainingCameraController trainingCamera;
        private Func<string, string> resolveLabel;
        private SimulationSnapshot snapshot;
        private ElectricalPortView hovered;
        private bool faultMode;
        private Plane dragPlane;
        private Vector3 dragOffset, homePosition;
        private Quaternion homeRotation;
        private float nextCapture;
        public OscilloscopeView View { get; private set; }
        public OscilloscopeFrame Frame { get; } = new OscilloscopeFrame();
        public bool IsSelected { get; private set; }
        public int HeldProbe { get; private set; } = -1;
        public bool IsDragging { get; private set; }
        public bool IsBusy => HeldProbe >= 0 || IsDragging;
        public bool InteractionBlocked { get; set; }
        public string ProbePort(int i) => contacts[i]?.Port;
        public void Initialize(OscilloscopeView view, TrainingCameraController camera, Func<string, string> labels)
        { View = view; trainingCamera = camera; resolveLabel = labels; View.Bind(Frame); Deselect(); }
        public void SetFaultMode(bool active) { faultMode = active; if (!active) Deselect(); }
        public void Select(Camera camera)
        {
            if (!faultMode || View == null || camera == null) return;
            IsSelected = true; InteractionBlocked = false; Frame.Frozen = false;
            homeRotation = camera.transform.rotation;
            homePosition = camera.ViewportToWorldPoint(new Vector3(.28f, .28f, 1.15f));
            View.gameObject.SetActive(true); ReturnHome(); RetractProbes();
        }
        public void Deselect()
        {
            IsSelected = false; HeldProbe = -1; Array.Clear(contacts, 0, 4); SuspendPointer();
            Frame.Frozen = false;
            foreach (var c in Frame.Channels) { c.Signal = default; c.PositiveLabel = c.NegativeLabel = "未连接"; }
            Frame.Render(); if (View != null) View.gameObject.SetActive(false);
        }
        public void ResetSettings() { Frame.ResetSettings(); SettingsChanged(); }
        public void ToggleRunning()
        {
            if (!IsSelected || InteractionBlocked) return;
            if (!Frame.Frozen) Capture();
            Frame.Frozen = !Frame.Frozen; Frame.Revision++; View.RefreshScreen(Frame);
            if (!Frame.Frozen) Capture();
        }
        public void AutoScale() { if (InteractionBlocked || Frame.Frozen) return; Capture(); Frame.AutoScale(); SettingsChanged(); }
        public void SetChannelEnabled(int i, bool value) { Frame.Channels[i].Enabled = value; SettingsChanged(); }
        public void SetCoupling(int i, OscilloscopeCoupling value) { if (Frame.Frozen) return; Frame.Channels[i].Coupling = value; SettingsChanged(); }
        public void SetWaveform(int i, OscilloscopeWaveform value) { if (Frame.Frozen) return; Frame.Channels[i].Waveform = value; SettingsChanged(); }
        public void SetVoltageIndex(int i, int index) { Frame.Channels[i].VoltageIndex = Mathf.Clamp(index, 0, OscilloscopeFrame.VoltageSteps.Length - 1); SettingsChanged(); }
        public void SetPosition(int i, float value) { Frame.Channels[i].Position = Mathf.Clamp(value, -4, 4); SettingsChanged(); }
        public void SetTimeIndex(int index) { if (Frame.Frozen) return; Frame.TimeIndex = Mathf.Clamp(index, 0, OscilloscopeFrame.TimeStepsMs.Length - 1); SettingsChanged(); }
        private void SettingsChanged() { Frame.Render(); if (View != null) View.RefreshScreen(Frame); }
        public void RetractProbes()
        {
            Array.Clear(contacts, 0, 4); HeldProbe = -1; SuspendPointer();
            if (View != null) for (var i = 0; i < 4; i++) View.DockProbe(i);
            Refresh(snapshot, true);
        }
        public void ReturnHome() { SuspendPointer(); View.Body.SetPositionAndRotation(homePosition, homeRotation); Refresh(snapshot, true); }
        public void PickUpProbe(int index)
        {
            if (!IsSelected || InteractionBlocked || index < 0 || index >= 4) return;
            SuspendPointer(); if (HeldProbe >= 0) View.DockProbe(HeldProbe);
            contacts[index] = null; HeldProbe = index;
            for (var i = 0; i < 4; i++) View.SetProbePickable(i, false);
            Refresh(snapshot, true);
        }
        public bool TryAttach(ElectricalPortView port, Camera camera)
        {
            if (!IsSelected || InteractionBlocked || HeldProbe < 0 || port == null || !port.IsVisible || camera == null) return false;
            var anchor = port.CurrentAnchor != null ? port.CurrentAnchor : port.transform;
            var direction = Quaternion.AngleAxis(-36 + HeldProbe * 24, camera.transform.up) * camera.transform.forward;
            var rotation = Quaternion.FromToRotation(Vector3.up, direction);
            contacts[HeldProbe] = new Contact { Port = port.QualifiedPort, Label = resolveLabel?.Invoke(port.QualifiedPort) ?? port.HoverLabel,
                Anchor = anchor, Point = anchor.InverseTransformPoint(port.CurrentAnchorPosition), Rotation = Quaternion.Inverse(anchor.rotation) * rotation };
            HeldProbe = -1; SetHover(null); Refresh(snapshot, true); return true;
        }
        public bool HandleInput(Camera camera) => HandlePointer(camera, Input.mousePosition, Input.GetMouseButtonDown(0), Input.GetMouseButton(0), EventSystem.current != null && EventSystem.current.IsPointerOverGameObject());
        public bool HandlePointer(Camera camera, Vector2 pointer, bool pressed, bool held, bool overUi)
        {
            if (!IsSelected || camera == null) return false;
            if (InteractionBlocked || overUi) { SuspendPointer(); return true; }
            var ray = camera.ScreenPointToRay(pointer);
            if (IsDragging)
            {
                if (!held) SuspendPointer();
                else if (dragPlane.Raycast(ray, out var distance)) View.Body.position = ray.GetPoint(distance) + dragOffset;
                Refresh(snapshot); return true;
            }
            var hitSomething = Physics.Raycast(ray, out var hit, 100);
            var control = hitSomething ? hit.collider.GetComponentInParent<OscilloscopeInteractable>() : null;
            if (control != null && control.GetComponentInParent<OscilloscopeView>() == View)
            { SetHover(null); if (pressed) Execute(control.Action, camera, hit.point); return true; }
            if (HeldProbe < 0) { SetHover(null); return false; }
            var port = hitSomething ? hit.collider.GetComponent<ElectricalPortView>() : null;
            SetHover(port);
            View.PlaceProbe(HeldProbe, port != null ? port.CurrentAnchorPosition : hitSomething ? hit.point : ray.GetPoint(.65f), Quaternion.FromToRotation(Vector3.up, ray.direction));
            if (pressed && port != null) TryAttach(port, camera);
            View.RefreshLeads(); return true;
        }
        private void Execute(OscilloscopeAction action, Camera camera, Vector3 point)
        {
            if ((int)action <= 3) PickUpProbe((int)action);
            else if (action == OscilloscopeAction.RunStop) ToggleRunning();
            else if (action == OscilloscopeAction.AutoScale) AutoScale();
            else if (action == OscilloscopeAction.Timebase) SetTimeIndex((Frame.TimeIndex + 1) % OscilloscopeFrame.TimeStepsMs.Length);
            else if (action == OscilloscopeAction.Voltage1 || action == OscilloscopeAction.Voltage2)
            { var c = action == OscilloscopeAction.Voltage1 ? 0 : 1; SetVoltageIndex(c, (Frame.Channels[c].VoltageIndex + 1) % OscilloscopeFrame.VoltageSteps.Length); }
            else if (action == OscilloscopeAction.Retract) RetractProbes();
            else if (action == OscilloscopeAction.Home) ReturnHome();
            else if (HeldProbe < 0)
            { dragPlane = new Plane(camera.transform.forward, point); dragOffset = View.Body.position - point; IsDragging = true; if (trainingCamera != null) trainingCamera.InstrumentInputBlocked = true; }
        }
        public void SuspendPointer() { IsDragging = false; SetHover(null); if (trainingCamera != null) trainingCamera.InstrumentInputBlocked = false; }
        public void Refresh(SimulationSnapshot current, bool force = false)
        {
            snapshot = current; if (!IsSelected) return;
            for (var i = 0; i < 4; i++)
            {
                var c = contacts[i];
                if (c != null && (c.Anchor == null || current != null && !current.ContainsPort(c.Port))) contacts[i] = c = null;
                if (c != null) View.PlaceProbe(i, c.Anchor.TransformPoint(c.Point), c.Anchor.rotation * c.Rotation);
                else if (HeldProbe != i) View.DockProbe(i);
                View.SetProbePickable(i, HeldProbe < 0);
            }
            View.RefreshLeads();
            if (!Frame.Frozen && !InteractionBlocked && (force || Time.unscaledTime >= nextCapture)) Capture();
        }
        private void Capture()
        {
            if (!IsSelected || InteractionBlocked) return;
            for (var i = 0; i < 2; i++)
            {
                var c = Frame.Channels[i]; c.Signal = OscilloscopeMeasurement.Read(ProbePort(i * 2), ProbePort(i * 2 + 1), snapshot);
                c.PositiveLabel = contacts[i * 2]?.Label ?? "未连接"; c.NegativeLabel = contacts[i * 2 + 1]?.Label ?? "未连接";
            }
            Frame.Render(); View.RefreshScreen(Frame); nextCapture = Time.unscaledTime + 1f / 30;
        }
        private void SetHover(ElectricalPortView port) { if (hovered == port) return; if (hovered != null) hovered.SetHighlighted(false); hovered = port; if (hovered != null) hovered.SetHighlighted(true); }
        private void OnDisable() => Deselect();
        private void OnDestroy() { if (View != null) Destroy(View.gameObject); }
    }
}
