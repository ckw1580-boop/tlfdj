using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace ElectricalSim.Tests
{
    public sealed class PlcSceneTests
    {
        private SimulationController controller;
        private string path;
        private sealed class Loopback : IPlcTransport
        {
            private readonly Dictionary<string, bool> bits = new Dictionary<string, bool>();
            public Task ConnectAsync(PlcConfiguration c, CancellationToken ct) => Task.CompletedTask;
            public Task<bool[]> ReadBitsAsync(IReadOnlyList<PlcBitAddress> addresses, CancellationToken ct)
            { return Task.FromResult(addresses.Select(a => bits.TryGetValue(a.ToString() == "Q0.0" ? "M0.0" : a.ToString(), out var value) && value).ToArray()); }
            public Task WriteBitAsync(PlcBitAddress a, bool value, CancellationToken ct) { bits[a.ToString()] = value; return Task.CompletedTask; }
            public void Dispose() { }
        }
        [UnitySetUp]
        public IEnumerator Setup()
        {
            SceneManager.LoadScene("ElectricalTraining"); yield return null; yield return null;
            controller = Object.FindObjectOfType<SimulationController>();
            path = Path.Combine(Application.temporaryCachePath, "plc-test-" + Guid.NewGuid().ToString("N") + ".cc3d");
        }
        [TearDown] public void Cleanup() { if (File.Exists(path)) File.Delete(path); }
        [UnityTest]
        public IEnumerator OriginalBodiesAndAllSixtyTerminalsAreBoundAndSelectable()
        {
            Assert.That(controller.PlcViews.Count, Is.EqualTo(2));
            foreach (var view in controller.PlcViews)
            {
                Assert.That(view.gameObject.activeInHierarchy, Is.True);
                Assert.That(view.Picker.enabled, Is.True);
                Assert.That(view.Runtime.Ports.Count, Is.EqualTo(30));
                Assert.That(controller.Graph.Devices[view.Runtime.DeviceId], Is.SameAs(view.Runtime));
                controller.SelectPlc(view);
                Assert.That(controller.PlcProperties.gameObject.activeInHierarchy, Is.True);
                foreach (var terminal in view.Runtime.Ports)
                {
                    controller.PlcProperties.Locate(terminal);
                    Assert.That(controller.PlcProperties.HighlightedTerminal.PortName, Is.EqualTo(view.Runtime.DeviceId + "_" + terminal));
                    Assert.That(controller.Graph.Solve().SameNet(controller.PlcProperties.HighlightedTerminal.QualifiedPort, view.Runtime.Port(terminal)), Is.True);
                }
                var camera = Camera.main; camera.nearClipPlane = 0.001f;
                camera.transform.position = view.transform.position + view.transform.forward * 0.4f;
                camera.transform.LookAt(view.Picker.bounds.center, view.transform.up);
                Physics.SyncTransforms();
                var ray = new Ray(camera.transform.position, view.Picker.bounds.center - camera.transform.position);
                Assert.That(view.Picker.Raycast(ray, out _, 3f), Is.True);
            }
            controller.PlcProperties.Locate("M0.0");
            yield return null;
            Capture("plc-properties.png");
            var scroll = controller.PlcProperties.GetComponentInChildren<ScrollRect>();
            scroll.verticalNormalizedPosition = 0;
            controller.PlcProperties.Locate("Q1.1");
            yield return null;
            Capture("plc-properties-outputs.png", 0);
            controller.SelectPlc(null);
            Assert.That(controller.PlcProperties.HighlightedTerminal, Is.Null);
        }
        [Test]
        public void ConfigurationsRoundTripAndLegacyFilesResetDefaultsWithoutConnecting()
        {
            var config = controller.PlcSessions["PLC_1"].Configuration;
            config.Ip = "192.168.0.10"; config.Cpu = SiemensCpu.S71500; config.Inputs[0].Address = "DB10.DBX0.0";
            config.Extra["future"] = "preserved";
            controller.PlcSessions["PLC_1"].Configure(config);
            Assert.That(controller.HasUnsavedWiring, Is.True);
            Assert.That(controller.SaveCc3dToPath(path), Is.EqualTo(WiringFileResult.Success), controller.LastFileError);
            Assert.That(controller.HasUnsavedWiring, Is.False);
            Assert.That(controller.OpenCc3dFromPath(path), Is.EqualTo(WiringFileResult.Success), controller.LastFileError);
            var restored = controller.PlcSessions["PLC_1"].Configuration;
            Assert.That(restored.Cpu, Is.EqualTo(SiemensCpu.S71500)); Assert.That(restored.Inputs[0].Address, Is.EqualTo("DB10.DBX0.0"));
            Assert.That(restored.Extra["future"].Value<string>(), Is.EqualTo("preserved"));
            Assert.That(controller.PlcSessions.Values.All(s => s.State == PlcConnectionState.Disconnected), Is.True);
            var doc = Cc3dSerializer.Load(path); doc.Extra.Remove("plcConfigurations"); doc.Extra["unknownRoot"] = 23; Cc3dSerializer.Save(path, doc);
            Assert.That(controller.OpenCc3dFromPath(path), Is.EqualTo(WiringFileResult.Success));
            Assert.That(controller.PlcSessions["PLC_1"].Configuration.Ip, Is.Empty);
            Assert.That(controller.SaveCc3dToPath(path), Is.EqualTo(WiringFileResult.Success));
            Assert.That(Cc3dSerializer.Load(path).Extra["unknownRoot"].Value<int>(), Is.EqualTo(23));
        }
        [UnityTest]
        public IEnumerator WiredButtonLoopsThroughPlcAndDrivesExistingLamp()
        {
            controller.PlcTransportFactory = () => new Loopback();
            var c = controller.PlcSessions["PLC_1"].Configuration; c.Ip = "127.0.0.1"; c.PollIntervalMs = 20;
            controller.PlcSessions["PLC_1"].Configure(c);
            var runtime = controller.PlcViews[0].Runtime;
            var graph = controller.Graph;
            foreach (var terminal in new[] { "L+", "3L+" }) graph.AddWire("TERMINAL_BUS.DC_POSITIVE", Physical(terminal), Color.red);
            foreach (var terminal in new[] { "M", "1M", "3M-" }) graph.AddWire("TERMINAL_BUS.DC_NEGATIVE", Physical(terminal), Color.blue);
            graph.AddWire("TERMINAL_BUS.DC_POSITIVE", "SB1.COM1", Color.red);
            graph.AddWire("SB1.NO1", Physical("M0.0"), Color.red);
            graph.AddWire(Physical("Q0.0"), "HL1.L", Color.red);
            graph.AddWire("HL1.N", "TERMINAL_BUS.DC_NEGATIVE", Color.blue);
            controller.PanelPower.StartForAssessment(); controller.SetMode(SimulationMode.Simulate);
            controller.ConnectPlc("PLC_1");
            controller.PanelControls.Single(p => p.Runtime.DeviceId == "SB1" && !p.IsRear).Runtime.SetControl(true);
            var lamp = controller.PanelControls.Single(p => p.Runtime.DeviceId == "HL1").Runtime;
            var deadline = Time.realtimeSinceStartup + 4;
            while (!lamp.IsActive && Time.realtimeSinceStartup < deadline) yield return null;
            Assert.That(runtime.InputStates[0], Is.True); Assert.That(lamp.IsActive, Is.True);
            controller.SetMode(SimulationMode.View); yield return null;
            Assert.That(lamp.IsActive, Is.False);
            var disconnected = controller.PlcSessions["PLC_1"].DisconnectAsync();
            while (!disconnected.IsCompleted) yield return null;
            Assert.That(controller.PlcSessions["PLC_1"].State, Is.EqualTo(PlcConnectionState.Disconnected));
        }
        private string Physical(string terminal) => controller.ResolvePlcTerminal("PLC_1", terminal).QualifiedPort;
        private void Capture(string name, float scrollPosition = 1)
        {
            var camera = Camera.main;
            var canvas = controller.PlcProperties.GetComponentInParent<Canvas>();
            var previousMode = canvas.renderMode; var previousCamera = canvas.worldCamera; var previousDistance = canvas.planeDistance;
            var previousTarget = camera.targetTexture; var previousActive = RenderTexture.active;
            var target = new RenderTexture(1920, 1080, 24); var texture = new Texture2D(1920, 1080, TextureFormat.RGB24, false);
            try
            {
                camera.targetTexture = target;
                canvas.renderMode = RenderMode.ScreenSpaceCamera; canvas.worldCamera = camera; canvas.planeDistance = 0.05f;
                Canvas.ForceUpdateCanvases();
                var scroll = controller.PlcProperties.GetComponentInChildren<ScrollRect>();
                scroll.StopMovement(); scroll.verticalNormalizedPosition = scrollPosition; Canvas.ForceUpdateCanvases();
                if (scrollPosition == 0)
                {
                    var lastRow = scroll.content.Find("Supply_3M-");
                    var bounds = RectTransformUtility.CalculateRelativeRectTransformBounds(scroll.viewport, lastRow);
                    Assert.That(scroll.viewport.rect.Contains(bounds.center), Is.True, "滚动到底部必须显示最后一个供电端子");
                }
                camera.Render(); RenderTexture.active = target;
                texture.ReadPixels(new Rect(0, 0, 1920, 1080), 0, 0); texture.Apply();
                Directory.CreateDirectory("Build/Reports"); File.WriteAllBytes("Build/Reports/" + name, texture.EncodeToPNG());
            }
            finally
            {
                camera.targetTexture = previousTarget; RenderTexture.active = previousActive;
                canvas.renderMode = previousMode; canvas.worldCamera = previousCamera; canvas.planeDistance = previousDistance;
                Object.Destroy(texture); Object.Destroy(target);
            }
        }
    }
}
