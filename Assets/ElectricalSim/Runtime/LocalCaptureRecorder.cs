using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;
using UnityEngine;

namespace ElectricalSim
{
    public sealed class LocalCaptureRecorder : MonoBehaviour
    {
        private readonly List<string> frames = new List<string>();
        private LocalSessionStore store;
        private Coroutine recording;
        private string recordingDirectory;
        private int frameIndex;

        public bool IsRecording => recording != null;
        public string LastCapturePath { get; private set; } = string.Empty;

        private void Awake() => store = new LocalSessionStore();

        public void CaptureScreenshot()
        {
            var path = Path.Combine(store.CapturesDirectory, $"截图_{DateTime.Now:yyyyMMdd_HHmmss}.png");
            ScreenCapture.CaptureScreenshot(path);
            LastCapturePath = path;
        }

        public void ToggleRecording()
        {
            if (recording == null) StartRecording();
            else StopRecording();
        }

        public void StartRecording()
        {
            if (recording != null) return;
            recordingDirectory = Path.Combine(store.RecordingsDirectory, $"录像_{DateTime.Now:yyyyMMdd_HHmmss}");
            Directory.CreateDirectory(recordingDirectory);
            frames.Clear();
            frameIndex = 0;
            recording = StartCoroutine(RecordFrames());
        }

        public string StopRecording()
        {
            if (recording == null) return LastCapturePath;
            StopCoroutine(recording);
            recording = null;
            var manifest = Path.Combine(recordingDirectory, "recording.json");
            File.WriteAllText(manifest, JsonConvert.SerializeObject(new
            {
                format = "png-sequence",
                frameRate = 15,
                width = Screen.width,
                height = Screen.height,
                frames
            }, Formatting.Indented));
            LastCapturePath = manifest;
            return manifest;
        }

        private IEnumerator RecordFrames()
        {
            var delay = new WaitForSecondsRealtime(1f / 15f);
            while (true)
            {
                var name = $"frame_{frameIndex++:D7}.png";
                var path = Path.Combine(recordingDirectory, name);
                ScreenCapture.CaptureScreenshot(path);
                frames.Add(name);
                yield return delay;
            }
        }

        private void OnApplicationQuit()
        {
            if (recording != null) StopRecording();
        }
    }
}
