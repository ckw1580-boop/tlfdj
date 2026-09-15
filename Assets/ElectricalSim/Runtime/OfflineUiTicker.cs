using System;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;

namespace ElectricalSim
{
    public sealed class OfflineUiTicker : MonoBehaviour
    {
        private GameObject toolbar;
        private float nextUpdate;

        public void Initialize(GameObject navigationRoot, GameObject toolbarRoot)
        {
            toolbar = toolbarRoot;
            Hide(navigationRoot, "countdownText");
            Hide(navigationRoot, "countdownIcon");
            Hide(toolbar, "txt_time_right");
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
        }

        private static void Hide(GameObject root, string name)
        {
            if (root == null) return;
            foreach (var item in root.GetComponentsInChildren<Transform>(true).Where(item => item.name == name))
                item.gameObject.SetActive(false);
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
