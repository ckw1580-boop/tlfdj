using System;
using System.Collections.Generic;
using System.Linq;

namespace ElectricalSim
{
    [Serializable]
    public sealed class CircuitTaskSpec
    {
        public string Id = string.Empty;
        public string Name = string.Empty;
        public string Description = string.Empty;
        public List<PortPair> RequiredConnections = new List<PortPair>();
        public List<PortPair> ForbiddenConnections = new List<PortPair>();
        public List<TaskActionStep> Actions = new List<TaskActionStep>();
    }

    public static class CircuitTaskCatalog
    {
        public static IReadOnlyList<CircuitTaskSpec> CreateAll()
        {
            return new[]
            {
                PointControl(),
                SingleStart(),
                SelfLock(),
                OverloadSelfLock(),
                ForwardReverse(),
                MultiLocation(),
                TimedControl(),
                SequentialStart(),
                ReverseBraking(),
                EnergyBraking()
            };
        }

        private static CircuitTaskSpec PointControl()
        {
            var task = Base("point", "三相异步电动机点动控制仿真", "按住启动按钮时电机运行，松开后停止。", "KM1");
            Add(task, "POWER.L1", "SB1.COM", "SB1.NO", "KM1.A1", "KM1.A2", "POWER.N");
            task.Actions.Add(Action("SB1", true, "M1", MotorDirection.Forward));
            task.Actions.Add(Action("SB1", false, "M1", MotorDirection.Stopped));
            return task;
        }

        private static CircuitTaskSpec SingleStart()
        {
            var task = Base("single-start", "三相异步电动机单点启动控制仿真", "单处启动、停止控制。", "KM1");
            Add(task, "POWER.L1", "SB0.COM", "SB0.NC", "SB1.COM", "SB1.NO", "KM1.A1", "KM1.A2", "POWER.N");
            task.Actions.Add(Action("SB1", true, "M1", MotorDirection.Forward));
            return task;
        }

        private static CircuitTaskSpec SelfLock()
        {
            var task = Base("self-lock", "三相异步电动机自锁控制仿真", "启动后由接触器辅助触点维持线圈通电。", "KM1");
            AddSelfLockControl(task, "KM1", "SB0", "SB1");
            task.Actions.Add(Action("SB1", true, "M1", MotorDirection.Forward));
            task.Actions.Add(Action("SB1", false, "M1", MotorDirection.Forward));
            task.Actions.Add(Action("SB0", true, "M1", MotorDirection.Stopped));
            return task;
        }

        private static CircuitTaskSpec OverloadSelfLock()
        {
            var task = Base("overload", "三相异步电动机过载保护自锁控制仿真", "热继电器常闭保护触点串入接触器线圈回路。", "KM1");
            Add(task, "POWER.L1", "SB0.COM", "SB0.NC", "SB1.COM", "SB1.NO", "KM1.A1", "KM1.A2", "FR.95", "FR.96", "POWER.N");
            Add(task, "SB1.COM", "KM1.13", "SB1.NO", "KM1.14");
            task.Actions.Add(Action("SB1", true, "M1", MotorDirection.Forward));
            task.Actions.Add(Action("FR", true, "M1", MotorDirection.Stopped));
            return task;
        }

        private static CircuitTaskSpec ForwardReverse()
        {
            var task = Base("forward-reverse", "三相异步电动机联锁正反转控制仿真", "两个接触器交换任意两相，并通过常闭辅助触点互锁。", "KMF");
            AddReversePower(task);
            Add(task,
                "POWER.L1", "SB0.COM", "SB0.NC", "SBF.COM", "SBF.NO", "KMR.A1", "KMR.A2", "KMF.A1", "KMF.A2", "POWER.N",
                "SB0.NC", "SBR.COM", "SBR.NO", "KMF.A1", "KMF.A2", "KMR.A1", "KMR.A2", "POWER.N");
            task.Actions.Add(Action("SBF", true, "M1", MotorDirection.Forward));
            task.Actions.Add(Action("SB0", true, "M1", MotorDirection.Stopped));
            task.Actions.Add(Action("SBF", false, "M1", MotorDirection.Stopped));
            task.Actions.Add(Action("SB0", false, "M1", MotorDirection.Stopped));
            task.Actions.Add(Action("SBR", true, "M1", MotorDirection.Reverse));
            return task;
        }

        private static CircuitTaskSpec MultiLocation()
        {
            var task = Base("multi-location", "三相异步电动机两地与多地控制仿真", "停止按钮串联、启动按钮并联，实现多地点控制。", "KM1");
            Add(task,
                "POWER.L1", "SB0A.COM", "SB0A.NC", "SB0B.COM", "SB0B.NC", "SB1A.COM", "SB1A.NO", "KM1.A1", "KM1.A2", "POWER.N",
                "SB1A.COM", "SB1B.COM", "SB1A.NO", "SB1B.NO", "SB1A.COM", "KM1.13", "SB1A.NO", "KM1.14");
            task.Actions.Add(Action("SB1A", true, "M1", MotorDirection.Forward));
            task.Actions.Add(Action("SB0B", true, "M1", MotorDirection.Stopped));
            return task;
        }

        private static CircuitTaskSpec TimedControl()
        {
            var task = Base("timed", "三相异步电动机时间电路控制仿真", "时间继电器延时触点控制第二接触器。", "KM1");
            AddSelfLockControl(task, "KM1", "SB0", "SB1");
            Add(task, "KM1.14", "KT.A1", "KT.A2", "POWER.N", "POWER.L1", "KT.15", "KT.18", "KM2.A1", "KM2.A2", "POWER.N");
            task.Actions.Add(Action("SB1", true, "M1", MotorDirection.Forward));
            return task;
        }

        private static CircuitTaskSpec SequentialStart()
        {
            var task = Base("sequence", "三相异步电动机顺序启动控制仿真", "第一接触器动作后才允许第二接触器启动。", "KM1");
            AddSelfLockControl(task, "KM1", "SB0", "SB1");
            Add(task, "POWER.L1", "KM1.13", "KM1.14", "SB2.COM", "SB2.NO", "KM2.A1", "KM2.A2", "POWER.N");
            Add(task,
                "QF.T1", "KM2.L1", "QF.T2", "KM2.L2", "QF.T3", "KM2.L3",
                "KM2.T1", "M2.U", "KM2.T2", "M2.V", "KM2.T3", "M2.W");
            task.Actions.Add(Action("SB1", true, "M1", MotorDirection.Forward));
            task.Actions.Add(Action("SB2", true, "M2", MotorDirection.Forward));
            return task;
        }

        private static CircuitTaskSpec ReverseBraking()
        {
            var task = Base("reverse-brake", "三相异步电动机反接制动仿真", "停止时短时接入反相序接触器完成反接制动。", "KMF");
            AddReversePower(task);
            Add(task, "POWER.L1", "SB1.COM", "SB1.NO", "KMF.A1", "KMF.A2", "POWER.N", "POWER.L1", "SBB.COM", "SBB.NO", "KMB.A1", "KMB.A2", "POWER.N");
            task.Actions.Add(Action("SB1", true, "M1", MotorDirection.Forward));
            task.Actions.Add(Action("SBB", true, "M1", MotorDirection.Braking));
            return task;
        }

        private static CircuitTaskSpec EnergyBraking()
        {
            var task = Base("energy-brake", "三相异步电动机能耗制动仿真", "停机后通过制动单元向定子注入直流进行能耗制动。", "KM1");
            AddSelfLockControl(task, "KM1", "SB0", "SB1");
            Add(task, "POWER.L1", "SBE.COM", "SBE.NO", "KB.A1", "KB.A2", "POWER.N", "KB.13", "BRAKE.IN", "BRAKE.OUT", "M1.U");
            task.Actions.Add(Action("SB1", true, "M1", MotorDirection.Forward));
            task.Actions.Add(Action("SBE", true, "M1", MotorDirection.Braking));
            return task;
        }

        private static CircuitTaskSpec Base(string id, string name, string description, string mainContactor)
        {
            var task = new CircuitTaskSpec { Id = id, Name = name, Description = description };
            Add(task,
                "POWER.L1", "QF.L1", "POWER.L2", "QF.L2", "POWER.L3", "QF.L3",
                "QF.T1", $"{mainContactor}.L1", "QF.T2", $"{mainContactor}.L2", "QF.T3", $"{mainContactor}.L3",
                $"{mainContactor}.T1", "FR.L1", $"{mainContactor}.T2", "FR.L2", $"{mainContactor}.T3", "FR.L3",
                "FR.T1", "M1.U", "FR.T2", "M1.V", "FR.T3", "M1.W");
            task.ForbiddenConnections.Add(new PortPair("POWER.L1", "POWER.N"));
            task.ForbiddenConnections.Add(new PortPair("POWER.L1", "POWER.L2"));
            return task;
        }

        private static void AddSelfLockControl(CircuitTaskSpec task, string contactor, string stop, string start)
        {
            Add(task,
                "POWER.L1", $"{stop}.COM", $"{stop}.NC", $"{start}.COM", $"{start}.NO", $"{contactor}.A1", $"{contactor}.A2", "POWER.N",
                $"{start}.COM", $"{contactor}.13", $"{start}.NO", $"{contactor}.14");
        }

        private static void AddReversePower(CircuitTaskSpec task)
        {
            Add(task,
                "POWER.L1", "QF.L1", "POWER.L2", "QF.L2", "POWER.L3", "QF.L3",
                "QF.T1", "KMR.L2", "QF.T2", "KMR.L1", "QF.T3", "KMR.L3",
                "KMR.T1", "FR.L1", "KMR.T2", "FR.L2", "KMR.T3", "FR.L3");
        }

        private static void Add(CircuitTaskSpec task, params string[] ports)
        {
            if (ports.Length % 2 != 0) throw new ArgumentException("Connections must be supplied as port pairs.");
            for (var i = 0; i < ports.Length; i += 2)
                task.RequiredConnections.Add(new PortPair(ports[i], ports[i + 1]));
        }

        private static TaskActionStep Action(string deviceId, bool active, string expectedDevice, MotorDirection direction)
        {
            return new TaskActionStep
            {
                DeviceId = deviceId,
                Active = active,
                HoldSeconds = 0.12f,
                ExpectedDeviceId = expectedDevice,
                ExpectedMotorDirection = direction
            };
        }
    }

    public sealed class TaskEvaluationResult
    {
        public bool Passed => MissingConnections.Count == 0 && ForbiddenConnections.Count == 0 && ActionErrors.Count == 0;
        public List<PortPair> MissingConnections { get; } = new List<PortPair>();
        public List<PortPair> ForbiddenConnections { get; } = new List<PortPair>();
        public List<string> ActionErrors { get; } = new List<string>();

        public string Summary()
        {
            if (Passed) return "验收通过：接线拓扑和动作序列均正确。";
            var parts = new List<string>();
            if (MissingConnections.Count > 0) parts.Add($"缺少 {MissingConnections.Count} 组连接");
            if (ForbiddenConnections.Count > 0) parts.Add($"存在 {ForbiddenConnections.Count} 组危险连接");
            if (ActionErrors.Count > 0) parts.Add($"动作错误 {ActionErrors.Count} 项");
            return "验收未通过：" + string.Join("；", parts) + "。";
        }
    }

    public static class CircuitTaskEvaluator
    {
        public static TaskEvaluationResult EvaluateTopology(CircuitGraph graph, CircuitTaskSpec task)
        {
            var result = new TaskEvaluationResult();
            foreach (var required in task.RequiredConnections)
                if (!graph.AreConnectedByWiring(required.A, required.B)) result.MissingConnections.Add(required);
            foreach (var forbidden in task.ForbiddenConnections)
                if (graph.AreConnectedByWiring(forbidden.A, forbidden.B)) result.ForbiddenConnections.Add(forbidden);
            return result;
        }
    }
}
