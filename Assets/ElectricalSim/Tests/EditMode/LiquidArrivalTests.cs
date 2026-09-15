using NUnit.Framework;
using UnityEngine;

namespace ElectricalSim.Tests
{
    public sealed class LiquidArrivalTests
    {
        private static LiquidSimulationRuntime WithPipe(float initial = 0)
        {
            var route = new LiquidPipeRoute { radius = 0.02f, points = new[] { new Vector3(0, 1, 0), new Vector3(1, 1, 0) }, valve = new Vector3(0.5f, 1, 0) };
            var liquid = new LiquidSimulationRuntime();
            liquid.InitializeTransport(new[] { route, route, route }, 0, 1, 0);
            liquid.Configure(new LiquidConfiguration { InitialLevelPercent = initial }, true);
            return liquid;
        }
        [Test]
        public void PipeThenFallingJetMustReachBottomAndOnlyRemainingTickTimeAccumulates()
        {
            var liquid = WithPipe();
            liquid.Advance(1, 1450, 0, true, false, false);
            Assert.That(liquid.Streams[0].Pipe.Front, Is.EqualTo(0.8f).Within(1e-6));
            Assert.That(liquid.Streams[0].JetFront, Is.Zero);
            liquid.Advance(1.4, 1450, 0, true, false, false);
            Assert.That(liquid.Streams[0].JetFront, Is.EqualTo(0.92f).Within(1e-6));
            Assert.That(liquid.Level, Is.Zero);
            Assert.That(liquid.Pump1Transporting, Is.True);
            liquid.Advance(0.2, 1450, 0, true, false, false);
            Assert.That(liquid.Level, Is.EqualTo(0.1 / 30).Within(1e-7));
            Assert.That(liquid.Pump1Flow, Is.EqualTo(0.5 / 30).Within(1e-6));
            Assert.That(liquid.Contents, Is.EqualTo(LiquidContents.A));
        }
        [Test]
        public void ExistingLiquidIsTheContactSurface()
        {
            var liquid = WithPipe(50);
            liquid.Advance(1.8, 1450, 0, true, false, false);
            Assert.That(liquid.Level, Is.EqualTo(0.5));
            liquid.Advance(0.175, 1450, 0, true, false, false);
            Assert.That(liquid.Level, Is.EqualTo(0.5 + 0.1 / 30).Within(1e-7));
        }
        [Test]
        public void ClosedValveBlocksAndFadingJetCannotContributeVolume()
        {
            var liquid = WithPipe();
            liquid.Advance(10, 1450, 0, false, false, false);
            Assert.That(liquid.Streams[0].Pipe.IsBlocked, Is.True);
            Assert.That(liquid.Level, Is.Zero);
            liquid.Advance(1.8, 1450, 0, true, false, false);
            Assert.That(liquid.Streams[0].JetFront, Is.GreaterThan(0));
            Assert.That(liquid.Level, Is.Zero);
            liquid.Advance(0.25, 1450, 0, false, false, false);
            Assert.That(liquid.Streams[0].Pipe.DownstreamOpacity, Is.EqualTo(0.5));
            Assert.That(liquid.Level, Is.Zero);
            liquid.Advance(0.25, 0, 0, false, false, false);
            Assert.That(liquid.Streams[0].JetFront, Is.Zero);
            Assert.That(liquid.Level, Is.Zero);
        }
        [TestCase(false)]
        [TestCase(true)]
        public void SecondColourBlendsOnlyAfterItArrivesAndEmptyTankForgetsTheMixture(bool bFirst)
        {
            var liquid = new LiquidSimulationRuntime();
            liquid.Advance(3, bFirst ? 0 : 1450, bFirst ? 1450 : 0, true, true, false);
            var firstColor = bFirst ? LiquidTankView.LiquidBColor : LiquidTankView.LiquidAColor;
            Assert.That(liquid.CurrentColor, Is.EqualTo(firstColor));
            liquid.Advance(0.25, bFirst ? 1450 : 0, bFirst ? 0 : 1450, true, true, false);
            Assert.That(liquid.MixProgress, Is.EqualTo(0.25));
            Assert.That(liquid.CurrentColor, Is.EqualTo(Color.Lerp(firstColor, LiquidTankView.MixedColor, 0.25f)));
            liquid.Advance(0.75, 0, 0, false, false, false);
            Assert.That(liquid.CurrentColor, Is.EqualTo(LiquidTankView.MixedColor));
            liquid.Advance(10, 0, 0, false, false, true);
            Assert.That(liquid.Level, Is.Zero); Assert.That(liquid.MixProgress, Is.Zero);
            liquid.Advance(0.2, bFirst ? 1450 : 0, bFirst ? 0 : 1450, true, true, false);
            Assert.That(liquid.CurrentColor, Is.EqualTo(bFirst ? LiquidTankView.LiquidAColor : LiquidTankView.LiquidBColor));
        }
        [Test]
        public void InitialLevelIsAlreadyMixedAndDrainAndOverflowRemoveBothComponentsProportionally()
        {
            var liquid = WithPipe(50);
            Assert.That(liquid.VolumeA, Is.EqualTo(0.25)); Assert.That(liquid.VolumeB, Is.EqualTo(0.25));
            Assert.That(liquid.MixProgress, Is.EqualTo(1));
            liquid = new LiquidSimulationRuntime();
            liquid.Advance(30, 1450, 725, true, true, false);
            Assert.That(liquid.OverflowVolume, Is.EqualTo(0.5).Within(1e-9));
            Assert.That(liquid.VolumeA / liquid.VolumeB, Is.EqualTo(2).Within(1e-9));
            liquid.Advance(5, 0, 0, false, false, true);
            Assert.That(liquid.Level, Is.EqualTo(0.75).Within(1e-9));
            Assert.That(liquid.VolumeA / liquid.VolumeB, Is.EqualTo(2).Within(1e-9));
            Assert.That(liquid.DischargeColor, Is.EqualTo(LiquidTankView.MixedColor));
        }
        [TestCase(30, 1450)]
        [TestCase(60, 1450)]
        [TestCase(120, 1450)]
        [TestCase(60, 725)]
        public void ArrivalAndAccumulationFollowElapsedTimeAndActualSpeed(int fps, float rpm)
        {
            var liquid = WithPipe();
            for (var i = 0; i < fps * 6; i++) liquid.Advance(1d / fps, rpm, 0, true, false, false);
            var expected = (6 - 2 / (0.8 * rpm / 1450)) * rpm / 1450 / 30;
            Assert.That(liquid.Level, Is.EqualTo(expected).Within(1e-5));
        }
        [Test]
        public void SimultaneousContactStartsOneSecondMixAndPauseFreezesItsProgress()
        {
            var liquid = WithPipe();
            liquid.Advance(2.4, 1450, 1450, true, true, false);
            Assert.That(liquid.Contents, Is.EqualTo(LiquidContents.Empty));
            liquid.Advance(0.2, 1450, 1450, true, true, false);
            Assert.That(liquid.VolumeA, Is.EqualTo(0.1 / 30).Within(1e-7));
            Assert.That(liquid.VolumeB, Is.EqualTo(liquid.VolumeA));
            Assert.That(liquid.MixProgress, Is.EqualTo(0.1).Within(1e-6));
            var color = liquid.CurrentColor;
            liquid.AdvanceIdle(0.25);
            Assert.That(liquid.CurrentColor, Is.EqualTo(color));
            liquid.Advance(0.91, 0, 0, false, false, false);
            Assert.That(liquid.CurrentColor, Is.EqualTo(LiquidTankView.MixedColor));
        }
        [Test]
        public void PauseDoesNotMixAndRestartBeforeFadeCompletesKeepsTheFallingFront()
        {
            var liquid = WithPipe();
            liquid.Advance(2, 1450, 0, true, false, false);
            var jet = liquid.Streams[0].JetFront;
            liquid.AdvanceIdle(0.2);
            Assert.That(liquid.Streams[0].JetFront, Is.EqualTo(jet));
            liquid.Advance(0.6, 1450, 0, true, false, false);
            Assert.That(liquid.Level, Is.EqualTo(0.1 / 30).Within(1e-6));
            liquid.AdvanceIdle(0.5);
            Assert.That(liquid.Streams[0].Pipe.Front, Is.Zero);
            Assert.That(liquid.Streams[0].JetFront, Is.Zero);
        }
    }
}
