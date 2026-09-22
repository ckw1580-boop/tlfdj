using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace ElectricalSim
{
    public sealed partial class SimulationController
    {
        private readonly List<FrontBreakerView> frontBreakers = new List<FrontBreakerView>();
        public IReadOnlyList<FrontBreakerView> FrontBreakers => frontBreakers;
        public FrontBreakerView SelectedFrontBreaker { get; private set; }
        public FrontBreakerPropertiesPresenter FrontBreakerProperties { get; private set; }
        public bool CanOperateFrontBreakers => !IsInteractionBlocked &&
            (Mode == SimulationMode.Simulate || Mode == SimulationMode.Drag || CanOperateFaultControls);

        public void RegisterFrontBreakers(IEnumerable<FrontBreakerView> views, Canvas canvas, Font font)
        {
            frontBreakers.Clear();
            frontBreakers.AddRange(views);
            FrontBreakerProperties = new GameObject("Front Breaker Properties", typeof(RectTransform))
                .AddComponent<FrontBreakerPropertiesPresenter>();
            FrontBreakerProperties.Initialize(this, canvas, font);
            ModeChanged += OnFrontBreakerModeChanged;
            OnFrontBreakerModeChanged(Mode);
        }

        private void OnFrontBreakerModeChanged(SimulationMode mode)
        {
            SelectFrontBreaker(null);
            foreach (var view in frontBreakers) view.Animation.SetHighlighted(mode == SimulationMode.Drag);
        }

        public void SelectFrontBreaker(FrontBreakerView view)
        {
            if (view != null) SelectInverter(false);
            if (view != null)
            {
                if (IsInteractionBlocked || Mode == SimulationMode.Wiring || !frontBreakers.Contains(view)) return;
                ClearWireSelection(); SelectContactor(null); SelectThermalRelay(null); SelectRelay(null);
                SelectPanelDevice(null); SelectPlc(null); SelectSceneIo(null); SelectPowerTerminalBlock(null);
            }
            SelectedFrontBreaker = view;
            FrontBreakerProperties?.Show(view);
        }

        public bool SetFrontBreakerClosed(FrontBreakerView view, bool closed)
        {
            if (!CanOperateFrontBreakers || view == null || !frontBreakers.Contains(view)) return false;
            view.SetClosed(closed);
            SetStatus($"{view.Runtime.DeviceId} 已{(closed ? "合闸" : "开闸")}。", false);
            return true;
        }

        private bool HandleFrontBreakerHit(Collider collider)
        {
            var view = collider != null ? collider.GetComponentInParent<FrontBreakerView>() : null;
            if (view == null) { SelectFrontBreaker(null); return false; }
            if (view.IsHandle(collider) && CanOperateFrontBreakers)
                SetFrontBreakerClosed(view, !view.Runtime.IsClosed);
            else SelectFrontBreaker(view);
            return true;
        }

        public string DescribeFrontBreaker(FrontBreakerView view)
        {
            if (view == null) return string.Empty;
            return view.Runtime.DeviceId + " · 四极断路器\n" +
                "安装位置：柜体正面\n状态：" + (view.Runtime.IsClosed ? "合闸" : "开闸") +
                "\n\n上排端子：N1、L1、L3、L5\n下排端子：N2、L2、L4、L6\n\n内部接点（四极同步）：\n" +
                string.Join("\n", FrontBreakerView.Contacts.Select(p =>
                    view.Runtime.DeviceId + "." + p.A + " ↔ " + view.Runtime.DeviceId + "." + p.B +
                    (view.Runtime.IsClosed ? "  接通" : "  断开"))) +
                "\n\n本断路器独立控制以上接点，供电关系由实际接线决定。";
        }
    }
}
