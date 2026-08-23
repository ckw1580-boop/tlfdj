using System.IO;
using NUnit.Framework;

namespace ElectricalSim.Tests
{
    public sealed class OriginalTerminalBoardMapTests
    {
        private static string ConfigurationPath => Path.GetFullPath(
            "Assets/StreamingAssets/" + OriginalTerminalBoardMap.RelativeConfigurationPath);

        [Test]
        public void OriginalConfigurationProvidesAllSeventySixTerminalBindings()
        {
            var map = OriginalTerminalBoardMap.Load(ConfigurationPath);
            Assert.That(map.Bindings.Count, Is.EqualTo(76));
            AssertBinding(map, "a1", "U1", OriginalTerminalZone.ThreePhasePower);
            AssertBinding(map, "a33", "HL1_L", OriginalTerminalZone.Indicator);
            AssertBinding(map, "a83", "SA1_COM1", OriginalTerminalZone.SelectorAndButton);
            AssertBinding(map, "a75", "SB8_COM2", OriginalTerminalZone.SelectorAndButton);
        }

        [Test]
        public void EveryElectricalAnchorHasADistinctUppercaseJumperAnchor()
        {
            var map = OriginalTerminalBoardMap.Load(ConfigurationPath);
            foreach (var binding in map.Bindings)
            {
                Assert.That(binding.AnchorId, Does.StartWith("a"));
                Assert.That(binding.JumperAnchorId, Does.StartWith("A"));
                Assert.That(binding.JumperAnchorId.Substring(1), Is.EqualTo(binding.AnchorId.Substring(1)));
                Assert.That(binding.WireTransformPath, Does.EndWith("/" + binding.AnchorId));
                Assert.That(binding.JumperTransformPath, Does.EndWith("/" + binding.JumperAnchorId));
            }
        }

        private static void AssertBinding(
            OriginalTerminalBoardMap map,
            string anchor,
            string displayName,
            OriginalTerminalZone zone)
        {
            var binding = map.Find(anchor);
            Assert.That(binding, Is.Not.Null, anchor);
            Assert.That(binding.DisplayName, Is.EqualTo(displayName));
            Assert.That(binding.Zone, Is.EqualTo(zone));
            Assert.That(binding.LogicalNode, Is.Not.Empty);
        }
    }
}
