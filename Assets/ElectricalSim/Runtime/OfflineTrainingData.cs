using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;

namespace ElectricalSim
{
    [Flags]
    public enum PortConnectionKind
    {
        Electrical = 1,
        Jumper = 2,
        Probe = 4,
        Network = 8,
        Pneumatic = 16
    }

    [Serializable]
    public sealed class OriginalPortRecord
    {
        public string DeviceId = string.Empty;
        public string DeviceType = string.Empty;
        public string PortName = string.Empty;
        public string DisplayName = string.Empty;
        public string TransformPath = string.Empty;
        public Vector3 LocalPosition;
        public Vector3 ReferenceWorldPosition;
        public Vector2 ReferenceScreenPosition;
        public PortConnectionKind ConnectionKinds = PortConnectionKind.Electrical | PortConnectionKind.Jumper | PortConnectionKind.Probe;

        public string QualifiedPort => CircuitGraph.Port(DeviceId, PortName);
    }

    [CreateAssetMenu(menuName = "Electrical Sim/Original Port Map", fileName = "OriginalPortMap")]
    public sealed class OriginalPortMap : ScriptableObject
    {
        public Vector2Int ReferenceResolution = new Vector2Int(1920, 1080);
        public List<OriginalPortRecord> Ports = new List<OriginalPortRecord>();

        public OriginalPortRecord Find(string deviceId, string portName)
            => Ports.FirstOrDefault(item => item.DeviceId == deviceId && item.PortName == portName);
    }

    [Serializable]
    public sealed class OriginalUiElementLayout
    {
        public string Id = string.Empty;
        public Rect ReferenceRect;
        public Vector2 AnchorMin;
        public Vector2 AnchorMax;
        public Vector2 Pivot = new Vector2(0.5f, 0.5f);
        public int FontSize;
        public Color Color = Color.white;
    }

    [CreateAssetMenu(menuName = "Electrical Sim/Original UI Layout", fileName = "OriginalUiLayoutProfile")]
    public sealed class OriginalUiLayoutProfile : ScriptableObject
    {
        public Vector2Int ReferenceResolution = new Vector2Int(1920, 1080);
        public List<OriginalUiElementLayout> Elements = new List<OriginalUiElementLayout>();

        public OriginalUiElementLayout Find(string id) => Elements.FirstOrDefault(item => item.Id == id);
    }

    public sealed class LocalSessionStore
    {
        public string RootDirectory { get; }

        public LocalSessionStore(string root = null)
        {
            RootDirectory = string.IsNullOrEmpty(root)
                ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "电气控制系统仿真软件")
                : root;
        }

        public string ProjectsDirectory => Ensure("存档");
        public string CapturesDirectory => Ensure("截图");
        public string RecordingsDirectory => Ensure("录像");
        private string Ensure(string name)
        {
            var path = Path.Combine(RootDirectory, name);
            Directory.CreateDirectory(path);
            return path;
        }

    }
}
