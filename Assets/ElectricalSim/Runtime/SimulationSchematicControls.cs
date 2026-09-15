using UnityEngine;

namespace ElectricalSim
{
    public sealed partial class SimulationController
    {
        private int schematicInputResumeFrame = -1;
        public SchematicGalleryPresenter SchematicGallery { get; private set; }
        public bool IsSchematicViewerOpen => SchematicGallery != null && SchematicGallery.IsViewerOpen;
        public bool IsInteractionBlocked => IsFileOperationActive || IsSchematicViewerOpen ||
            Time.frameCount <= fileInputResumeFrame || Time.frameCount <= schematicInputResumeFrame;

        public void RegisterSchematicGallery(SchematicGalleryPresenter gallery)
        {
            if (SchematicGallery != null) SchematicGallery.ViewerVisibilityChanged -= OnSchematicViewerVisibility;
            SchematicGallery = gallery;
            if (gallery != null) gallery.ViewerVisibilityChanged += OnSchematicViewerVisibility;
            OnSchematicViewerVisibility(IsSchematicViewerOpen);
        }

        private void OnSchematicViewerVisibility(bool visible)
        {
            if (!visible) schematicInputResumeFrame = Time.frameCount;
            if (trainingCamera != null) trainingCamera.SetSchematicInputBlocked(visible);
            if (visible) portHover?.Hide();
        }
    }
}
