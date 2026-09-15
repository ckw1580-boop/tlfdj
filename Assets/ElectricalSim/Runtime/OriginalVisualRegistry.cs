using System;
using System.Collections.Generic;
using UnityEngine;

namespace ElectricalSim
{
    [Serializable]
    public sealed class OriginalVisualEntry
    {
        public string DeviceId = string.Empty;
        public string TypeId = string.Empty;
        public GameObject Prefab;
    }

    [Serializable]
    public sealed class OriginalUiEntry
    {
        public string Id = string.Empty;
        public GameObject Prefab;
    }

    [CreateAssetMenu(menuName = "Electrical Sim/Original Visual Registry", fileName = "OriginalVisualRegistry")]
    public sealed class OriginalVisualRegistry : ScriptableObject
    {
        public GameObject EnvironmentPrefab;
        public GameObject CabinetPrefab;
        public List<OriginalVisualEntry> Entries = new List<OriginalVisualEntry>();
        public List<OriginalUiEntry> UiPrefabs = new List<OriginalUiEntry>();

        public List<string> FindMissingVisuals()
        {
            var missing = new List<string>();
            if (EnvironmentPrefab == null) missing.Add("OriginalVisualRegistry.EnvironmentPrefab is missing.");
            if (CabinetPrefab == null) missing.Add("OriginalVisualRegistry.CabinetPrefab is missing.");
            if (Entries == null || Entries.Count == 0) missing.Add("OriginalVisualRegistry has no device entries.");
            else foreach (var entry in Entries)
                if (entry == null || entry.Prefab == null)
                    missing.Add("Original device prefab is missing: " + (entry == null ? "<null entry>" : entry.DeviceId + " / " + entry.TypeId));
            return missing;
        }

        public GameObject Resolve(string deviceId, string typeId)
        {
            foreach (var entry in Entries)
            {
                if (entry.Prefab == null) continue;
                if (!string.IsNullOrEmpty(entry.DeviceId) && entry.DeviceId == deviceId) return entry.Prefab;
                if (!string.IsNullOrEmpty(entry.TypeId) && entry.TypeId == typeId) return entry.Prefab;
            }
            return null;
        }

        public GameObject ResolveUi(string id)
        {
            foreach (var entry in UiPrefabs)
                if (entry.Id == id && entry.Prefab != null) return entry.Prefab;
            return null;
        }
    }
}
