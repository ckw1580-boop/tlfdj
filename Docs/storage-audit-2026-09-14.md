# 项目存储占用检查 — 2026-09-14

检查路径：D:\unity程序\tlfdj。Unity 2022.3.62f3c1。仅检查，未删除项目文件，未修改源码、资源或配置。

**扫描结果：20,451,209,385 字节 = 19.05 GiB（20.45 GB），64,970 个文件，扫描错误 0。Build 占 68.05%；Assets 仅占 4.22%。**

统计口径：递归统计文件逻辑长度，包含隐藏的 .git；未发现重解析点。未统计 NTFS 簇开销、压缩占用或硬链接去重，因此实际“占用空间”可能不同。结果为生成本报告前的快照，不包括 Unity 安装、项目目录以外的全局包缓存。1 GiB = 1024³ 字节，1 MiB = 1024² 字节。

## 一级目录占用

| 目录 | 文件大小合计 | 占比 | 文件数 |
| --- | ---: | ---: | ---: |
| [Build](<D:/unity程序/tlfdj/Build>) | 12.96 GiB | 68.05% | 26991 |
| [Library](<D:/unity程序/tlfdj/Library>) | 1.92 GiB | 10.06% | 19091 |
| [OriginalAssetsSource](<D:/unity程序/tlfdj/OriginalAssetsSource>) | 1.90 GiB | 9.96% | 8975 |
| [.git](<D:/unity程序/tlfdj/.git>) | 884.80 MiB | 4.54% | 2633 |
| [Assets](<D:/unity程序/tlfdj/Assets>) | 822.29 MiB | 4.22% | 5436 |
| [lfs-storage-temp](<D:/unity程序/tlfdj/lfs-storage-temp>) | 571.88 MiB | 2.93% | 1556 |
| [Logs](<D:/unity程序/tlfdj/Logs>) | 26.88 MiB | 0.14% | 151 |
| [tmp](<D:/unity程序/tlfdj/tmp>) | 16.13 MiB | 0.08% | 61 |
| [obj](<D:/unity程序/tlfdj/obj>) | 1.65 MiB | 0.01% | 20 |
| [ProjectSettings](<D:/unity程序/tlfdj/ProjectSettings>) | 49.90 KiB | 0.00% | 23 |
| [UserSettings](<D:/unity程序/tlfdj/UserSettings>) | 26.92 KiB | 0.00% | 3 |
| [SafeNet Sentinel](<D:/unity程序/tlfdj/SafeNet Sentinel>) | 25.63 KiB | 0.00% | 2 |
| [Docs](<D:/unity程序/tlfdj/Docs>) | 19.64 KiB | 0.00% | 5 |
| [Tools](<D:/unity程序/tlfdj/Tools>) | 15.04 KiB | 0.00% | 5 |
| [Packages](<D:/unity程序/tlfdj/Packages>) | 11.69 KiB | 0.00% | 2 |
| [codex-objects](<D:/unity程序/tlfdj/codex-objects>) | 0.49 KiB | 0.00% | 2 |
| 根目录散文件 | 1.97 MiB | 0.01% | 见完整扫描记录 |

gh-config、lfs-tmp-git 为零文件占用的目录。codex-objects 仅 502 字节，不是主要问题。

## 优先清理清单

下列项目互不重叠。实际清理前关闭对应 Unity 项目、运行程序和仍在写入的下载任务；旧版本在不再需要回归对照时清理。

| 项目 | 可释放 | 条件与影响 |
| --- | ---: | --- |
| Build 中 17 份旧构建/QA 程序 | 8.38 GiB | 保留 Build/Windows 和最新的 Build/GrayWall；旧构建具体目录见下一表。删除会失去对应旧版本的直接运行副本。 |
| Build/Recovery/verified-project/Library | 1.45 GiB | 验证项目的导入缓存，关闭该副本后可删除；不触碰其源码。 |
| Build/Recovery 的两个 .zip.partial | 411.81 MiB | 下载中断的临时文件；确认不再续传。不是完整恢复包。 |
| lfs-storage-temp 中与 .git/lfs/objects 完全相同的 1,536 个对象 | 541.48 MiB | 已逐文件 SHA-256 验证一致；只清重复部分，保留剩余 20 个对象。 |
| 根项目 Logs 与 obj | 28.53 MiB | 日志与编译中间产物；如需故障日志先另存。 |

**按以上条件清理可释放 10.79 GiB，项目约剩 8.26 GiB。** 此方案保留两个完整 ZIP、验证副本源码、原始素材、当前两个程序版本、主项目 Library 和全部 Git 历史。

主项目 Library 另占 1.92 GiB，关闭 Unity 后也可清理；合计可释放 12.71 GiB，约剩 6.34 GiB。重新打开会重新导入资源、编译和恢复依赖缓存，耗时且部分包可能需要重新下载；持续开发时缓存还会增长，不应作为日常反复清理对象。[Unity 官方缓存说明](https://docs.unity3d.com/2022.3/Documentation/Manual/AssetDatabase.html)

## Build 目录明细

共 19 份完整运行程序目录，另有 Recovery 与 Reports。大多数程序副本分别重复包含约 343–345 MiB 的 sharedassets0.assets.resS；同名或相同大小不代表整个程序版本内容一致，应按整个旧版本目录管理。

| 目录 | 占用 | 建议 |
| --- | ---: | --- |
| [Build/Recovery](<D:/unity程序/tlfdj/Build/Recovery>) | 3.45 GiB | 分项清理，见恢复目录核查 |
| [Build/GrayWall](<D:/unity程序/tlfdj/Build/GrayWall>) | 515.22 MiB | 保留：最近生成的变体，不假定它与默认构建完全相同 |
| [Build/Windows](<D:/unity程序/tlfdj/Build/Windows>) | 515.22 MiB | 保留：README 指定的默认构建 |
| [Build/Windows-SevenContactors](<D:/unity程序/tlfdj/Build/Windows-SevenContactors>) | 514.83 MiB | 旧版本/QA 构建，不再需要回归时可清理 |
| [Build/Windows-MotorHalfSize](<D:/unity程序/tlfdj/Build/Windows-MotorHalfSize>) | 514.82 MiB | 旧版本/QA 构建，不再需要回归时可清理 |
| [Build/Windows-ThermalRelay](<D:/unity程序/tlfdj/Build/Windows-ThermalRelay>) | 514.82 MiB | 旧版本/QA 构建，不再需要回归时可清理 |
| [Build/Windows-RearContactor](<D:/unity程序/tlfdj/Build/Windows-RearContactor>) | 511.48 MiB | 旧版本/QA 构建，不再需要回归时可清理 |
| [Build/Windows-Contactor](<D:/unity程序/tlfdj/Build/Windows-Contactor>) | 511.47 MiB | 旧版本/QA 构建，不再需要回归时可清理 |
| [Build/Windows-SmallRelaySchematic](<D:/unity程序/tlfdj/Build/Windows-SmallRelaySchematic>) | 508.18 MiB | 旧版本/QA 构建，不再需要回归时可清理 |
| [Build/Windows-RelaySchematic](<D:/unity程序/tlfdj/Build/Windows-RelaySchematic>) | 505.38 MiB | 旧版本/QA 构建，不再需要回归时可清理 |
| [Build/Windows-PLC](<D:/unity程序/tlfdj/Build/Windows-PLC>) | 504.61 MiB | 旧版本/QA 构建，不再需要回归时可清理 |
| [Build/Windows-RearButtons](<D:/unity程序/tlfdj/Build/Windows-RearButtons>) | 504.61 MiB | 旧版本/QA 构建，不再需要回归时可清理 |
| [Build/Windows-MotorFix](<D:/unity程序/tlfdj/Build/Windows-MotorFix>) | 504.60 MiB | 旧版本/QA 构建，不再需要回归时可清理 |
| [Build/Windows-TachometerFix](<D:/unity程序/tlfdj/Build/Windows-TachometerFix>) | 504.45 MiB | 旧版本/QA 构建，不再需要回归时可清理 |
| [Build/LocalWiringQA](<D:/unity程序/tlfdj/Build/LocalWiringQA>) | 498.77 MiB | 旧版本/QA 构建，不再需要回归时可清理 |
| [Build/Windows_before_offset_2_5](<D:/unity程序/tlfdj/Build/Windows_before_offset_2_5>) | 497.26 MiB | 旧版本/QA 构建，不再需要回归时可清理 |
| [Build/Windows_before_fr_label](<D:/unity程序/tlfdj/Build/Windows_before_fr_label>) | 497.26 MiB | 旧版本/QA 构建，不再需要回归时可清理 |
| [Build/Windows_before_upper_annotations](<D:/unity程序/tlfdj/Build/Windows_before_upper_annotations>) | 497.26 MiB | 旧版本/QA 构建，不再需要回归时可清理 |
| [Build/Windows_old](<D:/unity程序/tlfdj/Build/Windows_old>) | 497.26 MiB | 旧版本/QA 构建，不再需要回归时可清理 |
| [Build/Windows_before_remove_old_annotations](<D:/unity程序/tlfdj/Build/Windows_before_remove_old_annotations>) | 497.26 MiB | 旧版本/QA 构建，不再需要回归时可清理 |
| [Build/Reports](<D:/unity程序/tlfdj/Build/Reports>) | 121.18 MiB | 保留近期验收报告；过期截图、日志可另行筛选，未计入预计释放 |

## 恢复目录核查

| 内容 | 占用 | 判断 |
| --- | ---: | --- |
| verified-project | 2.26 GiB | 解压后的完整工程副本，含 1.45 GiB Library；存在源码差异，不能直接按纯重复目录删除。 |
| tlfdj-unity-complete-20260913-202204.zip | 406.83 MiB | 较早恢复包；如不需要历史恢复版本可删除，但与 final 不是完全相同的包。 |
| tlfdj-unity-complete-final.zip | 406.83 MiB | 建议至少保留这一完整恢复包及对应 .sha256。 |
| github-main-lfs-interrupted.zip.partial | 208.89 MiB | 中断下载残留。 |
| github-main-lfs-verified.zip.partial | 202.92 MiB | 中断下载残留。 |

已逐个解压读取两个完整 ZIP 的 5,475 个条目并比较 SHA-256；差异仅出现在 ProjectInstaller.cs、RESTORE-MANIFEST.json 和 .gitattributes。两个 ZIP 不能称作完全重复。final ZIP 整体 SHA-256 与旁边的校验文件一致：

6fe3f7f9ca511f58054d7e2c3505c112753a6724b2ed4c86224d4c3c33087979

验证副本的 Assets、Packages、ProjectSettings、Tools、Docs 与主项目按路径和哈希比较，发现以下四个差异文件：

| 副本中的相对路径 | 差异 |
| --- | --- |
| Assets/ElectricalSim/Editor/ProjectInstaller.cs | 构建前校验及 EnsureSceneResources 的调用顺序不同。 |
| Assets/ElectricalSim/Runtime/TrainingSceneBootstrap.cs | 背墙颜色不同。 |
| Assets/ElectricalSim/Tests/PlayMode/RecoveryVisualCapture.cs | 主项目没有的恢复截图测试脚本。 |
| Assets/ElectricalSim/Tests/PlayMode/RecoveryVisualCapture.cs.meta | 对应元数据。 |

这四个文件合计约 143 KiB。若要删除整个 verified-project，应先保存这些差异，并检查副本根目录的恢复清单等是否需要留档。仅删除其 Library 不会影响这些差异文件。

## 原始素材与 Git

- [OriginalAssetsSource](<D:/unity程序/tlfdj/OriginalAssetsSource>)：1.90 GiB。README 和 OriginalAssetsImporter.cs 确认它是重新导入的源目录；当前正常运行使用 Assets/OriginalContent。这里包含额外原始网格、贴图、原程序 bundle 等，不是 Assets 的完整重复拷贝。建议归档到另一块磁盘后移出；同一磁盘内移动仅改变项目占用，不释放该磁盘空间。
- [.git](<D:/unity程序/tlfdj/.git>)：884.80 MiB。其中 lfs 约 577.53 MiB，普通 Git 对象约 306.31 MiB。保留，不手工删 objects/pack 或整个 .git。
- 已运行 git lfs prune --dry-run：工具报告有 16 个对象、约 37 MB 可清理。这只是本地预演，没有删除，也未验证远端对象可恢复。实际 prune 前应确认远端或备份完整。
- lfs-storage-temp 总计 571.88 MiB，其中 1,536 个对象（541.48 MiB）与当前 Git LFS 缓存同路径且哈希相同；另 20 个对象（30.40 MiB）在当前缓存没有同路径对象，暂留。最大独有对象为 31,851,128 字节，不能因目录名含 temp 就删掉。
- 当前 Git LFS 使用 .git/lfs/objects；未发现 .git/objects/info/alternates 或 GIT_OBJECT_DIRECTORY / GIT_INDEX_FILE 指向上述临时目录的配置。自定义工具未来是否仍使用临时数据不在本次只读检查的证明范围内。
- tmp 仅 16.13 MiB，包含 PDF、中间图片、PLC 依赖与比对文件；逐项确认后可清，收益很小，未计入释放估算。

## Assets 中的优化候选

| 内容 | 占用/数量 | 优化方向 |
| --- | --- | --- |
| Assets/OriginalContent/Texture2D | 403.80 MiB | 优先合并相同贴图；再评估分辨率、平台压缩与源文件无损压缩。 |
| Assets/OriginalContent/Mesh | 201.31 MiB | 合并重复 Mesh；按实际可见效果评估减面，需保留柜体和端子几何精度。 |
| Assets/OriginalContent/Font | 62.38 MiB | 先合并相同字体；评估汉字覆盖后再做字体子集，不能直接裁掉所需字符。 |
| 全项目 PNG 中 2048×2048 的贴图 | 152 张，283.62 MiB | 低细节背景与小物体可试降尺寸；端子文字、标牌和近景模型需视觉验证。 |
| MainFont SDF Atlas 4096×4096 | 3 张，共 40.08 MiB | 三份文件内容完全一致，先做引用合并；不要直接缩放 SDF 图集导致文字变差。 |

**对 Assets 中非 .meta 且至少 64 KiB 的同大小文件做 SHA-256 比较，发现 114 组相同内容，保留每组一份的理论源文件节省为 170.60 MiB。** 小于 64 KiB 的重复文件未纳入，因此不是全量去重上限。

其中 PNG 56 组可合并内容 114.67 MiB、.asset 56 组 33.90 MiB、字体 1 组 21.53 MiB、JSON 1 组 0.49 MiB。内容相同不代表用途、导入设置、GUID 或子资源 fileID 相同；需逐项选择保留对象，修复场景/Prefab/材质及动态加载路径，随后再删除多余资源及其 .meta，并验证运行和构建。尚未执行 Unity 依赖图分析，不能将这些候选直接列为未使用资源。[Unity 元数据说明](https://docs.unity3d.com/2022.3/Documentation/Manual/AssetMetadata.html)

| 重复候选 | 份数 | 理论可合并 |
| --- | ---: | ---: |
| [Assets/OriginalContent/Texture2D/MainFont SDF Atlas_0.png](<D:/unity程序/tlfdj/Assets/OriginalContent/Texture2D/MainFont SDF Atlas_0.png>)<br>[Assets/OriginalContent/Texture2D/MainFont SDF Atlas_1.png](<D:/unity程序/tlfdj/Assets/OriginalContent/Texture2D/MainFont SDF Atlas_1.png>)<br>[Assets/OriginalContent/Texture2D/MainFont SDF Atlas_2.png](<D:/unity程序/tlfdj/Assets/OriginalContent/Texture2D/MainFont SDF Atlas_2.png>) | 3 | 26.72 MiB |
| [Assets/OriginalContent/Font/STHeiti-Light-3_0.ttf](<D:/unity程序/tlfdj/Assets/OriginalContent/Font/STHeiti-Light-3_0.ttf>)<br>[Assets/OriginalContent/Font/STHeiti-Light-3.ttf](<D:/unity程序/tlfdj/Assets/OriginalContent/Font/STHeiti-Light-3.ttf>) | 2 | 21.53 MiB |
| [Assets/OriginalContent/Texture2D/PLC_Albedo_1.png](<D:/unity程序/tlfdj/Assets/OriginalContent/Texture2D/PLC_Albedo_1.png>)<br>[Assets/OriginalContent/Texture2D/PLC_Albedo_2.png](<D:/unity程序/tlfdj/Assets/OriginalContent/Texture2D/PLC_Albedo_2.png>)<br>[Assets/OriginalContent/Texture2D/PLC_Albedo_3.png](<D:/unity程序/tlfdj/Assets/OriginalContent/Texture2D/PLC_Albedo_3.png>)<br>[Assets/OriginalContent/Texture2D/PLC_Albedo_4.png](<D:/unity程序/tlfdj/Assets/OriginalContent/Texture2D/PLC_Albedo_4.png>)<br>[Assets/OriginalContent/Texture2D/PLC_Albedo_5.png](<D:/unity程序/tlfdj/Assets/OriginalContent/Texture2D/PLC_Albedo_5.png>) | 5 | 13.89 MiB |
| [Assets/OriginalContent/Mesh/NONE_0.asset](<D:/unity程序/tlfdj/Assets/OriginalContent/Mesh/NONE_0.asset>)<br>[Assets/OriginalContent/Mesh/NONE_1.asset](<D:/unity程序/tlfdj/Assets/OriginalContent/Mesh/NONE_1.asset>) | 2 | 10.27 MiB |
| [Assets/OriginalContent/Mesh/Object015_2.asset](<D:/unity程序/tlfdj/Assets/OriginalContent/Mesh/Object015_2.asset>)<br>[Assets/OriginalContent/Mesh/Object015_3.asset](<D:/unity程序/tlfdj/Assets/OriginalContent/Mesh/Object015_3.asset>) | 2 | 8.36 MiB |
| [Assets/OriginalContent/Texture2D/JiaZiBuJian_03 - Default_Normal_0.png](<D:/unity程序/tlfdj/Assets/OriginalContent/Texture2D/JiaZiBuJian_03 - Default_Normal_0.png>)<br>[Assets/OriginalContent/Texture2D/JiaZiBuJian_03 - Default_Normal_1.png](<D:/unity程序/tlfdj/Assets/OriginalContent/Texture2D/JiaZiBuJian_03 - Default_Normal_1.png>)<br>[Assets/OriginalContent/Texture2D/JiaZiBuJian_03 - Default_Normal_2.png](<D:/unity程序/tlfdj/Assets/OriginalContent/Texture2D/JiaZiBuJian_03 - Default_Normal_2.png>) | 3 | 7.19 MiB |
| [Assets/OriginalContent/Texture2D/DianXiang_ling_0.png](<D:/unity程序/tlfdj/Assets/OriginalContent/Texture2D/DianXiang_ling_0.png>)<br>[Assets/OriginalContent/Texture2D/DianXiang_ling_2.png](<D:/unity程序/tlfdj/Assets/OriginalContent/Texture2D/DianXiang_ling_2.png>) | 2 | 4.28 MiB |
| [Assets/OriginalContent/Texture2D/JiaZiBuJian_01 - Default_MetallicSmoothness_0.png](<D:/unity程序/tlfdj/Assets/OriginalContent/Texture2D/JiaZiBuJian_01 - Default_MetallicSmoothness_0.png>)<br>[Assets/OriginalContent/Texture2D/JiaZiBuJian_01 - Default_MetallicSmoothness_1.png](<D:/unity程序/tlfdj/Assets/OriginalContent/Texture2D/JiaZiBuJian_01 - Default_MetallicSmoothness_1.png>) | 2 | 3.95 MiB |
| [Assets/OriginalContent/Texture2D/JiaZiBuJian_03---Default_AlbedoTransparency_0.png](<D:/unity程序/tlfdj/Assets/OriginalContent/Texture2D/JiaZiBuJian_03---Default_AlbedoTransparency_0.png>)<br>[Assets/OriginalContent/Texture2D/JiaZiBuJian_03---Default_AlbedoTransparency_1.png](<D:/unity程序/tlfdj/Assets/OriginalContent/Texture2D/JiaZiBuJian_03---Default_AlbedoTransparency_1.png>)<br>[Assets/OriginalContent/Texture2D/JiaZiBuJian_03---Default_AlbedoTransparency_2.png](<D:/unity程序/tlfdj/Assets/OriginalContent/Texture2D/JiaZiBuJian_03---Default_AlbedoTransparency_2.png>) | 3 | 3.76 MiB |
| [Assets/OriginalContent/Texture2D/ZHUANZ_01 - Default_MetallicSmoothness_0.png](<D:/unity程序/tlfdj/Assets/OriginalContent/Texture2D/ZHUANZ_01 - Default_MetallicSmoothness_0.png>)<br>[Assets/OriginalContent/Texture2D/ZHUANZ_01 - Default_MetallicSmoothness_1.png](<D:/unity程序/tlfdj/Assets/OriginalContent/Texture2D/ZHUANZ_01 - Default_MetallicSmoothness_1.png>) | 2 | 2.83 MiB |
| [Assets/OriginalContent/Texture2D/XiChuang_LinJian_01--Default_AlbedoTransparency_0.png](<D:/unity程序/tlfdj/Assets/OriginalContent/Texture2D/XiChuang_LinJian_01--Default_AlbedoTransparency_0.png>)<br>[Assets/OriginalContent/Texture2D/XiChuang_LinJian_01--Default_AlbedoTransparency_1.png](<D:/unity程序/tlfdj/Assets/OriginalContent/Texture2D/XiChuang_LinJian_01--Default_AlbedoTransparency_1.png>) | 2 | 2.80 MiB |
| [Assets/OriginalContent/Texture2D/XiChuang_LinJian_01- Default_MetallicSmoothness_0.png](<D:/unity程序/tlfdj/Assets/OriginalContent/Texture2D/XiChuang_LinJian_01- Default_MetallicSmoothness_0.png>)<br>[Assets/OriginalContent/Texture2D/XiChuang_LinJian_01- Default_MetallicSmoothness_1.png](<D:/unity程序/tlfdj/Assets/OriginalContent/Texture2D/XiChuang_LinJian_01- Default_MetallicSmoothness_1.png>) | 2 | 2.73 MiB |

完整 114 组文件路径与哈希另见 [Docs/storage-duplicates-2026-09-14.csv](<D:/unity程序/tlfdj/Docs/storage-duplicates-2026-09-14.csv>)。

调整 Texture Importer 的 Max Size/压缩主要影响导入缓存与构建数据，不会直接缩小 Assets 中原始 PNG；若目标是工程源文件占用，需要实际优化源文件或合并重复资源。关闭 Read/Write 主要是运行内存优化，不应当作本次磁盘回收估算。[Unity 构建体积说明](https://docs.unity3d.com/2022.3/Documentation/Manual/ReducingFilesize.html)

Experiment.unity 虽未列入 EditorBuildSettings 的启动场景，但导入器/原场景生成流程会使用它；不能仅因未勾选构建就删除。OriginalLabEnvironment.prefab 约 34.94 MiB，是当前实训室资源，也不应按大文件直接删除。代码、测试和配置占用很小，清理它们不解决空间问题。

## 大文件列表（前 30 项）

| 文件 | 占用 |
| --- | ---: |
| [Build/Recovery/tlfdj-unity-complete-20260913-202204.zip](<D:/unity程序/tlfdj/Build/Recovery/tlfdj-unity-complete-20260913-202204.zip>) | 406.83 MiB |
| [Build/Recovery/tlfdj-unity-complete-final.zip](<D:/unity程序/tlfdj/Build/Recovery/tlfdj-unity-complete-final.zip>) | 406.83 MiB |
| [Build/Windows-TachometerFix/ElectricalTraining_Data/sharedassets0.assets.resS](<D:/unity程序/tlfdj/Build/Windows-TachometerFix/ElectricalTraining_Data/sharedassets0.assets.resS>) | 344.55 MiB |
| [Build/Windows-Contactor/ElectricalTraining_Data/sharedassets0.assets.resS](<D:/unity程序/tlfdj/Build/Windows-Contactor/ElectricalTraining_Data/sharedassets0.assets.resS>) | 344.55 MiB |
| [Build/Windows-RelaySchematic/ElectricalTraining_Data/sharedassets0.assets.resS](<D:/unity程序/tlfdj/Build/Windows-RelaySchematic/ElectricalTraining_Data/sharedassets0.assets.resS>) | 344.55 MiB |
| [Build/Windows/ElectricalTraining_Data/sharedassets0.assets.resS](<D:/unity程序/tlfdj/Build/Windows/ElectricalTraining_Data/sharedassets0.assets.resS>) | 344.55 MiB |
| [Build/Windows-RearButtons/ElectricalTraining_Data/sharedassets0.assets.resS](<D:/unity程序/tlfdj/Build/Windows-RearButtons/ElectricalTraining_Data/sharedassets0.assets.resS>) | 344.55 MiB |
| [Build/Windows-MotorHalfSize/ElectricalTraining_Data/sharedassets0.assets.resS](<D:/unity程序/tlfdj/Build/Windows-MotorHalfSize/ElectricalTraining_Data/sharedassets0.assets.resS>) | 344.55 MiB |
| [Build/Windows-PLC/ElectricalTraining_Data/sharedassets0.assets.resS](<D:/unity程序/tlfdj/Build/Windows-PLC/ElectricalTraining_Data/sharedassets0.assets.resS>) | 344.55 MiB |
| [Build/Windows-SmallRelaySchematic/ElectricalTraining_Data/sharedassets0.assets.resS](<D:/unity程序/tlfdj/Build/Windows-SmallRelaySchematic/ElectricalTraining_Data/sharedassets0.assets.resS>) | 344.55 MiB |
| [Library/PlayerDataCache/Win64/Data/sharedassets0.assets.resS](<D:/unity程序/tlfdj/Library/PlayerDataCache/Win64/Data/sharedassets0.assets.resS>) | 344.55 MiB |
| [Build/GrayWall/ElectricalTraining_Data/sharedassets0.assets.resS](<D:/unity程序/tlfdj/Build/GrayWall/ElectricalTraining_Data/sharedassets0.assets.resS>) | 344.55 MiB |
| [Build/LocalWiringQA/ElectricalTraining_Data/sharedassets0.assets.resS](<D:/unity程序/tlfdj/Build/LocalWiringQA/ElectricalTraining_Data/sharedassets0.assets.resS>) | 344.55 MiB |
| [Build/Windows-MotorFix/ElectricalTraining_Data/sharedassets0.assets.resS](<D:/unity程序/tlfdj/Build/Windows-MotorFix/ElectricalTraining_Data/sharedassets0.assets.resS>) | 344.55 MiB |
| [Build/Windows-ThermalRelay/ElectricalTraining_Data/sharedassets0.assets.resS](<D:/unity程序/tlfdj/Build/Windows-ThermalRelay/ElectricalTraining_Data/sharedassets0.assets.resS>) | 344.55 MiB |
| [Build/Windows-SevenContactors/ElectricalTraining_Data/sharedassets0.assets.resS](<D:/unity程序/tlfdj/Build/Windows-SevenContactors/ElectricalTraining_Data/sharedassets0.assets.resS>) | 344.55 MiB |
| [Build/Windows-RearContactor/ElectricalTraining_Data/sharedassets0.assets.resS](<D:/unity程序/tlfdj/Build/Windows-RearContactor/ElectricalTraining_Data/sharedassets0.assets.resS>) | 344.55 MiB |
| [Build/Windows_before_fr_label/ElectricalTraining_Data/sharedassets0.assets.resS](<D:/unity程序/tlfdj/Build/Windows_before_fr_label/ElectricalTraining_Data/sharedassets0.assets.resS>) | 343.19 MiB |
| [Build/Windows_before_remove_old_annotations/ElectricalTraining_Data/sharedassets0.assets.resS](<D:/unity程序/tlfdj/Build/Windows_before_remove_old_annotations/ElectricalTraining_Data/sharedassets0.assets.resS>) | 343.19 MiB |
| [Build/Windows_before_offset_2_5/ElectricalTraining_Data/sharedassets0.assets.resS](<D:/unity程序/tlfdj/Build/Windows_before_offset_2_5/ElectricalTraining_Data/sharedassets0.assets.resS>) | 343.19 MiB |
| [Build/Windows_before_upper_annotations/ElectricalTraining_Data/sharedassets0.assets.resS](<D:/unity程序/tlfdj/Build/Windows_before_upper_annotations/ElectricalTraining_Data/sharedassets0.assets.resS>) | 343.19 MiB |
| [Build/Windows_old/ElectricalTraining_Data/sharedassets0.assets.resS](<D:/unity程序/tlfdj/Build/Windows_old/ElectricalTraining_Data/sharedassets0.assets.resS>) | 343.19 MiB |
| [.git/objects/pack/pack-18a122f811d8fb1e6eca1bb4fa6b6a39f71bb5ed.pack](<D:/unity程序/tlfdj/.git/objects/pack/pack-18a122f811d8fb1e6eca1bb4fa6b6a39f71bb5ed.pack>) | 301.82 MiB |
| [OriginalAssetsSource/ExportedProject/Assets/StreamingAssets/aa/StandaloneWindows64/prefabs_assets_all_95d426aa64c3cdd14a97d227360e839c.bundle](<D:/unity程序/tlfdj/OriginalAssetsSource/ExportedProject/Assets/StreamingAssets/aa/StandaloneWindows64/prefabs_assets_all_95d426aa64c3cdd14a97d227360e839c.bundle>) | 227.88 MiB |
| [OriginalAssetsSource/ExportedProject/Assets/StreamingAssets/aa/StandaloneWindows64/scenes_scenes_all_b146e48548b63bfcd304ea8d86ce4b5f.bundle](<D:/unity程序/tlfdj/OriginalAssetsSource/ExportedProject/Assets/StreamingAssets/aa/StandaloneWindows64/scenes_scenes_all_b146e48548b63bfcd304ea8d86ce4b5f.bundle>) | 216.81 MiB |
| [Build/Recovery/github-main-lfs-interrupted.zip.partial](<D:/unity程序/tlfdj/Build/Recovery/github-main-lfs-interrupted.zip.partial>) | 208.89 MiB |
| [Build/Recovery/github-main-lfs-verified.zip.partial](<D:/unity程序/tlfdj/Build/Recovery/github-main-lfs-verified.zip.partial>) | 202.92 MiB |
| [Build/Windows-RearContactor/ElectricalTraining_Data/sharedassets0.assets](<D:/unity程序/tlfdj/Build/Windows-RearContactor/ElectricalTraining_Data/sharedassets0.assets>) | 77.34 MiB |
| [Build/Windows-RearButtons/ElectricalTraining_Data/sharedassets0.assets](<D:/unity程序/tlfdj/Build/Windows-RearButtons/ElectricalTraining_Data/sharedassets0.assets>) | 77.34 MiB |
| [Build/Windows-PLC/ElectricalTraining_Data/sharedassets0.assets](<D:/unity程序/tlfdj/Build/Windows-PLC/ElectricalTraining_Data/sharedassets0.assets>) | 77.34 MiB |

## 验证与后续维护

- 已运行 Tools/Test-ProjectResources.ps1，结果 PASS：没有检测到 LFS 指针或缺失的必需文件/.meta。主工程没有 RESTORE-MANIFEST.json，清单哈希验证数量为 0；此结果不是一次完整 PlayMode 或构建测试。
- 未启动 Unity、未重新构建、未改资源。推荐先清旧运行副本和验证缓存，再考虑改动 Assets。
- .gitignore 已忽略 Build、Library、Logs、obj、tmp、OriginalAssetsSource；忽略只影响版本跟踪，不会自动释放磁盘空间。
- 后续默认构建使用固定目录，只额外保留必要的一两个历史版本；完整恢复包放到项目外的备份磁盘，验证解压副本用完即归档差异并清理。
- 两个恢复 ZIP 内容有差异，verified-project 有独有文件，LFS 临时目录有未在当前缓存出现的对象；这些均已在建议中单独保留，不以名称或同大小推断可删。

进一步归档原始素材、保存验证副本差异后删除其余副本、只留 final 恢复包，再清主 Library，可使项目降到约 3.24 GiB；这是满足归档条件后的估算，未执行，且未计算 Assets 去重。主 Library 在后续开发中会重新生成。
