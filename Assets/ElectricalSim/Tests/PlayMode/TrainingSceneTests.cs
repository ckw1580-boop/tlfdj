using System.Collections;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace ElectricalSim.Tests
{
    public sealed class TrainingSceneTests
    {
        [UnitySetUp]
        public IEnumerator LoadTrainingScene()
        {
            SceneManager.LoadScene("ElectricalTraining", LoadSceneMode.Single);
            yield return null;
            yield return null;
        }

        [UnityTest]
        public IEnumerator SceneBootstrapsControllerCameraDevicesAndHud()
        {
            Assert.That(Object.FindObjectOfType<SimulationController>(), Is.Not.Null);
            Assert.That(Object.FindObjectOfType<TrainingCameraController>(), Is.Not.Null);
            Assert.That(Object.FindObjectsOfType<ElectricalDeviceView>().Length, Is.GreaterThanOrEqualTo(20));
            Assert.That(GameObject.Find("Simulation HUD"), Is.Not.Null);
            yield return null;
        }

        [UnityTest]
        public IEnumerator EveryModeCanBeSelected()
        {
            var controller = Object.FindObjectOfType<SimulationController>();
            foreach (SimulationMode mode in System.Enum.GetValues(typeof(SimulationMode)))
            {
                controller.SetMode(mode);
                Assert.That(controller.Mode, Is.EqualTo(mode));
                yield return null;
            }
        }

        [UnityTest]
        public IEnumerator ReferenceWiringPassesCurrentTaskTopology()
        {
            var controller = Object.FindObjectOfType<SimulationController>();
            controller.LoadReferenceWiring();
            yield return null;
            var result = CircuitTaskEvaluator.EvaluateTopology(controller.Graph, controller.CurrentTask);
            Assert.That(result.Passed, Is.True, result.Summary());
            Assert.That(controller.Graph.Wires.Count, Is.GreaterThan(10));
        }

        [UnityTest]
        public IEnumerator OriginalModelConnectionPointsUseTerminalTransforms()
        {
            AssertPortMatchesTerminal("QF", "L1", "1");
            AssertPortMatchesTerminal("QF", "T3", "6");
            AssertPortMatchesTerminal("KM1", "L1", "1L1");
            AssertPortMatchesTerminal("KM1", "T2", "4T2");
            AssertPortMatchesTerminal("KM1", "A1", "A1");
            AssertPortMatchesTerminal("KM1", "13", "13NO");
            AssertPortMatchesTerminal("FR", "95", "95NC");
            AssertPortMatchesTerminal("SB1", "COM", "COM1");
            AssertPortMatchesTerminal("SB1", "NO", "NO1");
            AssertPortMatchesTerminal("M1", "U", "U1");
            yield return null;
        }

        private static void AssertPortMatchesTerminal(string deviceId, string portName, string terminalName)
        {
            var device = Object.FindObjectsOfType<ElectricalDeviceView>()
                .Single(view => view.Runtime.DeviceId == deviceId);
            var port = device.Ports.Single(view => view.PortName == portName);
            var environment = GameObject.Find("OriginalLabEnvironment");
            var environmentNames = deviceId == "FR"
                ? new[] { "FR1_" + terminalName, "FR_" + terminalName }
                : new[] { deviceId + "_" + terminalName };
            var terminal = environment == null ? null : environment.GetComponentsInChildren<Transform>(true)
                .FirstOrDefault(transform => environmentNames.Any(name =>
                    string.Equals(transform.name, name, System.StringComparison.OrdinalIgnoreCase)));
            if (terminal == null)
                terminal = device.GetComponentsInChildren<Transform>(true)
                    .First(transform => string.Equals(transform.name, terminalName, System.StringComparison.OrdinalIgnoreCase));
            Assert.That(Vector3.Distance(port.transform.position, terminal.position), Is.LessThan(0.0005f),
                $"{deviceId}.{portName} must be located on original terminal {terminalName}");
        }
    }
}
