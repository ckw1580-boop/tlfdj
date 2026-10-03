using UnityEngine;

namespace ElectricalSim
{
    public sealed partial class SimulationController
    {
        private int homeInputResumeFrame = -1;
        public HomePagePresenter HomePage { get; private set; }
        public bool IsHomePageOpen => HomePage != null && HomePage.IsOpen;

        public void RegisterHomePage(HomePagePresenter homePage)
        {
            if (HomePage != null) HomePage.VisibilityChanged -= OnHomeVisibilityChanged;
            HomePage = homePage;
            if (HomePage != null) HomePage.VisibilityChanged += OnHomeVisibilityChanged;
            OnHomeVisibilityChanged(IsHomePageOpen);
        }

        private void OnHomeVisibilityChanged(bool visible)
        {
            if (!visible) homeInputResumeFrame = Time.frameCount;
            if (trainingCamera != null) trainingCamera.SetHomeInputBlocked(visible);
            if (!visible) return;
            portHover?.Hide();
            ReleasePanelButton();
            Multimeter?.SuspendPointer();
            if (Oscilloscope != null) { Oscilloscope.InteractionBlocked = true; Oscilloscope.SuspendPointer(); }
            // End pointer gestures without clearing the selection or unfinished wire route.
            draggedDevice = null;
            draggingWirePoint = false;
        }
    }
}
