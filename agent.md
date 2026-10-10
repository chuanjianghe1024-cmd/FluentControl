# FluentControl 项目结构、开发规范与计划

本文件是桌面仓库统一维护的工程与 AI 协作指南，适用于整个仓库。`AGENTS.md` 是自动发现入口；修改规则时只维护本文件。

先理解用户本次目标，检查工作区已有修改，再做必要的局部变更；保留用户工作。按当前用户授权范围提交或发布，不把此前的测试结论套用到新改动上。

## 项目边界

- C# / .NET 8 / WinUI 3 的 Windows x64 桌面程序，使用 Win32 DDC/CI、WASAPI 和系统设置接口。
- 桌面应用不依赖 Node.js。Actions 的 JavaScript action 运行时与应用技术栈无关。
- 分享网站位于独立的 `chuanjianghe1024-cmd/FluentControl-Web`；不要在这里重新引入 `web/`、前端依赖或网站构建。品牌域名为 `fctrl.app`。
- 当前行为以代码与 CHANGELOG 为准。README 面向用户介绍产品；`docs/profile-library-design.md` 是后续设计备忘录，不代表其中所有内容已实现。

## 文档职责

- `README.md`：同页中英文产品简介、使用价值、主要功能、下载、兼容性、参考项目及产品方向；两种语言同步维护。
- `agent.md`：项目结构、开发约束、当前工程状态、详细任务和验收条件，是开发计划的统一入口。
- `AGENTS.md`：指向本文件，供 Codex 等工具自动发现，避免重复维护规则。
- `CHANGELOG.md` 与 `docs/releases/`：分别记录未发布变更和已发布版本，不把开发构建写成正式发布。
- `docs/development.md`：完整构建、测试、安装与发布操作手册。
- `docs/monitor-profiles.md`、`docs/monitor-adapters.md`：文件格式、接口与型号适配协议；计划不能覆盖现行协议。
- `docs/profile-library-design.md`：配置组织与迁移的详细设计备忘录，实施状态以本文件和代码为准。
- `docs/code-signing.md`：签名方案与实际执行条件。

README 保留产品层面的计划，详细实现步骤、模块路径和验收清单放在本文件或链接的工程文档。宣传文字不得把未上线服务、未经实机验证的型号或设计稿写成已可用功能。

## 仓库结构

| 路径 | 职责 |
| --- | --- |
| `src/FluentControl/` | WinUI 主窗口、桌面面板、每屏软件菜单及其他窗口 |
| `src/FluentControl/Services/` | 硬件通信、能力解析、配置存储、分享、适配及本地化 |
| `src/FluentControl/Assets/` | 应用图标与品牌资源 |
| `src/FluentControl/Properties/PublishProfiles/` | 离线安装包的发布配置 |
| `tests/FluentControl.Tests/` | 控制逻辑、配置、适配与可选原生接口回归 |
| `tests/release/` | 发布脚本的离线验证 |
| `scripts/` | 启动、MSI、发布与签名辅助脚本 |
| `installer/` | WiX 安装器定义及组件组织 |
| `.github/workflows/` | Windows 构建验收与独立正式发布流程 |
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
| 桌面面板 | `DesktopPanelWindow.cs`、`Services/DesktopLayer.cs`、`TransparentBackdrop.cs` |
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

1. 功能发现只能读取，不以写入、重置或电源切换探测支持情况。未知私有码只做占位，不能猜测后下发。
2. 同一物理屏幕的读取保持顺序，当前最多两台不同屏幕并行。所有任务结束后才能释放物理句柄；不能用超时返回后任由原生调用继续运行并销毁其句柄。
3. 能力缓存仅保存经过校验的能力元数据，不保存句柄或当前控制值。键包含设备实例/连接身份，不能仅按型号复用；保留失效回退和强制重新检测。
4. 保留刷新与写入的生命周期保护，旧队列不能写入已替换或已释放的设备。某台失败不能阻止其他设备；准确报告部分完成、跳过和失败。
5. 联动取已连接设备能力的并集，不因型号不同拆成多个整体滑块。每个值只写到支持它的目标，UI 标明部分支持。
6. 连续值按真实范围转换，离散值必须校验允许选项，读取到的值不一定可重放。`0x72` 是 Gamma，`0x8A` 是饱和度；Gamma 不能当普通百分比，静音/OSD 操作保留其他字节。
7. 输入、电源、重置和按键锁保留确认。场景不存重置、电源和按键锁；输入切换最后执行。不宣称 VCP 支持等于厂商所有模式可控。
8. 色温预设 `0x14` 和场景模式 `0xDC` 保留有限次数的写后回读。只重读、不重复写入；不一致时显示原始请求值和实际值，保留硬件回读结果，不伪造成功或按猜测交换编码。
9. FC 软件菜单、原厂 OSD 启用状态和原厂菜单导航是三种不同能力。未声明但可读的 `0xCA` 仅展示只读状态，不授予写入权限；无已验证的型号指令时仍显示适配状态。Windows 音频端点音量与显示器 DDC/CI 音量分别处理，不按设备名称猜测绑定。

## 数据和界面约束

- 保存场景按字段合并：离线屏幕、暂不可读/不可写项、别名和亮度映射不能因为本次枚举缺失而丢失。涉及结构迁移时保留旧格式读取并设计备份/恢复。
- 分享协议见 `docs/monitor-profiles.md`：v1 兼容、v2 批量、UTF-8 可读 Unicode、严格大小及数量限制。公共配置不含本机路径、序列号、设备实例、音频/鼠标身份、命令或动态脚本；导入不直接操作硬件。
- 应用/型号/品牌筛选组合必须匹配同一场景，型号与品牌匹配同一屏幕，避免跨目标拼接结果。
- 保持 WinUI/Windows 11 风格、主题一致性及紧凑布局。用户可见文字经 `Strings.T/F` 本地化，维护八种语言相同键和占位符；不要翻译用户名称或硬件名称。
- 桌面面板锁定时不激活或升层、不写入设备；双击必须立即解锁并激活到普通窗口前台，不能依赖第三次点击或永久置顶；失焦/Esc 锁定。桌面恢复任务不得覆盖更晚的解锁操作，Win+D 后解锁也必须保持前台。原生测试检查真实点击、前台句柄、Z 序及恢复定时器；拖动、缩放、透明度与最小尺寸提示布局同样需要验证。
- 不修改系统默认音频路由，不为自启动索取管理员权限，不关闭系统防护。凭据、私钥和用户数据不能提交仓库。

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
- MSI 使用 WiX 5、当前用户安装、稳定 UpgradeCode/组件身份和既有数据目录。publish 与 MSI 使用同一个版本号；不要破坏升级或删除用户配置。
- `.github/workflows/build.yml` 先完成全部 Windows 检查再上传安装包；版本为 `0.3.<run_number>`。
- 正式发布通过 `.github/release.json` 指定成功构建及确切安装包，工作流校验来源和 SHA-256 后发布。标签指向该安装包的源码提交，不因文档更新改指向其他构建。
- 已发布标签和附件不能移动或替换；修复用新版本。签名状态如实记录，域名、Manufacturer 和文件摘要不等于 Authenticode 签名。
- `docs/releases/v<版本>.md` 是对应 Release 正文的维护入口，采用同页中英文，突出用户收益、功能变化、升级和实际限制；不要把后续开发构建的修复追记为旧安装包的能力。标题首行为 `# FluentControl v<版本>`，链接使用绝对地址。
- 修改发布说明会触发发布工作流：先验证既有发布，再用 `scripts/publish-release.py --sync-notes` 同步已发布正式版本的正文。同步只发送 `body`，核对前后标签、标题、发布状态、附件身份与摘要；未发布版本跳过，不因补写说明创建新 Release。

交付说明清楚写出改了什么、验证结果、提交/Release 链接和实际限制；尚未通过的 CI 或未部署的服务不能写成完成。


## 型号适配包

协议与操作流程见 `docs/monitor-adapters.md`。适配包与用户分享配置分离，固定官方 HTTPS 源、严格纯数据结构、完整型号和固件版本匹配；读取失败不授予写权限。只有显式安装的已审核包可提供已验证的私有原厂菜单指令，执行保留确认，不参与总配置、分享或聚合。不从未知私有码探测或采集差异猜测可写指令。报告仅供研究，不能自动发布为适配包。

## 当前交互与持久化约定

- 显示器、音频、鼠标并行初始化；显示器读取按前述同屏顺序和最多两屏并发执行。能力缓存有效期 7 天、最多 32 个条目；“重新检测”绕过缓存。当前值始终从设备读取。
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
| 程序安装 | `%LOCALAPPDATA%\Programs\FluentControl` |
| 设置和总配置、型号预设 | `%LOCALAPPDATA%\FluentControl\user-state.json` |
| 屏幕名称 | `%LOCALAPPDATA%\FluentControl\monitor-names.json` |
| 能力缓存 | `%LOCALAPPDATA%\FluentControl\monitor-capabilities.json` |
| 诊断日志 | `%LOCALAPPDATA%\FluentControl\Logs\startup.log` |
| 型号库首次迁移备份 | `%LOCALAPPDATA%\FluentControl\user-state.json.before-model-library-v1.bak` |

升级与卸载保留个人数据。连接或驱动变化可能改变设备身份，不应据此静默丢弃旧配置。公开诊断与分享仍遵守隐私边界；用户实际上传的诊断文件不提交仓库。

## 开发状态与详细计划

状态基线：**2026-10-10**。这里只列工程进度与后续验收；不因条目写入计划就宣称已完成或已发布。

### 已完成的基础

- 正式版本 `v0.3.50` 已发布：整体/单独多屏控制、完整总配置、型号预设库、JSON 分享、软件 OSD、只读采集、数据型适配框架、桌面面板、托盘、快捷键和八种语言。
- 开发构建 `0.3.52` 已通过 Windows CI：补回 OSD 状态展示、色温/场景模式回读校验和日志、无预设的已连接型号分组，以及 DDC/CI 音量说明。这些变更仍在 CHANGELOG 的“未发布”部分，不修改既有 Release。
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
| [ddcutil](https://github.com/rockowitz/ddcutil) / [UDF 文档](https://www.ddcutil.com/udf/) | 能力解析、用户定义功能、按型号覆盖选项 | 自定义定义不自动证明硬件支持写入 |
| [ddccontrol-db](https://github.com/ddccontrol/ddccontrol-db) | 独立型号描述库、贡献与校验流程 | 跨型号和跨固件不能直接继承写入权限 |
| [msigd](https://github.com/couriersud/msigd) | MSI USB/HID 协议、设备识别和型号差异 | MSI 协议不视为 HKC 协议，也不等同于通用原厂菜单导航 |
| [ddc-mode-switcher](https://github.com/Gunther-Schulz/ddc-mode-switcher) | ASUS XG27JCG 的 OSD 动作序列和执行后状态检查 | 项目记录的 `0x03` 行为属于该型号证据，不能把它推广成所有显示器的模拟按键接口 |
| [ddc-toolkit](https://github.com/andres-valencia/ddc-toolkit) | Lenovo R45w-30 的映射记录、单项实体调整前后对比方法 | 仅借鉴有证据的研究流程；保留 FC 不探测未知私有码、不自动生成写权限的限制 |

引用外部实现时记录来源、适用型号、固件和验证状态；引入代码或数据前核对对应版本的许可证。工程文档中的参考列表不代表运行时依赖或已经集成的功能。
