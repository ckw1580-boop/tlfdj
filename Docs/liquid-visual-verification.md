# 液体外观与管路动画

三只方形水槽保持原模型液位：未混合液体 B 为红褐色，A 为灰蓝色，混合液接收槽为淡紫色。水面使用双层滚动法线、缓慢波纹和柔和环境反光。中央搅拌罐从底部开始蓄水，液位和颜色由实际到达罐内的液体决定。

## 控制与显示

| 管路 | 电机／阀门 | 行为 |
|---|---|---|
| A 液，上层泵1 | M_DOUBLE／SOLENOID1 | 按实际正转转速推进，1450 rpm 对应中心线每秒 0.8 单位 |
| B 液，下层泵2 | M1／SOLENOID2 | 独立于泵1，使用相同速度比例 |
| 罐内液体排出 | SOLENOID3／实际 DrainFlow | 有实际排液时显示管内液体和出口水柱，颜色跟随罐内液体 |

- 关闭进液阀时，液体前沿停在阀门处；上游已充满的液体保持静止，下游为空。
- 打开进液阀后，前沿从当前位置继续；到达出口后水柱逐渐向下延伸。空罐时触及罐底才蓄水，已有液体时触及当前液面才增加液量。
- 输送与液位共用仿真的 20 毫秒时间步。接触发生在时间步中途时，仅接触后剩余时间的流量计入罐内；到达前界面显示“输送中”，实际到罐流量为零。
- 运行中关阀立即停止跨阀流动，阀后液体在 0.5 秒内淡出，阀前液体保留。
- 泵实际停转、反转或退出仿真后，管内液体在 0.5 秒内淡出至空管。正转惯性减速期间仍按实际转速运动。
- 淡出期间重新启动或开阀，会保留现有前沿。文件弹窗期间冻结全部液体动画，取消后继续；重置液位或成功载入时清空动画状态。
- 停泵、关阀后的淡出残影不继续蓄水。退出仿真暂停蓄水和混合进度，同时淡出管内液体。
- 罐内只有 A 时为灰蓝色，只有 B 时为红褐色；第二种液体接触后，从当前颜色约 1 秒渐变为淡紫色，无需启动搅拌电机。两路同时到达空罐时，从按来液比例合成的颜色开始渐变。
- 排液和溢流按 A、B 当前组成比例扣减，液位传感器继续读取实际液位。排空后清除组成及混合进度，再次进液重新确定颜色。
- 配置中的非零初始液位视为已混合液，A、B 各半，初始即为淡紫色。保持原 CC3D 配置格式，不保存运行中的输送、组成或混合状态。

## 资源与维护

`LiquidPipeRoutes.json` 使用 EnvironmentBench 局部坐标；A 来自 Line001、Line006、Line004，B 来自 Line002、Line005、Line003 的实际网格截面中心，包括原有弯头采样。泵体内连接两端，阀门投影到中心线作为截断点；排液弯头按 polySurface2002 测量。

原 guan 网格仅作为透明管壁。旧 guangdao、进排料水流、流动箭头，以及 rivet/17–20 的 ShuiLiu 图层都被隐藏，防止空管仍显示旧青色液体。水槽液面单独绑定，旧中央罐静态液位层单独隐藏。

`LiquidSimulationRuntime` 管理实际液量、组成和混合进度，`LiquidStreamRuntime` 管理管内前沿与下降水柱；显示组件仅读取这些输送状态。水面波纹使用独立显示时钟，不改变蓄水计时。

动画使用运行时独立材质和网格；不修改导入资源的共享材质。运行时状态、材质和网格随场景销毁。

## 验证记录

- EditMode：218/218 通过（`Logs/liquid-arrival-edit.xml`），覆盖分段接触计量、已有液位、两种来液顺序、排空再灌、比例排液和溢流、不同转速、30／60／120 FPS，以及现有电气逻辑回归。
- PlayMode：22/22 通过（`Logs/liquid-arrival-play.xml`），覆盖实际场景中的下降水柱、接触后蓄水、A／B 单色与自动混合、三槽固定液位、泵阀与电机联动、文件冻结与载入清空，以及场景 IO 回归。
- 双路同时触底与退出仿真暂停混合的补充测试：1/1 通过（`Logs/liquid-arrival-simultaneous.xml`）。本次共验证 219 项 EditMode 用例及 22 项 PlayMode 用例。
- Windows 构建成功，已更新 `Build/Windows/ElectricalTraining.exe` 及配套数据目录（`Logs/liquid-arrival-build.log`）。
- 隐藏启动检查完成场景初始化，进程正常响应，未发现运行异常或着色器错误（`Logs/liquid-arrival-smoke.log`）；检查进程已关闭。
- 截图由 Unity 实际渲染，覆盖初始空管、关阀受阻、开阀推进、持续流动和半秒淡出。渲染器清单保存在 `Logs/liquid-renderers.json`，便于检查重复网格。

![A、B 液面](../Logs/liquid-basins-ab.png)

![混合液接收槽](../Logs/liquid-basin-mixed.png)

![关闭阀门，液体停在上游](../Logs/liquid-pipes-blocked.png)

![打开阀门，液体继续流动](../Logs/liquid-pipes-flowing.png)

![停止后透明空管](../Logs/liquid-pipes-empty.png)

![水柱逐渐下降，接触前罐内为空](../Logs/liquid-arrival-a-falling.png)

![A 液首次接触罐底并开始蓄水](../Logs/liquid-arrival-a-contact.png)

![仅 A 液进入时保持灰蓝色](../Logs/liquid-arrival-a-single.png)

![仅 B 液进入时保持红褐色](../Logs/liquid-arrival-b-single.png)

![第二种液体到达后自动渐变](../Logs/liquid-arrival-a-blending.png)

![两种液体混合后呈淡紫色](../Logs/liquid-arrival-a-mixed.png)
