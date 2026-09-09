using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace ElectricalSim
{
    public sealed class MotorBindingDefinition
    {
        public string Id { get; }
        public string Nut { get; }
        public string Label => Id == "M_DOUBLE" ? "双速电机" : "三相电机 " + Id;
        public string ModelPath => "Bench/ElectricBench/Nuts/" + Nut + "/" +
            (Id == "M_DOUBLE" ? "ShuangSuDianJi" : "SanXiangShuLongDianJi");
        public Vector3 FallbackPosition { get; }

        private MotorBindingDefinition(string id, string nut, float x)
        { Id = id; Nut = nut; FallbackPosition = new Vector3(x, 0.25f, -0.45f); }

        public static readonly IReadOnlyList<MotorBindingDefinition> All = new[]
        {
            new MotorBindingDefinition("M1", "38", -0.55f),
            new MotorBindingDefinition("M2", "49", 0.45f),
            new MotorBindingDefinition("M3", "107", 1.15f),
            new MotorBindingDefinition("M_DOUBLE", "118", 0f)
        };

        public static MotorBindingDefinition Find(string id) => All.FirstOrDefault(m => m.Id == id);

        // Use the actual terminal plane, oriented away from the model body.
        public static Vector3 TerminalOutward(IReadOnlyList<Vector3> terminals, Vector3 bodyCenter)
        {
            var normal = Vector3.zero;
            var center = Vector3.zero;
            foreach (var point in terminals) center += point;
            center /= terminals.Count;
            for (var i = 1; i < terminals.Count; i++)
                for (var j = i + 1; j < terminals.Count; j++)
                {
                    var candidate = Vector3.Cross(terminals[i] - terminals[0], terminals[j] - terminals[0]);
                    if (candidate.sqrMagnitude > normal.sqrMagnitude) normal = candidate;
                }
            if (normal.sqrMagnitude < 1e-12f)
                throw new System.InvalidOperationException("Motor terminals must define a plane.");
            return (Vector3.Dot(normal, center - bodyCenter) < 0 ? -normal : normal).normalized;
        }
    }
}
