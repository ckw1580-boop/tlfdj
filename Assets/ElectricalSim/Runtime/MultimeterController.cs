using System;
using UnityEngine;
using UnityEngine.EventSystems;

namespace ElectricalSim
{
    public sealed class MultimeterController : MonoBehaviour
    {
        private sealed class Contact
        {
            public string Port, Label;
            public Transform Anchor;
            public Vector3 LocalPoint;
            public Quaternion LocalRotation;
        }
        private readonly Contact[] contacts = new Contact[2];
        private readonly ElectricalInstrument instrument = new ElectricalInstrument(InstrumentKind.Multimeter);
        private TrainingCameraController trainingCamera;
        private Func<string, string> resolveLabel;
        private SimulationSnapshot snapshot;
        private ElectricalPortView hovered;
        private bool faultMode;
        private Plane dragPlane;
        private Vector3 dragOffset, homePosition;
        private Quaternion homeRotation;
        public MultimeterView View { get; private set; }
        public MultimeterMode MeasurementMode { get; private set; } = MultimeterMode.AcVoltage;
        public MultimeterReading Reading { get; private set; }
        public bool IsSelected { get; private set; }
        public int HeldProbe { get; private set; } = -1;
        public bool IsDragging { get; private set; }
        public bool IsBusy => HeldProbe >= 0 || IsDragging;
        public string RedPort => contacts[0]?.Port;
        public string BlackPort => contacts[1]?.Port;

        public void Initialize(MultimeterView view, TrainingCameraController camera, Func<string, string> labelResolver)
        {
            View = view; trainingCamera = camera; resolveLabel = labelResolver;
            Deselect();
        }

        public void SetFaultMode(bool active)
        {
            faultMode = active;
            if (!active) Deselect();
        }

        public void Select(Camera camera)
        {
            if (!faultMode || View == null || camera == null) return;
            IsSelected = true;
            MeasurementMode = MultimeterMode.AcVoltage;
            // Capture a world-space location beside the cabinet, never parent the meter to the camera.
            homeRotation = camera.transform.rotation;
            homePosition = camera.ViewportToWorldPoint(new Vector3(0.76f, 0.27f, 0.80f));
            View.gameObject.SetActive(true);
            ReturnHome();
            RetractProbes();
        }

        public void Deselect()
        {
            IsSelected = false;
            contacts[0] = contacts[1] = null;
            HeldProbe = -1;
            CancelDrag();
            SetHover(null);
            if (View != null) View.gameObject.SetActive(false);
        }

        public void SetMode(MultimeterMode mode)
        {
            if (!IsSelected) return;
            MeasurementMode = mode;
            Refresh(snapshot);
        }

        public void RetractProbes()
        {
            contacts[0] = contacts[1] = null;
            HeldProbe = -1;
            CancelDrag(); SetHover(null);
            if (View == null) return;
            for (var i = 0; i < 2; i++) View.DockProbe(i);
            Refresh(snapshot);
        }

        public void ReturnHome()
        {
            CancelDrag();
            View.Body.SetPositionAndRotation(homePosition, homeRotation);
            Refresh(snapshot);
        }

        public void PickUpProbe(int index)
        {
            if (!IsSelected || index < 0 || index > 1) return;
            CancelDrag();
            if (HeldProbe >= 0) View.DockProbe(HeldProbe);
            contacts[index] = null;
            HeldProbe = index;
            View.SetProbePickable(index, false);
            Refresh(snapshot);
        }

        public bool TryAttach(ElectricalPortView port, Camera camera)
        {
            if (!IsSelected || HeldProbe < 0 || port == null || !port.IsVisible || camera == null) return false;
            var anchor = port.CurrentAnchor != null ? port.CurrentAnchor : port.transform;
            // Capture the actual physical anchor; ElectricalPortView itself can move to the opposite face.
            var approach = Quaternion.AngleAxis(HeldProbe == 0 ? 18f : -18f, camera.transform.up) * camera.transform.forward;
            var rotation = Quaternion.FromToRotation(Vector3.up, approach);
            contacts[HeldProbe] = new Contact { Port = port.QualifiedPort,
                Label = resolveLabel != null ? resolveLabel(port.QualifiedPort) : port.HoverLabel,
                Anchor = anchor, LocalPoint = anchor.InverseTransformPoint(port.CurrentAnchorPosition),
                LocalRotation = Quaternion.Inverse(anchor.rotation) * rotation };
            View.SetProbePickable(HeldProbe, true);
            HeldProbe = -1;
            SetHover(null);
            Refresh(snapshot);
            return true;
        }

        public bool HandleInput(Camera camera) => HandlePointer(camera, Input.mousePosition,
            Input.GetMouseButtonDown(0), Input.GetMouseButton(0),
            EventSystem.current != null && EventSystem.current.IsPointerOverGameObject());

        public bool HandlePointer(Camera camera, Vector2 pointer, bool pressed, bool held, bool overUi)
        {
            if (!IsSelected || camera == null) return false;
            if (overUi) { SetHover(null); CancelDrag(); return true; }
            var ray = camera.ScreenPointToRay(pointer);
            if (IsDragging)
            {
                if (!held) CancelDrag();
                else if (dragPlane.Raycast(ray, out var distance)) View.Body.position = ray.GetPoint(distance) + dragOffset;
                Refresh(snapshot);
                return true;
            }
            var hitSomething = Physics.Raycast(ray, out var hit, 100f);
            var control = hitSomething ? hit.collider.GetComponentInParent<MultimeterInteractable>() : null;
            if (control != null && control.GetComponentInParent<MultimeterView>() == View)
            {
                SetHover(null);
                if (pressed) Execute(control.Action, camera, hit.point);
                return true;
            }
            if (HeldProbe < 0) { SetHover(null); return false; }
            var port = hitSomething ? hit.collider.GetComponent<ElectricalPortView>() : null;
            SetHover(port);
            var position = port != null ? port.CurrentAnchorPosition : hitSomething ? hit.point : ray.GetPoint(0.65f);
            View.PlaceProbe(HeldProbe, position, Quaternion.FromToRotation(Vector3.up, ray.direction));
            if (pressed && port != null) TryAttach(port, camera);
            View.RefreshLeads();
            return true;
        }

        private void Execute(MultimeterAction action, Camera camera, Vector3 point)
        {
            if (action == MultimeterAction.RedProbe || action == MultimeterAction.BlackProbe)
                PickUpProbe(action == MultimeterAction.RedProbe ? 0 : 1);
            else if (action == MultimeterAction.Retract) RetractProbes();
            else if (action == MultimeterAction.Home) ReturnHome();
            else if (action == MultimeterAction.CycleMode) SetMode((MultimeterMode)(((int)MeasurementMode + 1) % 4));
            else if (action == MultimeterAction.MoveBody)
            {
                if (HeldProbe >= 0) return;
                dragPlane = new Plane(camera.transform.forward, point);
                dragOffset = View.Body.position - point;
                IsDragging = true;
                if (trainingCamera != null) trainingCamera.InstrumentInputBlocked = true;
            }
            else SetMode((MultimeterMode)((int)action - (int)MultimeterAction.Off));
        }

        public void SuspendPointer()
        {
            CancelDrag();
            SetHover(null);
        }

        private void CancelDrag()
        {
            IsDragging = false;
            if (trainingCamera != null) trainingCamera.InstrumentInputBlocked = false;
        }

        public void Refresh(SimulationSnapshot current)
        {
            snapshot = current;
            if (!IsSelected) return;
            for (var i = 0; i < 2; i++)
            {
                var contact = contacts[i];
                if (contact != null && (contact.Anchor == null || current != null && !current.ContainsPort(contact.Port)))
                    contacts[i] = contact = null;
                if (contact != null) View.PlaceProbe(i, contact.Anchor.TransformPoint(contact.LocalPoint), contact.Anchor.rotation * contact.LocalRotation);
                else if (HeldProbe != i) View.DockProbe(i);
            }
            Reading = instrument.Measure(MeasurementMode, RedPort, BlackPort, current);
            // A connected probe must not hide the terminal from the other probe's picking ray.
            for (var i = 0; i < 2; i++) View.SetProbePickable(i, HeldProbe < 0);
            View.SetReading(Reading, contacts[0]?.Label ?? "未连接", contacts[1]?.Label ?? "未连接");
            View.RefreshLeads();
        }

        public string DescribeReading() => "万用表 · " + MultimeterReading.ModeLabel(MeasurementMode) + "\n" +
            Reading.DisplayValue + " " + (Reading.State == MultimeterReadingState.Valid ? Reading.Unit : "") + "  " + Reading.Hint +
            "\n红：" + (contacts[0]?.Label ?? "未连接") + "\n黑：" + (contacts[1]?.Label ?? "未连接");

        private void SetHover(ElectricalPortView port)
        {
            if (hovered == port) return;
            if (hovered != null) hovered.SetHighlighted(false);
            hovered = port;
            if (hovered != null) hovered.SetHighlighted(true);
        }
        private void OnDisable() { Deselect(); }
        private void OnDestroy() { if (View != null) Destroy(View.gameObject); }
    }
}
