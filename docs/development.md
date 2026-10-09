# 开发、验证与发布

## 开发环境

Windows x64、.NET 8 SDK、Windows SDK、WinUI 构建组件（可通过 Visual Studio 2022 安装）。安装器工具通过仓库 `.config/dotnet-tools.json` 恢复，使用 WiX 5。源码入口与修改约束见 [AGENTS.md](../AGENTS.md)。

```powershell
dotnet build src/FluentControl/FluentControl.csproj -c Debug -p:Platform=x64
dotnet run --project src/FluentControl/FluentControl.csproj -p:Platform=x64
dotnet run --project tests/FluentControl.Tests -c Release
```

桌面应用和 MSI 不使用 Node.js。Actions 官方组件自身使用的 Node 运行时不属于应用依赖。网站在独立的 FluentControl-Web 仓库构建。

## 本地生成 MSI

选择未发布的新版本号；下面以首发版本展示命令，不能用重新编译的文件覆盖已发布附件。更换依赖后先清理专用 `publish` 输出目录，避免旧依赖残留。

```powershell
$releaseVersion = '0.3.33'
dotnet publish src/FluentControl/FluentControl.csproj -c Release -p:PublishProfile=Installer -p:Platform=x64 -p:Version=$releaseVersion -o publish
./scripts/Build-Msi.ps1 -Version $releaseVersion
```

输出为 `artifacts/installer/FluentControl-<版本>-x64.msi`，内部包含压缩 CAB 和离线运行库。保留 PRI/XAML，不做未经验证的 DLL 删除或反射裁剪。真实签名流程见 [code-signing.md](code-signing.md)。

安装、升级或卸载前，请从托盘菜单退出 FluentControl；关闭主窗口仅会隐藏到托盘。安装包设置 `MSIRESTARTMANAGERCONTROL=Disable`，不使用 Restart Manager 关闭或重启其他程序；保留标准 `FilesInUse` 对话框、真实文件锁与必要的重启提示。这是包级设置，不修改 Windows 策略，也不跳过 `InstallValidate`。收到真实文件占用提示时，退出对应程序后重试；静默部署方必须处理返回码 3010，不能视为所有文件都已立即替换。

## 验证范围

```powershell
./scripts/Test-Startup.ps1
./scripts/Test-Startup.ps1 -UiTest
```

启动测试针对 `publish` 目录；`-UiTest` 使用模拟设备进行界面回归。真实显示器测试应记录型号、固件、线缆/转接方式、DDC/CI/HDR/ECO 状态，并对照 OSD 核验。能力和当前值的缓存行为、异常屏幕隔离、同屏请求顺序属于读取优化的关键回归。

窗口尺寸使用 XAML 逻辑像素，经目标窗口 DPI 换算后调用 `ResizeClient`；跨屏弹窗先定位到目标显示器，再读取该窗口 DPI，工作区使用 Win32 的绝对坐标。纯逻辑测试覆盖 4K 的 100%–200% 缩放、负坐标副屏及受限工作区；WinUI 回归检查当前测试屏幕 DPI 下的原生客户区尺寸、长识别名称换行和文字边界。虚拟运行器不能代替实体 4K / 150% 及混合 DPI 双屏拖动验证，不能把计算测试描述成这些硬件场景已实测。

以下命令只用于可丢弃的 Windows 测试环境：

```powershell
dotnet run --project tests/FluentControl.Tests -c Release -- --native
./scripts/Test-Msi.ps1 -Version $releaseVersion
```

`--native` 短暂修改并恢复鼠标设置。MSI 测试会实际安装、升级、启动、检查托盘和单实例、卸载并核对个人数据保留。Windows build 工作流完成这些检查后才上传安装包。无 Windows/实体设备环境的检查不能替代这些验证。

安装回归还会保持一个独立测试窗口运行：它加载安装目录外的同名运行库，安装、升级和卸载不能关闭它。另一个测试窗口加载安装目录内的运行库：升级时未变更的运行库应保留，不能先由旧 MSI 删除后重装；卸载确实需要删除该文件时，必须产生标准占用提示，取消后产品、文件和进程完整保留，释放文件后再继续验证。旧版测试 MSI 移除新属性，以覆盖从仍启用 Restart Manager 的旧安装包升级。测试夹具只在 CI 临时目录编译，不随产品发布；它不能代替用户机器上 PowerToys、输入法等具体进程的复现日志。

主要升级安排在 `afterInstallExecute`：先注册新版本对稳定组件的引用，再在同一个可回滚事务中移除旧产品。严格保留每个安装路径对应的组件 GUID、HKCU keypath 和目录，不能为新版本重生成组件身份。这样旧 MSI 不会再次删除仍被新版使用的未变更运行库；真实被更新或删除的文件仍受 Windows Installer 占用规则约束。

## 发布已通过的构建

正式 Release 复用已通过验收的 MSI，不为发布再编译一次。发布工作流为 `Publish release`，仅在主分支发布清单或发布脚本发生变化、或手动运行时执行。

1. 在 `Windows build` 选择已成功完成的主分支构建，读取版本、提交 SHA、run ID、artifact ID、ZIP 摘要和 MSI 摘要/大小。
2. 写好 `docs/releases/v<版本>.md`，更新 CHANGELOG 与 README 下载入口。
3. 更新 `.github/release.json`，填写确切构建及文件信息。提交清单就是请求发布该版本，不能填写尚未验证的产物。
4. 工作流校验构建来源、成功状态、产物归属、ZIP 和 MSI 摘要，创建草稿并上传 MSI 与 `SHA256SUMS.txt`；核对附件后转为正式 Latest Release。
5. 检查 Release、标签目标与下载附件。标签指向原构建源码，主分支随后更新的文档不改变安装包来源。

工作流仅使用本仓库 `GITHUB_TOKEN` 的 `actions: read` 与 `contents: write` 权限，无需个人访问令牌。已发布版本只验证、不改写；失败草稿可重试，但已有同名不同内容的附件会阻止发布。不要移动发布标签、覆盖附件或把哈希校验描述为代码签名。

发布脚本的本地验证不访问 GitHub、不发布内容：

```bash
python3 scripts/publish-release.py --validate-only
python3 -m unittest discover -s tests/release -v
```
