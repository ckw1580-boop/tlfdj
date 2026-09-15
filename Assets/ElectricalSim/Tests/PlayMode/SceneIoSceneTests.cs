using System;
using System.Collections;
using System.IO;
using System.Linq;
using System.Reflection;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace ElectricalSim.Tests
{
    public sealed class SceneIoSceneTests
    {
        private SimulationController controller;
        private Transform environment;
        private string savePath;
        [UnitySetUp]
        public IEnumerator Setup()
        {
            SceneManager.LoadScene("ElectricalTraining"); yield return null; yield return null;
            controller = Object.FindObjectOfType<SimulationController>();
            controller.enabled = false;
            environment = GameObject.Find("OriginalLabEnvironment").transform;
            savePath = Path.Combine(Application.temporaryCachePath, "liquid-" + Guid.NewGuid().ToString("N") + ".cc3d");
        }
        [TearDown] public void Cleanup() { if (File.Exists(savePath)) File.Delete(savePath); }
        private void Wire(string a, string b) => controller.Graph.AddWire(a, b, Color.red);
        private void PowerValve(int index)
        {
            Wire("DuanZiPai_6.V_1", "DuanZiPai_8.Diancifa" + index + "_VCC");
            Wire("DuanZiPai_6.N_1", "DuanZiPai_8.Diancifa" + index + "_GND");
        }
        private void PowerMotor(string id)
        {
            for (var i = 0; i < 3; i++) Wire("POWER.L" + (i + 1), id + "." + new[] { "U", "V", "W" }[i]);
        }
        [UnityTest]
        public IEnumerator ExactModelsAndAllPortsAreBoundWithOrderedProbeHeights()
        {
            Assert.That(controller.SceneIoDevices.Count, Is.EqualTo(7));
            Assert.That(controller.SceneIoViews.Count, Is.EqualTo(14));
            var ports = Object.FindObjectsOfType<ElectricalPortView>();
            foreach (var d in SceneIoCatalog.Devices)
            {
                var model = environment.Find(d.ModelPath);
                Assert.That(model.GetComponent<SceneIoView>().Id, Is.EqualTo(d.Id));
                Assert.That(model.GetComponent<SceneIoView>().Picker.enabled, Is.True);
                foreach (var p in d.Ports)
                {
                    var port = ports.Single(v => v.QualifiedPort == "DuanZiPai_8." + d.Prefix + "_" + p);
                    Assert.That(port.HoverLabel, Is.EqualTo(d.Name + "_" + p));
                    Assert.That(controller.Graph.Solve(0).SameNet(port.QualifiedPort, d.Id + "." + p), Is.True);
                }
            }
            var heights = controller.SceneIoDevices.Values.Where(d => d.Definition.IsSensor).OrderBy(d => d.DeviceId).Select(d => d.TriggerLevel).ToArray();
            Assert.That(heights, Is.Ordered.Ascending); Assert.That(heights[0], Is.GreaterThan(0)); Assert.That(heights[3], Is.LessThan(1));
            foreach (var pump in SceneIoCatalog.Pumps)
            {
                var scene = environment.Find(pump.ModelPath).GetComponent<SceneIoView>();
                var motor = environment.Find(MotorBindingDefinition.Find(pump.MotorId).ModelPath).GetComponent<SceneIoView>();
                controller.SelectSceneIo(scene); var pumpText = controller.SceneIoProperties.DisplayedText;
                controller.SelectSceneIo(motor); Assert.That(controller.SceneIoProperties.DisplayedText, Is.EqualTo(pumpText));
                Assert.That(pumpText, Does.Contain(pump.MotorId));
            }
            yield return null;
        }
        [UnityTest]
        public IEnumerator MixerSharesLeftCabinetMotorAndTracksSpeedDirectionAndCoast()
        {
            Assert.That(SceneIoCatalog.MixerMotorId, Is.EqualTo("M2"));
            Assert.That(MotorBindingDefinition.Find(SceneIoCatalog.MixerMotorId).Nut, Is.EqualTo("49"));
            var mixer = environment.Find(SceneIoCatalog.EnvironmentPath + "/rivet/4/Jiaoban");
            var cabinet = environment.Find(MotorBindingDefinition.Find("M2").ModelPath);
            var sceneView = mixer.GetComponent<SceneIoView>();
            Assert.That(sceneView.Id, Is.EqualTo(SceneIoCatalog.MixerName));
            Assert.That(cabinet.GetComponent<SceneIoView>().Id, Is.EqualTo(sceneView.Id));
            Assert.That(controller.Graph.Devices.Values.Count(d => d.Kind == ElectricalDeviceKind.Motor), Is.EqualTo(4));
            var rotor = mixer.GetComponent<MotorRotorView>();
            rotor.enabled = false;
            var update = typeof(MotorRotorView).GetMethod("Update", BindingFlags.Instance | BindingFlags.NonPublic);
            var paddles = mixer.Find("mesh/JiaoBanJi");
            var housing = mixer.Find("mesh/jiaobanji");
            var position = paddles.position;
            var housingPosition = housing.position;
            var housingRotation = housing.rotation;
            controller.PanelPower.StartForAssessment(); PowerValve(1); PowerValve(2);
            var rpm = 0f;
            controller.Graph.RegisterDevice(new InverterDriveRuntime("MIXER_TEST_DRIVE", () => rpm, () => false));
            foreach (var phase in new[] { "L1", "L2", "L3" }) Wire("POWER." + phase, "MIXER_TEST_DRIVE." + phase);
            for (var i = 0; i < 3; i++) Wire("MIXER_TEST_DRIVE." + new[] { "U2", "V2", "W2" }[i], "M2." + new[] { "U", "V", "W" }[i]);
            controller.SetMode(SimulationMode.Simulate);
            foreach (var speed in new[] { 0f, 60f, 120f, -60f, 1450f })
            {
                rpm = speed;
                var snapshot = controller.AdvanceSimulation(0.04f);
                Assert.That(snapshot.GetMotorSpeedRpm("M2"), Is.EqualTo(speed));
                var expected = Quaternion.AngleAxis(speed * 6f * Time.deltaTime, mixer.up) * paddles.rotation;
                update.Invoke(rotor, null);
                Assert.That(Quaternion.Angle(paddles.rotation, expected), Is.LessThan(0.1f), speed + " rpm");
                Assert.That(Vector3.Distance(paddles.position, position), Is.LessThan(0.00001f));
                Assert.That(Vector3.Distance(housing.position, housingPosition), Is.LessThan(0.00001f));
                Assert.That(Quaternion.Angle(housing.rotation, housingRotation), Is.LessThan(0.001f));
                Assert.That(controller.Liquid.Pump1Flow, Is.Zero);
                Assert.That(controller.Liquid.Pump2Flow, Is.Zero);
                controller.SelectSceneIo(sceneView);
                var text = controller.SceneIoProperties.DisplayedText;
                controller.SelectSceneIo(cabinet.GetComponent<SceneIoView>());
                Assert.That(controller.SceneIoProperties.DisplayedText, Is.EqualTo(text));
                Assert.That(text, Does.Contain("正面左侧").And.Contain("M2").And.Contain(speed.ToString("F1")));
            }
            controller.Graph.RemoveWire(controller.Graph.Wires.Single(w => w.EndPort == "M2.U").Id);
            var coast = controller.AdvanceSimulation(0.5f).GetMotorSpeedRpm("M2");
            Assert.That(coast, Is.GreaterThan(0).And.LessThan(1450));
            var coastingRotation = Quaternion.AngleAxis(coast * 6f * Time.deltaTime, mixer.up) * paddles.rotation;
            update.Invoke(rotor, null);
            Assert.That(Quaternion.Angle(paddles.rotation, coastingRotation), Is.LessThan(0.1f));
            Assert.That(controller.AdvanceSimulation(10).GetMotorSpeedRpm("M2"), Is.Zero);
            var stopped = paddles.rotation;
            update.Invoke(rotor, null);
            Assert.That(Quaternion.Angle(stopped, paddles.rotation), Is.LessThan(0.001f));
            yield return null;
        }

        [UnityTest]
        public IEnumerator OtherMotorsCannotRotateMixer()
        {
            var mixer = environment.Find(SceneIoCatalog.MixerModelPath);
            var paddles = mixer.Find("mesh/JiaoBanJi");
            var before = paddles.rotation;
            controller.PanelPower.StartForAssessment();
            foreach (var id in new[] { "M1", "M_DOUBLE", "M3" }) PowerMotor(id);
            PowerValve(1); PowerValve(2);
            controller.SetMode(SimulationMode.Simulate);
            var snapshot = controller.AdvanceSimulation(20);
            Assert.That(snapshot.GetMotorSpeedRpm("M2"), Is.Zero);
            Assert.That(controller.Liquid.Pump1Flow, Is.GreaterThan(0));
            Assert.That(controller.Liquid.Pump2Flow, Is.GreaterThan(0));
            yield return null; yield return null;
            Assert.That(Quaternion.Angle(before, paddles.rotation), Is.LessThan(0.001f));
        }

        [UnityTest]
        public IEnumerator OnlyBoundMotorFeedsItsOwnPumpAndPausePreservesLiquid()
        {
            controller.PanelPower.StartForAssessment(); PowerValve(1); PowerValve(2); PowerMotor("M_DOUBLE");
            controller.SetMode(SimulationMode.Simulate);
            controller.AdvanceSimulation(1);
            Assert.That(controller.Liquid.Level, Is.Zero);
            Assert.That(controller.Liquid.Pump1Transporting, Is.True);
            controller.AdvanceSimulation(19);
            var delivered = controller.Liquid.Level;
            for (var i = 0; i < 50; i++) controller.AdvanceSimulation(0.02f);
            Assert.That(controller.Liquid.Level - delivered, Is.EqualTo(1d / 30).Within(0.001));
            Assert.That(controller.Liquid.Pump1Flow, Is.GreaterThan(0)); Assert.That(controller.Liquid.Pump2Flow, Is.Zero);
            controller.SetMode(SimulationMode.View); var before = controller.Liquid.Level;
            controller.AdvanceSimulation(10); Assert.That(controller.Liquid.Level, Is.EqualTo(before));
            controller.SetMode(SimulationMode.Simulate); controller.AdvanceSimulation(1);
            Assert.That(controller.Liquid.Level, Is.EqualTo(before));
            controller.AdvanceSimulation(12);
            Assert.That(controller.Liquid.Level, Is.GreaterThan(before));
            controller.Graph.ClearWires();
            foreach (var motor in controller.Graph.Devices.Values.OfType<ElectricalDeviceRuntime>().Where(d => d.Kind == ElectricalDeviceKind.Motor)) motor.ResetMotorSpeed();
            PowerValve(1); PowerValve(2); PowerMotor("M1"); controller.AdvanceSimulation(15);
            Assert.That(controller.Liquid.Pump1Flow, Is.Zero); Assert.That(controller.Liquid.Pump2Flow, Is.GreaterThan(0));
            controller.SetMode(SimulationMode.View); controller.ResetLiquid(); Assert.That(controller.Liquid.Level, Is.Zero);
            Assert.That(controller.Graph.Wires, Is.Not.Empty);
            yield return null;
        }
        [UnityTest]
        public IEnumerator SensorsFeedPlcThroughCabinetWiringAndLoseSignalWithoutCommon()
        {
            controller.PanelPower.StartForAssessment();
            foreach (var sensor in SceneIoCatalog.Devices.Where(d => d.IsSensor))
            {
                Wire("DuanZiPai_6.V_1", "DuanZiPai_8." + sensor.Prefix + "_VCC");
                Wire("DuanZiPai_6.N_1", "DuanZiPai_8." + sensor.Prefix + "_GND");
            }
            Wire("DuanZiPai_6.V_1", "PLC_1.L+"); Wire("DuanZiPai_6.N_1", "PLC_1.M");
            Wire("DuanZiPai_6.N_1", "PLC_1.1M");
            Wire("DuanZiPai_8.A_SIGNAL", "PLC_1." + PlcConfiguration.InputTerminals[0]);
            controller.ConfigureLiquid(new LiquidConfiguration { InitialLevelPercent = 100 }); controller.ResetLiquid();
            controller.SetMode(SimulationMode.Simulate); var snapshot = controller.AdvanceSimulation(0.02f);
            Assert.That(controller.SceneIoDevices.Values.Where(d => d.Definition.IsSensor).All(d => d.IsActive), Is.True);
            var plc = controller.PlcViews.Single(p => p.Runtime.DeviceId == "PLC_1").Runtime;
            Assert.That(plc.InputStates[0], Is.True);
            controller.Graph.RemoveWire(controller.Graph.Wires.Single(w => w.EndPort == "PLC_1.1M").Id);
            controller.AdvanceSimulation(0.02f); Assert.That(plc.InputStates[0], Is.False);
            yield return null;
        }
        [UnityTest]
        public IEnumerator ConfigurationRoundTripsAndInvalidFileDoesNotReplaceCurrentState()
        {
            controller.ConfigureLiquid(new LiquidConfiguration { InitialLevelPercent = 25, Pump1FillSeconds = 45, Pump2FillSeconds = 60, DrainSeconds = 35 });
            Assert.That(controller.HasUnsavedWiring, Is.True);
            Assert.That(controller.SaveCc3dToPath(savePath), Is.EqualTo(WiringFileResult.Success));
            Assert.That(controller.HasUnsavedWiring, Is.False);
            controller.ConfigureLiquid(new LiquidConfiguration());
            Assert.That(controller.OpenCc3dFromPath(savePath), Is.EqualTo(WiringFileResult.Success));
            Assert.That(controller.Liquid.Level, Is.EqualTo(0.25)); Assert.That(controller.Liquid.Configuration.Pump2FillSeconds, Is.EqualTo(60));
            var json = JObject.Parse(File.ReadAllText(savePath)); json["liquidConfiguration"]["DrainSeconds"] = 0; File.WriteAllText(savePath, json.ToString());
            Assert.That(controller.OpenCc3dFromPath(savePath), Is.EqualTo(WiringFileResult.Failed));
            Assert.That(controller.Liquid.Level, Is.EqualTo(0.25)); Assert.That(controller.Liquid.Configuration.DrainSeconds, Is.EqualTo(35));
            json.Remove("liquidConfiguration"); File.WriteAllText(savePath, json.ToString());
            Assert.That(controller.OpenCc3dFromPath(savePath), Is.EqualTo(WiringFileResult.Success));
            Assert.That(controller.Liquid.Level, Is.Zero); Assert.That(controller.Liquid.Configuration.Pump1FillSeconds, Is.EqualTo(30));
            yield return null;
        }
        [UnityTest]
        public IEnumerator LiquidBottomStaysFixedAndAttributesDoNotMoveModels()
        {
            var view = Object.FindObjectOfType<LiquidTankView>();
            var water = environment.Find(SceneIoCatalog.EnvironmentPath + "/rivet/5/JiaoBanWater/mesh/Water").GetComponentInChildren<Renderer>(true);
            controller.ConfigureLiquid(new LiquidConfiguration { InitialLevelPercent = 50 }); controller.ResetLiquid();
            yield return null;
            Assert.That(water.enabled, Is.True); Assert.That(water.bounds.min.y, Is.EqualTo(view.BottomWorldY).Within(0.002));
            Assert.That(water.bounds.max.y, Is.EqualTo((view.BottomWorldY + view.TopWorldY) / 2).Within(0.002));
            controller.ConfigureLiquid(new LiquidConfiguration { InitialLevelPercent = 100 }); controller.ResetLiquid(); yield return null;
            Assert.That(water.bounds.max.y, Is.EqualTo(view.TopWorldY).Within(0.002));
            controller.SelectSceneIo(controller.SceneIoViews.Single(v => v.Id == "TANK"));
            Assert.That(controller.SceneIoProperties.DisplayedText, Does.Contain("100.0%"));
            controller.SelectContactor(controller.ContactorViews.First());
            Assert.That(controller.SelectedSceneIo, Is.Null);
            controller.SetMode(SimulationMode.View);
            yield return null;
        }

        [UnityTest]
        public IEnumerator VariableSpeedCoastValveGateAndFrameRatesUseActualMotorState()
        {
            controller.PanelPower.StartForAssessment(); PowerValve(1);
            var rpm = 725f;
            controller.Graph.RegisterDevice(new InverterDriveRuntime("PUMP_TEST_DRIVE", () => rpm, () => false));
            foreach (var phase in new[] { "L1", "L2", "L3" }) Wire("POWER." + phase, "PUMP_TEST_DRIVE." + phase);
            for (var i = 0; i < 3; i++) Wire("PUMP_TEST_DRIVE." + new[] { "U2", "V2", "W2" }[i], "M_DOUBLE." + new[] { "U", "V", "W" }[i]);
            foreach (var fps in new[] { 30, 60, 120 })
            {
                controller.SetMode(SimulationMode.View); controller.ResetLiquid(); controller.SetMode(SimulationMode.Simulate);
                controller.AdvanceSimulation(30);
                var delivered = controller.Liquid.Level;
                for (var i = 0; i < fps * 2; i++) controller.AdvanceSimulation(1f / fps);
                Assert.That(controller.Liquid.Level - delivered, Is.EqualTo(1d / 30).Within(0.00001), fps + " fps");
            }
            rpm = -725; controller.AdvanceSimulation(0.02f); Assert.That(controller.Liquid.Pump1Flow, Is.Zero);
            rpm = 1450; controller.AdvanceSimulation(0.02f); Assert.That(controller.Liquid.Pump1Flow, Is.EqualTo(1d / 30).Within(1e-8));
            var lead = controller.Graph.Wires.Single(w => w.EndPort == "M_DOUBLE.U"); controller.Graph.RemoveWire(lead.Id);
            controller.AdvanceSimulation(0.5f);
            Assert.That(controller.Liquid.Pump1Flow, Is.InRange(0.02, 1d / 30));
            var valveLead = controller.Graph.Wires.Single(w => w.EndPort == "DuanZiPai_8.Diancifa1_VCC"); controller.Graph.RemoveWire(valveLead.Id);
            controller.AdvanceSimulation(0.02f); Assert.That(controller.Liquid.Pump1Flow, Is.Zero);
            yield return null;
        }

        [UnityTest]
        public IEnumerator ProcessVisualsAndTankPropertiesAreCapturedForInspection()
        {
            controller.ConfigureLiquid(new LiquidConfiguration { InitialLevelPercent = 55 }); controller.ResetLiquid();
            controller.PanelPower.StartForAssessment(); PowerValve(1); PowerValve(2); PowerMotor("M_DOUBLE"); PowerMotor("M1");
            foreach (var d in SceneIoCatalog.Devices.Where(d => d.IsSensor))
            { Wire("DuanZiPai_6.V_1", "DuanZiPai_8." + d.Prefix + "_VCC"); Wire("DuanZiPai_6.N_1", "DuanZiPai_8." + d.Prefix + "_GND"); }
            controller.SetMode(SimulationMode.Simulate); controller.AdvanceSimulation(0.02f);
            var space = environment.Find(SceneIoCatalog.EnvironmentPath);
            var camera = Camera.main; camera.nearClipPlane = 0.01f;
            camera.transform.position = space.TransformPoint(new Vector3(0.65f, 1.65f, -1.9f));
            camera.transform.LookAt(space.TransformPoint(new Vector3(0.65f, 1.35f, -4.83f)));
            Object.FindObjectOfType<TrainingCameraController>().enabled = false;
            yield return null;
            Capture(camera, "scene-io-process.png");
            controller.SelectSceneIo(controller.SceneIoViews.Single(v => v.Id == "TANK"));
            controller.SetMode(SimulationMode.View);
            controller.SelectSceneIo(controller.SceneIoViews.Single(v => v.Id == "TANK"));
            yield return null;
            var canvases = Object.FindObjectsOfType<Canvas>().Where(c => c.isRootCanvas && c.renderMode == RenderMode.ScreenSpaceOverlay).ToArray();
            foreach (var canvas in canvases) { canvas.renderMode = RenderMode.ScreenSpaceCamera; canvas.worldCamera = camera; canvas.planeDistance = 0.2f; }
            Canvas.ForceUpdateCanvases();
            Capture(camera, "scene-io-properties.png");
            foreach (var canvas in canvases) canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            Assert.That(controller.SceneIoProperties.DisplayedText, Does.Contain("55."));
        }
        private static void Capture(Camera camera, string file)
        {
            var target = new RenderTexture(1400, 1000, 24);
            var previous = camera.targetTexture; var active = RenderTexture.active;
            camera.targetTexture = target; camera.Render(); RenderTexture.active = target;
            var texture = new Texture2D(target.width, target.height, TextureFormat.RGB24, false);
            texture.ReadPixels(new Rect(0, 0, target.width, target.height), 0, 0); texture.Apply();
            var directory = Path.GetFullPath("Logs"); Directory.CreateDirectory(directory);
            File.WriteAllBytes(Path.Combine(directory, file), texture.EncodeToPNG());
            camera.targetTexture = previous; RenderTexture.active = active;
            Object.Destroy(texture); target.Release(); Object.Destroy(target);
        }

        [UnityTest]
        public IEnumerator ActualPointerClicksSelectEachBodyAndBlankSpaceClearsProperties()
        {
            var camera = Camera.main; camera.nearClipPlane = 0.001f;
            var cameraControls = Object.FindObjectOfType<TrainingCameraController>(); cameraControls.enabled = false;
            var pointer = typeof(SimulationController).GetMethod("HandleScenePointerDown", BindingFlags.Instance | BindingFlags.NonPublic);
            foreach (var view in controller.SceneIoViews)
            {
                controller.SelectSceneIo(null);
                var target = view.Picker.bounds.center;
                camera.transform.position = target + environment.Find("Bench").forward * (view.Picker.bounds.extents.magnitude + 0.1f);
                camera.transform.LookAt(target);
                Physics.SyncTransforms();
                var screen = (Vector2)camera.WorldToScreenPoint(target);
                pointer.Invoke(controller, new object[] { camera, screen });
                var ray = camera.ScreenPointToRay(screen);
                Physics.Raycast(ray, out var hit, 100);
                Assert.That(controller.SelectedSceneIo, Is.SameAs(view), view.Id + " / " + view.name + "; hit=" + (hit.collider != null ? hit.collider.name + " parent=" + hit.collider.transform.parent?.name : "none"));
            }
            camera.transform.position = new Vector3(0, 20, 0); camera.transform.rotation = Quaternion.LookRotation(Vector3.up, Vector3.forward);
            pointer.Invoke(controller, new object[] { camera, new Vector2(camera.pixelWidth / 2f, camera.pixelHeight / 2f) });
            Assert.That(controller.SelectedSceneIo, Is.Null);
            yield return null;
        }
    }
}
