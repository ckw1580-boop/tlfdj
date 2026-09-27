# 渐进优化验收记录（2026-09-22）

## 实施内容

- 接线历史由 `WireHistory` 管理，最多保留最近 64 条完整快照；超限淘汰最旧项，撤销/恢复顺序正确，新编辑清空恢复栈。
- `WireViewCollection` 按导线 ID 增量同步对象。撤销、恢复和加载同 ID 数据时重绑现有显示对象；删除时销毁对应对象。
- 导线缓存端点几何、器件包围盒、折点、布线面及线槽配置版本。静止刷新不再生成路径；颜色、高亮、线宽分别更新。`Refresh(true)` 可强制重建，`GeometryBuildCount` 供验证使用。
- 求解器缓存器件分类、端口和基础接线连通关系，每次求解比较导线端点以兼容直接修改；动态触点仍逐轮合并。工作连通表与信号缓冲复用，返回快照的数据独立保存。
- 快照新增 `IsConverged` 和 `IterationCount`。两阶段求解都稳定才标记收敛，次数为两个阶段的累计求解轮数；未收敛单独提示，不混入短路错误。不改变电机、变频器和液体的时间推进次序。
- 柜体贴图处理与 HUD 装配拆入 partial 文件；主场景初始化文件由 2705 行降至 1880 行，原组件类型、序列化字段和 GUID 保留。
- 构建入口增加可选 `-trainingPreserveAssets`，供验收构建直接使用当前场景和资源，避免构建前重新生成资源。原有菜单构建默认行为不变。

本轮不增加创新功能，不升级 Unity/依赖，不修改场景、模型或存档格式，不自动提交或推送。

## 测试修正依据

1. DQG01 当前明确清除原纹理中的旧标识，不生成 WCK 面板。旧测试把 `wanggui` 下的两个旧子对象当作必要条件；现验证实际柜体材质、清除区域透明度、无附加标识和两侧视角可见性，并使用 5 秒有界初始化等待。
2. 接点测试纳入现有 QFFRONT1–3 和排故电源端子排；验证正面断路器端子数、锚点和拾取开关。接线分组测试显式覆盖正反视角与两种线型，保留电机、端子排和 FR 输出端的独立行为断言。
3. 全量回归另发现两项接触器测试使用 `DuanZiPai_6.V_1/N_1` 驱动 220V AC 线圈；这些端子当前为 24V DC。在优化前 HEAD 代码中单独复跑，同样失败。改用实际 AC 端子 `DuanZiPai_0.a2/a4`，断言线圈电压 220V，继续验证负载、前后独立性和保存恢复；使用有界状态等待代替假设两帧足够。
4. 9 月 27 日续跑发现柜体断路器测试的四分之一时长等待可能跨过整个动画，得到终点 45°。测试临时将动画时长设为当前帧时间的四倍，同步检查协程首步的中间姿态，再恢复原时长验证反向动画和电气联动。运行时代码不变；单项复跑通过，报告为 `Build/Reports/optimization-breaker.xml`。

对照证据：`Build/Reports/optimization-baseline-scene.xml` 中两项接触器失败；该次性能计数器校准失败的结果不用于性能结论。

## 性能对比

环境：同一台电脑、Unity 2022.3.62f3c1、Built-in、ElectricalTraining 场景、1920×1080。优化前来自本轮开始时的 HEAD；临时替换前备份 15 个修改/新增源文件，恢复时逐个校验 SHA-256。原始资源保持一致。

固定样本为 50/200/500 条具有独立 ID 的导线，连接相同的实际 QF/KM1 端子对并分布折点。每种工况预热 10 次，每轮计时 60 次，重复 3 轮；下表取三轮中位数。编辑工况每次改变一条线的折点，仿真工况调用完整场景电路 `Solve(0.02)`。这是受控的组件耗时比较，不是整帧 FPS 测试，也不代表 500 个独立复杂回路的性能。

| 导线数 | 工况 | 优化前 ms/次 | 优化后 ms/次 | 耗时下降 |
| --- | --- | ---: | ---: | ---: |
| 50 | 静止导线刷新 | 0.977 | 0.034 | 96.6% |
| 50 | 折点编辑及刷新 | 0.989 | 0.055 | 94.5% |
| 50 | 电路求解 | 6.569 | 4.341 | 33.9% |
| 200 | 静止导线刷新 | 4.039 | 0.138 | 96.6% |
| 200 | 折点编辑及刷新 | 4.069 | 0.164 | 96.0% |
| 200 | 电路求解 | 6.905 | 4.480 | 35.1% |
| 500 | 静止导线刷新 | 10.623 | 0.318 | 97.0% |
| 500 | 折点编辑及刷新 | 10.778 | 0.351 | 96.7% |
| 500 | 电路求解 | 7.617 | 4.116 | 46.0% |

200/500 条静止刷新耗时下降至少 50% 的目标已达到，测得求解耗时也下降。

使用 Unity `GC.Alloc` 记录当前线程的分配事件，并用已知数组分配校准：静止刷新在 50/200/500 条样本中分别由 1100/4400/11000 次降为 0 次；单条折点编辑为 22 次；求解由 12031 次降为 6443 次。

**字节统计限制：**本机 Mono 的 `GC.GetAllocatedBytesForCurrentThread()` 恒为 0，整帧字节计数器也存在采样空值，相关试测已弃用。最终 CSV 保存单次调用的托管堆增量估计和可靠的分配事件次数；堆增量受分配块精度、GC 和编辑器线程影响，不能当作精确的逐帧分配字节数。方法依据 [Unity 托管堆统计说明](https://docs.unity3d.com/ja/2020.2/ScriptReference/Profiling.Profiler.GetMonoUsedSizeLong.html) 与 [当前线程分配记录说明](https://docs.unity3d.com/cn/2021.1/ScriptReference/Unity.Profiling.ProfilerRecorderOptions.CollectOnlyOnCurrentThread.html)。

有效原始数据：

- `Build/Reports/optimization-baseline-scene.csv`
- `Build/Reports/optimization-after-scene.csv`
- `Build/Reports/optimization-performance-summary.json`
- `Build/Reports/optimization-baseline-performance.xml`

测试源码保留两个显式性能用例，常规全量测试不执行计时基准；可通过类名筛选单独运行，并用 `-optimizationBenchmarkReport` 指定 CSV 输出。

## 最终验证

- EditMode：278 项通过，0 失败；另有 1 项显式性能测试不在常规运行中执行。报告：`Build/Reports/optimization-final-edit.xml`。
- PlayMode：2026-09-27 全量复跑 163 项通过，0 失败；另有 1 项显式性能测试不在常规运行中执行。报告：`Build/Reports/optimization-final-play.xml`。
- 资源检查：2026-09-27 通过，无 LFS 指针、必要文件或 `.meta` 缺失。报告：`Build/Reports/optimization-resource-integrity.json`。
- Windows x64 构建：2026-09-27 成功，Unity 返回码 0，构建报告大小 518,693,823 字节。程序：`Build/Windows-Optimized/ElectricalTraining.exe`；日志：`Build/Reports/optimization-build.log`。同目录的数据文件和 UnityPlayer.dll 必须随程序保留。
- Windows 实际窗口冒烟：2026-09-27 通过。通过物理端子拾取创建 `DuanZiPai_1.PLC_1_1M → DuanZiPai_1.KA3_1` 测试导线，验证 Ctrl+Z 撤销、Ctrl+Y 恢复及对应提示/导线显示；用原生保存/打开对话框往返 `Build/Reports/optimization-smoke.cc3d`，保存和加载均显示 1 条导线。浏览、拖动、接线、仿真、排故模式及正反视角切换正常。该连线用于操作冒烟，不是完整电气功能回路。
- 启动和操作日志未出现运行异常；退出时 Unity 分配器摘要中的 `Failed count` 是桶分配回退统计，不计为运行错误。记录见 `Build/Reports/optimization-player.log`、`Build/Reports/optimization-smoke-results.json`。冒烟结束后退出测试程序。
- `git diff --check` 通过；场景、模型、材质、ProjectSettings 和 Packages 未修改。所有代码、报告和构建保留本地，未提交或推送。

新增回归覆盖 63/64/65/128 次编辑的撤销恢复、历史独立性、300 次/300 帧静止刷新、对象复用及存档重绑、折点/端点/器件几何变化、接线缓存直接修改和替换、动态触点隔离、旧快照不变与振荡诊断。既有回归继续覆盖线槽避让、软跳线、仪表、G120、PLC 和液体仿真。

真实 PLC 硬件联调不属于本轮验证，既有通信回归不能等同于硬件验收。
