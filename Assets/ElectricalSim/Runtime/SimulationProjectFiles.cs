using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEngine.EventSystems;

namespace ElectricalSim
{
    public enum WiringFileResult { Success, Cancelled, Failed }
    public enum UnsavedWiringChoice { Save, Discard, Cancel }

    public interface IWiringFileDialogs
    {
        string ChooseOpen(string directory);
        string ChooseSave(string directory, string fileName);
        void ConfirmUnsaved(Action<UnsavedWiringChoice> completed);
    }

    public sealed partial class SimulationController
    {
        private List<WireConnection> savedWires = new List<WireConnection>();
        private string lastProjectDirectory;
        private string lastProjectFileName = "接线.cc3d";
        private int fileInputResumeFrame = -1;
        private bool cameraInputWasBlocked;
        private IWiringFileDialogs fileDialogs;

        public bool IsFileOperationActive { get; private set; }
        public string LastFileError { get; private set; } = string.Empty;
        public IWiringFileDialogs FileDialogs
        {
            get => fileDialogs ??= new UnityWiringFileDialogs(statusText.canvas, statusText.font);
            set => fileDialogs = value;
        }

        // Compare stable wire data, independent of ordering and generated CC3D point IDs.
        public bool HasUnsavedWiring
        {
            get
            {
                if (savedWires.Count != graph.Wires.Count) return true;
                var current = graph.Wires.OrderBy(w => w.Id, StringComparer.Ordinal).ToArray();
                var saved = savedWires.OrderBy(w => w.Id, StringComparer.Ordinal).ToArray();
                for (var i = 0; i < current.Length; i++)
                {
                    var a = current[i];
                    var b = saved[i];
                    if (a.Id != b.Id || a.StartPort != b.StartPort || a.EndPort != b.EndPort ||
                        !a.Color.Equals(b.Color) || !a.Area.Equals(b.Area) || a.LineType != b.LineType ||
                        a.FaultSide != b.FaultSide || !a.Points.SequenceEqual(b.Points)) return true;
                }
                return false;
            }
        }

        public void SaveCc3d()
        {
            if (IsFileOperationActive) return;
            BeginFileOperation();
            try { SaveWithDialog(); }
            catch (Exception exception) { ReportFileFailure("保存", exception); }
            finally { EndFileOperation(); }
        }

        public void OpenCc3d()
        {
            if (IsFileOperationActive) return;
            BeginFileOperation();
            try
            {
                if (HasUnsavedWiring) FileDialogs.ConfirmUnsaved(ContinueOpen);
                else ContinueOpen(UnsavedWiringChoice.Discard);
            }
            catch (Exception exception)
            {
                ReportFileFailure("打开", exception);
                EndFileOperation();
            }
        }

        private void ContinueOpen(UnsavedWiringChoice choice)
        {
            if (!IsFileOperationActive) return;
            try
            {
                if (choice == UnsavedWiringChoice.Cancel) return;
                if (choice == UnsavedWiringChoice.Save && SaveWithDialog() != WiringFileResult.Success) return;
                OpenCc3dFromPath(FileDialogs.ChooseOpen(DialogDirectory()));
            }
            catch (Exception exception) { ReportFileFailure("打开", exception); }
            finally { EndFileOperation(); }
        }

        private WiringFileResult SaveWithDialog() =>
            SaveCc3dToPath(FileDialogs.ChooseSave(DialogDirectory(), lastProjectFileName));

        public WiringFileResult SaveCc3dToPath(string path)
        {
            LastFileError = string.Empty;
            if (string.IsNullOrWhiteSpace(path)) return WiringFileResult.Cancelled;
            try
            {
                path = Path.GetFullPath(path);
                var snapshot = SnapshotWires();
                var staged = new CircuitGraph();
                staged.ReplaceWires(snapshot);
                var states = FindObjectsOfType<ElectricalDeviceView>()
                    .Select(view => new DeviceSceneState(view.Runtime.DeviceId, view.Runtime.Kind.ToString(),
                        view.gameObject.name, view.transform.position, view.transform.rotation));
                var document = Cc3dCircuitAdapter.Export(staged, states, loadedDocument);
                ValidateProjectWires(Cc3dCircuitAdapter.ReadWires(document));
                Cc3dSerializer.Save(path, document);
                loadedDocument = document;
                savedWires = snapshot;
                RememberProjectPath(path);
                ClearWireSelection();
                SetStatus($"已保存接线：{path}\n共 {snapshot.Count} 条导线。", false);
                return WiringFileResult.Success;
            }
            catch (Exception exception) { return ReportFileFailure("保存", exception); }
        }

        public WiringFileResult OpenCc3dFromPath(string path)
        {
            LastFileError = string.Empty;
            if (string.IsNullOrWhiteSpace(path)) return WiringFileResult.Cancelled;
            try
            {
                path = Path.GetFullPath(path);
                var document = Cc3dSerializer.Load(path);
                var incoming = Cc3dCircuitAdapter.ReadWires(document);
                ValidateProjectWires(incoming);
                // Legacy projects have no cabinet-side field; retain the existing
                // convention of resolving it against the current view on import.
                foreach (var wire in incoming) wire.FaultSide ??= trainingCamera.IsViewingFaultSide;
                graph.ReplaceWires(incoming);
                SetMode(SimulationMode.Wiring);
                RefreshWireViews();
                undoWires.Clear();
                redoWires.Clear();
                loadedDocument = document;
                savedWires = SnapshotWires();
                RememberProjectPath(path);
                SetStatus($"已打开接线：{path}\n共 {graph.Wires.Count} 条导线，可继续修改。", false);
                return WiringFileResult.Success;
            }
            catch (Exception exception) { return ReportFileFailure("打开", exception); }
        }

        private void ValidateProjectWires(IEnumerable<WireConnection> wires)
        {
            foreach (var wire in wires)
            {
                var preset = (wire.FaultSide ?? trainingCamera.IsViewingFaultSide)
                    ? TrainingViewPreset.FaultBack : TrainingViewPreset.WiringFront;
                var jumper = wire.LineType.IndexOf("jumper", StringComparison.OrdinalIgnoreCase) >= 0 ||
                             wire.LineType.IndexOf("rope", StringComparison.OrdinalIgnoreCase) >= 0;
                foreach (var port in new[] { wire.StartPort, wire.EndPort })
                    if (ResolveWireAnchor(port, preset, jumper) == null)
                        throw new InvalidDataException($"导线 {wire.Id} 的端子不存在或无法定位：{port}");
            }
        }

        private string DialogDirectory() => !string.IsNullOrEmpty(lastProjectDirectory) && Directory.Exists(lastProjectDirectory)
            ? lastProjectDirectory : ProjectDirectory();

        private void RememberProjectPath(string path)
        {
            lastProjectDirectory = Path.GetDirectoryName(path);
            lastProjectFileName = Path.GetFileName(path);
        }

        private WiringFileResult ReportFileFailure(string operation, Exception exception)
        {
            LastFileError = operation + "接线失败：" + exception.Message;
            Debug.LogWarning(LastFileError + "\n" + exception);
            SetStatus(LastFileError, true);
            return WiringFileResult.Failed;
        }

        private void BeginFileOperation()
        {
            IsFileOperationActive = true;
            LastFileError = string.Empty;
            cameraInputWasBlocked = trainingCamera.InputBlocked;
            trainingCamera.InputBlocked = true;
            portHover?.Hide();
            EventSystem.current?.SetSelectedGameObject(null);
        }

        private void EndFileOperation()
        {
            IsFileOperationActive = false;
            trainingCamera.InputBlocked = cameraInputWasBlocked;
            fileInputResumeFrame = Time.frameCount;
        }
    }
}
