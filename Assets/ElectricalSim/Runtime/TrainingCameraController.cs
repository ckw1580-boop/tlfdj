using UnityEngine;
using UnityEngine.EventSystems;

namespace ElectricalSim
{
    public sealed class TrainingCameraController : MonoBehaviour
    {
        public float MoveSpeed = 2.5f;
        public float VerticalSpeed = 1.8f;
        public float LookSensitivity = 2.2f;
        public float ZoomSpeed = 3f;
        public Vector3 DefaultPosition = new Vector3(0f, 1.7f, -5.2f);
        public Vector3 DefaultEuler = new Vector3(5f, 0f, 0f);

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
        }

        public void ResetView()
        {
            transform.position = DefaultPosition;
            transform.eulerAngles = DefaultEuler;
        }

        public void SetWiringView()
        {
            transform.position = new Vector3(0f, 1.75f, -4.1f);
            transform.eulerAngles = new Vector3(1f, 0f, 0f);
        }

        public void SetFaultView()
        {
            transform.position = new Vector3(1.2f, 1.75f, -4.7f);
            transform.eulerAngles = new Vector3(3f, -12f, 0f);
        }

        private static bool IsPointerOverUi()
        {
            return EventSystem.current != null && EventSystem.current.IsPointerOverGameObject();
        }
    }
}
