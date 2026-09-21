# 背部断路器导线遮挡修复验证

## 修改内容

- QF106/QF122 端子绑定各自的器件几何，引线从真实端子绕出本体后接入布线面。
- 布线高度场加入 DQG11 安装板及其副本；从 DQG01 合并网格中，根据断路器端子位置提取其背后最近的平行板面，不使用整个柜壳包围盒抬高导线。
- 纳入断路器下方 DQG18 支撑横梁，消除上部面板至水平线槽入口的遮挡。
- 预览、成品、高亮和鼠标编辑共用投影。端子位置、存档格式、用户折点及电路逻辑不变。

实际渲染发现上部挡板属于 DQG01，不属于最初计划中的 DQG11；只处理 DQG11 无法解决截图问题。上部横梁与盖板网格的厚度存在重叠，因此该处按横梁外侧 3 mm 间隙布线，并通过像素测试验证关闭盖板仍遮挡导线和选中高亮；其余线槽保留距盖内侧 10 mm 的规则。未修改模型、盖板开关规则或导线深度测试材质。

## 验证结果

Unity 2022.3.62f3c1：

- `Build/Reports/rear-breaker-final-edit.xml`：34/34 通过，覆盖导线几何、旋转坐标、合并柜壳板面提取及接线存档。
- `Build/Reports/rear-breaker-final-play.xml`：3/3 通过，覆盖两只背部断路器上下端子通往侧向与下方线槽、正视与斜视像素可见性、盖板关闭遮挡、预览一致性、节点命中，以及原有背部器件引线与存档往返。
- `Build/Reports/rear-breaker-regression-play.xml`：扩大回归首次运行 71/77 通过。其中本次相关的两项失败已修正并在上述最终场景报告中通过；正面断路器 5/5 和接线工程 13/13 通过。
- 人工检查 `Build/Reports/rear-breaker-QF122-L2-True-False.png` 和 `Build/Reports/rear-breaker-QF106-L2-False-True.png` 等实际 Unity 截图，断路器周围及紧邻的下方线槽入口不再出现原先的异常中断。
- `git diff --check` 通过。保留工作区原有未提交修改，未重新生成 Windows 发布包。

## 扩大回归中保留的失败

以下四项未在本次修复范围内修改，不能将扩大回归描述为全部通过：

- `CabinetBrandingCoversBothOriginalHeaderLogos`：预期标识对象为空；历史 `seven-contactors-flaky-check.xml` 中也有失败记录。
- `InverterLowerTerminalBoardAnnotationsUseOriginalLowerBoardGroups`：G120 备注位置断言不符；同一历史报告中也有失败记录。
- `DeviceBodyConnectionPointsAppearOnlyInFaultView`：测试预期正面断路器端子集合为空，但当前已有 QFFRONT3 本体端子。
- `LineTypeShowsTheCorrectConnectionPointGroups`：连接点显示断言不符；尚未单独定位根因。
