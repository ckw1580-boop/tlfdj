using NUnit.Framework;

namespace ElectricalSim.Tests
{
    public sealed class PipeFlowStateTests
    {
        [Test]
        public void ClosedValveFillsUpstreamThenStopsAndOpeningContinuesFromValve()
        {
            var state = new PipeFlowState(4, 2);
            state.Advance(1, 1, false, true);
            Assert.That(state.Front, Is.EqualTo(1));
            state.Advance(5, 1, false, true);
            Assert.That(state.Front, Is.EqualTo(2));
            Assert.That(state.IsBlocked, Is.True);
            Assert.That(state.DownstreamOpacity, Is.Zero);
            var phase = state.UpstreamPhase;
            state.Advance(2, 1, false, true);
            Assert.That(state.UpstreamPhase, Is.EqualTo(phase));
            state.Advance(0.5f, 1, true, true);
            Assert.That(state.Front, Is.EqualTo(2.5f));
            Assert.That(state.DownstreamOpacity, Is.EqualTo(1));
            Assert.That(state.IsBlocked, Is.False);
        }
        [Test]
        public void ClosingValveImmediatelyStopsMovementAndOnlyDownstreamFades()
        {
            var state = new PipeFlowState(4, 2);
            state.Advance(4, 1, true, true);
            var up = state.UpstreamPhase; var down = state.DownstreamPhase;
            state.Advance(0.25f, 1, false, true);
            Assert.That(state.UpstreamOpacity, Is.EqualTo(1));
            Assert.That(state.DownstreamOpacity, Is.EqualTo(0.5f));
            Assert.That(state.UpstreamPhase, Is.EqualTo(up));
            Assert.That(state.DownstreamPhase, Is.EqualTo(down));
            state.Advance(0.25f, 1, false, true);
            Assert.That(state.Front, Is.EqualTo(2));
            Assert.That(state.DownstreamOpacity, Is.Zero);
        }
        [TestCase(0, true)]
        [TestCase(-1, true)]
        [TestCase(1, false)]
        public void StopReverseOrLeavingSimulationFadesToEmpty(float speed, bool simulating)
        {
            var state = new PipeFlowState(4, 2);
            state.Advance(4, 1, true, true);
            var phase = state.UpstreamPhase;
            state.Advance(0.25f, speed, true, simulating);
            Assert.That(state.UpstreamOpacity, Is.EqualTo(0.5f));
            Assert.That(state.DownstreamOpacity, Is.EqualTo(0.5f));
            Assert.That(state.UpstreamPhase, Is.EqualTo(phase));
            state.Advance(0.25f, speed, true, simulating);
            Assert.That(state.Front, Is.Zero);
            Assert.That(state.UpstreamOpacity, Is.Zero);
            Assert.That(state.DownstreamOpacity, Is.Zero);
        }
        [Test]
        public void RestartAndReopenDuringFadeKeepCurrentFront()
        {
            var state = new PipeFlowState(4, 2);
            state.Advance(3, 1, true, true);
            state.Advance(0.2f, 0, true, true);
            state.Advance(0.1f, 1, true, true);
            Assert.That(state.Front, Is.EqualTo(3.1f).Within(1e-5));
            state.Advance(0.2f, 1, false, true);
            state.Advance(0.1f, 1, true, true);
            Assert.That(state.Front, Is.EqualTo(3.2f).Within(1e-5));
            Assert.That(state.DownstreamOpacity, Is.EqualTo(1));
        }
        [Test]
        public void FileOperationFreezesFrontPhaseAndFadeAndResetClearsEverything()
        {
            var state = new PipeFlowState(4, 2);
            state.Advance(3, 1, true, true);
            var phase = state.UpstreamPhase;
            state.Advance(10, 0, false, false, true);
            Assert.That(state.Front, Is.EqualTo(3));
            Assert.That(state.UpstreamPhase, Is.EqualTo(phase));
            Assert.That(state.DownstreamOpacity, Is.EqualTo(1));
            state.Reset();
            Assert.That(state.Front, Is.Zero);
            Assert.That(state.UpstreamPhase, Is.Zero);
            Assert.That(state.DownstreamPhase, Is.Zero);
            Assert.That(state.UpstreamOpacity, Is.Zero);
            Assert.That(state.DownstreamOpacity, Is.Zero);
        }
        [TestCase(30)]
        [TestCase(60)]
        [TestCase(120)]
        public void FrontTravelUsesElapsedTimeAndActualSpeed(int fps)
        {
            var state = new PipeFlowState(4, 2);
            for (var i = 0; i < fps * 3; i++) state.Advance(1f / fps, 0.5f, true, true);
            Assert.That(state.Front, Is.EqualTo(1.5f).Within(0.0001f));
        }
    }
}
