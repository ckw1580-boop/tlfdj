# 旧版资源清理记录（2026-09-29）

本次删除当前项目不再使用的账号、更新、考试、竞赛相关独立资源和原始素材包，保留当前实训功能、编辑器生成工具和接线存档格式。

## 删除结果

| 分类 | 文件数（含元数据） | 删除文件体积 |
| --- | ---: | ---: |
| 当前项目未使用的旧资源 | 3,601 | 537.47 MiB |
| 原始素材包 OriginalAssetsSource | 8,961 | 1,437.50 MiB |
| 旧导入器、导入测试和 SampleScene | 6 | 0.03 MiB |
| 项目内 Sentinel 日志 | 2 | 0.03 MiB |
| 合计 | **12,570** | **1,975.03 MiB（约 1.93 GiB）** |

精确删除体积为 **2,070,964,972 字节**。这是删除文件的逻辑大小，不包含新生成的测试报告和验证构建，也不表示 Git 历史或 LFS 缓存被压缩。

完整路径、大小、SHA-256、删除原因和执行状态见 [删除清单](legacy-resource-deletions-2026-09-29.csv)。原始素材包没有另建备份。现有 Git 历史、恢复 ZIP、用户存档、旧构建和开发缓存未清理。

原始包内四份 `.cc3d` 项目文件在删除前已与当前 `StreamingAssets/OfflineData/project` 中的副本逐一比对，内容完全相同。

## 保留依据

- 先精简 UI 注册表，只保留 `TopNavigation`、`ExperimentToolbar`、`LineForm`、`LineParam`、`Inverter`。一并删除没有运行时调用的 KT 注册项；当前代码和原有场景测试已确认不再创建 KT。
- 使用 Unity 2022.3.62f3c1 的 `AssetDatabase.GetDependencies(..., true)` 计算递归依赖。根集合覆盖实训场景、资源注册表、当前代码与测试、Resources、StreamingAssets、生成资源，以及编辑器按路径加载的转速表源 Prefab 和标识贴图。另扫描代码中的实际资源路径。
- 确认 1,792 个资源文件不属于保留集合后，删除其本体、配套 `.meta` 和空目录元数据。删除时检查绝对路径边界及重解析点，再核对清单哈希；没有按文件名中的“旧”“Original”或体积直接删除。
- 保留资源的模型、贴图、材质、字体及 GUID 不做合并或压缩。共享资源即使被旧界面使用过，只要当前功能仍依赖它就继续保留。
- 已移除旧素材导入器及测试；增加当前 UI 完整性和编辑器直接资源输入测试。场景安装、参考数据生成、资源校验和 Windows 构建入口继续可用。标准构建重新生成了精简后的 UI 布局数据及其报告。
- 提供只读依赖审计入口 `ElectricalSim.Editor.LegacyResourceAudit.WriteReport`；可使用 `-legacyAuditReport` 指定 JSON 输出路径。它只生成报告，不执行删除。

## 验证记录

报告目录为 `Build/Reports/legacy-cleanup/`。

| 检查 | 结果 | 报告 |
| --- | --- | --- |
| 清理前文件完整性 | 通过 | baseline-resources.json |
| 清理前 EditMode | 354 通过、0 失败、1 跳过 | baseline-editmode.xml |
| 清理前主要功能 PlayMode | 43 通过、0 失败 | baseline-focused-playmode.xml |
| 清理后文件完整性 | 通过 | final-resources.json |
| 清理后 EditMode | 352 通过、0 失败、1 跳过 | final-editmode.xml |
| Windows 构建 | 成功，478,148,296 字节 | windows-build.log |
| Windows 程序图形模式离线启动 | 实训场景初始化完成，未记录异常或着色器错误 | windows-player-graphics.log |
| 清理后完整 PlayMode | 172 通过、0 失败、1 跳过 | final-playmode.xml |
| 最终依赖复查 | 保留 914 项；未使用旧资源候选 0 项；新增引用告警 0 项 | final-audit.json |
| 截图复查 | 实训柜、设备属性、接线 UI 与原理图显示正常 | final-*.png |

EditMode 数量变化来自移除 4 项旧导入测试并新增 2 项保留资源测试；两套完整测试中的跳过项均为显式性能基准。第一轮完整 PlayMode 基线没有生成结果，不能作为通过记录；随后补跑的 43 项覆盖场景启动、接线保存读取、万用表、原理图、PLC、变频器及电机。清理后的完整 PlayMode 回归已完成，液体、场景 I/O、热继电器、接触器和其他场景测试也通过。

删除后校验了 **914 个不同保留资源文件**的 SHA-256，内容均与删除前一致。随后正常构建按预期刷新了 UI 布局生成文件；最终复查其余 913 个文件哈希不变。元数据通过文件完整性、GUID 依赖审计及 Git 差异单独检查，没有修改现有保留资源的 `.meta`。

Windows 验证程序：`Build/Windows-LegacyCleanup/ElectricalTraining.exe`。现有 `Build/Windows` 和历史恢复包保留原样。

## 既有告警与保留边界

清理前依赖审计记录了 6 条既有问题：转速表编辑器源 Prefab 有 1 个无法解析的旧 GUID；实训室环境 Prefab 的液体模型有 5 个空材质槽。精简注册表后和最终清理后，告警集合均与基线完全相同。本次没有将它们当作未使用资源删除，也不将原有告警宣称为零。

当前保留的导航与工具栏内部仍有运行时隐藏的按钮节点；其共用贴图按实际 Prefab 依赖保留。独立旧登录、考试、上传等界面及原始 DLL 已删除。Unity 通用网络模块与 S7.Net PLC 通信库继续保留。

旧资源导入能力已取消。缺失资源应从当前版本的完整源码或恢复包恢复，详见 [恢复说明](project-recovery.md)。历史审计文档记录的是当时的文件状态，不作为当前资源清单。
