# 三维万用表验收记录

Unity 2022.3.62f3c1；在现有实训场景和工作区改动基础上验证。

## 实现

- 场景内可移动表体、红黑表笔、软线，四档旋钮及独立 HUD。
- 红笔减黑笔的电压极性；交流 220/380V、直流 ±24V；通断蜂鸣及带电保护。
- 双表笔可接同一端子，笔柄可分别拾取；已接表笔绑定物理锚点，不随相机换面跳转。
- 选中万用表后，电气端子和电机跳线端子均可测，不依赖之前的接线工具。
- 排故模式下空闲仪表允许原供电链、按钮和断路器操作；仪表退出、切换、重置后清理。
- 模型：`Assets/ElectricalSim/Resources/Multimeter.prefab`；重建菜单：`Electrical Sim > Build Multimeter Model`。

## 自动验证

| 检查 | 结果 | 本地报告 |
| --- | --- | --- |
| 测量、电路图、供电面板电路、电机速度 EditMode 回归 | 49/49 通过 | `Build/Reports/multimeter-editmode.xml` |
| 万用表、转速表、面板、端子显示、接线存档 PlayMode 回归 | 39/39 通过 | `Build/Reports/multimeter-playmode.xml` |
| 含 HUD 的多分辨率截图生成 | 1/1 通过 | `Build/Reports/multimeter-visual.xml` |
| `Tools/Test-ProjectResources.ps1` | 通过，无 LFS 指针或缺失文件/.meta | 命令输出 |

测量测试覆盖同电位带电保护、隔离支路、跨端子排和开闭触点、断线、极性反接、失电、悬空参考、电势冲突和变频输出标记。场景测试使用实际物理射线验证表笔拾取、同端子双笔、档位点击、表体拖动及实体/UI 遮挡，并验证原理图窗口屏蔽输入、蜂鸣停止和测量不改变接线状态。

## 画面验收

已检查表体近景、1920×1080 交流测量界面及 1280×720 通断界面：液晶和右侧 HUD 可读，表体与右侧读数区不重叠。

- `Build/Reports/multimeter-model.png`
- `Build/Reports/multimeter-ac-hud-1920.png`
- `Build/Reports/multimeter-continuity-hud-1280.png`
- `Build/Reports/multimeter-dc-cabinet.png`
- `Build/Reports/multimeter-live-warning.png`

## 模型边界

沿用理想电路模型，不提供线圈/电机绕组阻抗、电流档或变频波形。变频器输出网络的电压显示暂不支持；运行中的变频器输出触发通断带电提示。表笔软线不接入电路，不加入撤销历史或 `.cc3d` 存档。声响服从现有全局静音设置。
