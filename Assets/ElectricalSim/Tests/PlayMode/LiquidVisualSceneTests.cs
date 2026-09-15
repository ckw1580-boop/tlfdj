using System.Collections;
using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace ElectricalSim.Tests
{
    public sealed class LiquidVisualSceneTests
    {
        private SimulationController controller;
        private LiquidTankView view;
        private Transform space;
        [UnitySetUp]
        public IEnumerator Setup()
        {
            SceneManager.LoadScene("ElectricalTraining"); yield return null; yield return null;
            controller = Object.FindObjectOfType<SimulationController>(); controller.enabled = false;
            view = Object.FindObjectOfType<LiquidTankView>(); view.enabled = false;
            space = GameObject.Find("OriginalLabEnvironment").transform.Find(SceneIoCatalog.EnvironmentPath);
        }
        private void Step(float seconds) { controller.AdvanceSimulation(seconds); view.AdvanceVisuals(0); }
        private void Wire(string a, string b) => controller.Graph.AddWire(a, b, Color.red);
        private void PowerMotor(string id)
        {
            for (var i = 0; i < 3; i++) Wire("POWER.L" + (i + 1), id + "." + new[] { "U", "V", "W" }[i]);
        }
        private void PowerValve(int i)
        {
            Wire("DuanZiPai_6.V_1", "DuanZiPai_8.Diancifa" + i + "_VCC");
            Wire("DuanZiPai_6.N_1", "DuanZiPai_8.Diancifa" + i + "_GND");
        }
        [UnityTest]
        public IEnumerator ThreeBasinsKeepAuthoredLevelsAndMaterialsWhileOldFillAndArrowsStayHidden()
        {
            var paths = new[] { "YELLOW01", "RED01", "xiang01 (1)/RED01 (1)" };
            var colors = new[] { LiquidTankView.LiquidAColor, LiquidTankView.LiquidBColor, LiquidTankView.MixedColor };
            var heights = new[] { 0.7265f, 0.7265f, 0.3103f };
            for (var i = 0; i < paths.Length; i++)
            {
                var root = space.Find("mesh/View/YeTiHunHe/" + paths[i]);
                var renderer = root.GetComponent<Renderer>();
                Assert.That(renderer.enabled && renderer.gameObject.activeInHierarchy, Is.True, paths[i]);
                Assert.That(renderer.sharedMaterial.color, Is.EqualTo(colors[i]));
                Assert.That(renderer.sharedMaterial.shader.name, Is.EqualTo("ElectricalSim/LiquidSurface"));
                var positions = root.GetComponent<MeshFilter>().sharedMesh.vertices.Select(p => space.InverseTransformPoint(root.TransformPoint(p))).ToArray();
                Assert.That(positions.Max(p => p.y), Is.EqualTo(heights[i]).Within(0.001f));
            }
            foreach (var path in new[] { "rivet/15/JinLiaoliudong_A", "rivet/16/JinLiaoliudong_B", "rivet/17/ShuiLiu_1",
                "rivet/18/ShuiLiu_2", "rivet/19/ShuiLiu_3", "rivet/20/ShuiLiu_4", "mesh/View/YeTiHunHe/guangdao" })
                Assert.That(space.Find(path).GetComponentsInChildren<Renderer>(true).All(r => !r.enabled), Is.True, path);
            Assert.That(view.Pipes.All(p => !p.Body.enabled && !p.Jet.enabled), Is.True);
            view.AdvanceVisuals(1);
            var material = space.Find("mesh/View/YeTiHunHe/RED01").GetComponent<Renderer>().sharedMaterial;
            Assert.That(material.GetFloat("_FlowTime"), Is.GreaterThan(0));
            yield return null;
        }
        [UnityTest]
        public IEnumerator IndependentPumpsRespectValvesAndResetClearsTransientState()
        {
            controller.PanelPower.StartForAssessment(); PowerMotor("M_DOUBLE");
            controller.SetMode(SimulationMode.Simulate); controller.AdvanceSimulation(0.04f);
            Step(20);
            Assert.That(view.Pipes[0].State.IsBlocked, Is.True);
            Assert.That(view.Pipes[0].State.DownstreamOpacity, Is.Zero);
            Assert.That(view.Pipes[1].State.Front, Is.Zero);
            Assert.That(controller.Liquid.Level, Is.Zero);
            PowerValve(1); controller.AdvanceSimulation(0.04f);
            var level = controller.Liquid.Level;
            Assert.That(level, Is.Zero); // Opening a valve does not yet deliver to the tank.
            view.AdvanceVisuals(20); // Rendering cannot advance transport or volume.
            Assert.That(controller.Liquid.Level, Is.Zero);
            Step(20);
            Assert.That(controller.Liquid.Level, Is.GreaterThan(level));
            Assert.That(view.Pipes[0].State.Front, Is.EqualTo(view.Pipes[0].State.Length));
            Assert.That(view.Pipes[0].Jet.enabled, Is.True);
            PowerMotor("M1"); PowerValve(2); controller.AdvanceSimulation(0.04f); Step(20);
            Assert.That(view.Pipes[1].Body.enabled, Is.True);
            var valveLead = controller.Graph.Wires.Single(w => w.EndPort == "DuanZiPai_8.Diancifa1_VCC");
            controller.Graph.RemoveWire(valveLead.Id); controller.AdvanceSimulation(0.04f); Step(0.5f);
            Assert.That(view.Pipes[0].State.IsBlocked, Is.True);
            Assert.That(view.Pipes[0].State.DownstreamOpacity, Is.Zero);
            Assert.That(view.Pipes[1].State.DownstreamOpacity, Is.EqualTo(1));
            controller.SetMode(SimulationMode.View); Step(0.25f);
            Assert.That(view.Pipes[0].State.UpstreamOpacity, Is.EqualTo(0.5f));
            controller.ResetLiquid();
            Assert.That(view.Pipes.All(p => p.State.Front == 0 && !p.Body.enabled), Is.True);
            yield return null;
        }
        [UnityTest]
        public IEnumerator PendingFileDialogFreezesAnimationAndSuccessfulLoadClearsPipes()
        {
            controller.PanelPower.StartForAssessment(); PowerMotor("M_DOUBLE"); PowerValve(1);
            controller.SetMode(SimulationMode.Simulate); controller.AdvanceSimulation(0.04f); Step(4);
            var state = view.Pipes[0].State;
            var front = state.Front; var phase = state.UpstreamPhase;
            var dialogs = new PendingDialogs(); controller.FileDialogs = dialogs;
            controller.OpenCc3d();
            Assert.That(controller.IsFileOperationActive, Is.True);
            Step(10);
            Assert.That(state.Front, Is.EqualTo(front));
            Assert.That(state.UpstreamPhase, Is.EqualTo(phase));
            dialogs.Complete(UnsavedWiringChoice.Cancel);
            Assert.That(controller.IsFileOperationActive, Is.False);
            Step(0.2f);
            Assert.That(state.Front, Is.GreaterThan(front));
            var file = Path.Combine(Application.temporaryCachePath, "liquid-visual-" + System.Guid.NewGuid().ToString("N") + ".cc3d");
            try
            {
                Assert.That(controller.SaveCc3dToPath(file), Is.EqualTo(WiringFileResult.Success));
                Assert.That(controller.OpenCc3dFromPath(file), Is.EqualTo(WiringFileResult.Success));
                Assert.That(view.Pipes.All(p => p.State.Front == 0 && !p.Body.enabled && !p.Jet.enabled), Is.True);
            }
            finally { if (File.Exists(file)) File.Delete(file); }
            yield return null;
        }
        private sealed class PendingDialogs : IWiringFileDialogs
        {
            public System.Action<UnsavedWiringChoice> Complete;
            public string ChooseOpen(string directory) => "";
            public string ChooseSave(string directory, string fileName) => "";
            public void ConfirmUnsaved(System.Action<UnsavedWiringChoice> completed) => Complete = completed;
        }
        [UnityTest]
        public IEnumerator AArrivesAtBottomThenBBlendsToPurple() { yield return CaptureColourSequence(false); }
        [UnityTest]
        public IEnumerator BArrivesAtBottomThenABlendsToPurple() { yield return CaptureColourSequence(true); }
        private IEnumerator CaptureColourSequence(bool bFirst)
        {
            Object.FindObjectOfType<TrainingCameraController>().enabled = false;
            foreach (var canvas in Object.FindObjectsOfType<Canvas>().Where(c => c.isRootCanvas)) canvas.enabled = false;
            var camera = Camera.main; camera.nearClipPlane = 0.01f;
            Position(camera, new Vector3(1.40f, 1.48f, -3.15f), new Vector3(1.40f, 0.89f, -4.84f));
            var first = bFirst ? 1 : 0; var second = 1 - first;
            var kind = bFirst ? LiquidContents.B : LiquidContents.A;
            var sourceColor = bFirst ? LiquidTankView.LiquidBColor : LiquidTankView.LiquidAColor;
            var prefix = bFirst ? "liquid-arrival-b" : "liquid-arrival-a";
            var water = space.Find("rivet/5/JiaoBanWater/mesh/Water").GetComponentInChildren<Renderer>();
            controller.PanelPower.StartForAssessment(); PowerMotor(SceneIoCatalog.Pumps[first].MotorId); PowerValve(first + 1);
            controller.SetMode(SimulationMode.Simulate);
            for (var i = 0; i < 1200 && view.Pipes[first].Stream.JetFront < 0.20f; i++) Step(0.02f);
            Assert.That(view.Pipes[first].Stream.JetFront, Is.GreaterThanOrEqualTo(0.20f));
            Assert.That(view.Pipes[first].Jet.enabled, Is.True);
            Assert.That(controller.Liquid.Level, Is.Zero);
            Assert.That(water.enabled, Is.False);
            yield return null; Capture(camera, prefix + "-falling.png");
            for (var i = 0; i < 300 && controller.Liquid.Level == 0; i++) Step(0.02f);
            Assert.That(controller.Liquid.Level, Is.GreaterThan(0));
            Assert.That(controller.Liquid.Contents, Is.EqualTo(kind));
            yield return null; Capture(camera, prefix + "-contact.png");
            Step(4);
            var expected = sourceColor; expected.a = 0.78f;
            Assert.That(water.sharedMaterial.color, Is.EqualTo(expected));
            yield return null; Capture(camera, prefix + "-single.png");
            var lead = controller.Graph.Wires.Single(w => w.EndPort == "DuanZiPai_8.Diancifa" + (first + 1) + "_VCC");
            controller.Graph.RemoveWire(lead.Id);
            PowerMotor(SceneIoCatalog.Pumps[second].MotorId); PowerValve(second + 1);
            for (var i = 0; i < 1200 && controller.Liquid.Contents == kind; i++)
            {
                Assert.That(controller.Liquid.CurrentColor, Is.EqualTo(sourceColor));
                Step(0.02f);
            }
            Assert.That(controller.Liquid.Contents, Is.EqualTo(LiquidContents.Mixed));
            Assert.That(controller.Liquid.MixProgress, Is.InRange(0d, 0.021d));
            Step(0.4f);
            yield return null; Capture(camera, prefix + "-blending.png");
            Step(0.65f);
            Assert.That(controller.Liquid.CurrentColor, Is.EqualTo(LiquidTankView.MixedColor));
            Assert.That(controller.Graph.Devices["M2"], Is.TypeOf<ElectricalDeviceRuntime>());
            Assert.That(((ElectricalDeviceRuntime)controller.Graph.Devices["M2"]).ActualSpeedRpm, Is.Zero);
            yield return null; Capture(camera, prefix + "-mixed.png");
            PowerValve(3); Step(0.04f);
            Assert.That(view.Pipes[2].Body.sharedMaterial.color, Is.EqualTo(controller.Liquid.CurrentColor));
        }
        [UnityTest]
        public IEnumerator CaptureBasinsBlockedFlowAndStoppedPipes()
        {
            controller.ConfigureLiquid(new LiquidConfiguration { InitialLevelPercent = 55 }); controller.ResetLiquid();
            Object.FindObjectOfType<TrainingCameraController>().enabled = false;
            foreach (var canvas in Object.FindObjectsOfType<Canvas>().Where(c => c.isRootCanvas)) canvas.enabled = false;
            var camera = Camera.main; camera.nearClipPlane = 0.01f;
            var geometry = Object.FindObjectsOfType<Renderer>(true).Where(r => {
                var p = space.InverseTransformPoint(r.bounds.center);
                return p.x > -3 && p.x < 3 && p.y > 0 && p.y < 3 && p.z > -5.5f && p.z < -4;
            }).Select(r => new {
                path = FullPath(r.transform), r.enabled, active = r.gameObject.activeInHierarchy,
                materials = r.sharedMaterials.Select(m => m == null ? "null" : m.name + " | " + m.shader.name).ToArray()
            });
            File.WriteAllText("Logs/liquid-renderers.json", Newtonsoft.Json.JsonConvert.SerializeObject(geometry, Newtonsoft.Json.Formatting.Indented));
            Position(camera, new Vector3(-1.8f, 1.8f, -3.0f), new Vector3(-1.8f, 0.78f, -4.73f));
            view.AdvanceVisuals(1); yield return null; Capture(camera, "liquid-basins-ab.png");
            Position(camera, new Vector3(2.3f, 1.55f, -3.25f), new Vector3(2.23f, 0.64f, -4.82f));
            yield return null; Capture(camera, "liquid-basin-mixed.png");
            Position(camera, new Vector3(0, 2.65f, -1.2f), new Vector3(-0.1f, 1.40f, -4.78f));
            controller.PanelPower.StartForAssessment(); PowerMotor("M_DOUBLE"); PowerMotor("M1");
            controller.SetMode(SimulationMode.Simulate); controller.AdvanceSimulation(0.04f); Step(20);
            yield return null; Capture(camera, "liquid-pipes-blocked.png");
            Assert.That(view.Pipes.Take(2).All(p => p.State.IsBlocked), Is.True);
            PowerValve(1); PowerValve(2); PowerValve(3); controller.AdvanceSimulation(0.04f);
            Step(0.35f); yield return null; Capture(camera, "liquid-pipes-opening.png");
            Step(20); yield return null; Capture(camera, "liquid-pipes-flowing.png");
            Assert.That(view.Pipes.All(p => p.Jet.enabled), Is.True);
            controller.SetMode(SimulationMode.View); Step(0.25f);
            yield return null; Capture(camera, "liquid-pipes-fading.png");
            Step(0.25f); yield return null; Capture(camera, "liquid-pipes-empty.png");
            Assert.That(view.Pipes.All(p => !p.Body.enabled && !p.Jet.enabled), Is.True);
        }
        private void Position(Camera camera, Vector3 position, Vector3 target)
        { camera.transform.position = space.TransformPoint(position); camera.transform.LookAt(space.TransformPoint(target)); }
        private static string FullPath(Transform node) => node.parent == null ? node.name : FullPath(node.parent) + "/" + node.name;
        private static void Capture(Camera camera, string file)
        {
            var target = new RenderTexture(1800, 1200, 24);
            var previous = camera.targetTexture; var active = RenderTexture.active;
            var texture = new Texture2D(target.width, target.height, TextureFormat.RGB24, false);
            try
            {
                camera.targetTexture = target; camera.Render(); RenderTexture.active = target;
                texture.ReadPixels(new Rect(0, 0, target.width, target.height), 0, 0); texture.Apply();
                Directory.CreateDirectory("Logs"); File.WriteAllBytes(Path.Combine("Logs", file), texture.EncodeToPNG());
            }
            finally
            {
                camera.targetTexture = previous; RenderTexture.active = active;
                Object.Destroy(texture); target.Release(); Object.Destroy(target);
            }
        }
    }
}
