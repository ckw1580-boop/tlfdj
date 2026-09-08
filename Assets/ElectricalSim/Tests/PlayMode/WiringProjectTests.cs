using System;
using System.Collections;
using System.IO;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace ElectricalSim.Tests
{
    public sealed class WiringProjectTests
    {
        private string directory;
        private SimulationController controller;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            directory = Path.Combine(Path.GetTempPath(), "本地接线测试 " + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            SceneManager.LoadScene("ElectricalTraining");
            yield return null;
            yield return null;
            controller = Object.FindObjectOfType<SimulationController>();
        }

        [TearDown]
        public void TearDown() => Directory.Delete(directory, true);

        private string PathFor(string name) => Path.Combine(directory, name + ".cc3d");

        private WireConnection AddWire()
        {
            var wire = controller.Graph.AddWire("QF.T1", "KM1.L1", new Color(.2f, .3f, .8f), "ElectricalWire", .025f);
            wire.FaultSide = false;
            return wire;
        }

        private void Invoke(string method, params object[] args) => typeof(SimulationController)
            .GetMethod(method, BindingFlags.NonPublic | BindingFlags.Instance).Invoke(controller, args);

        [UnityTest]
        public IEnumerator SavedFileRestoresInFreshSceneAndRemainsEditable()
        {
            var wire = AddWire();
            controller.AddBendPointToLastWire(new Vector3(0, 1, -1));
            var id = wire.Id;
            var points = wire.Points.ToArray();
            Assert.That(controller.SaveCc3dToPath(PathFor("初始接线")), Is.EqualTo(WiringFileResult.Success), controller.LastFileError);
            SceneManager.LoadScene("ElectricalTraining");
            yield return null;
            yield return null;
            controller = Object.FindObjectOfType<SimulationController>();
            Assert.That(controller.OpenCc3dFromPath(PathFor("初始接线")), Is.EqualTo(WiringFileResult.Success), controller.LastFileError);
            yield return null;
            Assert.That(controller.Mode, Is.EqualTo(SimulationMode.Wiring));
            Assert.That(controller.Graph.Wires.Single().Id, Is.EqualTo(id));
            Assert.That(controller.Graph.Wires.Single().Points, Is.EqualTo(points));
            Assert.That(controller.HasUnsavedWiring, Is.False);
            var view = Object.FindObjectsOfType<ElectricalWireView>().Single(v => v.Connection.Id == id);
            Invoke("SelectWire", view);
            Assert.That(controller.SelectedWire.Id, Is.EqualTo(id));
            Assert.That(controller.AddBendPointToLastWire(new Vector3(.1f, 1.1f, -1)), Is.True);
            Assert.That(controller.HasUnsavedWiring, Is.True);
            controller.UndoWiring();
            Assert.That(controller.HasUnsavedWiring, Is.False);
            controller.RedoWiring();
            Assert.That(controller.HasUnsavedWiring, Is.True);
            Assert.That(controller.SaveCc3dToPath(PathFor("修改接线")), Is.EqualTo(WiringFileResult.Success));
            Assert.That(controller.OpenCc3dFromPath(PathFor("修改接线")), Is.EqualTo(WiringFileResult.Success));
            Assert.That(controller.Graph.Wires.Single().Points.Count, Is.EqualTo(points.Length + 1));
            Invoke("DeleteWireSelectionOrLast");
            Assert.That(controller.Graph.Wires, Is.Empty);
            Assert.That(controller.HasUnsavedWiring, Is.True);
            controller.UndoWiring();
            Assert.That(controller.HasUnsavedWiring, Is.False);
        }

        [Test]
        public void ResetReferenceUndoAndOpenTrackSavedStateAndClearOldHistory()
        {
            AddWire();
            Assert.That(controller.SaveCc3dToPath(PathFor("接线")), Is.EqualTo(WiringFileResult.Success));
            controller.ResetTraining();
            Assert.That(controller.HasUnsavedWiring, Is.True);
            controller.UndoWiring();
            Assert.That(controller.HasUnsavedWiring, Is.False);
            controller.LoadReferenceWiring();
            Assert.That(controller.HasUnsavedWiring, Is.True);
            controller.SetMode(SimulationMode.Wiring);
            var port = Object.FindObjectsOfType<ElectricalPortView>(true).First(p => p.QualifiedPort == "FR.T1");
            Invoke("BeginWireRoute", port);
            Assert.That(controller.IsRoutingWire, Is.True);
            Assert.That(controller.OpenCc3dFromPath(PathFor("接线")), Is.EqualTo(WiringFileResult.Success));
            Assert.That(controller.IsRoutingWire, Is.False);
            controller.UndoWiring();
            controller.RedoWiring();
            Assert.That(controller.Graph.Wires.Count, Is.EqualTo(1));
            Assert.That(controller.HasUnsavedWiring, Is.False);
        }

        [TestCase("cancel")]
        [TestCase("badJson")]
        [TestCase("unknownPort")]
        [TestCase("missingFile")]
        public void FailedOrCancelledOpenRetainsWiringAndHistory(string failure)
        {
            var original = AddWire();
            controller.AddBendPointToLastWire(Vector3.one);
            var path = PathFor("损坏文件");
            if (failure == "cancel") path = "";
            else if (failure == "badJson") File.WriteAllText(path, "not json");
            else if (failure == "unknownPort")
            {
                var graph = new CircuitGraph();
                graph.AddWire("unknown.port", "QF.T1", Color.red);
                Cc3dSerializer.Save(path, Cc3dCircuitAdapter.Export(graph, Array.Empty<DeviceSceneState>()));
            }
            Assert.That(controller.OpenCc3dFromPath(path), Is.EqualTo(failure == "cancel" ? WiringFileResult.Cancelled : WiringFileResult.Failed));
            Assert.That(controller.Graph.Wires.Single(), Is.SameAs(original));
            controller.UndoWiring();
            Assert.That(controller.Graph.Wires.Single().Points, Is.Empty);
        }

        [TestCase(UnsavedWiringChoice.Cancel, false)]
        [TestCase(UnsavedWiringChoice.Discard, true)]
        [TestCase(UnsavedWiringChoice.Save, true)]
        public void UnsavedPromptBranchesOnlyProceedWhenAppropriate(UnsavedWiringChoice choice, bool opens)
        {
            var wire = AddWire();
            Cc3dSerializer.Save(PathFor("空接线"), new Cc3dDocument());
            var dialogs = new FakeDialogs { OpenPath = PathFor("空接线"), SavePath = PathFor("保留接线") };
            controller.FileDialogs = dialogs;
            controller.OpenCc3d();
            Assert.That(controller.IsFileOperationActive, Is.True);
            Assert.That(Object.FindObjectOfType<TrainingCameraController>().InputBlocked, Is.True);
            controller.OpenCc3d();
            Assert.That(dialogs.ConfirmCount, Is.EqualTo(1));
            dialogs.Complete(choice);
            Assert.That(dialogs.OpenCount, Is.EqualTo(opens ? 1 : 0));
            Assert.That(controller.Graph.Wires.Count, Is.EqualTo(opens ? 0 : 1));
            Assert.That(controller.IsFileOperationActive, Is.False);
            Assert.That(Object.FindObjectOfType<TrainingCameraController>().InputBlocked, Is.False);
            if (choice == UnsavedWiringChoice.Save)
                Assert.That(Cc3dCircuitAdapter.ReadWires(Cc3dSerializer.Load(dialogs.SavePath)).Single().Id, Is.EqualTo(wire.Id));
        }

        [TestCase(false)]
        [TestCase(true)]
        public void CancelledOrFailedSaveStopsOpening(bool fail)
        {
            AddWire();
            var dialogs = new FakeDialogs { SavePath = fail ? directory : "" };
            controller.FileDialogs = dialogs;
            controller.OpenCc3d();
            dialogs.Complete(UnsavedWiringChoice.Save);
            Assert.That(dialogs.OpenCount, Is.Zero);
            Assert.That(controller.Graph.Wires.Count, Is.EqualTo(1));
            Assert.That(controller.HasUnsavedWiring, Is.True);
            Assert.That(controller.IsFileOperationActive, Is.False);
            if (fail) Assert.That(controller.LastFileError, Does.Contain("保存接线失败"));
        }

        [Test]
        public void RealToolbarButtonsUseDialogsEveryTimeAndRememberSuccessfulPath()
        {
            AddWire();
            var dialogs = new FakeDialogs { SavePath = PathFor("电柜 A") };
            controller.FileDialogs = dialogs;
            var save = Object.FindObjectsOfType<Button>().Single(b => b.name == "saveBtn");
            var open = Object.FindObjectsOfType<Button>().Single(b =>
                b.GetComponentsInChildren<Image>().Any(i => i.sprite != null && i.sprite.name == "打开"));
            Assert.That(open.name, Is.EqualTo("btn_submit"), "原始文件夹图标的对象名与用途不一致。");
            save.onClick.Invoke();
            Assert.That(File.Exists(dialogs.SavePath), Is.True, controller.LastFileError);
            save.onClick.Invoke();
            Assert.That(dialogs.SaveCount, Is.EqualTo(2));
            Assert.That(dialogs.Directory, Is.EqualTo(directory));
            Assert.That(dialogs.FileName, Is.EqualTo("电柜 A.cc3d"));
            dialogs.OpenPath = dialogs.SavePath;
            open.onClick.Invoke();
            Assert.That(dialogs.OpenCount, Is.EqualTo(1));
            Assert.That(dialogs.ConfirmCount, Is.Zero);
            Assert.That(controller.Mode, Is.EqualTo(SimulationMode.Wiring));
        }

        [UnityTest]
        public IEnumerator ActualConfirmationModalCanCancelAndBlocksCamera()
        {
            AddWire();
            controller.OpenCc3d();
            yield return null;
            var modal = GameObject.Find("UnsavedWiringDialog");
            Assert.That(modal, Is.Not.Null);
            Assert.That(modal.GetComponent<Image>().raycastTarget, Is.True);
            Assert.That(modal.GetComponent<Canvas>().overrideSorting, Is.True);
            Assert.That(Object.FindObjectOfType<TrainingCameraController>().InputBlocked, Is.True);
            foreach (var button in modal.GetComponentsInChildren<Button>())
            {
                Assert.That(button.FindSelectableOnLeft().transform.IsChildOf(modal.transform), Is.True);
                Assert.That(button.FindSelectableOnRight().transform.IsChildOf(modal.transform), Is.True);
                Assert.That(button.FindSelectableOnUp().transform.IsChildOf(modal.transform), Is.True);
                Assert.That(button.FindSelectableOnDown().transform.IsChildOf(modal.transform), Is.True);
            }
            modal.GetComponentsInChildren<Button>().Single(b => b.name == "CancelOpen").onClick.Invoke();
            yield return null;
            Assert.That(GameObject.Find("UnsavedWiringDialog"), Is.Null);
            Assert.That(controller.IsFileOperationActive, Is.False);
            Assert.That(controller.Graph.Wires.Count, Is.EqualTo(1));
        }

        private sealed class FakeDialogs : IWiringFileDialogs
        {
            public string OpenPath = "";
            public string SavePath = "";
            public string Directory;
            public string FileName;
            public int OpenCount, SaveCount, ConfirmCount;
            public Action<UnsavedWiringChoice> Complete;
            public string ChooseOpen(string directory) { OpenCount++; Directory = directory; return OpenPath; }
            public string ChooseSave(string directory, string fileName)
            { SaveCount++; Directory = directory; FileName = fileName; return SavePath; }
            public void ConfirmUnsaved(Action<UnsavedWiringChoice> completed) { ConfirmCount++; Complete = completed; }
        }
    }
}
