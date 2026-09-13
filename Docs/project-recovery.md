# 项目下载、恢复与完整打包

本项目使用 Unity **2022.3.62f3c1**。GitHub 源码 ZIP 是否包含模型、贴图、字体和资源注册表，取决于仓库的 LFS 压缩包设置。只有文件名而没有真实内容的 ZIP 无法恢复画面；重新导入和删除 Library 无法下载这些内容。

## 推荐：完整项目 ZIP

使用维护者提供的 `tlfdj-unity-complete-*.zip`，解压到一个新目录。不要覆盖已有修改。

在解压目录打开 PowerShell，执行：

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\Tools\Test-ProjectResources.ps1 -ReportPath .\Build\Reports\resource-integrity.json
```

看到 PASS 后，用 Unity Hub 添加同时包含 Assets、Packages、ProjectSettings 的目录，以 2022.3.62f3c1 打开。首次打开保持联网，让 Package Manager 下载依赖。打开 `Assets/Scenes/ElectricalTraining.unity`，点击 Play。

完整包不包含 Library 和 PackageCache，因此并不保证在全新电脑上离线完成首次依赖安装。运行时的默认离线行为不变。

完整包附带 `RESTORE-MANIFEST.json`，检查工具会逐个验证记录文件的大小和 SHA-256。它用于刚解压后的验收；开始开发后修改文件会导致对应哈希检查失败，这是预期行为。需要重新打包修改后的项目时，将旧清单另存到项目目录之外，再运行打包工具。

## 使用 GitHub Download ZIP

仓库管理员进入 Settings → General → Archives，启用 **Include Git LFS objects in archives**，然后重新下载 ZIP。之前下载的 ZIP 不会自动补齐。

官方说明：https://docs.github.com/en/repositories/managing-your-repositorys-settings-and-features/managing-repository-settings/managing-git-lfs-objects-in-archives-of-your-repository

如打开某个 `.asset` / `.prefab` / 图片看到以下内容，说明仍是指针，必须重新获取真实文件：

```text
version https://git-lfs.github.com/spec/v1
oid sha256:...
size ...
```

## 使用 Git 克隆

安装 Git 与 Git LFS 后，在用于存放项目的目录执行：

```powershell
git lfs install
git clone https://github.com/ckw1580-boop/tlfdj.git tlfdj-complete
cd tlfdj-complete
git lfs pull
powershell -NoProfile -ExecutionPolicy Bypass -File .\Tools\Test-ProjectResources.ps1
```

普通 ZIP 解压目录没有 `.git`，不能直接在那里运行 `git lfs pull`。若下载报对象不存在或配额问题，维护者需要补传对象或处理 GitHub LFS 服务问题。

## 维护者：上传资源与生成完整包

从拥有真实资源和 `.git` 的原项目检查 LFS 对象并补传：

```powershell
git lfs fsck --objects
git lfs push --all origin main
```

`--objects` 校验实际对象。旧提交中的部分普通文本文件与当时的宽泛 LFS 规则不一致，会使不带选项的 `git lfs fsck` 报指针规则问题；它与对象缺失是两类问题。本项目将 ProjectSettings 显式保留为普通文本，后续提交时生效，无需重写历史。

在 Unity 菜单运行 **Electrical Sim → Validate Project Resources**，通过后执行：

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\Tools\Build-CompleteProject.ps1
```

输出到 `Build/Recovery/`，包括 ZIP 和 SHA-256 校验文件。工具从本地实际文件打包，不使用会产生 LFS 指针的 `git archive`。打包前检测缺失文件、LFS 指针和缺失的资源 `.meta`，打包后重新读取全部压缩内容并核对哈希；失败时不会产生标记为成功的 ZIP，只可能留下 `.partial` 诊断文件。只打包 Assets、Packages、ProjectSettings、Tools、Docs 和 README、Git 配置说明文件，不包含仓库凭据、原程序、Library、构建程序或个人数据目录。

正式发布前将 ZIP 解压到新目录，运行资源检查，再用上述 Unity 版本完成首次导入与场景运行验收。将经过验收的完整 ZIP 作为 GitHub Release 附件交付；Release 自动生成的 Source code ZIP 仍受仓库 LFS 设置影响。

## 项目内保护

- 打开 Unity 后自动检查资源，失败时提示恢复办法并将完整问题列表写入 `Build/Reports/resource-integrity.txt`。
- 点击 Play 前检查实际文件和当前场景的原始资源引用；失败时取消 Play。
- 自定义构建菜单及标准 Unity 构建回调均执行资源检查。
- 运行时若原始注册表、环境或设备 Prefab 缺失，启动立即报错，不再默默运行方块、圆柱组成的替代实训场景。
- `Electrical Sim → Install Training Scene` 会绑定现有原始资源注册表，避免重建启动场景后丢失该引用。

文件检查工具用于确认资源已下载和解压完整；Unity 菜单检查进一步验证已导入的注册表和设备引用。最终画面仍需以实际场景运行验收为准。
