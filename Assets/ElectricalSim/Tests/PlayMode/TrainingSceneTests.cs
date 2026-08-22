using System.Collections;
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
    }
}
