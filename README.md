# 电气控制系统仿真软件（完整离线复刻）

Unity 2022.3.62f3c1 / Built-in Render Pipeline。启动场景为 `Assets/Scenes/ElectricalTraining.unity`。

程序跳过登录并直接进入原实训室。当前实现包括原 `Experiment` 场景和导航/工具栏素材、五种互斥操作模式、三相离散电气图求解、10 项电机控制任务、拓扑与动作验收、接线/拖动/排故、四类仪表、A–D 本地考试包、虚拟 PLC、本地成绩与会话存储，以及 `.cc3d` 四根字典结构的保真读写。账号、联网更新和真实 PLC 通信不在离线版范围内。

## 原始素材接入

1. 将带完整 `.meta` 的原 `Assets` 子集放入项目根目录 `OriginalAssetsSource/`。
2. Unity 菜单执行 `Electrical Sim > Import Original Assets`。
3. 素材复制到 `Assets/OriginalContent/`，导入器会创建并挂接 `OriginalVisualRegistry.asset`。
4. 自动识别不到的 Prefab 可在注册表中按 `DeviceId` 或 `TypeId` 手工指定。

当前仓库已经生成 `Assets/OriginalContent/OriginalVisualRegistry.asset`、原实训室 Prefab、原 UI Prefab、端口清单和 1920×1080 UI 布局清单。

## 操作

- `1` 视角、`2` 拖动、`3` 接线、`4` 仿真、`5` 排故，`Esc` 返回视角模式。
- `WASD` 平移，`Q/E` 升降，按住右键旋转，滚轮缩放。
- 接线模式依次点选两个端子；`Delete` 删除最后一条线。
- `Ctrl+Z`/`Ctrl+Y` 撤销或恢复接线。
- 仿真模式点击按钮、断路器或热继电器；排故模式选择仪表和测量端子。
- `F5`/`F6`/`F7`/`F8` 分别进入 A/B/C/D 本地考试包。
- 截图、成绩、考试进度和录屏帧序列保存到“文档/电气控制系统仿真软件”。

## 验证与构建

Unity 菜单 `Electrical Sim > Build Windows x64` 构建到 `Build/Windows/ElectricalTraining.exe`。EditMode 与 PlayMode 测试位于 `Assets/ElectricalSim/Tests/`。端口、UI、场景边界和启动截图报告位于 `Build/Reports/`。
