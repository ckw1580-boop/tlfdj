using UnityEngine;

namespace ElectricalSim
{
    public sealed partial class SimulationController
    {
        private int resetInputResumeFrame = -1;
        public ResetConfirmationPresenter ResetConfirmation { get; private set; }
        public bool IsResetConfirmationOpen => ResetConfirmation != null && ResetConfirmation.IsOpen;

        public void RegisterResetConfirmation(ResetConfirmationPresenter presenter)
        {
            if (ResetConfirmation != null) ResetConfirmation.VisibilityChanged -= OnResetConfirmationVisibilityChanged;
            ResetConfirmation = presenter;
            if (presenter != null) presenter.VisibilityChanged += OnResetConfirmationVisibilityChanged;
            OnResetConfirmationVisibilityChanged(IsResetConfirmationOpen);
        }

        public void RequestResetTraining()
        {
            if (IsInteractionBlocked || ResetConfirmation == null) return;
            ResetConfirmation.Open(ResetTraining);
        }

        private void OnResetConfirmationVisibilityChanged(bool visible)
        {
            if (!visible) resetInputResumeFrame = Time.frameCount;
            if (trainingCamera != null) trainingCamera.SetResetInputBlocked(visible);
            if (!visible) return;
            portHover?.Hide();
            ReleasePanelButton();
            Multimeter?.SuspendPointer();
            VoltageProbe?.SuspendPointer();
            // End pointer gestures without discarding the unfinished wire route or selection.
            draggedDevice = null;
            draggingWirePoint = false;
        }
    }
}
