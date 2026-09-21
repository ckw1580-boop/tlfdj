using System;
using System.Collections.Generic;
using UnityEngine;

namespace ElectricalSim
{
    public sealed class FrontBreakerView : MonoBehaviour
    {
        public static readonly IReadOnlyList<string> Terminals = Array.AsReadOnly(
            new[] { "N1", "L1", "L3", "L5", "N2", "L2", "L4", "L6" });
        public static readonly IReadOnlyList<PortPair> Contacts = Array.AsReadOnly(new[] {
            new PortPair("N1", "N2"), new PortPair("L1", "L2"),
            new PortPair("L3", "L4"), new PortPair("L5", "L6") });
        public ElectricalDeviceRuntime Runtime { get; private set; }
        public CabinetBreakerInteractable Animation { get; private set; }
        public ElectricalDeviceView Device { get; private set; }

        public void Initialize(ElectricalDeviceView device, CabinetBreakerInteractable animation)
        {
            Device = device;
            Runtime = device.Runtime;
            Animation = animation;
            animation.StateChanged += OnStateChanged;
        }

        public bool IsHandle(Collider collider) => collider != null &&
            (collider.transform == Animation.Handle || collider.transform.IsChildOf(Animation.Handle));

        public void SetClosed(bool closed, bool animate = true)
        {
            Runtime.SetControl(closed);
            Animation.SetClosed(closed, animate);
        }

        private void OnStateChanged(CabinetBreakerInteractable sender, bool closed) => Runtime.SetControl(closed);
        private void LateUpdate()
        {
            if (Animation != null && Runtime.IsClosed != Animation.IsClosed)
                Animation.SetClosed(Runtime.IsClosed, true);
        }
        private void OnDestroy()
        {
            if (Animation != null) Animation.StateChanged -= OnStateChanged;
        }
    }
}
