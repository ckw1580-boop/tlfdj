using System;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;

namespace ElectricalSim
{
    public sealed class OfflineUiTicker : MonoBehaviour
    {
        private GameObject navigation;
        private GameObject toolbar;
        private OfflineExamController exam;
        private float nextUpdate;

        public void Initialize(GameObject navigationRoot, GameObject toolbarRoot, OfflineExamController examController)
        {
            navigation = navigationRoot;
            toolbar = toolbarRoot;
            exam = examController;
            Refresh();
        }

        private void Update()
        {
            if (Time.unscaledTime < nextUpdate) return;
            nextUpdate = Time.unscaledTime + 0.25f;
            Refresh();
        }

        private void Refresh()
        {
            SetText(toolbar, "txt_time_left", DateTime.Now.ToString("yyyy/M/d    HH:mm:ss"));
            var remaining = exam?.ActiveSession?.RemainingSeconds ?? 0d;
            var time = TimeSpan.FromSeconds(Math.Max(0d, remaining)).ToString(@"hh\:mm\:ss");
            SetText(toolbar, "txt_time_right", time);
            SetText(navigation, "countdownText", time);
        }

        private static void SetText(GameObject root, string name, string value)
        {
            if (root == null) return;
            var legacy = root.GetComponentsInChildren<Text>(true).FirstOrDefault(item => item.name == name);
            if (legacy != null) legacy.text = value;
            var modern = root.GetComponentsInChildren<MonoBehaviour>(true)
                .FirstOrDefault(item => item != null && item.name == name && item.GetType().GetProperty("text")?.CanWrite == true);
            modern?.GetType().GetProperty("text")?.SetValue(modern, value);
        }
    }
}
