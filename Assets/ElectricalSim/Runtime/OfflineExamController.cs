using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;

namespace ElectricalSim
{
    public sealed class ExamEvaluationResult
    {
        public float Score;
        public float MaximumScore;
        public readonly List<string> Passed = new List<string>();
        public readonly List<string> Failed = new List<string>();
    }

    public sealed class OfflineExamController : MonoBehaviour
    {
        private IReadOnlyList<ExamPackageDefinition> packages = Array.Empty<ExamPackageDefinition>();
        private readonly HashSet<string> clearedFaults = new HashSet<string>();
        private LocalSessionStore store;
        private ExamPackageDefinition activePackage;
        private ExamSessionRecord activeSession;

        public IReadOnlyList<ExamPackageDefinition> Packages => packages;
        public ExamPackageDefinition ActivePackage => activePackage;
        public ExamSessionRecord ActiveSession => activeSession;
        public bool IsRunning => activeSession != null && !activeSession.Submitted && activeSession.RemainingSeconds > 0d;

        private void Awake()
        {
            store = new LocalSessionStore();
            packages = OfflineExamCatalog.LoadAll();
        }

        private void Update()
        {
            if (!IsRunning) return;
            activeSession.RemainingSeconds = Math.Max(0d, activeSession.RemainingSeconds - Time.unscaledDeltaTime);
        }

        public bool Begin(string packageId)
        {
            activePackage = packages.FirstOrDefault(item => string.Equals(item.PackageId, packageId, StringComparison.OrdinalIgnoreCase));
            if (activePackage == null) return false;
            clearedFaults.Clear();
            activeSession = new ExamSessionRecord
            {
                PackageId = activePackage.PackageId,
                StartedUtc = DateTime.UtcNow.ToString("O"),
                RemainingSeconds = activePackage.Duration.TotalSeconds
            };
            return true;
        }

        public bool LoadFaultWiring(CircuitGraph graph)
        {
            if (activePackage == null || graph == null) return false;
            var path = Path.Combine(OfflineExamCatalog.RootDirectory, activePackage.PackageId, "FaultWiring.cc3d");
            if (!File.Exists(path)) return false;
            Cc3dCircuitAdapter.ImportWires(Cc3dSerializer.Load(path), graph);
            foreach (var fault in activePackage.Faults.Where(item => item.OpenCircuit))
            {
                var wire = graph.Wires.FirstOrDefault(item => SameEndpoints(item, fault.PortA, fault.PortB));
                if (wire != null) graph.RemoveWire(wire.Id);
            }
            return true;
        }

        public bool ClearFault(string faultId, CircuitGraph graph)
        {
            if (activePackage == null || graph == null) return false;
            var fault = activePackage.Faults.FirstOrDefault(item => item.Id == faultId);
            if (fault == null || string.IsNullOrEmpty(fault.PortA) || string.IsNullOrEmpty(fault.PortB)) return false;
            graph.AddWire(fault.PortA, fault.PortB, Color.red, "FaultRepair");
            clearedFaults.Add(fault.Id);
            return true;
        }

        public ExamEvaluationResult Submit(CircuitGraph graph)
        {
            var result = Evaluate(graph);
            if (activeSession == null) return result;
            activeSession.Score = result.Score;
            activeSession.Submitted = true;
            activeSession.CompletedUtc = DateTime.UtcNow.ToString("O");
            activeSession.CompletedRuleIds = new List<string>(result.Passed);
            activeSession.ClearedFaultIds = clearedFaults.ToList();
            store.SaveExam(activeSession);
            return result;
        }

        public ExamEvaluationResult Evaluate(CircuitGraph graph)
        {
            var result = new ExamEvaluationResult();
            if (activePackage == null || graph == null) return result;
            foreach (var rule in activePackage.WiringRules.Concat(activePackage.DebugRules))
            {
                result.MaximumScore += rule.Score;
                var passed = rule.RequiredConnections.Count == 0 || rule.RequiredConnections.All(pair => graph.AreConnectedByWiring(pair.A, pair.B));
                if (passed)
                {
                    result.Score += rule.Score;
                    result.Passed.Add(rule.Id);
                }
                else result.Failed.Add(rule.Id);
            }
            foreach (var fault in activePackage.Faults)
            {
                result.MaximumScore += fault.Score;
                if (clearedFaults.Contains(fault.Id) || graph.AreConnectedByWiring(fault.PortA, fault.PortB))
                {
                    result.Score += fault.Score;
                    result.Passed.Add(fault.Id);
                }
                else result.Failed.Add(fault.Id);
            }
            return result;
        }

        private static bool SameEndpoints(WireConnection wire, string a, string b)
            => (wire.StartPort == a && wire.EndPort == b) || (wire.StartPort == b && wire.EndPort == a);
    }
}
