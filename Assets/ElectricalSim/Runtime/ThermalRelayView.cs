using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using UnityEngine;

namespace ElectricalSim
{
    public sealed class ThermalRelayView : MonoBehaviour
    {
        public ThermalRelayDefinition Definition { get; private set; }
        public ElectricalDeviceRuntime Runtime { get; private set; }
        public IReadOnlyDictionary<string, ElectricalPortView> Bindings { get; private set; }
        public Collider Picker { get; private set; }
        public bool IsRear => Definition.IsRear;
        public void Initialize(ThermalRelayDefinition definition, ElectricalDeviceRuntime runtime,
            IDictionary<string, ElectricalPortView> bindings)
        {
            Definition = definition; Runtime = runtime;
            Bindings = new ReadOnlyDictionary<string, ElectricalPortView>(new Dictionary<string, ElectricalPortView>(bindings));
            Picker = transform.Find("picker")?.GetComponent<Collider>();
            if (Picker == null) throw new InvalidOperationException("热继电器 picker 缺失：" + definition.ModelPath);
            Picker.gameObject.SetActive(true); Picker.enabled = true;
        }
    }
}
