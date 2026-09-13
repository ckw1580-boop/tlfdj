using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using UnityEngine;

namespace ElectricalSim
{
    public sealed class ContactorView : MonoBehaviour
    {
        public ContactorDefinition Definition { get; private set; }
        public ElectricalDeviceRuntime Runtime { get; private set; }
        public IReadOnlyDictionary<string, ElectricalPortView> Bindings { get; private set; }
        public Collider Picker { get; private set; }
        public bool IsRear { get; private set; }
        public void Initialize(ContactorDefinition definition, ElectricalDeviceRuntime runtime,
            IDictionary<string, ElectricalPortView> bindings, bool isRear = false)
        {
            Definition = definition; Runtime = runtime; IsRear = isRear || definition.IsRear;
            Bindings = new ReadOnlyDictionary<string, ElectricalPortView>(new Dictionary<string, ElectricalPortView>(bindings));
            Picker = transform.Find("picker")?.GetComponent<Collider>();
            if (Picker == null) throw new InvalidOperationException("交流接触器 picker 缺失：" + definition.ModelPath);
            Picker.gameObject.SetActive(true); Picker.enabled = true;
        }
    }
}
