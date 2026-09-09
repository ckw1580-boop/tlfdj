# PLC 属性与实机通信实现验收

日期：2026-09-09。

## 已交付

- PLC_1、PLC_2 绑定到现有两台模型，每台 14 个输入、10 个输出、6 个供电/公共端，共 60 个连接点。
- 属性面板提供独立 CPU 型号、IP、端口、Rack/Slot、轮询及超时配置，支持点位地址编辑、实时状态和黄色定位标记。
- 默认 M0.0～M1.5 写入虚拟输入，Q0.0～Q1.1 读取程序输出；支持 M/DB 输入映射与 Q/M/DB 输出映射。
- 后台串行通信、断网手动重连、过期结果隔离、在线停止时清理已写入输入；所有场景电路更新在 Unity 主线程执行。
- PLC 配置随 `.cc3d` 保存，包含变更检测、旧文件兼容和未知字段保留。
- 修复电路节点识别：已注册的本地端子名称即使含点号，也不会被误当成跨设备地址。

## 验证结果

| 检查 | 结果 | 证据 |
| --- | --- | --- |
| 新增 PLC EditMode 用例 | 20/20 通过 | `Build/Reports/plc-edit.xml` |
| 全部 EditMode 用例 | 131/134 通过；3 项原有失败 | 同上 |
| PLC、面板、工程存档 PlayMode 回归 | 22/22 通过 | `Build/Reports/plc-play.xml` |
| 最终 PLC 界面及场景复查 | 3/3 通过 | `Build/Reports/plc-ui-final.xml` |
| Windows x64 构建 | 成功，529116925 字节 | `Build/Reports/plc-build.log` |
| Windows Direct3D 启动 | 场景初始化完成，无脚本异常 | `Build/Reports/plc-player-graphics.log` |

实际 S7.Net Plus 0.20.0 DLL 已通过本地 TCP 协议对端测试，覆盖 S7-1200/S7-1500 配置的握手、M/DB 位写入、Q/M/DB 读取，以及相邻位保留；该测试不等同于真实 CPU 验收。

功能测试还覆盖通信超时、断网、两台连接独立性、超时后的迟到响应、长轮询期间快速断开、60 个端子绑定、虚拟按钮通过 PLC 驱动既有指示灯、供电缺失和配置保存恢复。

界面截图：`Build/Reports/plc-properties.png`、`Build/Reports/plc-properties-outputs.png`。已目视检查输入列表、输出/供电列表与端子定位标记。

## 原有失败

以下失败在此前 `Build/Reports/panel-edit.xml` 中已存在，本次未修改这些行为以消除失败：

- `CircuitGraphTests.EveryReferenceTaskPassesItsActionSequence`：联锁正反转参考任务中期望 Forward，得到 Stopped。
- `OriginalTerminalBoardMapTests.CabinetBoardsMapPhysicalAnchorsToRuntimeNodes`：KM2_61nc 期望 KM1.61，当前映射为 KM1.21。
- 同一测试的 KM3_72nc 用例：期望 KMR.72，当前映射为 KMR.22。

## 使用与实机状态

运行 `Build/Windows-PLC/ElectricalTraining.exe`。整个 `Windows-PLC` 文件夹需一起保留或复制，不能单独移动 EXE。

点击 PLC 本体配置实际 IP，按 [PLC 联调说明](PLC联调说明.md) 设置 PUT/GET 权限及预留输入地址，再连接和进入仿真模式。

未提供现场 S7-1200/S7-1500 连接参数，**真实硬件联调尚未验证**。当前报告仅记录已执行的本地测试与 Windows 构建启动结果。
