using System.Collections;
using System.IO;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace ElectricalSim.Tests
{
    public sealed class MotorSceneTests
    {
        private SimulationController controller;
        private ElectricalDeviceView[] motors;
        private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            SceneManager.LoadScene("ElectricalTraining");
            yield return null;
            yield return null;
            controller = Object.FindObjectOfType<SimulationController>();
            motors = Object.FindObjectsOfType<ElectricalDeviceView>().Where(v => v.Runtime.Kind == ElectricalDeviceKind.Motor).ToArray();
        }

        [UnityTest]
        public IEnumerator FourIndependentMotorsKeepTwentyFourAnchorsAcrossViews()
        {
            Assert.That(motors.Select(m => m.Runtime.DeviceId), Is.EquivalentTo(new[] { "M1", "M2", "M3", "M_DOUBLE" }));
            Assert.That(controller.Tachometer.Targets.Select(t => t.MotorId).Distinct().Count(), Is.EqualTo(4));
            controller.SetMode(SimulationMode.Wiring);
            controller.SetWireStyle(Color.red, 0.01f, "JumperLine");
            foreach (var binding in MotorBindingDefinition.All)
            {
                var motor = motors.Single(m => m.Runtime.DeviceId == binding.Id);
                Assert.That(motor.Ports.Count, Is.EqualTo(6));
                var model = GameObject.Find("OriginalLabEnvironment").transform.Find(binding.ModelPath);
                Assert.That(controller.Tachometer.Targets.Single(t => t.MotorId == binding.Id).transform.parent, Is.EqualTo(model));
                foreach (var port in motor.Ports)
                {
                    var front = port.GetOriginalAnchor(TrainingViewPreset.WiringFront, true);
                    var back = port.GetOriginalAnchor(TrainingViewPreset.FaultBack, true);
                    Assert.That(back, Is.SameAs(front));
                    Assert.That(front.IsChildOf(model), Is.True);
                    Assert.That(port.MotorId, Is.EqualTo(binding.Id));
                }
            }
            Object.FindObjectOfType<TrainingCameraController>().SetFaultView();
            yield return null;
            Assert.That(motors.SelectMany(m => m.Ports).Count(p => p.IsVisible), Is.EqualTo(24));
        }

        [UnityTest]
        public IEnumerator OnlyPoweredPhysicalRotorMovesAndMountsStayFixed()
        {
            controller.enabled = false;
            controller.PanelPower.StartForAssessment();
            var environment = GameObject.Find("OriginalLabEnvironment").transform;
            var models = MotorBindingDefinition.All.ToDictionary(b => b.Id, b => environment.Find(b.ModelPath));
            var discs = models.ToDictionary(m => m.Key, m => m.Value.GetComponentsInChildren<Transform>(true)
                .Where(t => t.name == "zhuanpan" || t.name == "zhuanpan (1)").ToArray());
            foreach (var pair in discs) Assert.That(pair.Value, Is.Not.Empty, pair.Key);
            var stationary = models.Values.SelectMany(m => m.GetComponentsInChildren<Transform>(true))
                .Where(t => !discs.Values.SelectMany(d => d).Any(d => t == d || t.IsChildOf(d))).ToArray();
            var positions = stationary.Select(t => t.position).ToArray();
            var rotations = stationary.Select(t => t.rotation).ToArray();
            foreach (var binding in MotorBindingDefinition.All)
            {
                controller.Graph.ClearWires();
                foreach (var motor in motors) motor.Runtime.ResetMotorSpeed();
                for (var i = 0; i < 3; i++) controller.Graph.AddWire("POWER.L" + (i + 1), binding.Id + "." + new[] { "U", "V", "W" }[i], Color.red);
                controller.Graph.Solve();
                var before = discs.ToDictionary(p => p.Key, p => p.Value.Select(t => t.rotation).ToArray());
                if (binding.Id == "M3") CaptureMotor(binding.Id, "motor-M3-powered-before");
                var moved = false;
                for (var frame = 0; frame < 8; frame++)
                {
                    yield return null;
                    foreach (var pair in discs)
                        for (var i = 0; i < pair.Value.Length; i++)
                        {
                            var angle = Quaternion.Angle(before[pair.Key][i], pair.Value[i].rotation);
                            if (pair.Key == binding.Id) moved |= angle > 0.01f;
                            else Assert.That(angle, Is.LessThan(0.001f), pair.Key + " moved while " + binding.Id + " powered");
                        }
                }
                Assert.That(moved, Is.True, binding.Id);
                if (binding.Id == "M3") CaptureMotor(binding.Id, "motor-M3-powered-after");
                var snapshot = controller.Graph.Solve(0);
                foreach (var target in controller.Tachometer.Targets)
                    Assert.That(snapshot.GetMotorSpeedRpm(target.MotorId), Is.EqualTo(target.MotorId == binding.Id ? 1450 : 0));
                for (var i = 0; i < stationary.Length; i++)
                {
                    Assert.That(Vector3.Distance(positions[i], stationary[i].position), Is.LessThan(0.00001f), stationary[i].name);
                    Assert.That(Quaternion.Angle(rotations[i], stationary[i].rotation), Is.LessThan(0.001f), stationary[i].name);
                }
            }
        }

        [UnityTest]
        public IEnumerator TerminalBridgesPreviewSaveUndoAndReloadWithoutCabinetProjection()
        {
            controller.SetMode(SimulationMode.Wiring);
            controller.SetWireStyle(Color.red, 0.01f, "JumperLine");
            yield return null;
            controller.enabled = false;
            var surface = new WireSurfacePlane(Vector3.zero, Vector3.back, 0.003f);
            foreach (var motor in motors)
                for (var i = 0; i < 6; i++)
                    for (var j = i + 1; j < 6; j++)
                    {
                        var a = motor.Ports[i]; var b = motor.Ports[j];
                        var path = WireRenderPath.Build(a.EndpointGeometry(a.CurrentAnchorPosition, false), b.EndpointGeometry(b.CurrentAnchorPosition, false), new[] { Vector3.zero }, surface, true);
                        Assert.That(path.RouteKind, Is.EqualTo(WireRouteKind.MotorTerminalBridge));
                        Assert.That(path.Trunk, Is.Empty);
                        foreach (var point in path.Points)
                            Assert.That(Vector3.Dot(point - a.CurrentAnchorPosition, a.MotorOutward), Is.GreaterThanOrEqualTo(-0.0001f));
                    }
            var ports = motors.Single(m => m.Runtime.DeviceId == "M3").Ports;
            var begin = typeof(SimulationController).GetMethod("BeginWireRoute", Private);
            var complete = typeof(SimulationController).GetMethod("CompleteWireRoute", Private);
            foreach (var pair in new[] { new[] { 3, 4 }, new[] { 4, 5 }, new[] { 0, 5 } })
            {
                var a = ports[pair[0]]; var b = ports[pair[1]];
                begin.Invoke(controller, new object[] { a });
                var draft = Object.FindObjectOfType<ElectricalWireDraftView>();
                draft.Refresh(new[] { Vector3.zero }, b.CurrentAnchorPosition, b.EndpointGeometry(b.CurrentAnchorPosition, false), true);
                var preview = draft.RenderPath.Points.ToArray();
                complete.Invoke(controller, new object[] { b });
                yield return null;
                var wire = Object.FindObjectsOfType<ElectricalWireView>().Single(v => v.Connection.StartPort == a.QualifiedPort);
                Assert.That(wire.RenderedPoints, Is.EqualTo(preview));
                Assert.That(wire.Connection.Points, Is.Empty);
                wire.Connection.Points.Add(Vector3.zero);
                wire.Refresh();
                Assert.That(wire.RenderedPoints, Is.EqualTo(preview));
            }
            controller.UndoWiring();
            Assert.That(controller.Graph.Wires.Count, Is.EqualTo(2));
            controller.RedoWiring();
            Assert.That(controller.Graph.Wires.Count, Is.EqualTo(3));
            yield return null;
            CaptureMotor("M3", "motor-terminal-bridges");
            var filename = Path.Combine(Application.dataPath, "../Build/Reports/motor-bridge-roundtrip.cc3d");
            Assert.That(controller.SaveCc3dToPath(filename), Is.EqualTo(WiringFileResult.Success));
            Assert.That(controller.OpenCc3dFromPath(filename), Is.EqualTo(WiringFileResult.Success));
            yield return null;
            Assert.That(controller.Graph.Wires.Count, Is.EqualTo(3));
            foreach (var wire in Object.FindObjectsOfType<ElectricalWireView>())
            {
                Assert.That(wire.Connection.StartPort, Does.StartWith("M3."));
                Assert.That(wire.RenderPath.RouteKind, Is.EqualTo(WireRouteKind.MotorTerminalBridge));
                Assert.That(wire.RenderPath.Trunk, Is.Empty);
            }
            CaptureMotor("M3", "motor-terminal-bridges-reloaded");
        }

        [UnityTest]
        public IEnumerator LegacyRearM1SaveStaysOnMotor38AndDoesNotConnectM3()
        {
            controller.SetMode(SimulationMode.Wiring);
            controller.SetWireStyle(Color.red, 0.01f, "JumperLine");
            var legacy = controller.Graph.AddWire("M1.U2", "M1.V2", Color.red);
            legacy.FaultSide = true;
            legacy.Points.Add(Vector3.zero);
            var filename = Path.Combine(Application.dataPath, "../Build/Reports/motor-legacy-M1.cc3d");
            Assert.That(controller.SaveCc3dToPath(filename), Is.EqualTo(WiringFileResult.Success));
            Assert.That(controller.OpenCc3dFromPath(filename), Is.EqualTo(WiringFileResult.Success));
            yield return null;
            var view = Object.FindObjectsOfType<ElectricalWireView>().Single();
            var ports = motors.Single(m => m.Runtime.DeviceId == "M1").Ports;
            Assert.That(view.Connection.StartPort, Is.EqualTo("M1.U2"));
            Assert.That(view.Connection.EndPort, Is.EqualTo("M1.V2"));
            Assert.That(view.RenderedPoints.First(), Is.EqualTo(ports.Single(p => p.PortName == "U2").CurrentAnchorPosition));
            Assert.That(view.RenderedPoints.Last(), Is.EqualTo(ports.Single(p => p.PortName == "V2").CurrentAnchorPosition));
            Assert.That(view.RenderPath.RouteKind, Is.EqualTo(WireRouteKind.MotorTerminalBridge));
            Assert.That(controller.Graph.Solve().SameNet("M1.U2", "M3.U2"), Is.False);
        }

        [UnityTest]
        public IEnumerator ReferenceTaskAuditKeepsUnwiredMotorsStopped()
        {
            var rows = new System.Collections.Generic.List<string>();
            var mismatches = 0;
            for (var index = 0; index < 10; index++)
            {
                controller.ResetTraining();
                controller.LoadReferenceWiring();
                controller.PanelPower.StartForAssessment();
                controller.enabled = false;
                var task = controller.CurrentTask;
                var topology = CircuitTaskEvaluator.EvaluateTopology(controller.Graph, task);
                rows.Add(task.Id + " | " + task.Name + " | topology=" + topology.Passed);
                foreach (var step in task.Actions)
                {
                    ((ElectricalDeviceRuntime)controller.Graph.Devices[step.DeviceId]).SetControl(step.Active);
                    SimulationSnapshot snapshot = null;
                    var ticks = Mathf.Max(8, Mathf.CeilToInt(step.HoldSeconds / 0.02f));
                    for (var tick = 0; tick < ticks; tick++) snapshot = controller.Graph.Solve(0.02f);
                    var actual = snapshot.GetMotorDirection(step.ExpectedDeviceId);
                    if (actual != step.ExpectedMotorDirection) mismatches++;
                    rows.Add("  " + step.DeviceId + "=" + step.Active + " | " + step.ExpectedDeviceId + " expected=" + step.ExpectedMotorDirection + " actual=" + actual);
                    foreach (var id in new[] { "M3", "M_DOUBLE" })
                    {
                        Assert.That(snapshot.GetMotorSpeedRpm(id), Is.Zero, task.Id + ": " + id);
                        Assert.That(snapshot.GetMotorDirection(id), Is.EqualTo(MotorDirection.Stopped), task.Id + ": " + id);
                    }
                }
                controller.NextTask();
                yield return null;
            }
            rows.Add("Action mismatches: " + mismatches + "; unwired M3/M_DOUBLE remained stopped for all ten tasks.");
            File.WriteAllLines(Path.Combine(Application.dataPath, "../Build/Reports/motor-ten-task-audit.txt"), rows);
        }

        [UnityTest]
        public IEnumerator CabinetMotorTerminalsDriveOnlyExplicitlyJumperedMotor()
        {
            controller.enabled = false;
            var rows = new System.Collections.Generic.List<string>();
            foreach (var group in new[] { "C", "A", "B" })
                foreach (var target in new[] { "M1", "M2", "M3", "M_DOUBLE" })
                {
                    controller.ResetTraining();
                    controller.PanelPower.StartForAssessment();
                    var motorPorts = new[] { "U", "V", "W" };
                    var jumperIds = new System.Collections.Generic.List<string>();
                    for (var i = 0; i < 3; i++)
                    {
                        var terminal = "DuanZiPai_7." + group + "_" + motorPorts[i].ToLowerInvariant() + "1";
                        controller.Graph.AddWire("POWER.L" + (i + 1), terminal, Color.blue, "ElectricalWire");
                        jumperIds.Add(controller.Graph.AddWire(terminal, target + "." + motorPorts[i], Color.blue).Id);
                    }
                    var snapshot = controller.Graph.Solve();
                    rows.Add(group + " -> " + target + ": " + string.Join(", ", motors.Select(m => m.Runtime.DeviceId + "=" + snapshot.GetMotorSpeedRpm(m.Runtime.DeviceId))));
                    File.WriteAllLines(Path.Combine(Application.dataPath, "../Build/Reports/motor-cabinet-jumper-audit.txt"), rows);
                    foreach (var motor in motors)
                        Assert.That(snapshot.GetMotorSpeedRpm(motor.Runtime.DeviceId), Is.EqualTo(motor.Runtime.DeviceId == target ? 1450 : 0), group + " -> " + target + ": " + motor.Runtime.DeviceId);
                    var environment = GameObject.Find("OriginalLabEnvironment").transform;
                    var rotors = MotorBindingDefinition.All.ToDictionary(b => b.Id, b => environment.Find(b.ModelPath + "/mesh/zhuanpan"));
                    var before = rotors.ToDictionary(p => p.Key, p => p.Value.rotation);
                    if (group == "C" && target == "M1")
                    {
                        controller.SetMode(SimulationMode.Wiring);
                        controller.SetWireStyle(Color.blue, 0.01f, "JumperLine");
                        var saved = Path.Combine(Application.dataPath, "../Build/Reports/motor-cabinet-C-to-M1.cc3d");
                        Assert.That(controller.SaveCc3dToPath(saved), Is.EqualTo(WiringFileResult.Success));
                        Assert.That(controller.OpenCc3dFromPath(saved), Is.EqualTo(WiringFileResult.Success));
                        CaptureMotor("M1", "motor-cabinet-single-wired-before", true);
                    }
                    var moved = false;
                    for (var frame = 0; frame < 8; frame++)
                    {
                        yield return null;
                        foreach (var rotor in rotors)
                        {
                            var angle = Quaternion.Angle(before[rotor.Key], rotor.Value.rotation);
                            if (rotor.Key == target) moved |= angle > 0.01f;
                            else Assert.That(angle, Is.LessThan(0.001f), "Unwired physical rotor moved: " + rotor.Key);
                        }
                    }
                    Assert.That(moved, Is.True, "Wired physical rotor must move: " + target);
                    if (group == "C" && target == "M1") CaptureMotor("M1", "motor-cabinet-single-wired-after", true);
                    foreach (var id in jumperIds) controller.Graph.RemoveWire(id);
                    snapshot = controller.Graph.Solve(3);
                    foreach (var motor in motors)
                        Assert.That(snapshot.GetMotorSpeedRpm(motor.Runtime.DeviceId), Is.Zero, "Powered terminal strip without motor leads: " + motor.Runtime.DeviceId);
                    yield return null;
                }
        }

        private void CaptureMotor(string id, string filename, bool frontOverview = false)
        {
            if (SystemInfo.graphicsDeviceType == UnityEngine.Rendering.GraphicsDeviceType.Null) return;
            var camera = Camera.main;
            var previousPosition = camera.transform.position;
            var previousRotation = camera.transform.rotation;
            var previousFov = camera.fieldOfView;
            var previousNear = camera.nearClipPlane;
            var ports = motors.Single(m => m.Runtime.DeviceId == id).Ports;
            var center = ports.Aggregate(Vector3.zero, (sum, p) => sum + p.CurrentAnchorPosition) / 6;
            var model = GameObject.Find("OriginalLabEnvironment").transform.Find(MotorBindingDefinition.Find(id).ModelPath);
            camera.transform.position = center + ports[0].MotorOutward * 0.42f + Vector3.up * 0.12f;
            camera.transform.LookAt(center, -model.right);
            if (frontOverview)
            {
                var frontPorts = motors.Where(m => m.Runtime.DeviceId != "M3").SelectMany(m => m.Ports).ToArray();
                center = frontPorts.Aggregate(Vector3.zero, (sum, p) => sum + p.CurrentAnchorPosition) / frontPorts.Length;
                camera.transform.position = center + controller.Tachometer.Targets.Single(t => t.MotorId == "M1").Outward * 0.7f + Vector3.up * 0.3f;
                camera.transform.LookAt(center - Vector3.up * 0.03f);
            }
            camera.fieldOfView = 45;
            camera.nearClipPlane = 0.01f;
            var canvases = Object.FindObjectsOfType<Canvas>().Where(c => c.renderMode != RenderMode.WorldSpace && c.enabled).ToArray();
            foreach (var canvas in canvases) canvas.enabled = false;
            var previousTarget = camera.targetTexture;
            var previousActive = RenderTexture.active;
            var target = new RenderTexture(1000, 1000, 24);
            var texture = new Texture2D(1000, 1000, TextureFormat.RGB24, false);
            camera.targetTexture = target;
            camera.Render();
            RenderTexture.active = target;
            texture.ReadPixels(new Rect(0, 0, 1000, 1000), 0, 0);
            texture.Apply();
            Directory.CreateDirectory(Path.Combine(Application.dataPath, "../Build/Reports"));
            File.WriteAllBytes(Path.Combine(Application.dataPath, "../Build/Reports/" + filename + ".png"), texture.EncodeToPNG());
            camera.targetTexture = previousTarget;
            RenderTexture.active = previousActive;
            camera.transform.SetPositionAndRotation(previousPosition, previousRotation);
            camera.fieldOfView = previousFov;
            camera.nearClipPlane = previousNear;
            foreach (var canvas in canvases) canvas.enabled = true;
            Object.Destroy(texture); Object.Destroy(target);
        }
    }
}
