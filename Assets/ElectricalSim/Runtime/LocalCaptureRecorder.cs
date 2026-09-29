using System;
using System.IO;
using UnityEngine;

namespace ElectricalSim
{
    public sealed class LocalCaptureRecorder : MonoBehaviour
    {
        private LocalSessionStore store;

        public string LastCapturePath { get; private set; } = string.Empty;

        private void Awake() => store = new LocalSessionStore();

        private void Update()
        {
            if (Input.GetKeyDown(KeyCode.F12)) CaptureScreenshot();
        }

        public void CaptureScreenshot()
        {
            var path = Path.Combine(store.CapturesDirectory, $"截图_{DateTime.Now:yyyyMMdd_HHmmss}.png");
            ScreenCapture.CaptureScreenshot(path);
            LastCapturePath = path;
        }

    }
}
