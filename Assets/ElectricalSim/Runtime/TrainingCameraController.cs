using System;
using UnityEngine;
using UnityEngine.EventSystems;

namespace ElectricalSim
{
    public enum TrainingViewPreset
    {
        Default,
        WiringFront,
        FaultBack
    }

    public sealed class TrainingCameraController : MonoBehaviour
    {
        public float MoveSpeed = 2.5f;
        public float VerticalSpeed = 1.8f;
        public float LookSensitivity = 2.2f;
        public float ZoomSpeed = 3f;
        public Vector3 DefaultPosition = new Vector3(-0.35f, 1.58f, 0.45f);
        public Vector3 DefaultEuler = new Vector3(10f, 180f, 0f);
        public Vector3 FaultPosition = new Vector3(-0.01f, 1.08f, -3.30f);
        public Vector3 FaultFallbackTarget = new Vector3(-0.01f, 1.00f, -1.45f);

        public Vector3 CurrentFaultTarget { get; private set; }
        public bool IsViewingFaultSide => IsPositionOnFaultSide(transform.position);

        public TrainingViewPreset CurrentPreset { get; private set; } = TrainingViewPreset.Default;
        public event Action<TrainingViewPreset> PresetChanged;
        public event Action<bool> ViewSideChanged;

        private bool faultTargetResolved;
        private bool viewSideInitialized;
        private bool lastViewingFaultSide;

        private void Start()
        {
            ResetView();
        }

        private void Update()
        {
            var speed = MoveSpeed * Time.unscaledDeltaTime;
            var horizontal = (Input.GetKey(KeyCode.D) ? 1f : 0f) - (Input.GetKey(KeyCode.A) ? 1f : 0f);
            var forward = (Input.GetKey(KeyCode.W) ? 1f : 0f) - (Input.GetKey(KeyCode.S) ? 1f : 0f);
            var vertical = (Input.GetKey(KeyCode.E) ? 1f : 0f) - (Input.GetKey(KeyCode.Q) ? 1f : 0f);
            transform.position += (transform.right * horizontal + Vector3.ProjectOnPlane(transform.forward, Vector3.up).normalized * forward) * speed;
            transform.position += Vector3.up * vertical * VerticalSpeed * Time.unscaledDeltaTime;

            if (Input.GetMouseButton(1) && !IsPointerOverUi())
            {
                var yaw = Input.GetAxis("Mouse X") * LookSensitivity;
                var pitch = -Input.GetAxis("Mouse Y") * LookSensitivity;
                transform.eulerAngles += new Vector3(pitch, yaw, 0f);
                var euler = transform.eulerAngles;
                if (euler.x > 180f) euler.x -= 360f;
                euler.x = Mathf.Clamp(euler.x, -75f, 75f);
                euler.z = 0f;
                transform.eulerAngles = euler;
            }

            if (!IsPointerOverUi())
            {
                var scroll = Input.mouseScrollDelta.y;
                transform.position += transform.forward * scroll * ZoomSpeed * Time.unscaledDeltaTime * 8f;
            }

            if (Input.GetKeyDown(KeyCode.Home)) ResetView();
            RefreshViewSide();
        }

        public void ResetView()
        {
            transform.position = DefaultPosition;
            transform.eulerAngles = DefaultEuler;
            SetPreset(TrainingViewPreset.Default);
            RefreshViewSide();
        }

        public void SetWiringView()
        {
            // PLC/terminal-board face, matching the original "接线视角".
            transform.position = new Vector3(0.10f, 1.53f, -0.05f);
            transform.eulerAngles = new Vector3(8f, 180f, 0f);
            SetPreset(TrainingViewPreset.WiringFront);
            RefreshViewSide();
        }

        public void SetFaultView()
        {
            // Match the original troubleshooting composition: stand behind the
            // electrical cabinet and aim at its centre so the preset remains
            // straight-on even if the imported environment moves slightly.
            transform.position = FaultPosition;
            CurrentFaultTarget = GetFaultTarget();
            var direction = CurrentFaultTarget - transform.position;
            transform.rotation = direction.sqrMagnitude > 0.0001f
                ? Quaternion.LookRotation(direction, Vector3.up)
                : Quaternion.Euler(0f, 180f, 0f);
            SetPreset(TrainingViewPreset.FaultBack);
            RefreshViewSide();
        }

        private bool IsPositionOnFaultSide(Vector3 position)
        {
            var target = GetFaultTarget();
            var faultDirection = FaultPosition - target;
            return faultDirection.sqrMagnitude > 0.0001f &&
                   Vector3.Dot(position - target, faultDirection) > 0f;
        }

        private Vector3 GetFaultTarget()
        {
            if (faultTargetResolved) return CurrentFaultTarget;
            CurrentFaultTarget = ResolveFaultTarget();
            faultTargetResolved = true;
            return CurrentFaultTarget;
        }

        private void RefreshViewSide()
        {
            var viewingFaultSide = IsViewingFaultSide;
            if (viewSideInitialized && viewingFaultSide == lastViewingFaultSide) return;
            viewSideInitialized = true;
            lastViewingFaultSide = viewingFaultSide;
            ViewSideChanged?.Invoke(viewingFaultSide);
        }

        private Vector3 ResolveFaultTarget()
        {
            var environment = GameObject.Find("OriginalLabEnvironment");
            if (environment == null) return FaultFallbackTarget;

            foreach (var renderer in environment.GetComponentsInChildren<Renderer>(true))
                if (string.Equals(renderer.name, "DQG01", StringComparison.OrdinalIgnoreCase))
                    return renderer.bounds.center;

            return FaultFallbackTarget;
        }

        private void SetPreset(TrainingViewPreset preset)
        {
            CurrentPreset = preset;
            PresetChanged?.Invoke(preset);
        }

        private static bool IsPointerOverUi()
        {
            return EventSystem.current != null && EventSystem.current.IsPointerOverGameObject();
        }
    }
}
