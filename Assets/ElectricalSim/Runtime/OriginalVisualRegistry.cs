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

    [CreateAssetMenu(menuName = "Electrical Sim/Original Visual Registry", fileName = "OriginalVisualRegistry")]
    public sealed class OriginalVisualRegistry : ScriptableObject
    {
        public List<OriginalVisualEntry> Entries = new List<OriginalVisualEntry>();

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
    }
}
