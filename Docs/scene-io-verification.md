# 场景液位与泵联动验收记录

验收日期：2026-09-13。Unity：2022.3.62f3c1。

## 已实现

- 18个端子中文备注及7个传感器／电磁阀模型绑定，保留原端口ID、物理锚点及旧接线。
- 泵1（场景安装位14）读取柜体中间电机M_DOUBLE（118）的实际转速；泵2（场景安装位13）读取右侧电机M1（38）的实际转速。
- 罐顶搅拌机（场景安装位4）绑定柜体正面左侧M2（49），两处共用转速、方向、接线及属性；仅旋转搅拌轴与叶片，外壳固定。
- DC 24V传感器输出、常闭电磁阀、正向转速比例进液、惯性停转、重力排液、净流量、四级限位与溢流记录。
- 器件／电机／泵属性、液位参数编辑、暂停恢复、独立液位重置及CC3D配置保存。
- 三槽彩色波纹液面、半透明淡紫色搅拌液体、供电及触发指示，以及实际转速与阀门驱动的管路动画；详见 [液体外观与管路动画](liquid-visual-verification.md)。

## 测试结果

| 验证 | 结果 | 记录 |
|---|---|---|
| 最终全部EditMode测试 | 188/188通过 | `Logs/scene-io-final-editmode.xml` |
| 场景绑定、供电、PLC反馈、液位、配置、变速／惯性／帧率与截图 | 7/7通过 | `Logs/scene-io-playmode-3.xml`；最终全量运行中亦通过 |
| 真实鼠标射线逐个选择器件、泵、柜体电机并点击空白关闭属性 | 1/1通过 | `Logs/scene-io-pointer.xml` |
| 全量PlayMode回归 | 120/124通过，4项原有失败 | `Logs/scene-io-full-playmode.xml` |
| 暂时禁用新增功能的隔离复验 | 同样4项失败 | `Logs/scene-io-baseline.xml` |

点击测试最初将相机放在泵模型包围盒内，修正测试相机距离后通过。隔离复验结束后已恢复完整实现，最终EditMode在恢复后的代码上执行。

### 搅拌电机绑定补充验证

2026-09-13：`Logs/mixer-playmode.xml` 中原有6项电机场景测试全部通过，场景IO的10项中9项通过（包括14处模型实际射线点击及其他电机不驱动搅拌轴）。变速测试改为每档推进40毫秒，确保浮点累计后至少跨过一个20毫秒仿真步；`Logs/mixer-motion-playmode.xml` 定向复验通过。16项相关用例均已验证通过。

新增验证覆盖：M2安装位49与场景安装位4的固定对应、四个电机运行时数量不变、两处属性一致、零速／60／120／-60／1450 rpm、惯性减速和停转、轴心及外壳固定，以及搅拌电机与两路进液的相互独立。

Windows程序已更新至 `Build/Windows/ElectricalTraining.exe`，构建退出码0（`Build/Reports/mixer-windows-build.log`）。新程序启动检查确认场景初始化完成、进程运行正常、错误行0（`Build/Reports/mixer-player-smoke.log`）。

## 原有回归失败

1. `ContactorSceneTests.PhysicalWiringControlsLoadAndSurvivesSaveReload`：用DC 24V端子供电，却期待AC 220V接触器吸合。
2. `ContactorSceneTests.RearCoilAndContactsStayIndependentFromFrontAndSurviveReload`：同类供电与断言不一致。
3. `TrainingSceneTests.CabinetBrandingCoversBothOriginalHeaderLogos`：预期的柜体标牌对象缺失。
4. `TrainingSceneTests.InverterLowerTerminalBoardAnnotationsUseOriginalLowerBoardGroups`：原有文字垂直位置断言失败。

以上失败在禁用场景液位注册、恢复原电机时间求解行为后，以相同位置和结果重现；保留工作区已有实现，未在本次任务中改动这些无关逻辑。

## 场景效果

![泵、电磁阀及液面](../Logs/scene-io-process.png)

![混合罐属性与配置](../Logs/scene-io-properties.png)

在查看模式点击混合罐可调整初始液位、泵1／泵2标定注满时间和重力排空时间。点击“应用配置”保存参数，点击“重置液位”应用初始液位；进入仿真后按实际接线和电机转速运行。
