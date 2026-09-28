using System;
using System.Collections;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace ElectricalSim.Tests
{
    public sealed class MotorPropertiesProjectTests
    {
        private SimulationController controller;
        private string path;
        [UnitySetUp]
        public IEnumerator SetUp()
        {
            SceneManager.LoadScene("ElectricalTraining"); yield return null; yield return null;
            controller = Object.FindObjectOfType<SimulationController>(); controller.enabled = false;
            path = Path.Combine(Application.temporaryCachePath, "motor-config-" + Guid.NewGuid().ToString("N") + ".cc3d");
        }
        [TearDown] public void TearDown() { if (File.Exists(path)) File.Delete(path); }
        private ElectricalDeviceRuntime Motor(string id) => (ElectricalDeviceRuntime)controller.Graph.Devices[id];
        private SceneIoView Body(string id)
        {
            var environment = GameObject.Find("OriginalLabEnvironment").transform;
            return environment.Find(MotorBindingDefinition.Find(id).ModelPath).GetComponent<SceneIoView>();
        }
        private static Button Button(Component panel, string name) => panel.GetComponentsInChildren<Button>(true).Single(b => b.name == name);
        private static InputField Input(Component panel, string name) => panel.GetComponentsInChildren<InputField>(true).Single(i => i.name == name);

        [UnityTest]
        public IEnumerator EveryMotorHasOneSharedEditablePanelAndLiveControls()
        {
            foreach (var definition in MotorBindingDefinition.All)
            {
                var body = Body(definition.Id);
                Assert.That(body, Is.Not.Null); Assert.That(body.Picker.enabled, Is.True);
                controller.SelectSceneIo(body);
                Assert.That(controller.SelectedSceneIo, Is.SameAs(body));
                Assert.That(controller.SceneIoProperties.DisplayedText, Does.Contain(definition.Id).And.Contain("教学仿真参数"));
                Assert.That(controller.SceneIoProperties.MotorEditor.gameObject.activeInHierarchy, Is.True);
            }
            controller.SelectSceneIo(Body("M3"));
            var editor = controller.SceneIoProperties.MotorEditor;
            Input(editor, "RatedPowerKw").text = "1.5";
            Button(editor, "应用电机铭牌").onClick.Invoke();
            Assert.That(Motor("M3").MotorConfiguration.RatedPowerKw, Is.EqualTo(1.5f));
            Assert.That(Motor("M1").MotorConfiguration.RatedPowerKw, Is.EqualTo(1.1f));
            Input(editor, "PoleCount").text = "3";
            Button(editor, "应用电机铭牌").onClick.Invoke();
            Assert.That(Motor("M3").MotorConfiguration.PoleCount, Is.EqualTo(4));
            controller.SetMode(SimulationMode.Simulate); controller.SelectSceneIo(Body("M3"));
            yield return null;
            Assert.That(Input(editor, "RatedPowerKw").interactable, Is.False);
            Input(editor, "LoadPercent").text = "125";
            Button(editor, "设置负载").onClick.Invoke();
            Button(editor, "模拟堵转").onClick.Invoke();
            Assert.That(Motor("M3").MotorConfiguration.LoadFactor, Is.EqualTo(1.25f));
            Assert.That(Motor("M3").MotorState.IsStalled, Is.True);
            Button(editor, "模拟堵转").onClick.Invoke();
            Assert.That(Motor("M3").MotorState.IsStalled, Is.False);
        }

        [Test]
        public void ZeroTimeSceneSolvesRefreshWiringWithoutAdvancingRotorOrHeat()
        {
            controller.PanelPower.StartForAssessment();
            for (var i = 0; i < 3; i++) controller.Graph.AddWire("POWER.L" + (i + 1), "M1." + new[] { "U", "V", "W" }[i], Color.red);
            ManualCircuitFixture.CompleteMotor(controller.Graph, "M1");
            controller.SetMode(SimulationMode.Simulate); controller.AdvanceSimulation(.1f);
            var previous = controller.GetMotorRuntimeState("M1");
            Assert.That(previous.SpeedRpm, Is.GreaterThan(0));
            controller.Graph.RemoveWire(controller.Graph.Wires.Single(w => w.StartPort == "POWER.L3").Id);
            foreach (var mode in new[] { SimulationMode.Simulate, SimulationMode.View, SimulationMode.Wiring, SimulationMode.Drag })
            {
                controller.SetMode(mode); controller.AdvanceSimulation(0);
                var current = controller.GetMotorRuntimeState("M1");
                Assert.That(current.Connection.PhaseLoss, Is.True, mode.ToString());
                Assert.That(current.SpeedRpm, Is.EqualTo(previous.SpeedRpm), mode.ToString());
                Assert.That(current.ThermalState, Is.EqualTo(previous.ThermalState), mode.ToString());
            }
        }

        [Test]
        public void ConfigurationRoundTripTracksDirtyStateAndDropsTransientState()
        {
            var config = controller.GetMotorConfiguration("M1"); config.RatedPowerKw = 1.5f; config.InertiaKgM2 = .05f;
            controller.ConfigureMotor("M1", config);
            var relay = controller.ThermalRelayViews[0];
            controller.ConfigureThermalRelay(relay, new ThermalRelayConfiguration { SettingCurrent = 4.2f, TimeConstantSeconds = 90, ResetThreshold = .4f });
            controller.Graph.AddWire("POWER.L1", "M1.U", Color.red).FaultSide = false;
            Assert.That(controller.HasUnsavedWiring, Is.True);
            Assert.That(controller.SaveCc3dToPath(path), Is.EqualTo(WiringFileResult.Success), controller.LastFileError);
            Assert.That(controller.HasUnsavedWiring, Is.False);
            controller.SetMode(SimulationMode.Simulate);
            controller.SetMotorStalled("M1", true); controller.SetThermalRelayTripped(relay, true);
            Assert.That(controller.HasUnsavedWiring, Is.False, "Transient test injection must not dirty the project.");
            controller.SetMotorLoad("M1", 1.8f); Assert.That(controller.HasUnsavedWiring, Is.True);
            Assert.That(controller.OpenCc3dFromPath(path), Is.EqualTo(WiringFileResult.Success), controller.LastFileError);
            Assert.That(Motor("M1").MotorConfiguration.RatedPowerKw, Is.EqualTo(1.5f));
            Assert.That(Motor("M1").MotorConfiguration.InertiaKgM2, Is.EqualTo(.05f));
            Assert.That(Motor("M1").MotorConfiguration.LoadFactor, Is.EqualTo(.5f));
            Assert.That(Motor("M1").MotorState.IsStalled, Is.False);
            Assert.That(Motor("M1").MotorState.SpeedRpm, Is.Zero);
            Assert.That(relay.Runtime.ThermalConfiguration.SettingCurrent, Is.EqualTo(4.2f));
            Assert.That(relay.Runtime.IsTripped, Is.False);
            Assert.That(relay.Runtime.ThermalState.Heat, Is.Zero);
            Assert.That(controller.Graph.Wires.Count, Is.EqualTo(1));
            Assert.That(controller.HasUnsavedWiring, Is.False);
            var document = Cc3dSerializer.Load(path);
            Assert.That(document.Extra["motorConfigurations"].Count(), Is.EqualTo(4));
            Assert.That(document.Extra["thermalRelayConfigurations"].Count(), Is.EqualTo(3));
            Assert.That(document.Extra["motorConfigurations"].ToString(), Does.Not.Contain("IsStalled").And.Not.Contain("ThermalState"));
        }

        [Test]
        public void InvalidIncomingConfigurationDoesNotPartiallyApplyProject()
        {
            controller.Graph.AddWire("POWER.L1", "M1.U", Color.red).FaultSide = false;
            Assert.That(controller.SaveCc3dToPath(path), Is.EqualTo(WiringFileResult.Success));
            var document = Cc3dSerializer.Load(path);
            ((JObject)document.Extra["motorConfigurations"][0]["Configuration"])["RatedPowerKw"] = 2.2f;
            ((JObject)document.Extra["thermalRelayConfigurations"][0]["Configuration"])["SettingCurrent"] = -1;
            Cc3dSerializer.Save(path, document);
            Assert.That(controller.OpenCc3dFromPath(path), Is.EqualTo(WiringFileResult.Failed));
            Assert.That(controller.Graph.Wires.Count, Is.EqualTo(1));
            Assert.That(Motor("M1").MotorConfiguration.RatedPowerKw, Is.EqualTo(1.1f));
            Assert.That(controller.ThermalRelayViews.All(r => r.Runtime.ThermalConfiguration.SettingCurrent == 3.1f), Is.True);
            Assert.That(controller.HasUnsavedWiring, Is.False);
        }

        [Test]
        public void LegacyFileUsesDefaultsWithoutAddingMotorJumpers()
        {
            controller.Graph.AddWire("POWER.L1", "M1.U", Color.red).FaultSide = false;
            Assert.That(controller.SaveCc3dToPath(path), Is.EqualTo(WiringFileResult.Success));
            var document = Cc3dSerializer.Load(path);
            document.Extra.Remove("motorConfigurations"); document.Extra.Remove("thermalRelayConfigurations");
            Cc3dSerializer.Save(path, document);
            var config = controller.GetMotorConfiguration("M1"); config.RatedPowerKw = 3;
            controller.ConfigureMotor("M1", config);
            Assert.That(controller.OpenCc3dFromPath(path), Is.EqualTo(WiringFileResult.Success), controller.LastFileError);
            Assert.That(Motor("M1").MotorConfiguration.RatedPowerKw, Is.EqualTo(1.1f));
            Assert.That(Motor("M_DOUBLE").MotorConfiguration.IsTwoSpeed, Is.True);
            Assert.That(controller.Graph.Wires.Count, Is.EqualTo(1));
            Assert.That(controller.Graph.Wires[0].EndPort, Is.EqualTo("M1.U"));
            Assert.That(controller.HasUnsavedWiring, Is.False);
        }

        [UnityTest]
        public IEnumerator ThermalSettingsAreEditableAndRespectSimulationMode()
        {
            var relay = controller.ThermalRelayViews.First(r => !r.IsRear);
            controller.SelectThermalRelay(relay);
            var panel = controller.ThermalRelayProperties;
            Input(panel, "SettingCurrent").text = "4.5";
            Input(panel, "TimeConstantSeconds").text = "120";
            Input(panel, "ResetThresholdPercent").text = "30";
            Button(panel, "应用保护配置").onClick.Invoke();
            Assert.That(relay.Runtime.ThermalConfiguration.SettingCurrent, Is.EqualTo(4.5f));
            Assert.That(relay.Runtime.ThermalConfiguration.ResetThreshold, Is.EqualTo(.3f).Within(1e-6f));
            controller.SetMode(SimulationMode.Simulate); controller.SelectThermalRelay(relay); yield return null;
            Assert.That(Input(panel, "SettingCurrent").interactable, Is.False);
            Assert.That(panel.DisplayedText, Does.Contain("热状态").And.Contain("三相支路电流").And.Contain("整定电流"));
        }
        [UnityTest]
        public IEnumerator CaptureReadableMotorAndProtectionProperties()
        {
            if (SystemInfo.graphicsDeviceType == UnityEngine.Rendering.GraphicsDeviceType.Null) Assert.Ignore("Requires rendered graphics.");
            controller.SelectSceneIo(Body("M_DOUBLE")); yield return null;
            Capture("induction-motor-properties.png");
            var scroll = controller.SceneIoProperties.GetComponentInChildren<ScrollRect>();
            scroll.verticalNormalizedPosition = 0; yield return null;
            Capture("induction-motor-editor.png");
            controller.SelectSceneIo(null); controller.SelectThermalRelay(controller.ThermalRelayViews[0]); yield return null;
            Capture("induction-thermal-properties.png");
        }
        private static void Capture(string file)
        {
            var camera = Camera.main;
            var overlays = Object.FindObjectsOfType<Canvas>().Where(c => c.renderMode == RenderMode.ScreenSpaceOverlay).ToArray();
            var target = new RenderTexture(1280, 720, 24); var oldTarget = camera.targetTexture; var oldActive = RenderTexture.active;
            var texture = new Texture2D(1280, 720, TextureFormat.RGB24, false);
            foreach (var canvas in overlays) { canvas.renderMode = RenderMode.ScreenSpaceCamera; canvas.worldCamera = camera; canvas.planeDistance = 1; }
            camera.targetTexture = target; Canvas.ForceUpdateCanvases(); camera.Render(); RenderTexture.active = target;
            texture.ReadPixels(new Rect(0, 0, 1280, 720), 0, 0); texture.Apply();
            var directory = Path.Combine(Application.dataPath, "../Build/Reports"); Directory.CreateDirectory(directory); File.WriteAllBytes(Path.Combine(directory, file), texture.EncodeToPNG());
            camera.targetTexture = oldTarget; RenderTexture.active = oldActive;
            foreach (var canvas in overlays) canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            Object.Destroy(texture); Object.Destroy(target);
        }
    }
}
