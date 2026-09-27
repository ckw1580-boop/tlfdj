using System;
using System.Collections.Generic;
using UnityEngine;

namespace ElectricalSim
{
    internal sealed class WireViewCollection
    {
        private readonly Dictionary<string, ElectricalWireView> byId = new Dictionary<string, ElectricalWireView>();
        private readonly List<ElectricalWireView> ordered = new List<ElectricalWireView>();
        private readonly HashSet<string> retained = new HashSet<string>();
        private readonly List<string> removed = new List<string>();

        public List<ElectricalWireView>.Enumerator GetEnumerator() => ordered.GetEnumerator();
        public void BeginUpdate() { retained.Clear(); ordered.Clear(); }

        public void Bind(WireConnection wire, Transform root, Func<string, Vector3> portResolver,
            Material material, WireSurfacePlane surface, Func<string, WireEndpointGeometry> endpointResolver)
        {
            if (!retained.Add(wire.Id)) return;
            if (!byId.TryGetValue(wire.Id, out var view) || view == null)
            {
                var obj = new GameObject("Wire_" + wire.Id);
                obj.transform.SetParent(root, false);
                view = obj.AddComponent<ElectricalWireView>();
                view.Initialize(wire, portResolver, material, surface, endpointResolver);
                byId[wire.Id] = view;
            }
            else view.Rebind(wire, portResolver, surface, endpointResolver);
            ordered.Add(view);
        }

        public void EndUpdate()
        {
            removed.Clear();
            foreach (var pair in byId)
                if (!retained.Contains(pair.Key))
                {
                    if (pair.Value != null) UnityEngine.Object.Destroy(pair.Value.gameObject);
                    removed.Add(pair.Key);
                }
            foreach (var id in removed) byId.Remove(id);
        }
    }
}
