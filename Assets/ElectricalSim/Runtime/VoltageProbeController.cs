using System;
using UnityEngine;
using UnityEngine.EventSystems;

namespace ElectricalSim
{
    public sealed class VoltageProbeController : MonoBehaviour
    {
        private readonly ElectricalInstrument instrument = new ElectricalInstrument(InstrumentKind.VoltageProbe);
        private Func<string, string> resolveLabel;
        private SimulationSnapshot snapshot;
        private ElectricalPortView hovered;
        private Transform anchor;
        private Vector3 localPoint;
        private Quaternion localRotation;
        private string contactLabel;
        private bool faultMode;
        public VoltageProbeView View { get; private set; }
        public VoltageProbeMode MeasurementMode { get; private set; } = VoltageProbeMode.AC;
        public VoltageProbeReading Reading { get; private set; }
        public bool IsSelected { get; private set; }
        public bool IsBusy => IsSelected && ContactPort == null;
        public string ContactPort { get; private set; }

        public void Initialize(VoltageProbeView view, Func<string, string> labelResolver)
        { View = view; resolveLabel = labelResolver; Deselect(); }
        public void SetFaultMode(bool active)
        { faultMode = active; if (!active) Deselect(); }
        public void Select(Camera camera)
        {
            if (!faultMode || View == null || camera == null) return;
            IsSelected = true;
            MeasurementMode = VoltageProbeMode.AC;
            PickUp();
            View.gameObject.SetActive(true);
            View.Place(camera.ViewportToWorldPoint(new Vector3(0.6f, 0.45f, 0.65f)), camera.transform.rotation);
        }
        public void Deselect()
        {
            IsSelected = false;
            ClearContact(); SetHover(null);
            if (View != null) { View.SetPickable(false); View.gameObject.SetActive(false); }
        }
        private void ClearContact() { ContactPort = null; contactLabel = null; anchor = null; }
        public void PickUp()
        {
            if (!IsSelected) return;
            ClearContact(); SetHover(null);
            View.SetPickable(false);
            Refresh(snapshot);
        }
        public void SetMode(VoltageProbeMode mode)
        { if (!IsSelected) return; MeasurementMode = mode; Refresh(snapshot); }
        public void ToggleMode() => SetMode(MeasurementMode == VoltageProbeMode.AC ? VoltageProbeMode.DC : VoltageProbeMode.AC);

        public bool TryAttach(ElectricalPortView port, Camera camera)
        {
            if (!IsBusy || port == null || !port.IsVisible || camera == null) return false;
            anchor = port.CurrentAnchor != null ? port.CurrentAnchor : port.transform;
            ContactPort = port.QualifiedPort;
            contactLabel = resolveLabel != null ? resolveLabel(ContactPort) : port.HoverLabel;
            localPoint = anchor.InverseTransformPoint(port.CurrentAnchorPosition);
            // Keep the LCD facing the operator and the handle below the contact, clear of the terminal.
            localRotation = Quaternion.Inverse(anchor.rotation) * camera.transform.rotation * Quaternion.Euler(28, 0, -22);
            SetHover(null); View.SetPickable(true);
            Refresh(snapshot);
            return true;
        }
        public bool HandleInput(Camera camera) => HandlePointer(camera, Input.mousePosition, Input.GetMouseButtonDown(0),
            EventSystem.current != null && EventSystem.current.IsPointerOverGameObject());

        public bool HandlePointer(Camera camera, Vector2 pointer, bool pressed, bool overUi)
        {
            if (!IsSelected || camera == null) return false;
            if (overUi) { SuspendPointer(); return true; }
            View.gameObject.SetActive(true);
            var ray = camera.ScreenPointToRay(pointer);
            var hitSomething = Physics.Raycast(ray, out var hit, 100f);
            var control = hitSomething ? hit.collider.GetComponentInParent<VoltageProbeInteractable>() : null;
            if (control != null && control.GetComponentInParent<VoltageProbeView>() == View)
            {
                SetHover(null);
                if (pressed) { if (control.Action == VoltageProbeAction.ToggleMode) ToggleMode(); else PickUp(); }
                return true;
            }
            if (!IsBusy) { SetHover(null); return false; }
            var port = hitSomething ? hit.collider.GetComponent<ElectricalPortView>() : null;
            if (port != null && !port.IsVisible) port = null;
            SetHover(port);
            var position = port != null ? port.CurrentAnchorPosition : hitSomething ? hit.point :
                ray.GetPoint(0.65f / Mathf.Max(0.01f, Vector3.Dot(ray.direction, camera.transform.forward)));
            View.Place(position, camera.transform.rotation * Quaternion.Euler(28, 0, -22));
            if (pressed && port != null) TryAttach(port, camera);
            return true;
        }
        public void SuspendPointer()
        {
            SetHover(null);
            if (IsBusy && View != null) View.gameObject.SetActive(false);
        }
        public void Refresh(SimulationSnapshot current)
        {
            snapshot = current;
            if (!IsSelected) return;
            if (ContactPort != null && (anchor == null || current != null && !current.ContainsPort(ContactPort)))
            { ClearContact(); View.SetPickable(false); }
            if (anchor != null) View.Place(anchor.TransformPoint(localPoint), anchor.rotation * localRotation);
            Reading = instrument.MeasureVoltageProbe(MeasurementMode, ContactPort, current);
            View.SetReading(Reading);
        }
        public string DescribeReading() => "数字验电笔 · " + MeasurementMode + "\n" + Reading.DisplayText + "  " + Reading.Hint +
            "\n测点：" + (contactLabel ?? "未接触") + "\n参考：" + Reading.ReferenceLabel;
        public string DescribeProperties() => "数字验电笔\n类型：数字接触式 · 支持 AC / DC\n当前档位：" +
            (MeasurementMode == VoltageProbeMode.AC ? "AC 交流" : "DC 直流") + "\n测点：" + (contactLabel ?? "未接触") +
            "\n参考：" + Reading.ReferenceLabel + "\n电压：" + Reading.DisplayText + "\n状态：" + Reading.Hint;
        private void SetHover(ElectricalPortView port)
        {
            if (hovered == port) return;
            if (hovered != null) hovered.SetHighlighted(false);
            hovered = port;
            if (hovered != null) hovered.SetHighlighted(true);
        }
        private void OnDisable() => Deselect();
        private void OnDestroy() { if (View != null) Destroy(View.gameObject); }
    }
}
