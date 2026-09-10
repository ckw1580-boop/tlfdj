using System;
using System.Collections;
using System.Collections.Generic;
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
    public sealed class ThermalRelaySceneTests
    {
        private SimulationController controller;
        private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
        [UnitySetUp] public IEnumerator Setup()
        {
            SceneManager.LoadScene("ElectricalTraining"); yield return null; yield return null;
            controller = Object.FindObjectOfType<SimulationController>();
            Canvas.ForceUpdateCanvases(); Physics.SyncTransforms();
        }
        private object Invoke(string name, params object[] args) => typeof(SimulationController).GetMethod(name, Private).Invoke(controller, args);
        private T Field<T>(string name) => (T)typeof(SimulationController).GetField(name, Private).GetValue(controller);
        private Vector2 BodyPoint(ThermalRelayView view)
        {
            var box = (BoxCollider)view.Picker;
            // The rear body's center projects onto terminal 95. Choose an exposed
            // body surface so the test respects the user's terminal priority rule.
            foreach (var y in new[] { 0f, -.25f, .25f, -.4f, .4f })
                foreach (var x in new[] { 0f, -.25f, .25f, -.4f, .4f })
                {
                    Vector2 point = Camera.main.WorldToScreenPoint(box.transform.TransformPoint(box.center +
                        Vector3.Scale(box.size, new Vector3(x, y, .5f))));
                    if (Physics.Raycast(Camera.main.ScreenPointToRay(point), out var hit, 100f) &&
                        hit.collider.GetComponent<ElectricalPortView>() == null &&
                        hit.collider.GetComponentInParent<ThermalRelayView>() == view) return point;
                }
            Assert.Fail("没有可见的热继电器本体点击面：" + view.Definition.Id);
            return Vector2.zero;
        }
        private void Click(ThermalRelayView view) => Invoke("HandleWiringPointerDown", Camera.main, BodyPoint(view));
        private string ClickDiagnostic(ThermalRelayView view)
        {
            var point = BodyPoint(view);
            var hits = Physics.RaycastAll(Camera.main.ScreenPointToRay(point), 100f).OrderBy(h => h.distance)
                .Select(h => h.collider.name + "/" + h.collider.transform.parent?.name + " " + h.distance);
            return view.Definition.Id + " " + controller.Mode + " at " + point + " hits " + string.Join(", ", hits);
        }

        [UnityTest] public IEnumerator ThreeBodiesBind30UniquePortsAndIndependentNets()
        {
            Assert.That(controller.ThermalRelayViews.Count, Is.EqualTo(3));
            Assert.That(controller.ThermalRelayViews.Select(v => v.Runtime.DeviceId), Is.EqualTo(new[] { "FR1", "FR2", "FR" }));
            Assert.That(controller.ThermalRelayViews.SelectMany(v => v.Bindings.Values).Distinct().Count(), Is.EqualTo(30));
            var snapshot = controller.Graph.Solve();
            foreach (var view in controller.ThermalRelayViews)
            {
                Assert.That(view.transform, Is.SameAs(GameObject.Find("OriginalLabEnvironment").transform.Find(view.Definition.ModelPath)));
                Assert.That(view.Bindings.Count, Is.EqualTo(10));
                foreach (var binding in view.Bindings)
                {
                    Assert.That(snapshot.SameNet(binding.Value.QualifiedPort, view.Runtime.DeviceId + "." + binding.Key), Is.True);
                    if (view.IsRear) Assert.That(binding.Value.GetOriginalAnchor(TrainingViewPreset.FaultBack, false).IsChildOf(view.transform), Is.True);
                    else Assert.That(binding.Value.PortName, Is.EqualTo(view.Definition.BindingName(binding.Key)));
                    foreach (var other in controller.ThermalRelayViews.Where(v => v != view))
                        Assert.That(snapshot.SameNet(binding.Value.QualifiedPort, other.Bindings[binding.Key].QualifiedPort), Is.False);
                }
            }
            yield return null;
        }

        [UnityTest] public IEnumerator WiringClicksKeepRouteAndSwitchKaKmFrInOneWindow()
        {
            controller.SetMode(SimulationMode.Wiring);
            var start = controller.ThermalRelayViews[0].Bindings["95"];
            Invoke("BeginWireRoute", start);
            Invoke("HandleWiringClick", null, Camera.main.ScreenPointToRay(new Vector2(Screen.width * .55f, Screen.height * .4f)));
            var before = Field<List<Vector3>>("pendingWirePoints").ToArray();
            foreach (var view in controller.ThermalRelayViews)
            {
                if (view.IsRear)
                {
                    // A camera-side change resets the existing route by design.
                    // Start a rear route before exercising body inspection there.
                    Object.FindObjectOfType<TrainingCameraController>().SetFaultView(); yield return null;
                    start = view.Bindings["95"];
                    Invoke("BeginWireRoute", start);
                    Invoke("HandleWiringClick", null, Camera.main.ScreenPointToRay(new Vector2(Screen.width * .55f, Screen.height * .4f)));
                    before = Field<List<Vector3>>("pendingWirePoints").ToArray();
                }
                Click(view); Click(view);
                Assert.That(controller.SchematicThermalRelay, Is.SameAs(view), ClickDiagnostic(view));
                Assert.That(controller.RelaySchematic.Title, Is.EqualTo(view.Definition.Id + " · 热继电器原理图"));
                Assert.That(controller.RelaySchematic.Diagram.texture, Is.SameAs(Resources.Load<Texture2D>("ThermalRelaySchematic")));
                Assert.That(Field<ElectricalPortView>("selectedPort"), Is.SameAs(start));
                Assert.That(Field<List<Vector3>>("pendingWirePoints"), Is.EqualTo(before));
                Assert.That(controller.Graph.Wires, Is.Empty); Assert.That(view.Runtime.IsTripped, Is.False);
                controller.ShowRelaySchematic(controller.RelayViews[0]); Assert.That(controller.SchematicThermalRelay, Is.Null);
                controller.ShowThermalRelaySchematic(view); controller.ShowContactorSchematic(controller.ContactorViews[0]);
                Assert.That(controller.SchematicThermalRelay, Is.Null); Click(view);
                controller.RelaySchematic.GetComponentInChildren<Button>().onClick.Invoke();
                Assert.That(controller.SchematicThermalRelay, Is.Null);
                Assert.That(Field<List<Vector3>>("pendingWirePoints"), Is.EqualTo(before));
            }
            controller.ShowThermalRelaySchematic(controller.ThermalRelayViews[2]);
            controller.SetMode(SimulationMode.View); Assert.That(controller.RelaySchematic.gameObject.activeSelf, Is.False);
        }

        [UnityTest] public IEnumerator PropertiesClickIsReadOnlyAndTripResetRemainIndependent()
        {
            foreach (var view in controller.ThermalRelayViews)
            {
                if (view.IsRear) { Object.FindObjectOfType<TrainingCameraController>().SetFaultView(); yield return null; }
                foreach (var mode in view.IsRear ? new[] { SimulationMode.View, SimulationMode.Simulate, SimulationMode.Fault } : new[] { SimulationMode.View, SimulationMode.Simulate })
                {
                    controller.SetMode(mode);
                    Invoke("HandleScenePointerDown", Camera.main, BodyPoint(view));
                    Assert.That(controller.SelectedThermalRelay, Is.SameAs(view), ClickDiagnostic(view));
                    Assert.That(view.Runtime.IsTripped, Is.False);
                    controller.SetThermalRelayTripped(view, true); yield return null;
                    Assert.That(view.Runtime.IsTripped, Is.EqualTo(mode == SimulationMode.Simulate));
                    foreach (var other in controller.ThermalRelayViews.Where(v => v != view)) Assert.That(other.Runtime.IsTripped, Is.False);
                    if (mode == SimulationMode.Simulate)
                    {
                        Assert.That(controller.ThermalRelayProperties.DisplayedText, Does.Contain("95–96 常闭：断开").And.Contain("97–98 常开：闭合"));
                        controller.ThermalRelayProperties.transform.Find("Reset").GetComponent<Button>().onClick.Invoke(); yield return null;
                        Assert.That(view.Runtime.IsTripped, Is.False);
                    }
                    controller.ThermalRelayProperties.transform.Find("Close").GetComponent<Button>().onClick.Invoke();
                    Assert.That(controller.SelectedThermalRelay, Is.Null);
                }
            }
            controller.SetMode(SimulationMode.View); controller.SelectThermalRelay(controller.ThermalRelayViews[0]);
            controller.SelectRelay(controller.RelayViews[0]); Assert.That(controller.SelectedThermalRelay, Is.Null);
            controller.SelectThermalRelay(controller.ThermalRelayViews[0]); Assert.That(controller.SelectedRelay, Is.Null);
            controller.SelectContactor(controller.ContactorViews[0]); Assert.That(controller.SelectedThermalRelay, Is.Null);
        }

        [UnityTest] public IEnumerator ActualTerminalControlAndSavedWiresStayIndependent()
        {
            var path = Path.Combine(Application.temporaryCachePath, "thermal-" + Guid.NewGuid().ToString("N") + ".cc3d");
            try
            {
                controller.SetMode(SimulationMode.Wiring);
                var fr = controller.ThermalRelayViews[0]; var km = controller.ContactorViews[0];
                controller.Graph.AddWire("POWER.L1", fr.Bindings["95"].QualifiedPort, Color.red);
                controller.Graph.AddWire(fr.Bindings["96"].QualifiedPort, km.Bindings["A1"].QualifiedPort, Color.red);
                controller.Graph.AddWire(km.Bindings["A2"].QualifiedPort, "POWER.N", Color.blue);
                var endpoints = controller.Graph.Wires.Select(w => w.StartPort + ">" + w.EndPort).ToArray();
                Assert.That(controller.SaveCc3dToPath(path), Is.EqualTo(WiringFileResult.Success));
                Assert.That(controller.OpenCc3dFromPath(path), Is.EqualTo(WiringFileResult.Success));
                Assert.That(controller.Graph.Wires.Select(w => w.StartPort + ">" + w.EndPort), Is.EqualTo(endpoints));
                controller.PanelPower.StartForAssessment(); controller.SetMode(SimulationMode.Simulate); yield return null; yield return null;
                Assert.That(km.Runtime.IsActive, Is.True);
                controller.SetThermalRelayTripped(controller.ThermalRelayViews[2], true); yield return null; yield return null;
                Assert.That(km.Runtime.IsActive, Is.True);
                controller.SetThermalRelayTripped(fr, true); yield return null; yield return null;
                Assert.That(km.Runtime.IsActive, Is.False);
                controller.SetThermalRelayTripped(fr, false); yield return null; yield return null;
                Assert.That(km.Runtime.IsActive, Is.True);
            }
            finally { if (File.Exists(path)) File.Delete(path); }
        }

        [UnityTest] public IEnumerator DiagramAndScrollablePropertiesFitBothResolutions()
        {
            controller.SetMode(SimulationMode.Wiring); controller.ShowThermalRelaySchematic(controller.ThermalRelayViews[0]); yield return null;
            RelaySchematicTests.CaptureAndCheck(controller, 1920, 1080, "thermal-relay-schematic-");
            RelaySchematicTests.CaptureAndCheck(controller, 1280, 720, "thermal-relay-schematic-");
            controller.SetMode(SimulationMode.View); controller.SelectThermalRelay(controller.ThermalRelayViews[0]); yield return null;
            RelaySceneTests.CapturePanel(controller.ThermalRelayProperties, "thermal-relay-properties-top.png", 1);
            RelaySceneTests.CapturePanel(controller.ThermalRelayProperties, "thermal-relay-properties-bottom.png", 0);
        }

        [UnityTest] public IEnumerator RearTerminalsRetainWiringAndMeterPriority()
        {
            var rear = controller.ThermalRelayViews[2];
            Object.FindObjectOfType<TrainingCameraController>().SetFaultView();
            controller.SetMode(SimulationMode.Wiring); yield return null;
            var port = rear.Bindings.Values.First(p => p.IsVisible &&
                Physics.Raycast(Camera.main.ScreenPointToRay(Camera.main.WorldToScreenPoint(p.CurrentAnchorPosition)), out var hit, 100f) &&
                hit.collider.GetComponent<ElectricalPortView>() == p);
            Invoke("HandleWiringPointerDown", Camera.main, (Vector2)Camera.main.WorldToScreenPoint(port.CurrentAnchorPosition));
            Assert.That(Field<ElectricalPortView>("selectedPort"), Is.SameAs(port));
            Assert.That(controller.SchematicThermalRelay, Is.Null);
            Click(rear); Assert.That(controller.SchematicThermalRelay, Is.SameAs(rear));
            Assert.That(Field<ElectricalPortView>("selectedPort"), Is.SameAs(port));
            controller.SetMode(SimulationMode.Fault); yield return null;
            Invoke("HandleScenePointerDown", Camera.main, (Vector2)Camera.main.WorldToScreenPoint(port.CurrentAnchorPosition));
            Assert.That(controller.SelectedThermalRelay, Is.Null);
            Assert.That(Field<List<ElectricalPortView>>("meterPorts"), Does.Contain(port));
            Invoke("HandleScenePointerDown", Camera.main, BodyPoint(rear));
            Assert.That(controller.SelectedThermalRelay, Is.SameAs(rear));
            Assert.That(Field<List<ElectricalPortView>>("meterPorts"), Does.Contain(port));
        }
    }
}
