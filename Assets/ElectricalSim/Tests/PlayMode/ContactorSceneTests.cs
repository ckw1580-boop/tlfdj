using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace ElectricalSim.Tests
{
    public sealed class ContactorSceneTests
    {
        private SimulationController controller;
        private string savePath;
        private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
        [UnitySetUp] public IEnumerator Setup()
        {
            SceneManager.LoadScene("ElectricalTraining"); yield return null; yield return null;
            controller = Object.FindObjectOfType<SimulationController>();
            savePath = Path.Combine(Application.temporaryCachePath, "contactor-" + Guid.NewGuid().ToString("N") + ".cc3d");
            Canvas.ForceUpdateCanvases(); Physics.SyncTransforms();
        }
        [TearDown] public void Cleanup() { if (File.Exists(savePath)) File.Delete(savePath); }
        private object Invoke(string name, params object[] args) => typeof(SimulationController).GetMethod(name, Private).Invoke(controller, args);
        private T Field<T>(string name) => (T)typeof(SimulationController).GetField(name, Private).GetValue(controller);
        private void Click(Vector2 point) => Invoke("HandleWiringPointerDown", Camera.main, point);
        private Vector2 BodyPoint(Collider picker)
        {
            var box = (BoxCollider)picker;
            return Camera.main.WorldToScreenPoint(box.transform.TransformPoint(box.center + Vector3.forward * box.size.z * 0.5f));
        }
        private WireConnection Wire(string a, string b) => controller.Graph.AddWire(a, b, Color.red, "ElectricalWire");
        private ElectricalPortView[] VisibleRearPorts(ContactorView rear)
            => rear.Bindings.Values.Where(port => port.IsVisible &&
                Physics.Raycast(Camera.main.ScreenPointToRay(Camera.main.WorldToScreenPoint(port.CurrentAnchorPosition)), out var hit, 100f) &&
                hit.collider.GetComponent<ElectricalPortView>() == port).ToArray();

        [UnityTest] public IEnumerator FourBodiesAnd72PhysicalEndpointsHaveIndependentMappings()
        {
            var environment = GameObject.Find("OriginalLabEnvironment").transform;
            Assert.That(controller.ContactorViews.Count, Is.EqualTo(4));
            Assert.That(Object.FindObjectsOfType<ContactorView>(true).Length, Is.EqualTo(7));
            Assert.That(controller.ContactorViews.Select(v => v.Runtime.DeviceId), Is.EqualTo(new[] { "KMF", "KM1", "KMR", "KM2" }));
            var all = controller.ContactorViews.SelectMany(v => v.Bindings.Values).ToArray();
            Assert.That(all.Length, Is.EqualTo(72)); Assert.That(all.Distinct().Count(), Is.EqualTo(72));
            var snapshot = controller.Graph.Solve();
            foreach (var view in controller.ContactorViews)
            {
                Assert.That(view.transform, Is.SameAs(environment.Find(view.Definition.ModelPath)));
                Assert.That(view.Picker.transform, Is.SameAs(view.transform.Find("picker")));
                Assert.That(controller.Graph.Devices[view.Runtime.DeviceId], Is.SameAs(view.Runtime));
                Assert.That(view.Bindings.Count, Is.EqualTo(18));
                foreach (var binding in view.Bindings)
                {
                    var board = new[] { "T1", "T2", "T3", "14", "54", "62", "72", "84" }.Contains(binding.Key) ? "DuanZiPai_4" : "DuanZiPai_3";
                    var port = binding.Value;
                    Assert.That(port.DeviceId, Is.EqualTo(board));
                    Assert.That(port.PortName, Is.EqualTo(view.Definition.BindingName(binding.Key)));
                    Assert.That(snapshot.SameNet(port.QualifiedPort, view.Runtime.DeviceId + "." + binding.Key), Is.True);
                    Assert.That(port.HoverLabel, Does.Contain(port.PortName));
                }
                Assert.That(snapshot.SameNet(view.Bindings["61"].QualifiedPort, view.Bindings["71"].QualifiedPort), Is.False);
                Assert.That(snapshot.SameNet(view.Bindings["53"].QualifiedPort, view.Bindings["83"].QualifiedPort), Is.False);
            }
            yield return null;
        }

        [UnityTest] public IEnumerator AllBodiesSwitchTheSharedWindowAndModeChangesHideIt()
        {
            controller.SetMode(SimulationMode.Wiring);
            var window = controller.RelaySchematic;
            var ka = controller.RelayViews[0];
            foreach (var view in controller.ContactorViews)
            {
                Click(BodyPoint(view.Picker));
                Assert.That(controller.SchematicContactor, Is.SameAs(view), view.Definition.Id);
                Assert.That(controller.SchematicRelay, Is.Null);
                Assert.That(window.Title, Is.EqualTo(view.Definition.Id + " · 交流接触器原理图"));
                Assert.That(window.Diagram.texture, Is.SameAs(Resources.Load<Texture2D>("ContactorSchematic")));
                Click(BodyPoint(view.Picker)); Assert.That(window.gameObject.activeSelf, Is.True);
                Click(BodyPoint(ka.Picker));
                Assert.That(controller.SchematicContactor, Is.Null);
                Assert.That(controller.SchematicRelay, Is.SameAs(ka));
                Assert.That(window.Diagram.texture, Is.SameAs(Resources.Load<Texture2D>("RelaySchematic")));
                Assert.That(view.Runtime.IsActive, Is.False);
            }
            Assert.That(Object.FindObjectsOfType<RelaySchematicPresenter>(true).Length, Is.EqualTo(1));
            foreach (var mode in new[] { SimulationMode.View, SimulationMode.Simulate, SimulationMode.Fault, SimulationMode.Drag })
            {
                controller.ShowContactorSchematic(controller.ContactorViews[0]);
                controller.SetMode(mode);
                Assert.That(window.gameObject.activeSelf, Is.False);
                Assert.That(controller.SchematicContactor, Is.Null);
                controller.ShowContactorSchematic(controller.ContactorViews[0]);
                Assert.That(window.gameObject.activeSelf, Is.False);
                controller.SetMode(SimulationMode.Wiring);
            }
            yield return null;
        }

        [UnityTest] public IEnumerator ViewingDiagramPreservesRouteAndBlocksOnlyWindowClicks()
        {
            controller.SetMode(SimulationMode.Wiring);
            var view = controller.ContactorViews[0]; var start = view.Bindings["A1"];
            Invoke("BeginWireRoute", start);
            Invoke("HandleWiringClick", null, Camera.main.ScreenPointToRay(new Vector2(Screen.width * .55f, Screen.height * .4f)));
            var pending = Field<List<Vector3>>("pendingWirePoints"); var before = pending.ToArray();
            Assert.That(before, Is.Not.Empty);
            Click(BodyPoint(view.Picker));
            Assert.That(controller.SchematicContactor, Is.SameAs(view));
            Assert.That(Field<ElectricalPortView>("selectedPort"), Is.SameAs(start));
            Assert.That(pending, Is.EqualTo(before));
            Canvas.ForceUpdateCanvases();
            Click(RectTransformUtility.WorldToScreenPoint(null, controller.RelaySchematic.Diagram.rectTransform.position));
            var close = controller.RelaySchematic.GetComponentInChildren<Button>();
            Click(RectTransformUtility.WorldToScreenPoint(null, close.transform.position));
            Assert.That(pending, Is.EqualTo(before)); Assert.That(controller.Graph.Wires, Is.Empty);
            ExecuteEvents.Execute(close.gameObject, new PointerEventData(EventSystem.current) { button = PointerEventData.InputButton.Left }, ExecuteEvents.pointerClickHandler);
            Assert.That(controller.SchematicContactor, Is.Null); Assert.That(controller.IsRoutingWire, Is.True);
            Click(BodyPoint(view.Picker));
            Click(Camera.main.WorldToScreenPoint(view.Bindings["A2"].CurrentAnchorPosition));
            Assert.That(controller.IsRoutingWire, Is.False);
            Assert.That(controller.Graph.Wires.Single().Points, Is.EqualTo(before));
            Assert.That(controller.SchematicContactor, Is.SameAs(view));
            Click(new Vector2(Screen.width * .6f, Screen.height * .3f));
            Assert.That(controller.SchematicContactor, Is.SameAs(view));
            controller.HideContactorSchematic();
            typeof(SimulationController).GetField("draggingWirePoint", Private).SetValue(controller, true);
            Click(BodyPoint(view.Picker)); Assert.That(controller.SchematicContactor, Is.Null);
            typeof(SimulationController).GetField("draggingWirePoint", Private).SetValue(controller, false);
            yield return null;
        }

        [UnityTest] public IEnumerator PropertiesAreReadOnlyAndMutuallyExclusive()
        {
            foreach (var view in controller.ContactorViews)
            {
                controller.SelectContactor(view); yield return null;
                Assert.That(controller.ContactorProperties.DisplayedText, Does.Contain(view.Definition.Id).And.Contain(view.Runtime.DeviceId).And.Contain("AC 220V"));
                Assert.That(controller.ContactorProperties.GetComponentsInChildren<InputField>().Length, Is.Zero);
                Assert.That(view.Runtime.IsActive, Is.False);
            }
            var first = controller.ContactorViews[0];
            controller.SelectRelay(controller.RelayViews[0]); Assert.That(controller.SelectedContactor, Is.Null);
            controller.SelectContactor(first); Assert.That(controller.SelectedRelay, Is.Null);
            controller.SelectPlc(controller.PlcViews[0]); Assert.That(controller.SelectedContactor, Is.Null);
            controller.SelectContactor(first); Assert.That(controller.SelectedPlc, Is.Null);
            controller.SelectPanelDevice(controller.PanelControls.First()); Assert.That(controller.SelectedContactor, Is.Null);
            controller.SelectContactor(first); Assert.That(controller.SelectedPanelDevice, Is.Null);
            Wire(first.Bindings["A1"].QualifiedPort, first.Bindings["A2"].QualifiedPort);
            Invoke("RefreshWireViews"); Invoke("SelectWire", Object.FindObjectOfType<ElectricalWireView>());
            Assert.That(controller.SelectedContactor, Is.Null);
            controller.SelectContactor(first); Assert.That(controller.SelectedWire, Is.Null);
            controller.ContactorProperties.GetComponentInChildren<Button>().onClick.Invoke();
            Assert.That(controller.SelectedContactor, Is.Null);
            controller.SelectContactor(first); controller.SetMode(SimulationMode.Wiring);
            Assert.That(controller.SelectedContactor, Is.Null);
            controller.SelectContactor(first); Assert.That(controller.SelectedContactor, Is.Null);
        }

        [UnityTest] public IEnumerator PhysicalWiringControlsLoadAndSurvivesSaveReload()
        {
            var view = controller.ContactorViews[0];
            var supply = Object.FindObjectsOfType<ElectricalPortView>().Single(p => p.DeviceId == "DuanZiPai_6" && p.PortName == "V_1");
            var neutral = Object.FindObjectsOfType<ElectricalPortView>().Single(p => p.DeviceId == "DuanZiPai_6" && p.PortName == "N_1");
            Wire(supply.QualifiedPort, view.Bindings["A1"].QualifiedPort);
            Wire(neutral.QualifiedPort, view.Bindings["A2"].QualifiedPort);
            Wire("TERMINAL_BUS.DC_POSITIVE", view.Bindings["53"].QualifiedPort);
            Wire(view.Bindings["54"].QualifiedPort, "HL1.L"); Wire("HL1.N", "TERMINAL_BUS.DC_NEGATIVE");
            controller.PanelPower.StartForAssessment(); controller.SetMode(SimulationMode.Simulate);
            controller.SelectContactor(view); yield return null; yield return null;
            Assert.That(view.Runtime.IsActive, Is.True);
            Assert.That(controller.Graph.Devices["HL1"].IsActive, Is.True);
            Assert.That(controller.ContactorProperties.DisplayedText, Does.Contain("220 V AC").And.Contain("53–54 辅助 常开：闭合").And.Contain("61–62 辅助 常闭：断开"));
            RelaySceneTests.CapturePanel(controller.ContactorProperties, "contactor-properties-top.png", 1);
            RelaySceneTests.CapturePanel(controller.ContactorProperties, "contactor-properties-bottom.png", 0);
            Assert.That(controller.SaveCc3dToPath(savePath), Is.EqualTo(WiringFileResult.Success), controller.LastFileError);
            Assert.That(controller.OpenCc3dFromPath(savePath), Is.EqualTo(WiringFileResult.Success), controller.LastFileError);
            controller.PanelPower.StartForAssessment(); controller.SetMode(SimulationMode.Simulate); controller.SelectContactor(view);
            yield return null; yield return null;
            Assert.That(view.Runtime.IsActive, Is.True);
            var wire = controller.Graph.Wires.Single(w => w.EndPort == view.Bindings["A1"].QualifiedPort);
            controller.Graph.RemoveWire(wire.Id); yield return null; yield return null;
            Assert.That(view.Runtime.IsActive, Is.False);
            Assert.That(controller.Graph.Devices["HL1"].IsActive, Is.False);
            Assert.That(controller.ContactorProperties.DisplayedText, Does.Contain("已释放").And.Contain("61–62 辅助 常闭：闭合"));
        }

        [UnityTest] public IEnumerator DiagramFitsBothResolutions()
        {
            controller.SetMode(SimulationMode.Wiring); controller.ShowContactorSchematic(controller.ContactorViews[0]);
            yield return null;
            RelaySchematicTests.CaptureAndCheck(controller, 1920, 1080, "contactor-schematic-");
            RelaySchematicTests.CaptureAndCheck(controller, 1280, 720, "contactor-schematic-");
        }

        [UnityTest] public IEnumerator RearBodiesBindAll54ExistingPortsAndShareFrontRuntime()
        {
            var environment = GameObject.Find("OriginalLabEnvironment").transform;
            Assert.That(controller.RearContactorViews.Count, Is.EqualTo(3));
            Assert.That(controller.RearContactorViews.Select(v => v.Runtime.DeviceId), Is.EqualTo(new[] { "KMF", "KM1", "KMR" }));
            var all = controller.RearContactorViews.SelectMany(v => v.Bindings.Values).ToArray();
            Assert.That(all.Length, Is.EqualTo(54)); Assert.That(all.Distinct().Count(), Is.EqualTo(54));
            var snapshot = controller.Graph.Solve();
            foreach (var rear in controller.RearContactorViews)
            {
                var front = controller.ContactorViews.Single(v => v.Definition == rear.Definition);
                Assert.That(rear.IsRear, Is.True); Assert.That(front.IsRear, Is.False);
                Assert.That(rear.Runtime, Is.SameAs(front.Runtime));
                Assert.That(rear.transform, Is.SameAs(environment.Find(rear.Definition.RearModelPath)));
                Assert.That(rear.Picker, Is.SameAs(rear.transform.Find("picker").GetComponent<Collider>()));
                foreach (var binding in rear.Bindings)
                {
                    var port = binding.Value;
                    Assert.That(port.QualifiedPort, Is.EqualTo(rear.Runtime.DeviceId + "." + binding.Key));
                    Assert.That(port.GetOriginalAnchor(TrainingViewPreset.FaultBack, false).IsChildOf(rear.transform), Is.True);
                    Assert.That(port.GetOriginalAnchor(TrainingViewPreset.FaultBack, true), Is.Null);
                    Assert.That(port.ElectricalOnly, Is.True);
                    Assert.That(snapshot.SameNet(port.QualifiedPort, front.Bindings[binding.Key].QualifiedPort), Is.True);
                }
            }
            yield return null;
        }

        [UnityTest] public IEnumerator RearBodyClicksShowPropertiesInViewSimulationAndFaultModes()
        {
            var camera = Object.FindObjectOfType<TrainingCameraController>(); camera.SetFaultView();
            foreach (var mode in new[] { SimulationMode.View, SimulationMode.Simulate, SimulationMode.Fault })
            {
                controller.SetMode(mode); yield return null;
                foreach (var rear in controller.RearContactorViews)
                {
                    controller.SelectContactor(null); Canvas.ForceUpdateCanvases(); Physics.SyncTransforms();
                    Invoke("HandleScenePointerDown", Camera.main, BodyPoint(rear.Picker));
                    Assert.That(controller.SelectedContactor, Is.SameAs(rear), rear.Definition.Id + " " + mode);
                    Assert.That(controller.ContactorProperties.DisplayedText, Does.Contain("柜体背部").And.Contain(rear.Runtime.DeviceId + ".A2"));
                    Assert.That(rear.Runtime.IsActive, Is.False);
                    Assert.That(Field<List<ElectricalPortView>>("meterPorts"), Is.Empty);
                }
            }
            controller.ContactorProperties.GetComponentInChildren<Button>().onClick.Invoke();
            Assert.That(controller.SelectedContactor, Is.Null);
        }

        [UnityTest] public IEnumerator RearWiringInspectionPreservesBothLineTypesAndSavedEndpoints()
        {
            controller.SetMode(SimulationMode.Wiring);
            Object.FindObjectOfType<TrainingCameraController>().SetFaultView();
            foreach (var lineType in new[] { "ElectricalWire", "JumperLine" })
            {
                controller.Graph.ClearWires(); Invoke("RefreshWireViews");
                controller.SetWireStyle(Color.red, .01f, lineType); yield return null;
                var rear = controller.RearContactorViews[0];
                if (lineType == "JumperLine")
                {
                    Assert.That(controller.RearContactorViews.SelectMany(v => v.Bindings.Values).All(p => !p.IsVisible), Is.True);
                    foreach (var other in controller.RearContactorViews)
                    {
                        Click(BodyPoint(other.Picker));
                        Assert.That(controller.SchematicContactor, Is.SameAs(other));
                        Assert.That(controller.Graph.Wires, Is.Empty);
                    }
                    continue;
                }
                // Depth-separated terminals can overlap in this preset. Click the
                // exposed terminals; never select one hidden behind another port.
                var visiblePorts = VisibleRearPorts(rear);
                Assert.That(visiblePorts.Length, Is.GreaterThanOrEqualTo(2));
                var start = visiblePorts[0]; var end = visiblePorts[1];
                Invoke("BeginWireRoute", start);
                Invoke("HandleWiringClick", null, Camera.main.ScreenPointToRay(new Vector2(Screen.width * .55f, Screen.height * .4f)));
                var pending = Field<List<Vector3>>("pendingWirePoints"); var before = pending.ToArray();
                Assert.That(before, Is.Not.Empty);
                foreach (var other in controller.RearContactorViews)
                {
                    Click(BodyPoint(other.Picker));
                    Assert.That(controller.SchematicContactor, Is.SameAs(other));
                    Assert.That(controller.RelaySchematic.Title, Is.EqualTo(other.Definition.Id + " · 背部接触器原理图"));
                    Assert.That(Field<ElectricalPortView>("selectedPort"), Is.SameAs(start));
                    Assert.That(pending, Is.EqualTo(before)); Assert.That(controller.Graph.Wires, Is.Empty);
                }
                Click(BodyPoint(rear.Picker)); Canvas.ForceUpdateCanvases();
                if (lineType == "ElectricalWire")
                {
                    RelaySchematicTests.CaptureAndCheck(controller, 1920, 1080, "rear-contactor-schematic-");
                    RelaySchematicTests.CaptureAndCheck(controller, 1280, 720, "rear-contactor-schematic-");
                }
                var close = controller.RelaySchematic.GetComponentInChildren<Button>();
                Click(RectTransformUtility.WorldToScreenPoint(null, close.transform.position));
                Assert.That(pending, Is.EqualTo(before));
                ExecuteEvents.Execute(close.gameObject, new PointerEventData(EventSystem.current) { button = PointerEventData.InputButton.Left }, ExecuteEvents.pointerClickHandler);
                Assert.That(controller.SchematicContactor, Is.Null);
                Click(BodyPoint(rear.Picker));
                Click(Camera.main.WorldToScreenPoint(end.CurrentAnchorPosition));
                Assert.That(controller.IsRoutingWire, Is.False);
                var wire = controller.Graph.Wires.Single();
                Assert.That(wire.StartPort, Is.EqualTo(start.QualifiedPort)); Assert.That(wire.EndPort, Is.EqualTo(end.QualifiedPort));
                Assert.That(wire.Points, Is.EqualTo(before));
                Assert.That(controller.SaveCc3dToPath(savePath), Is.EqualTo(WiringFileResult.Success), controller.LastFileError);
                Assert.That(controller.OpenCc3dFromPath(savePath), Is.EqualTo(WiringFileResult.Success), controller.LastFileError);
                var restored = controller.Graph.Wires.Single();
                Assert.That(restored.StartPort, Is.EqualTo(wire.StartPort)); Assert.That(restored.EndPort, Is.EqualTo(wire.EndPort));
                Assert.That(restored.Points, Is.EqualTo(before));
                controller.SetMode(SimulationMode.Wiring);
            }
        }

        [UnityTest] public IEnumerator RearCoilAndContactsControlLoadAndUpdateFrontProperties()
        {
            var rear = controller.RearContactorViews[0]; var front = controller.ContactorViews[0];
            Wire("DuanZiPai_6.V_1", rear.Bindings["A1"].QualifiedPort);
            Wire("DuanZiPai_6.N_1", rear.Bindings["A2"].QualifiedPort);
            Wire("TERMINAL_BUS.DC_POSITIVE", rear.Bindings["53"].QualifiedPort);
            Wire(rear.Bindings["54"].QualifiedPort, "HL1.L"); Wire("HL1.N", "TERMINAL_BUS.DC_NEGATIVE");
            controller.PanelPower.StartForAssessment(); controller.SetMode(SimulationMode.Simulate);
            Object.FindObjectOfType<TrainingCameraController>().SetFaultView();
            controller.SelectContactor(rear); yield return null; yield return null;
            Assert.That(front.Runtime.IsActive, Is.True); Assert.That(controller.Graph.Devices["HL1"].IsActive, Is.True);
            Assert.That(controller.ContactorProperties.DisplayedText, Does.Contain("已吸合").And.Contain("KMF.53").And.Contain("DuanZiPai_3.KM1_53NO"));
            RelaySceneTests.CapturePanel(controller.ContactorProperties, "rear-contactor-properties-top.png", 1);
            RelaySceneTests.CapturePanel(controller.ContactorProperties, "rear-contactor-properties-bottom.png", 0);
            Assert.That(controller.SaveCc3dToPath(savePath), Is.EqualTo(WiringFileResult.Success), controller.LastFileError);
            Assert.That(controller.OpenCc3dFromPath(savePath), Is.EqualTo(WiringFileResult.Success), controller.LastFileError);
            controller.PanelPower.StartForAssessment(); controller.SetMode(SimulationMode.Simulate); controller.SelectContactor(front);
            yield return null; yield return null;
            Assert.That(front.Runtime.IsActive, Is.True); Assert.That(controller.Graph.Devices["HL1"].IsActive, Is.True);
            Assert.That(controller.ContactorProperties.DisplayedText, Does.Contain("柜体正面").And.Contain("已吸合"));
            controller.Graph.RemoveWire(controller.Graph.Wires.Single(w => w.EndPort == "KMF.A1").Id);
            yield return null; yield return null;
            Assert.That(rear.Runtime.IsActive, Is.False); Assert.That(front.Runtime.IsActive, Is.False);
            Assert.That(controller.Graph.Devices["HL1"].IsActive, Is.False);
        }

        [UnityTest] public IEnumerator RearMeterTerminalsTakePriorityOverBodyInspection()
        {
            controller.SetMode(SimulationMode.Fault); controller.SetWireStyle(Color.red, .01f, "ElectricalWire");
            yield return null; Physics.SyncTransforms();
            var rear = controller.RearContactorViews[0];
            Invoke("HandleScenePointerDown", Camera.main, BodyPoint(rear.Picker));
            Assert.That(controller.SelectedContactor, Is.SameAs(rear));
            var visiblePorts = VisibleRearPorts(rear);
            Assert.That(visiblePorts.Length, Is.GreaterThanOrEqualTo(2));
            foreach (var port in visiblePorts.Take(2))
            {
                Invoke("HandleScenePointerDown", Camera.main, (Vector2)Camera.main.WorldToScreenPoint(port.CurrentAnchorPosition));
                Assert.That(controller.SelectedContactor, Is.Null);
                Assert.That(Field<List<ElectricalPortView>>("meterPorts"), Does.Contain(port));
            }
            var before = Field<List<ElectricalPortView>>("meterPorts").ToArray();
            Invoke("HandleScenePointerDown", Camera.main, BodyPoint(rear.Picker));
            Assert.That(Field<List<ElectricalPortView>>("meterPorts"), Is.EqualTo(before));
        }
    }
}
