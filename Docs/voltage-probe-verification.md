# 数字验电笔验收记录

2026-09-30；Unity 2022.3.62f3c1；Built-in Render Pipeline。

## 实现与使用

在排故工具中选择“验电笔”，默认 AC 档。笔尖跟随鼠标，点击可见端子后固定；点击笔身或右侧“拿起验电笔”更换测点。笔身 AC/DC 按键和右侧档位按钮共用测量状态，切档不移除接触。

约 180 mm 的三维笔身包括金属笔尖、绝缘护套、护指环、防滑纹、背光液晶、模式按键和笔夹。模型位于 `Assets/ElectricalSim/Resources/VoltageProbe.prefab`，菜单 `Electrical Sim > Build Voltage Probe Model` 可重建，材质及网格位于 `Assets/ElectricalSim/Generated/VoltageProbe/`。

| 档位 | 固定参考 | 有效读数 |
| --- | --- | --- |
| AC | 系统零线 `POWER.N` | 三相相线对零线 220.0 V；同参考网络 0.0 V |
| DC | 系统 24V− `TERMINAL_BUS.DC_NEGATIVE` | 24.0 V，以及具有公共参考的正负模拟电压 |

液晶屏与右侧属性框使用同一仿真快照结果，显示一位小数。属性框同时显示类型、AC/DC 支持、档位、测点、参考端、电压及状态。不存在接触、参考失效或测点悬空时不伪造零电压；档位不匹配、冲突、变频输出未建模和仿真未收敛均有明确提示。

固定后可操作供电按钮和断路器；拿起期间不触发被测设备操作。吸附保存真实物理锚点，切换视角不会换面；目标锚点销毁或端子从电路中移除时解除接触。切换仪表、退出排故及重置会清理模型、面板、接触和高亮。

## 自动验证

| 检查 | 结果 | 本地报告 |
| --- | --- | --- |
| 验电笔、万用表测量、电路图及排故电源端子 EditMode 回归 | 34/34 通过 | `Build/Reports/voltage-probe-editmode.xml` |
| 验电笔、万用表、排故电源端子及排故工具入口 PlayMode 回归 | 18/18 通过 | `Build/Reports/voltage-probe-playmode-final.xml` |
| 模型生成与最终脚本编译 | 通过 | `Build/Reports/voltage-probe-model-build.log`、`Build/Reports/voltage-probe-playmode-final.log` |
| 项目资源完整性 | 通过，无 LFS 指针或缺失资源/.meta | `Build/Reports/voltage-probe-resource-integrity.json` |

测量覆盖三相、直流正负模拟信号、同参考网络、悬空、失电、独立信号源、档位不匹配、电势及信号冲突、变频输出和未收敛状态。

场景测试使用实际物理射线验证吸附、笔身拾取、AC/DC 按键及实体/UI 遮挡；验证属性按钮同步、真实锚点移动与换面稳定、供电按钮引起读数变化、拿起时控制器操作被阻止、两类端子可测、目标移除、首页遮挡以及仪表互斥和清理。测量过程不增加接线数量或修改接线脏状态。截图测试检查属性文字完整显示及面板位于屏幕内。

## 画面检查

已检查笔身近景、1920×1080 交流界面和 1280×720 直流界面。模型液晶和属性框均显示对应档位与电压，属性和操作按钮无裁切。柜体全景中笔身保持实际比例，远距离读数可从右侧属性框查看。

- `Build/Reports/voltage-probe-model.png`
- `Build/Reports/voltage-probe-ac-1920.png`
- `Build/Reports/voltage-probe-dc-1280.png`

## 边界

采用单笔自动参考，不提供两相之间的 380 V 测量或任意两点电压；这些测量继续使用万用表。独立直流信号源需要与系统 24V− 建立有效参考后才显示数值。显示小数位不代表新增物理精度模型。

不增加参考夹、蜂鸣、电阻、通断档或变频波形模型。位置、档位和接触为临时状态，不加入 `.cc3d`、导线或撤销历史。本轮没有重建 Windows EXE；本地报告与截图位于忽略的 `Build/Reports/` 中。
