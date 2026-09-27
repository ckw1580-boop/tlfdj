using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

namespace ElectricalSim.Tests
{
    public sealed class WireHistoryTests
    {
        [TestCase(63)] [TestCase(64)] [TestCase(65)] [TestCase(128)]
        public void RetainsNewestSnapshotsInUndoAndRedoOrder(int edits)
        {
            var history = new WireHistory();
            var current = new List<WireConnection> { new WireConnection { Points = new List<Vector3> { Vector3.zero } } };
            for (var i = 0; i < edits; i++)
            {
                history.Record(current);
                current[0].Points[0] = Vector3.right * (i + 1);
            }
            var retained = System.Math.Min(edits, 64);
            for (var i = 1; i <= retained; i++)
            {
                Assert.That(history.Undo(current, out var previous), Is.True);
                current = previous;
                Assert.That(current[0].Points[0].x, Is.EqualTo(edits - i));
            }
            Assert.That(history.Undo(current, out _), Is.False);
            for (var i = 1; i <= retained; i++)
            {
                Assert.That(history.Redo(current, out var next), Is.True);
                current = next;
                Assert.That(current[0].Points[0].x, Is.EqualTo(edits - retained + i));
            }
            Assert.That(history.Redo(current, out _), Is.False);
        }

        [Test]
        public void NewEditClearsRedoAndClearDropsBothStacks()
        {
            var history = new WireHistory();
            var wires = new List<WireConnection> { new WireConnection() };
            history.Record(wires);
            history.Undo(wires, out _);
            history.Record(wires);
            Assert.That(history.Redo(wires, out _), Is.False);
            history.Record(wires);
            history.Undo(wires, out _);
            history.Clear();
            Assert.That(history.UndoCount, Is.Zero);
            Assert.That(history.RedoCount, Is.Zero);
        }
    }
}
