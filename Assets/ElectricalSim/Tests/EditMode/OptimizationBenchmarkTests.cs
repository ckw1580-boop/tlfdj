using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using NUnit.Framework;
using Unity.Profiling;
using UnityEngine;
using Object = UnityEngine.Object;

namespace ElectricalSim.Tests
{
    // Explicit component benchmark: the identical fixture is run before and after optimization.
    public sealed class OptimizationBenchmarkTests
    {
        [Test, Explicit("Run separately to collect comparable performance samples.")]
        public void MeasureWireAndSolverWorkloads()
        {
            Assert.That(AllocationEvents(() => GC.KeepAlive(new byte[4096])), Is.GreaterThan(0),
                "GC allocation recorder must detect a known allocation before measuring workloads.");
            var args = Environment.GetCommandLineArgs();
            var option = Array.IndexOf(args, "-optimizationBenchmarkReport");
            var report = option >= 0 ? args[option + 1] : "Build/Reports/optimization-performance.csv";
            Directory.CreateDirectory(Path.GetDirectoryName(report));
            File.WriteAllText(report, "wires,workload,repeat,milliseconds_per_tick,allocation_events_per_tick\n");
            var material = new Material(Shader.Find("Sprites/Default"));
            try
            {
                foreach (var count in new[] { 50, 200, 500 })
                {
                    var root = new GameObject("BenchmarkWires");
                    try
                    {
                        var graph = new CircuitGraph();
                        graph.RegisterDevice(ElectricalDeviceRuntime.CreatePowerSource());
                        graph.RegisterDevice(ElectricalDeviceRuntime.CreateMotor("M1"));
                        graph.AddWire("POWER.L1", "M1.U", Color.red);
                        graph.AddWire("POWER.L2", "M1.V", Color.yellow);
                        graph.AddWire("POWER.L3", "M1.W", Color.blue);
                        var views = new List<ElectricalWireView>();
                        var surface = new WireSurfacePlane(Vector3.zero, Vector3.forward, 0.003f);
                        for (var i = 0; i < count; i++)
                        {
                            var n = i;
                            var wire = graph.AddWire("NET." + i, "NET." + (i + 1), Color.red, "ElectricalWire");
                            wire.Points.Add(new Vector3(i * 0.001f, 0.3f, 0));
                            var obj = new GameObject("Wire");
                            obj.transform.SetParent(root.transform);
                            var view = obj.AddComponent<ElectricalWireView>();
                            view.Initialize(wire, port => port == wire.StartPort
                                ? new Vector3(n * 0.001f, 0, 0) : new Vector3(n * 0.001f + 0.2f, 0.5f, 0), material, surface);
                            views.Add(view);
                        }
                        var step = 0;
                        Measure(report, count, "stationary", () => { foreach (var view in views) view.Refresh(); });
                        Measure(report, count, "editing", () =>
                        {
                            var view = views[step++ % count];
                            view.Connection.Points[0] += Vector3.right * 0.00001f;
                            foreach (var item in views) item.Refresh();
                        });
                        Measure(report, count, "simulation", () => graph.Solve(0.02f));
                    }
                    finally { Object.DestroyImmediate(root); }
                }
            }
            finally { Object.DestroyImmediate(material); }
        }

        private static void Measure(string report, int count, string workload, Action tick)
        {
            const int iterations = 60;
            for (var i = 0; i < 10; i++) tick();
            for (var repeat = 0; repeat < 3; repeat++)
            {
                GC.Collect();
                var watch = new Stopwatch();
                watch.Start();
                for (var i = 0; i < iterations; i++) tick();
                watch.Stop();
                var allocations = AllocationEvents(tick);
                File.AppendAllText(report, FormattableString.Invariant(
                    $"{count},{workload},{repeat},{watch.Elapsed.TotalMilliseconds / iterations:F6},{allocations}\n"));
            }
        }

        private static int AllocationEvents(Action action)
        {
            using (var recorder = ProfilerRecorder.StartNew(ProfilerCategory.Internal, "GC.Alloc", 100000,
                ProfilerRecorderOptions.CollectOnlyOnCurrentThread))
            {
                action();
                recorder.Stop();
                Assert.That(recorder.Valid, Is.True);
                Assert.That(recorder.Count, Is.LessThan(recorder.Capacity), "Allocation sample buffer overflow.");
                return recorder.Count;
            }
        }
    }
}
