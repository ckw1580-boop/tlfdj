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
            Assert.That(map.Find("a1").LogicalNode, Is.EqualTo("POWER.L1"));
            Assert.That(map.Find("a33").LogicalNode, Is.EqualTo("HL1.L"));
            Assert.That(map.Find("a83").LogicalNode, Is.EqualTo("SA1.COM1"));
            Assert.That(map.Find("a75").LogicalNode, Is.EqualTo("SB8.COM2"));
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

        [TestCase("PLC_1_M0.0", "PLC_1.M0.0")]
        [TestCase("PLC_2_Q1.1", "PLC_2.Q1.1")]
        [TestCase("KA6_8", "KA6.8")]
        [TestCase("KA4_14", "KA4.14")]
        public void CabinetTerminalNamesMapToLogicalDevicePorts(string terminalName, string logicalNode)
        {
            Assert.That(OriginalCabinetTerminalBoardMap.IsTerminalName(terminalName), Is.True);
            Assert.That(OriginalCabinetTerminalBoardMap.ResolveLogicalNode(terminalName), Is.EqualTo(logicalNode));
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
