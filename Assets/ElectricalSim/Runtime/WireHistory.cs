using System.Collections.Generic;

namespace ElectricalSim
{
    // Snapshots own their points: later edits must not mutate undo/redo entries.
    public sealed class WireHistory
    {
        private const int Capacity = 64;
        private readonly List<List<WireConnection>> undo = new List<List<WireConnection>>(Capacity);
        private readonly List<List<WireConnection>> redo = new List<List<WireConnection>>(Capacity);
        public int UndoCount => undo.Count;
        public int RedoCount => redo.Count;

        public void Record(IReadOnlyList<WireConnection> current)
        {
            Push(undo, current);
            redo.Clear();
        }

        public bool Undo(IReadOnlyList<WireConnection> current, out List<WireConnection> previous)
            => Transfer(undo, redo, current, out previous);

        public bool Redo(IReadOnlyList<WireConnection> current, out List<WireConnection> next)
            => Transfer(redo, undo, current, out next);

        public void Clear() { undo.Clear(); redo.Clear(); }

        private static bool Transfer(List<List<WireConnection>> source, List<List<WireConnection>> target,
            IReadOnlyList<WireConnection> current, out List<WireConnection> result)
        {
            result = null;
            if (source.Count == 0) return false;
            Push(target, current);
            result = source[source.Count - 1];
            source.RemoveAt(source.Count - 1);
            return true;
        }

        private static void Push(List<List<WireConnection>> stack, IReadOnlyList<WireConnection> wires)
        {
            var snapshot = new List<WireConnection>(wires.Count);
            foreach (var wire in wires) snapshot.Add(CircuitGraph.CloneWire(wire));
            if (stack.Count == Capacity) stack.RemoveAt(0);
            stack.Add(snapshot);
        }
    }
}
