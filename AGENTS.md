# FluentControl AI 协作指南

本文件适用于整个桌面仓库。先理解用户本次目标，检查工作区已有修改，再做必要的局部变更；保留用户工作。按当前用户授权范围提交或发布，不把此前的测试结论套用到新改动上。

## 项目边界

- C# / .NET 8 / WinUI 3 的 Windows x64 桌面程序，使用 Win32 DDC/CI、WASAPI 和系统设置接口。
- 桌面应用不依赖 Node.js。Actions 的 JavaScript action 运行时与应用技术栈无关。
- 分享网站位于独立的 `chuanjianghe1024-cmd/FluentControl-Web`；不要在这里重新引入 `web/`、前端依赖或网站构建。品牌域名为 `fctrl.app`。
- 当前行为以代码、README 和 CHANGELOG 为准；`docs/profile-library-design.md` 是后续设计，不代表已实现。

## 先看哪里

以下应用路径均相对 `src/FluentControl/`；同格省略目录的文件沿用该格前一个目录。

| 范围 | 入口 |
| --- | --- |
| 启动、日志、单实例 | `App.xaml.cs`、`StartupLog.cs` |
| 主界面与初始化 | `MainWindow.xaml`、`MainWindow.xaml.cs` |
| 显示器界面 | `MainWindow.Monitors.cs` |
| 场景、导入导出 | `MainWindow.Profiles.cs`、`MainWindow.Exchange.cs` |
| 设置、托盘、快捷键、主题 | `MainWindow.Features.cs`、`MainWindow.Theme.cs` |
| 桌面面板 | `DesktopPanelWindow.cs`、`Services/DesktopLayer.cs`、`TransparentBackdrop.cs`（应用根目录） |
| 型号适配、只读采集 | `MainWindow.Adapters.cs`、`MainWindow.Adaptation.cs`、`Services/MonitorAdapters.cs`、`MonitorDiagnostics.cs` |
| DDC/CI 与能力 | `Services/MonitorService.cs`、`MonitorReadBatch.cs`、`MonitorCapabilityCache.cs`、`VcpCatalog.cs`、`VcpDiscovery.cs` |
| 控制及联动 | `Services/ControlChannel.cs`、`MonitorLinking.cs`、`BrightnessMapping.cs` |
| 数据与分享 | `Services/UserState.cs`、`ProfileUpdates.cs`、`ProfileGroups.cs`、`ProfileBundles.cs`、`ProfileExchange.cs` |
| 名称、音频、鼠标、自启动 | `Services/MonitorNames.cs`、`AudioService.cs`、`MouseService.cs`、`StartupService.cs` |
| 本地化 | `Services/Strings.cs`、`Services/Locales.json` |

验证与打包位于仓库根目录的 `tests/FluentControl.Tests/`、`scripts/`、`installer/` 和 `.github/workflows/`。

## 硬件控制约束

1. 功能发现只能读取，不以写入、重置或电源切换探测支持情况。未知私有码只做占位，不能猜测后下发。
2. 同一物理屏幕的读取保持顺序，当前最多两台不同屏幕并行。所有任务结束后才能释放物理句柄；不能用超时返回后任由原生调用继续运行并销毁其句柄。
3. 能力缓存仅保存经过校验的能力元数据，不保存句柄或当前控制值。键包含设备实例/连接身份，不能仅按型号复用；保留失效回退和强制重新检测。
4. 保留刷新与写入的生命周期保护，旧队列不能写入已替换或已释放的设备。某台失败不能阻止其他设备；准确报告部分完成、跳过和失败。
5. 联动取已连接设备能力的并集，不因型号不同拆成多个整体滑块。每个值只写到支持它的目标，UI 标明部分支持。
6. 连续值按真实范围转换，离散值必须校验允许选项，读取到的值不一定可重放。`0x72` 是 Gamma，`0x8A` 是饱和度；Gamma 不能当普通百分比，静音/OSD 操作保留其他字节。
7. 输入、电源、重置和按键锁保留确认。场景不存重置、电源和按键锁；输入切换最后执行。不宣称 VCP 支持等于厂商所有模式可控。

## 数据和界面约束

- 保存场景按字段合并：离线屏幕、暂不可读/不可写项、别名和亮度映射不能因为本次枚举缺失而丢失。涉及结构迁移时保留旧格式读取并设计备份/恢复。
- 分享协议见 `docs/monitor-profiles.md`：v1 兼容、v2 批量、UTF-8 可读 Unicode、严格大小及数量限制。公共配置不含本机路径、序列号、设备实例、音频/鼠标身份、命令或动态脚本；导入不直接操作硬件。
- 应用/型号/品牌筛选组合必须匹配同一场景，型号与品牌匹配同一屏幕，避免跨目标拼接结果。
- 保持 WinUI/Windows 11 风格、主题一致性及紧凑布局。用户可见文字经 `Strings.T/F` 本地化，维护八种语言相同键和占位符；不要翻译用户名称或硬件名称。
- 桌面面板锁定时不激活或升层、不写入设备；双击解锁，失焦/Esc 锁定。拖动、缩放、透明度与最小尺寸提示布局需要验证。
- 不修改系统默认音频路由，不为自启动索取管理员权限，不关闭系统防护。凭据、私钥和用户数据不能提交仓库。

## 构建与验证

在 Windows 上使用 .NET 8 SDK、Windows SDK 和 WinUI 构建组件。完整命令及测试副作用见 `docs/development.md`。

```powershell
dotnet run --project tests/FluentControl.Tests -c Release
dotnet build src/FluentControl/FluentControl.csproj -c Release -p:Platform=x64
```

- 按变更选择有意义的回归：硬件逻辑用模拟读写验证真实边界，UI 用 `Test-Startup.ps1 -UiTest`，安装变更用 `Test-Msi.ps1`。
- `--native` 会短暂修改并恢复鼠标设置；MSI 测试会实际安装、升级、卸载程序，只在可丢弃的 Windows 测试环境运行。不要在用户日常系统上顺手执行安装测试。
- Linux 或无显示器环境可做静态/纯逻辑验证，但不能声称通过 WinUI、MSI 或实体 DDC/CI 验证。记录实际运行的检查及未覆盖项。
- 文档修改不必增加镜像实现的测试；发布自动化需验证来源、摘要、不覆盖既有发布与重试行为。

## 打包与发布

- 保留 WinUI XAML/PRI、嵌入本地化资源和必要反射支持；不能为缩包随意删除 DLL 或启用未经验证的裁剪。更改依赖后使用干净 publish 目录。
- MSI 使用 WiX 5、当前用户安装、稳定 UpgradeCode/组件身份和既有数据目录。publish 与 MSI 使用同一个版本号；不要破坏升级或删除用户配置。
- `.github/workflows/build.yml` 先完成全部 Windows 检查再上传安装包；版本为 `0.3.<run_number>`。
- 正式发布通过 `.github/release.json` 指定成功构建及确切安装包，工作流校验来源和 SHA-256 后发布。标签指向该安装包的源码提交，不因文档更新改指向其他构建。
- 已发布标签和附件不能移动或替换；修复用新版本。签名状态如实记录，域名、Manufacturer 和文件摘要不等于 Authenticode 签名。

交付说明清楚写出改了什么、验证结果、提交/Release 链接和实际限制；尚未通过的 CI 或未部署的服务不能写成完成。


## 型号适配包

协议与操作流程见 `docs/monitor-adapters.md`。适配包与用户分享配置分离，固定官方 HTTPS 源、严格纯数据结构、完整型号和固件版本匹配；读取失败不授予写权限。只有显式安装的已审核包可提供已验证的私有原厂菜单指令，执行保留确认，不参与总配置、分享或聚合。不从未知私有码探测或采集差异猜测可写指令。报告仅供研究，不能自动发布为适配包。
