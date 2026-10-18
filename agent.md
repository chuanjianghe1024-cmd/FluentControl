# FluentControl 项目结构、开发规范与计划

本文件是桌面仓库统一维护的工程与 AI 协作指南，适用于整个仓库。`AGENTS.md` 是自动发现入口；修改规则时只维护本文件。

先理解用户本次目标，检查工作区已有修改，再做必要的局部变更；保留用户工作。按当前用户授权范围提交或发布，不把此前的测试结论套用到新改动上。

## 项目边界

- C# / .NET 8 / WinUI 3 的 Windows x64 桌面程序，使用 Win32 DDC/CI、WASAPI 和系统设置接口。
- 桌面应用不依赖 Node.js。Actions 的 JavaScript action 运行时与应用技术栈无关。
- 分享网站位于独立的 `chuanjianghe1024-cmd/FluentControl-Web`；不要在这里重新引入 `web/`、前端依赖或网站构建。品牌域名为 `fctrl.app`。
- 当前行为以代码与 CHANGELOG 为准。README 面向用户介绍产品；`docs/profile-library-design.md` 是后续设计备忘录，不代表其中所有内容已实现。

## 社区协作与 CI

- 按用户本次要求保留维护者直接推送 main 的能力；社区贡献走 PR。`CONTRIBUTING.md` 负责贡献入口，`docs/maintaining.md` 维护角色、CI 与未来规则切换步骤。
- `.github/rulesets/community-main.json` 仅为禁用的严格规则模板，不能自动应用或开启。未来经用户明确决定后，启用 PR、独立批准、讨论解决、必需检查；当前不得宣称分支保护已生效。CODEOWNERS 不等于强制审批，不授予访问权。
- 稳定必需检查候选名为 `Repository checks`、`Windows validation`。工作流保持每个 PR 都触发，文档跳过只在 job 内处理；最终门禁必须拒绝失败/取消，未知变更和比较失败走完整 Windows。
- PR CI 使用只读 token、不保留 checkout 凭据、不用 pull_request_target 执行贡献者代码；发布只在官方仓库 main 的独立流程进行。未来修改 job 名称须同步规则模板和验证器。
- Dependabot 仅提交依赖更新 PR，不自动合并或发布。不要为 fork CI 授予写 token、生产凭据或管理员令牌。

## 文档职责

- `README.md`：同页中英文产品简介、使用价值、主要功能、下载、兼容性、参考项目及产品方向；两种语言同步维护。
- `agent.md`：项目结构、开发约束、当前工程状态、详细任务和验收条件，是开发计划的统一入口。
- `AGENTS.md`：指向本文件，供 Codex 等工具自动发现，避免重复维护规则。
- `CHANGELOG.md` 与 `docs/releases/`：分别记录未发布变更和已发布版本，不把开发构建写成正式发布。
- `LICENSE`：按用户明确选择采用 MIT，版权署名为 FluentControl contributors；保留第三方原有许可，不将本仓库许可套用于独立的网站仓库。
- `docs/wiki/`：中英文用户手册及 Wiki 导航的维护源；同步流程、首次初始化和验证方法见 `docs/wiki-maintenance.md`。修改对应功能时同步用户说明，不能将已提交源文件等同于线上 Wiki 已发布。
- `docs/development.md`：完整构建、测试、安装与发布操作手册。
- `docs/monitor-profiles.md`、`docs/monitor-adapters.md`：文件格式、接口与型号适配协议；计划不能覆盖现行协议。
- `docs/profile-library-design.md`：配置组织与迁移的详细设计备忘录，实施状态以本文件和代码为准。
- `docs/code-signing.md`：签名方案与实际执行条件。

README 保留产品层面的计划，详细实现步骤、模块路径和验收清单放在本文件或链接的工程文档。宣传文字不得把未上线服务、未经实机验证的型号或设计稿写成已可用功能。

## 仓库结构

| 路径 | 职责 |
| --- | --- |
| `src/FluentControl/` | WinUI 主窗口、桌面面板、每屏软件菜单及其他窗口 |
| `src/FluentControl.DdcHost/` | 每个逻辑显示连接的独立原生 DDC 通信进程，与主程序共用离线 .NET 运行时 |
| `src/FluentControl/Services/` | 硬件通信、能力解析、配置存储、分享、适配及本地化 |
| `src/FluentControl/Assets/` | 应用图标与品牌资源 |
| `src/FluentControl/Properties/PublishProfiles/` | 离线安装包的发布配置 |
| `tests/FluentControl.Tests/` | 控制逻辑、配置、适配与可选原生接口回归 |
| `tests/release/` | 发布脚本的离线验证 |
| `tests/ci/` | 变更分类、必需检查接线和社区资料验证 |
| `tests/wiki/` | Wiki 链接校验、同步及页面保留验证 |
| `scripts/` | 启动、MSI、发布与签名辅助脚本 |
| `installer/` | MSI 与 Burn 定义；`FluentControl.Setup/` 为独立 WPF UI 安装前端 |
| `.github/workflows/` | Windows 构建验收、独立正式发布及 Wiki 同步流程 |
| `.github/release.json` | 正式发布的确切构建、附件及校验信息 |
| `docs/` | 协议、操作手册、设计备忘录与发布说明 |

## 修改入口

以下应用路径均相对 `src/FluentControl/`。

| 范围 | 入口 |
| --- | --- |
| 启动、日志、单实例 | `App.xaml.cs`、`StartupLog.cs` |
| 主界面与初始化 | `MainWindow.xaml`、`MainWindow.xaml.cs` |
| 显示器界面、软件 OSD | `MainWindow.Monitors.cs`、`MainWindow.Osd.cs` |
| 型号配置库 | `MainWindow.Library.cs`、`Services/MonitorPresetLibrary.cs` |
| DPI 与识别提示 | `WindowPlacement.cs`、`MonitorIdentificationWindow.cs`、`Services/WindowGeometry.cs` |
| 场景、导入导出 | `MainWindow.Profiles.cs`、`MainWindow.Exchange.cs` |
| 设置、托盘、快捷键、主题 | `MainWindow.Features.cs`、`MainWindow.Theme.cs` |
| 设置导航、手动更新 | `MainWindow.Settings.cs`、`MainWindow.Updates.cs`、`Services/ReleaseUpdates.cs`；界面回归 `MainWindow.SettingsChecks.cs` |
| 桌面面板 | `DesktopPanelWindow.cs`、`Services/DesktopLayer.cs`、`TransparentBackdrop.cs` |
| 本地 OSD 配对 | `MainWindow.OsdPairing.cs`、`Services/OsdPairing.cs`、`Services/OsdCandidateDiscovery.cs`；界面回归 `MainWindow.OsdPairingChecks.cs` |
| 型号适配、只读采集 | `MainWindow.Adapters.cs`、`MainWindow.Adaptation.cs`、`Services/MonitorAdapters.cs`、`Services/MonitorDiagnostics.cs` |
| DDC/CI 与能力 | `Services/MonitorService.cs`、`Services/MonitorReadBatch.cs`、`Services/MonitorCapabilityCache.cs`、`Services/VcpCatalog.cs`、`Services/VcpDiscovery.cs` |
| 控制及联动 | `Services/ControlChannel.cs`、`Services/MonitorLinking.cs`、`Services/BrightnessMapping.cs` |
| 数据与分享 | `Services/UserState.cs`、`Services/ProfileUpdates.cs`、`Services/ProfileGroups.cs`、`Services/ProfileBundles.cs`、`Services/ProfileExchange.cs` |
| 名称、音频、鼠标、自启动 | `Services/MonitorNames.cs`、`Services/AudioService.cs`、`Services/MouseService.cs`、`Services/StartupService.cs` |
| 系统音量与输入 | `MainWindow.Audio.cs`、`Services/SystemAudioControls.cs`；原生后端为 `Services/AudioService.cs` |
| 本地化 | `Services/Strings.cs`、`Services/Locales.json` |
| 模拟设备与原生界面回归 | `Services/UiTestData.cs`、`MainWindow.Regression.cs`、`MainWindow.Features.cs` |

验证与打包位于仓库根目录的 `tests/FluentControl.Tests/`、`scripts/`、`installer/` 和 `.github/workflows/`。

## 硬件控制约束

1. 常规功能发现只能读取，不以写入、重置或电源切换探测支持情况。按用户 2026-10-12 明确要求，本地 OSD 配对会从本次有效能力声明自动列出未知代码（包括 E0 以下未定义代码）：声明了值则列出这些值；未声明值则提供明确标为试验值的 0–5 及用户自填值。必须逐次由用户点击试发，再按实际效果绑定；生成列表、选择代码、导入和打开窗口都不写入。已知标准功能、重置和信息项单独分类，不能当作未知导航候选。手工具体指令及来源入口保留；禁止自动扫描、穷举或把事件读值转换成写命令。
2. 同一物理屏幕的读取保持顺序，当前最多两台不同屏幕并行。所有任务结束后才能释放物理句柄；主程序不持有物理句柄；DDC 辅助进程拥有并顺序使用句柄。通信超时中止该进程并永久失效旧客户端，不能用超时返回后任由原生调用继续运行并从别的线程销毁其句柄。
3. 能力缓存仅保存经过校验的能力元数据，不保存句柄或当前控制值。键包含设备实例/连接身份，不能仅按型号复用；保留失效回退和强制重新检测。
4. 保留刷新与写入的生命周期保护，旧队列不能写入已替换或已释放的设备。某台失败不能阻止其他设备；准确报告部分完成、跳过和失败。
5. 联动取已连接设备能力的并集，不因型号不同拆成多个整体滑块。每个值只写到支持它的目标，UI 标明部分支持。
6. 连续值按真实范围转换，离散值必须校验允许选项，读取到的值不一定可重放。`0x72` 是 Gamma，`0x8A` 是饱和度；Gamma 不能当普通百分比，静音/OSD 操作保留其他字节。
7. 输入、电源、重置和按键锁保留确认。场景不存重置、电源和按键锁；输入切换最后执行。不宣称 VCP 支持等于厂商所有模式可控。
8. 色温预设 `0x14` 和场景模式 `0xDC` 保留有限次数的写后回读。只重读、不重复写入；不一致时显示原始请求值和实际值，保留硬件回读结果，不伪造成功或按猜测交换编码。
9. FC 软件菜单、原厂 OSD 启用状态和原厂菜单导航是三种不同能力。未声明但可读的 `0xCA` 仅展示只读状态，不授予写入权限；无已验证的型号指令时仍显示适配状态。Windows 音频端点音量与显示器 DDC/CI 音量分别处理，不按设备名称猜测绑定。
10. OSD `0xCA` 的选择器是可重复发送的指令入口，不能用上次读数选中当前指令；完成、失败或取消后清空选择。写成功后读回失败须说明“已发送，状态未确认”，不伪造硬件状态，也不自动重复写入；常规入口写前读取失败仍阻止覆盖其他按键字段。独立配对窗口可由用户明确选择沿用当前会话已经成功读取的高字节；不从磁盘或其他屏幕继承，关闭窗口即失效，并记录这一选择。启用 OSD 是否弹出原厂菜单由型号固件决定，不能据此推断完整导航能力。
11. 色温、颜色预设及场景模式也采用独立的重复选择入口，真实读回值单独展示。写入后读回失败将该通道标为未确认；不保存旧值到新的总配置快照或型号预设，合并更新保留已有配置字段。成功读回后解除未确认；不一致仍保留真实值，不猜测编码。

## 数据和界面约束

- 系统色温见 `Services/SystemColor.cs` / `MainWindow.SystemColor.cs` / `docs/system-color-temperature.md`：SDR 兼容接口，不能与 DDC 色温、实测 CCT 或 ICC 校色混同。HDR/ACM/复制输出/未知状态禁写；每次写前重定位、捕获原曲线、先落盘恢复记录，再单次写和回读。外部曲线变化不得抢写或被旧恢复覆盖。正常退出在关窗前等待恢复；崩溃恢复由用户显式触发。仅本机总配置保存独立 `display-color/` 键，不进入型号预设和分享。无启动自动应用、定时写入或私有 Night Light 注册表操作。
- Windows HDR 通过 `Services/DisplayHdr.cs` 和 `MainWindow.Hdr.cs` 独立控制，与 DDC 能力分离。以精确目标接口路径匹配，写前重解析；Windows 11 24H2 使用 HDR 专用请求，不能把广色域/ACM 当 HDR。切换走 `gate` / `generation`，单次写入和有限只读回读，随后刷新重建 DDC 连接；不自动重放旧参数，不加入场景、预设或启动动作。详见 `docs/windows-hdr.md`。

- 保存场景按字段合并：离线屏幕、暂不可读/不可写项、别名和亮度映射不能因为本次枚举缺失而丢失。涉及结构迁移时保留旧格式读取并设计备份/恢复。
- 分享协议见 `docs/monitor-profiles.md`：v1 兼容、v2 批量、UTF-8 可读 Unicode、严格大小及数量限制。公共配置不含本机路径、序列号、设备实例、音频/鼠标身份、命令或动态脚本；导入不直接操作硬件。
- 应用/型号/品牌筛选组合必须匹配同一场景，型号与品牌匹配同一屏幕，避免跨目标拼接结果。
- 保持 WinUI/Windows 11 风格、主题一致性及紧凑布局。用户可见文字经 `Strings.T/F` 本地化，维护八种语言相同键和占位符；不要翻译用户名称或硬件名称。
- 桌面面板锁定时不激活或升层、不写入设备；双击必须立即解锁并激活到普通窗口前台，不能依赖第三次点击或永久置顶；失焦/Esc 锁定。桌面恢复任务不得覆盖更晚的解锁操作，Win+D 后解锁也必须保持前台。原生测试检查真实点击、前台句柄、Z 序及恢复定时器；拖动、缩放、透明度与最小尺寸提示布局同样需要验证。
- 不修改系统默认音频路由，不为自启动索取管理员权限，不关闭系统防护。凭据、私钥和用户数据不能提交仓库。
- 更新仅由用户点击触发，固定官方仓库的最新正式 Release；数字版本比较，不能降级或采用草稿/预发布。Setup EXE / MSI 必须匹配官方附件 URL、大小和 SHA-256，下载与安装前分别验证；失败/取消清理临时文件，不自动安装或关闭应用。安装调用优先使用经过验证的官方离线 Setup EXE，旧 Release 兼容 MSI；Setup 存在但资料无效时不得静默降级，先保存状态并等待待写入操作，关闭时取消网络任务。
- 设置子菜单独立滚动、保留当前分类。刷新、切语言或切分类不能丢失更新请求状态，不重复发请求；更新文案同步八种语言。网络回归使用可注入的 HttpClient，不执行模拟安装包。

## 构建与验证

在 Windows 上使用 .NET 8 SDK、Windows SDK 和 WinUI 构建组件。完整命令及测试副作用见 `docs/development.md`。

```powershell
dotnet build src/FluentControl/FluentControl.csproj -c Debug -p:Platform=x64
dotnet run --project src/FluentControl/FluentControl.csproj -p:Platform=x64
dotnet run --project tests/FluentControl.Tests -c Release
dotnet build src/FluentControl/FluentControl.csproj -c Release -p:Platform=x64
```

- 按变更选择有意义的回归：硬件逻辑用模拟读写验证真实边界，UI 用 `Test-Startup.ps1 -UiTest`，安装变更用 `Test-Msi.ps1`。
- `--native` 会短暂修改并恢复鼠标设置；MSI 测试会实际安装、升级、卸载程序，只在可丢弃的 Windows 测试环境运行。不要在用户日常系统上顺手执行安装测试。
- Linux 或无显示器环境可做静态/纯逻辑验证，但不能声称通过 WinUI、MSI 或实体 DDC/CI 验证。记录实际运行的检查及未覆盖项。
- 文档修改不必增加镜像实现的测试；发布自动化需验证来源、摘要、不覆盖既有发布与重试行为。

## 打包与发布

- 保留 WinUI XAML/PRI、嵌入本地化资源和必要反射支持；不能为缩包随意删除 DLL 或启用未经验证的裁剪。更改依赖后使用干净 publish 目录。
- MSI 使用 WiX 7（所有者已于 2026-10-13 接受对应 OSMF EULA；未来主版本需重新审查）、当前用户安装、稳定 UpgradeCode/组件身份和既有数据目录。publish 与 MSI 使用同一个版本号；不要破坏升级或删除用户配置。
- 安装向导允许新安装选择目录；升级从 HKCU 登记恢复并锁定原目录，不能移动共享的稳定组件。开机启动复用应用的当前用户 `FluentControl.lnk` 与 `--background`，以实际启动项预选，不按旧安装标记恢复；新安装默认关闭。MSI 完成页的立即启动只由成功后的“完成”按钮触发，Setup 由独立“启动”按钮触发，静默、修复、卸载、取消及需要重启时不能启动。回归入口 `tests/installer/InstallerWizardFixture.cs` / `scripts/Test-Msi.ps1`。
- 现代安装前端见 `docs/modern-installer.md`：独立 .NET Framework 4.8 WPF UI + WiX 7 Burn，使用 Windows 自带 Framework，离线包含原 MSI，主应用仍为自包含 .NET 8。前端禁止重复打包运行时，未压缩前端上限 20 MiB、EXE 相对 MSI 的额外体积上限 12 MiB；Burn high 压缩，记录逐文件体积。保留当前用户范围、MSI 组件身份和原目录；不将 WPF 依赖加入主程序。UI 文字使用安装器自己的 `Strings.T/F` 与八语言资源；目录选择使用系统 Common Item Dialog。取消等待 Burn 回滚，关闭不强杀进程，3010 不启动应用或自动重启。Windows 验收包含真实 CLR、目录选择/取消及固定旧 .NET 8 Bundle 跨运行时升级。
- `.github/workflows/build.yml` 先完成全部 Windows、MSI 与 Setup 检查再分别上传 MSI/EXE；版本为 `0.3.<run_number>`。
- 正式发布通过 `.github/release.json` 指定成功构建及确切安装包，可选 setup 对象必须与 MSI 来自同一个 main 构建，工作流校验来源和 SHA-256 后发布。标签指向该安装包的源码提交，不因文档更新改指向其他构建。
- Snap 预览使用独立 `.github/preview-release.json`（`prerelease: true`）及 `docs/releases/previews/v<版本>-snap.md`，标签为 `v<版本>-snap`；同样只提升完整成功 main 构建的确切附件，不设为 Latest，不改变正式版更新通道。
- 已发布标签和附件不能移动或替换；修复用新版本。签名状态如实记录，域名、Manufacturer 和文件摘要不等于 Authenticode 签名。
- `docs/releases/v<版本>.md` 是对应 Release 正文的维护入口，采用同页中英文，突出用户收益、功能变化、升级和实际限制；不要把后续开发构建的修复追记为旧安装包的能力。标题首行为 `# FluentControl v<版本>`，链接使用绝对地址。
- 修改发布说明会触发发布工作流：先验证既有发布，再用 `scripts/publish-release.py --sync-notes` 同步已发布正式版本的正文。同步只发送 `body`，核对前后标签、标题、发布状态、附件身份与摘要；未发布版本跳过，不因补写说明创建新 Release。

交付说明清楚写出改了什么、验证结果、提交/Release 链接和实际限制；尚未通过的 CI 或未部署的服务不能写成完成。


## 型号适配包

协议与操作流程见 `docs/monitor-adapters.md`。适配包与用户分享配置分离，固定官方 HTTPS 源、严格纯数据结构、完整型号和固件版本匹配；读取失败不授予写权限。只有显式安装的已审核包可提供已验证的私有原厂菜单指令，执行保留确认，不参与总配置、分享或聚合。不从未知私有码探测或采集差异猜测可写指令。报告仅供研究，不能自动发布为适配包。

## 本地 OSD 配对实验

用户授权范围：离线测试、确认、映射、遥控与本地文件交换。上传不是前置条件，不自动访问网站。协议见 `docs/osd-pairing.md`。新的 `fluentcontrol.osd-pairing` 格式与场景、型号适配包独立，不修改既有数据兼容约束。

- 默认三条 `0xCA` 候选；第三条按 MCCS 版本或明确能力值开放，每次二次确认。原厂菜单是否弹出由用户观察；传输成功和回读成功分别记录。
- 候选优先来自本次有效能力声明中的未知代码；代码和值的声明与用户选择的试验值必须区分并随记录保存。每次发送与遥控重放校验当前会话声明，不能用旧文件代替。也支持明确安装的型号适配包、精确匹配的本地文件或手工输入。远程操作只使用本机用户确认产生的绑定，不与总配置、聚合或快捷键混用。
- 导入不下发指令、不导入可执行绑定，保留来源试验记录并要求当前屏幕重新验证；相同型号的两台连接不自动共享本机绑定。
- `0x02 / 0x52 / 0x03` 采集仅在用户点击后短时运行，不写应答；`0x03` 是事件报告，不能当作通用按键注入。没有取值列表的已声明未知代码提供单次试验入口，来源自动记为当前能力声明，参数标为用户选择的试验值；不宣称是已支持的方向键。
- 操作共用生命周期 gate；关闭/刷新取消排队操作并等待在途原生调用完成；保存已完成写入的证据。单个文件限 4 MiB；512 条候选、512 次试验、64 个会话、32 次事件采集，重新配对前本地归档。
- 本机记录默认不含原始能力串与设备实例；导出原始串需要显式勾选。连接/HDR/环境说明为用户填写；不能宣称已在实体 HKC 或 HWV 显示器上验证。

## 当前交互与持久化约定

- 显示器、音频、鼠标并行初始化；显示器读取按前述同屏顺序和最多两屏并发执行。先发布仅含亮度/对比度的实时结果，再逐屏补齐高级功能，每屏之间释放 gate；能力缓存有效期 7 天、最多 32 个条目；“重新检测”绕过缓存。当前值始终从设备读取。
- 总配置保存全部设备快照；型号预设用于单屏复用。总配置保留来源引用和独立参数副本，不能因预设后续编辑或删除而改变。库视图合并已连接型号与已有预设型号，不为展示空分组创建伪预设。
- 声音页面只显示「音量」「输入」两个逻辑控制，不显示或选择具体音频设备，不合并默认通话设备。原生后端每次操作解析 `Role.Console` 的 Render/Capture 默认端点；同等处理物理和虚拟端点，不修改系统路由。设备及音量通知只排队，200ms 合并后在生命周期门控内读取，禁止在 COM 回调中读写、注销或释放端点。
- 音频配置键固定为 `audio/system-output/volume`、`audio/system-input/volume`，保存前刷新真实音量与静音；不可用项不写入快照，更新总配置时保留已存值。旧端点音量数据留存但不执行，缺少系统控制快照时提示更新配置。旧桌面音频选项备份到 `.before-system-audio-v1.bak` 后迁移为两个系统项，不能从不透明端点 ID 猜测方向。
- 启动不自动应用上次配置。导入仅保存，用户选择应用时才写入设备；应用标签目前用于筛选，不自动监控进程或切换场景。
- 桌面面板不透明度为 10%–85%，默认 30%；整体与单独模式各自保留控制项，离线选择不丢失。自动尺寸的可见行数上限为 1–16，更多内容滚动查看。
- 关闭主窗口默认隐藏到托盘；彻底退出在托盘菜单中。单实例重复启动唤起已有窗口。自启动默认关闭，使用当前用户启动目录快捷方式；便携版迁移后需更新路径。
- 默认快捷键：`Ctrl+Alt+Shift+Space` 打开主面板，`Ctrl+Alt+Shift+←/→` 跨组切换配置，`Ctrl+Alt+Shift+↓` 隐藏主面板。可以整体关闭，冲突需要反馈。
- 窗口按目标屏幕 DPI 换算逻辑尺寸；识别提示长名称换行并增高。普通状态提示约 4 秒、警告约 8 秒后消失，切页清除。

| 数据 | 当前路径 |
| --- | --- |
| 程序安装 | 默认 `%LOCALAPPDATA%\Programs\FluentControl`，新安装可自定义；升级使用 HKCU 记录的原目录 |
| 设置和总配置、型号预设 | `%LOCALAPPDATA%\FluentControl\user-state.json` |
| 屏幕名称 | `%LOCALAPPDATA%\FluentControl\monitor-names.json` |
| 能力缓存 | `%LOCALAPPDATA%\FluentControl\monitor-capabilities.json` |
| DDC 中断恢复标记 | `%LOCALAPPDATA%\FluentControl\DdcRecovery\`，仅连接哈希与高级探测状态 |
| 本地 OSD 配对 | `%LOCALAPPDATA%\FluentControl\OsdPairings\`，型号/固件/能力指纹与连接身份的哈希文件名；重新配对时保留 archive 文件 |
| 诊断日志 | `%LOCALAPPDATA%\FluentControl\Logs\startup.log` |
| 型号库首次迁移备份 | `%LOCALAPPDATA%\FluentControl\user-state.json.before-model-library-v1.bak` |

升级与卸载保留个人数据。连接或驱动变化可能改变设备身份，不应据此静默丢弃旧配置。公开诊断与分享仍遵守隐私边界；用户实际上传的诊断文件不提交仓库。

## 开发状态与详细计划

状态基线：**2026-10-18**。这里只列工程进度与后续验收；不因条目写入计划就宣称已完成或已发布。

### 托盘菜单

- `TrayMenuWindow.cs` 使用 WinUI MenuFlyout 和独立透明工具窗口；主窗隐藏时也能显示，菜单关闭隐藏宿主、退出销毁。`ShellIntegration` 只负责通知与托盘坐标，命令仍走 ShellCommand。主题同步覆盖浅色、深色和自动主题，沿用现有八语言文案；回归入口 `MainWindow.TrayChecks.cs`。此变更及透明背景主题回调修复收入 v0.3.135，通过原生托盘明暗主题回归；不在 v0.3.129-snap 安装包中。

### 当前正式版 v0.3.135

- 本地光照订阅：`Services/LocalLightSensor.cs` 与自动调节设置支持 RP2040 VID 限定的串口发现、READ/PING 身份握手、持久设备绑定及 0.2 秒至 24 小时输入/滑块/预设。`LightHistory.cs` 保存本地 64 MiB 分段历史，按来源分页查看；记录开关与自动亮度独立。固件在 `tools/light-source/rp2040/main.py`，配套私有仓库 `FluentControl-Sensors`；不依赖运行中的 Python 桥接。v0.3.135 来源主干构建 #135 / `d7221cdfbca31a6a584da58aef250cb24d649021`，通过 Windows、MSI 与 Setup 自动化验收；实体传感器联动未验收，C3/CH552 固件和 FC 刷写未完成。

- Snap 预览 `v0.3.129-snap` 使用主干构建 #129 / `dece26a38d476a28b109f74f18146a2ef31da2fd`。Windows、模拟自动调节界面、MSI 与 Setup 验收通过；当时正式通道为 v0.3.118，现由 v0.3.135 收入上述功能；实体硬件未验收。

- 自动亮度与光照订阅：`AdaptivePresets.cs` 保存独立 lux/clock 曲线；`AdaptiveAutomation.cs` 提供统一 `fluentcontrol.light-sample` JSON、过滤和平滑及 NOAA 本地太阳公式。`MainWindow.Automation*.cs` 提供运行时和设置，默认关闭；用户明确启用后允许定时亮度及 FC 主题（不修改系统主题/Gamma），持久启用可在下次运行继续。共用 gate/generation，手动保持 10 分钟，网络失败暂停、屏幕写入失败需重新开启，HDR/未知状态不自动写。订阅是动态数据源，不是远程预设安装器；导入曲线不自动开启；用户配置的源码不执行。`tools/light-source/server.py` 是独立本机模拟接口。原生 RP2040 USB 订阅已接入；实体硬件联动、BLE 接入及学习未完成。详见 `docs/adaptive-presets.md`。

- `Services/SystemBrightness.cs` 通过系统 WMI COM 自动化提供内屏亮度，不增加运行时依赖；只读发现 `WmiMonitorBrightness` 与 `WmiMonitorBrightnessMethods`，按完整 PnP 路径与 WMI 实例后缀匹配，歧义/无有效档位不开放写入。刷新销毁控制器，旧闭包拒绝写入；操作共用现有 gate，写前确认活动连接并重读，选最近支持档位，单次写入后由通用通道回读。Windows 亮度 `VcpCode` 为空；复用 `brightness` 通道接入现有界面/联动/桌面/场景/预设，匹配成功的屏幕不启动 DDC 探测。原生 WMI 查询失败不影响其他 DDC 显示器。WinUI 模拟回归位于 `MainWindow.SystemBrightnessChecks.cs`。尚未实机验证，不新增传感器采集、自动亮度或低蓝光自动化。
- 用户计划：传感器组装后增加环境光自适应与本地偏好学习，参考追光护眼使用教程的交互，独立实现；具体边界见 [docs/adaptive-brightness-plan.md](docs/adaptive-brightness-plan.md)。已增加通用 HTTP 光照来源、模拟器与显式启用的自动调节；原生 RP2040 USB 订阅已接入；BLE、学习和实物联动验证仍待完成。

- 应用主题保存于 `AppSettings.Theme`（`system/light/dark`，旧设置和未知值回退到 `system`）；设置 → 常规可立即切换。将 `RequestedTheme` 设置在 `ShellRoot`，覆盖导航及启动界面，窗口标题和已打开的 FC 菜单同步；桌面面板保留独立文字偏好。
- 动态卡片、型号标记和识别背景经 `App.xaml` 的样式使用 `ThemeResource`；不要通过 `Application.Current.Resources` 一次性取出主题画刷并赋给动态控件。对话框显式继承宿主主题。原生主题回归在 `MainWindow.ThemeChecks.cs`，包含同一组设置卡片连续切换、文字对比度、已打开的 FC 菜单、持久化及恢复跟随系统。

### 已完成的基础

- `v0.3.118` 发布来源为主干构建 #118 / `1688c9aaeec388c363548316c5276aa1ce5c043e`；同一构建的 Setup（53,475,263 字节）与 MSI（50,696,405 字节）通过来源及摘要校验后发布。包含现代安装向导、前端运行时缩包、优先 Setup 的手动更新、v0.3.92 之后的显示控制修复与社区文档。完整 Windows/MSI/Setup 回归通过，实体显示器和混合 DPI 验收边界不变。详见 `docs/releases/v0.3.118.md`。

- `v0.3.92` 发布来源为主分支构建 #92 / `b9785f4766c254cd8d138ff17687e4bf29678303` 的确切 MSI，正式标签固定指向该源码。增加按屏 HDR 常驻限制提示；切换关闭并刷新后移除。完整产品变化见 `docs/releases/v0.3.92.md`。
- 正式版本 `v0.3.92`：每屏系统色温软件调整、原曲线恢复、本机总配置保存、持久恢复记录与外部曲线变更处理；仅已确认 SDR 独立输出，HDR/ACM 不走 legacy gamma。ICC/MHC2 未接入，实体 GPU/显示器效果待本机验证。
- 正式版本 `v0.3.92`：每屏 Windows HDR 开关及切换后重新检测，内外屏均按 Windows 能力报告；区分 HDR/WCG、策略限制与状态未确认。WMI 内屏亮度和 OEM 专有接口未接入，实体 HDR 切换待本机验证。另含分阶段显示基础控制与高级功能；按逻辑显示连接隔离的 DDC 辅助进程、8 秒 IPC 限时、高级扫描恢复标记和普通只读传输重试。机制与验证边界见 `docs/windows-hdr.md`、`docs/ddc-reliability.md`；USB/HID 仍仅在 `docs/usb-hid-adaptation.md` 中记录研究及实机步骤。
- 正式版本 `v0.3.92` 配对 v3：用户确认后可绑定自定义按钮/选项组；0–2000ms 发送等待、可关闭写后回读，试验保存实际策略；视频接口为用户手动记录。旧 v1/v2 可读，导入不激活任何自定义控件、不覆盖本机兼容设置；失败观察撤销该命令全部绑定。没有自动生成未知连续范围的滑块。

- 正式版本 `v0.3.75`：离线 OSD 配对、已声明未知指令自动候选与单次试发、用户观察绑定和本机遥控、完整 JSON 记录；包含 OSD、色温和场景模式重复选择及失败读回处理。发布复用构建 #75 / `cf780020086231155e1150987c6c3b845cb85d16` 的 MSI，配对界面用模拟显示器验证，不宣称已完成实体型号导航适配。
- 正式版本 `v0.3.67`：安装目录选择、开机启动选项、完成后立即启动；Windows 实际向导覆盖中文/空格目录与返回导航，升级保留目录/自启动并支持取消，修复尊重应用关闭自启动，卸载保留配置。测试使用 Unicode Shell Link API，不能用 WScript.Shell 的 ANSI 路径结果判定中文快捷方式损坏。
- 正式版本 `v0.3.60`：六个设置子菜单、手动检查官方 GitHub 正式版本、校验下载与用户选择后启动 MSI；README 品牌图、下载状态及问题反馈模板已完善。仓库 About / Social preview 设置需单独应用，不能与已提交素材混淆。
- 正式版本 `v0.3.57`：在 v0.3.50 的多屏控制、总配置、型号预设库、软件 OSD、适配框架及桌面快捷操作基础上，加入两个系统音频控制、通知同步与旧配置迁移，以及桌面双击立即前置。
- v0.3.50 之后的 OSD 状态展示、色温/场景模式回读校验和日志、无预设的已连接型号分组，以及 DDC/CI 音量说明也收入 v0.3.57；旧 Release 的附件和功能说明保持原版本边界。
- Windows CI 覆盖控制逻辑、原生接口、模拟设备界面、Win+D、托盘、MSI 安装/升级/卸载和配置保留。不能将这些结果写成真实 HKC、实体 4K / 150% 或混合 DPI 多屏已经验证。
- 在线型号查询与报告提交的客户端入口、配套网站的报告审核代码已开发；网站尚未部署，生产登录、数据库和域名接入仍未完成。本地导入导出不依赖它们。

### 下一步：硬件兼容性与实际体验

| 任务 | 相关入口 | 完成条件 |
| --- | --- | --- |
| HKC 色温和场景模式排查 | `Services/VcpDiscovery.cs`、`Services/ControlChannel.cs`、`Services/MonitorService.cs`、采集助手 | 对 P272U PRO / HKC2752 与 PG271U / HKC2701 分别记录固件、连接、HDR/节能状态、请求原始码、回读原始码和实体 OSD 结果；区分延迟、未接受与编码差异，再决定是否发布适配 |
| 原厂 OSD 遥控适配 | `Services/MonitorAdapters.cs`、`MainWindow.Adapters.cs`、`MainWindow.Osd.cs` | 先取得型号及固件的协议或实机证据；逐项确认打开、关闭、方向及确认动作，未验证的动作保持不可用；不能用其他品牌命令试写 HKC |
| USB/HID 通道研究 | 现有适配与设备识别边界；尚无通用 HID 适配实现 | 确认目标提供可用的 USB 控制接口、身份和协议后再设计传输层；保持 DDC 与 USB 能力独立，扩展协议前同步桌面、网站校验和测试 |
| 系统音量与虚拟线路实测 | `MainWindow.Audio.cs`、`Services/SystemAudioControls.cs`、`Services/AudioService.cs` | 验证切换默认输出/输入、Voicemeeter/NVIDIA Broadcast 等虚拟端点、外部静音、设备断开恢复和总配置重放；保持仅两个系统控制，不绑定显示器、不干预用户路由；真实虚拟音频驱动仍需本机验证 |
| 4K / 150% 与混合 DPI 实测 | `WindowPlacement.cs`、`MonitorIdentificationWindow.cs`、`DesktopPanelWindow.cs` | 在实体多屏验证主窗、软件 OSD、识别提示与桌面面板的换屏、文字、缩放和可点击区域；记录实测环境 |
| 安装占用问题复测 | `installer/`、`scripts/Test-Msi.ps1` | 用用户实际日志确认占用来源；保持当前用户安装、稳定组件、事务回滚及不关闭无关程序的行为 |

### 之后：上线分享与适配服务

工作在独立的 `FluentControl-Web` 仓库进行，桌面仓库只做必要的协议和入口接入。

1. 配置数据库、迁移、Hyperdrive、登录和 `fctrl.app` 域名，验证生产环境可访问后再更改 README 的上线状态。
2. 完成配置查询、认证提交、作者归属、适配报告私有队列、维护者审核及撤回的端到端验证。
3. 验证精确型号/固件匹配、无匹配项、网络失败、损坏包、撤回和离线已安装适配的行为；公共配置不能携带本机身份或可执行内容。
4. 实机验证过的适配才进入审核发布流程；报告数量、参数相似度或读取成功均不能替代写入证据。
5. 桌面端与网站分别报告构建、部署和可用状态；网站代码合并不等于服务上线。

### 后续迭代：设备、配置与自动化

| 方向 | 实施要点 | 验收重点 |
| --- | --- | --- |
| 热插拔与状态同步 | 增加设备变化处理与适度的读取刷新；使用现有生命周期门控 | 不释放仍在使用的句柄，不让旧请求覆盖新设备/新场景，不持续高频轮询 |
| 持久设备档案与离线绑定 | 参考配置库设计备忘录；先验证身份和离线合并，再考虑收藏夹与延后绑定 | 同型号换接口不能误换左右参数；模糊身份显式待认领；迁移有备份、失败回退及旧格式兼容 |
| 导入导出与社区更新 | 统一预览、批量操作、来源和版本差异处理 | 离线可保存；本地修改不被上游更新覆盖；下载不自动操作硬件 |
| 应用联动 | 明确前台应用规则、进入前快照和手动修改优先级 | 当前仅为计划；退出恢复与新场景不冲突，敏感指令不参与自动操作 |
| 定时调节与快捷操作 | 共用受控执行队列；考虑时区、工作日、睡眠恢复与节流 | 不与手动操作或应用规则相互覆盖，允许关闭并说明当前生效规则 |
| 内屏及更多硬件 | 评估 WMI 亮度和厂商 SDK，逐项验证能力 | 许可、实际接口和设备行为确认后再承诺支持，不从外接 DDC 能力推断内屏能力 |
| 代码签名 | 按 `docs/code-signing.md` 落实真实签名与验证 | 对已签名产物做验证；在此之前保持“未签名”的准确说明，不覆盖已发布附件 |

配置组织的详细数据设计见 [docs/profile-library-design.md](docs/profile-library-design.md)。其中的收藏夹、持久档案、统一绑定和桌面内嵌社区等属于后续设计；已实现的型号预设库与总配置快照按当前代码维护，不重复重做。

## 外部参考的使用方式

| 项目 | 适用参考 | 使用边界 |
| --- | --- | --- |
| [Twinkle Tray](https://github.com/xanderfrangos/twinkle-tray) | 分阶段检测、隔离进程、异常探测恢复和错误分类 | 独立 C# 实现，不引入 Electron；禁止照搬写入重试或将旧值缓存当成当前状态 |
| [ddcutil](https://github.com/rockowitz/ddcutil) / [UDF 文档](https://www.ddcutil.com/udf/) | 能力解析、用户定义功能、按型号覆盖选项 | 自定义定义不自动证明硬件支持写入 |
| [ddccontrol-db](https://github.com/ddccontrol/ddccontrol-db) | 独立型号描述库、贡献与校验流程 | 跨型号和跨固件不能直接继承写入权限 |
| [msigd](https://github.com/couriersud/msigd) | MSI USB/HID 协议、设备识别和型号差异 | MSI 协议不视为 HKC 协议，也不等同于通用原厂菜单导航 |
| [ddc-mode-switcher](https://github.com/Gunther-Schulz/ddc-mode-switcher) | ASUS XG27JCG 的 OSD 动作序列和执行后状态检查 | 项目记录的 `0x03` 行为属于该型号证据，不能把它推广成所有显示器的模拟按键接口 |
| [ddc-toolkit](https://github.com/andres-valencia/ddc-toolkit) | Lenovo R45w-30 的映射记录、单项实体调整前后对比方法 | 仅借鉴有证据的研究流程；常规发现保持只读；未知代码只能在用户明确点击的本地实验入口试发，不能自动生成已验证绑定 |

引用外部实现时记录来源、适用型号、固件和验证状态；引入代码或数据前核对对应版本的许可证。工程文档中的参考列表不代表运行时依赖或已经集成的功能。

## 仓库展示

仓库简介、主页、Topics 的期望值在 `.github/repository-metadata.json`；分享图与可编辑源在 `docs/assets/`，操作与实际应用边界见 `docs/repository-presentation.md`。提交素材不等于 GitHub About 或 Social preview 已生效，必须读取页面核验；网站未部署前下载主页使用正式 Release 链接，许可证已按用户明确授权采用 MIT；变更许可仍需用户授权，不承诺搜索排名。
