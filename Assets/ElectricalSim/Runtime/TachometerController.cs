using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;

namespace ElectricalSim
{
    public sealed class TachometerController : MonoBehaviour
    {
        private readonly List<MotorSpeedTarget> targets = new List<MotorSpeedTarget>();
        private MotorSpeedTarget hovered;
        public TachometerView View { get; private set; }
        public MotorSpeedTarget AttachedTarget { get; private set; }
        public bool IsSelected { get; private set; }
        private bool faultModeActive;
        public IReadOnlyList<MotorSpeedTarget> Targets => targets;

        public void Initialize(TachometerView view, IEnumerable<MotorSpeedTarget> speedTargets)
        {
            View = view;
            targets.AddRange(speedTargets);
            Deselect();
        }

        public void Select()
        {
            if (!faultModeActive) return;
            IsSelected = true;
            View.gameObject.SetActive(true);
            PickUp();
        }

        public void Deselect()
        {
            IsSelected = false;
            if (AttachedTarget != null) AttachedTarget.Highlight(false);
            AttachedTarget = null;
            SetHover(null);
            RefreshTargets();
            if (View != null) { View.SetReading(null); View.gameObject.SetActive(false); }
        }

        public void SetFaultMode(bool active)
        {
            faultModeActive = active;
            if (!active) Deselect();
            else RefreshTargets();
        }

        private void RefreshTargets()
        {
            foreach (var target in targets)
            {
                target.SetVisible(faultModeActive);
                target.SetAvailable(faultModeActive && IsSelected && AttachedTarget == null);
            }
        }

        public void PickUp()
        {
            if (!IsSelected) return;
            if (AttachedTarget != null) AttachedTarget.Highlight(false);
            AttachedTarget = null;
            View.SetReading(null);
            View.SetPickable(false);
            RefreshTargets();
        }

        public bool TryAttach(MotorSpeedTarget target)
        {
            if (!IsSelected || target == null || !targets.Contains(target)) return false;
            AttachedTarget = target;
            SetHover(null);
            RefreshTargets();
            target.Highlight(true);
            View.PlaceProbe(target.transform.position, target.MeterRotation);
            View.SetPickable(true);
            return true;
        }

        public void HandleInput(Camera camera)
        {
            HandlePointer(camera, Input.mousePosition, Input.GetMouseButtonDown(0),
                EventSystem.current != null && EventSystem.current.IsPointerOverGameObject());
        }

        public void HandlePointer(Camera camera, Vector2 screenPosition, bool pressed, bool overUi)
        {
            if (!IsSelected || camera == null) return;
            if (overUi)
            {
                SetHover(null);
                if (AttachedTarget == null) View.gameObject.SetActive(false);
                return;
            }
            View.gameObject.SetActive(true);
            var ray = camera.ScreenPointToRay(screenPosition);
            var hitSomething = Physics.Raycast(ray, out var hit, 100f);
            if (AttachedTarget != null)
            {
                if (pressed && hitSomething && hit.collider.GetComponentInParent<TachometerView>() == View)
                    PickUp();
                return;
            }
            var target = hitSomething ? hit.collider.GetComponent<MotorSpeedTarget>() : null;
            SetHover(target);
            if (target != null)
            {
                View.PlaceProbe(target.transform.position, target.MeterRotation);
                if (pressed) TryAttach(target);
            }
            else
            {
                // Keep the hand-held preview near the pointer at a stable camera depth.
                var depth = 0.65f / Mathf.Max(0.01f, Vector3.Dot(ray.direction, camera.transform.forward));
                View.PlaceProbe(ray.GetPoint(depth), camera.transform.rotation);
            }
        }

        public void Refresh(SimulationSnapshot snapshot)
        {
            if (!IsSelected) return;
            if (AttachedTarget != null)
                View.PlaceProbe(AttachedTarget.transform.position, AttachedTarget.MeterRotation);
            View.SetReading(AttachedTarget != null && snapshot != null ? (float?)snapshot.GetMotorSpeedRpm(AttachedTarget.MotorId) : null);
        }

        private void SetHover(MotorSpeedTarget target)
        {
            if (hovered != null) hovered.Highlight(false);
            hovered = target;
            if (hovered != null) hovered.Highlight(true);
        }
    }
}
