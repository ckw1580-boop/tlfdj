using System.IO;
using System.Linq;
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

        [TestCase("DuanZiPai_3", "G120_l1", "G120_L1", "G120.L1")]
        [TestCase("DuanZiPai_3", "KM1_53no", "KM1_53NO", "KMF.13")]
        [TestCase("DuanZiPai_3", "KM2_61nc", "KM2_61NC", "KM1.61")]
        [TestCase("DuanZiPai_3", "FR1_95nc", "FR1_95NC", "FR.95")]
        [TestCase("DuanZiPai_3", "a60", "KT_A1", "KT.A1")]
        [TestCase("DuanZiPai_3", "a64", "KT_18", "KT.18")]
        [TestCase("DuanZiPai_4", "G120_u2", "G120_U2", "G120.U2")]
        [TestCase("DuanZiPai_4", "KM3_72nc", "KM3_72NC", "KMR.72")]
        [TestCase("DuanZiPai_4", "KM4_14no", "KM4_14NO", "KM2.14")]
        [TestCase("DuanZiPai_4", "FR2_6t3", "FR2_6T3", "FR.T3")]
        [TestCase("DuanZiPai_6", "v_1", "V_1", "POWER.L1")]
        [TestCase("DuanZiPai_6", "n_4", "N_4", "POWER.N")]
        [TestCase("DuanZiPai_7", "a_u1", "A_u1", "M1.U")]
        [TestCase("DuanZiPai_7", "a_w2", "A_w2", "M1.W2")]
        [TestCase("DuanZiPai_7", "b_v1", "B_v1", "M_DOUBLE.V")]
        [TestCase("DuanZiPai_7", "c_w1", "C_w1", "M2.W")]
        [TestCase("DuanZiPai_8", "a_SIGNAL", "A_SIGNAL", "SENSOR_A.SIGNAL")]
        [TestCase("DuanZiPai_8", "diancifa3_GND", "Diancifa3_GND", "SOLENOID3.GND")]
        public void CabinetBoardsMapPhysicalAnchorsToRuntimeNodes(
            string boardId,
            string electricalAnchor,
            string portName,
            string logicalNode)
        {
            var board = OriginalCabinetTerminalBoardMap.Boards.Single(item => item.DeviceId == boardId);
            Assert.That(OriginalCabinetTerminalBoardMap.IsTerminalName(board, electricalAnchor), Is.True);
            Assert.That(OriginalCabinetTerminalBoardMap.GetPortName(board, electricalAnchor), Is.EqualTo(portName));
            Assert.That(OriginalCabinetTerminalBoardMap.ResolveLogicalNode(board, portName), Is.EqualTo(logicalNode));
            var expectedJumperAnchor = board.Kind == OriginalCabinetTerminalBoardKind.Motor ||
                                       board.Kind == OriginalCabinetTerminalBoardKind.PowerDistribution ||
                                       board.Kind == OriginalCabinetTerminalBoardKind.SceneIo
                ? char.ToUpperInvariant(electricalAnchor[0]) + electricalAnchor.Substring(1)
                : electricalAnchor.ToUpperInvariant();
            Assert.That(OriginalCabinetTerminalBoardMap.GetJumperAnchorName(board, electricalAnchor),
                Is.EqualTo(expectedJumperAnchor));
        }

        [Test]
        public void LowerCabinetBoardDefinitionsDeclareReferenceCounts()
        {
            var upper = OriginalCabinetTerminalBoardMap.Boards.Single(item => item.DeviceId == "DuanZiPai_3");
            var lower = OriginalCabinetTerminalBoardMap.Boards.Single(item => item.DeviceId == "DuanZiPai_4");
            var power = OriginalCabinetTerminalBoardMap.Boards.Single(item => item.DeviceId == "DuanZiPai_6");
            var motor = OriginalCabinetTerminalBoardMap.Boards.Single(item => item.DeviceId == "DuanZiPai_7");
            var sceneIo = OriginalCabinetTerminalBoardMap.Boards.Single(item => item.DeviceId == "DuanZiPai_8");
            Assert.That(upper.ExpectedPortCount, Is.EqualTo(64));
            Assert.That(lower.ExpectedPortCount, Is.EqualTo(48));
            Assert.That(power.ExpectedPortCount, Is.EqualTo(8));
            Assert.That(motor.ExpectedPortCount, Is.EqualTo(18));
            Assert.That(sceneIo.ExpectedPortCount, Is.EqualTo(18));
            Assert.That(upper.UsesSeparateJumperAnchors, Is.True);
            Assert.That(lower.UsesSeparateJumperAnchors, Is.True);
            Assert.That(power.UsesSeparateJumperAnchors, Is.True);
            Assert.That(motor.UsesSeparateJumperAnchors, Is.True);
            Assert.That(sceneIo.UsesSeparateJumperAnchors, Is.True);
            Assert.That(upper.AlwaysUsesElectricalAnchor, Is.True);
            Assert.That(upper.AlwaysUsesJumperAnchor, Is.False);
            Assert.That(lower.AlwaysUsesElectricalAnchor, Is.False);
            Assert.That(lower.AlwaysUsesJumperAnchor, Is.True);
            Assert.That(motor.AlwaysUsesJumperAnchor, Is.False);
            Assert.That(sceneIo.AlwaysUsesElectricalAnchor, Is.True);
            Assert.That(sceneIo.AlwaysUsesJumperAnchor, Is.False);
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
