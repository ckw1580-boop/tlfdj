using System;
using System.Collections;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using Unity.Profiling;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace ElectricalSim.Tests
{
    public sealed class OptimizationSceneBenchmarkTests
    {
        [UnityTest, Explicit("Controlled before/after benchmark, not a timing assertion in regular CI.")]
        public IEnumerator MeasureTrainingScene()
        {
            var args = Environment.GetCommandLineArgs();
            var option = Array.IndexOf(args, "-optimizationBenchmarkReport");
            var report = option >= 0 ? args[option + 1] : "Build/Reports/optimization-scene-performance.csv";
            Directory.CreateDirectory(Path.GetDirectoryName(report));
            File.WriteAllText(report, "wires,workload,repeat,milliseconds_per_tick,managed_heap_delta_bytes_per_tick,allocation_events_per_tick\n");
            var calibration = MeasureAllocations(() => GC.KeepAlive(new byte[4096]));
            Assert.That(calibration.bytes, Is.GreaterThanOrEqualTo(4096));
            Assert.That(calibration.events, Is.GreaterThan(0));
            foreach (var count in new[] { 50, 200, 500 })
            {
                SceneManager.LoadScene("ElectricalTraining", LoadSceneMode.Single);
                yield return null;
                yield return null;
                Screen.SetResolution(1920, 1080, false);
                var controller = Object.FindObjectOfType<SimulationController>();
                controller.SetMode(SimulationMode.Wiring);
                controller.SetWireStyle(Color.red, 0.01f, "ElectricalWire");
                Object.FindObjectOfType<TrainingCameraController>().SetWiringView();
                for (var i = 0; i < count; i++)
                {
                    var wire = new WireConnection
                    {
                        Id = "benchmark-" + i, StartPort = "QF.T1", EndPort = "KM1.L1",
                        LineType = "ElectricalWire", FaultSide = false
                    };
                    wire.Points.Add(new Vector3(.05f + (i % 20) * .002f, 1.1f + (i / 20) * .002f, -1));
                    controller.Graph.AddWire(wire);
                }
                typeof(SimulationController).GetMethod("RefreshWireViews", BindingFlags.NonPublic | BindingFlags.Instance)
                    .Invoke(controller, null);
                yield return null;
                var views = Object.FindObjectsOfType<ElectricalWireView>().OrderBy(v => v.Connection.Id).ToArray();
                Assert.That(views.Length, Is.EqualTo(count));
                // Exclude unrelated UI and editor work from the timed component region.
                Action stationary = () => { foreach (var view in views) view.Refresh(); };
                var step = 0;
                Action editing = () =>
                {
                    views[step++ % count].Connection.Points[0] += Vector3.right * .00001f;
                    stationary();
                };
                Action simulation = () => controller.Graph.Solve(.02f);
                foreach (var item in new[] { ("stationary", stationary), ("editing", editing), ("simulation", simulation) })
                {
                    for (var i = 0; i < 10; i++) item.Item2();
                    for (var repeat = 0; repeat < 3; repeat++)
                    {
                        yield return null;
                        GC.Collect();
                        var watch = Stopwatch.StartNew();
                        for (var i = 0; i < 60; i++) item.Item2();
                        watch.Stop();
                        var allocations = MeasureAllocations(item.Item2);
                        File.AppendAllText(report, FormattableString.Invariant(
                            $"{count},{item.Item1},{repeat},{watch.Elapsed.TotalMilliseconds / 60:F6},{allocations.bytes},{allocations.events}\n"));
                    }
                }
            }
        }

        // Mono's per-thread byte API returns zero in this editor. Measure retained plus
        // uncollected heap growth for one synchronous tick, and independently count GC.Alloc events.
        // Heap deltas include possible editor-thread noise; report them as estimates, not exact bytes.
        private static (long bytes, int events) MeasureAllocations(Action action)
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
            using (var recorder = ProfilerRecorder.StartNew(ProfilerCategory.Internal, "GC.Alloc", 100000,
                ProfilerRecorderOptions.CollectOnlyOnCurrentThread))
            {
                var before = UnityEngine.Profiling.Profiler.GetMonoUsedSizeLong();
                action();
                var after = UnityEngine.Profiling.Profiler.GetMonoUsedSizeLong();
                recorder.Stop();
                Assert.That(recorder.Valid, Is.True);
                Assert.That(recorder.Count, Is.LessThan(recorder.Capacity));
                return (after - before, recorder.Count);
            }
        }
    }
}
