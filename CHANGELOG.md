# 更新记录

## Unreleased — node v2 and brightness transitions

- Gateway/MCP 增加 node/sensor/operation/data/sampling/cache/plugin v2 工具与能力检查；USB-only 采样写入默认不向 MCP 开放。
- FC 按 sensorId/signalId 绑定光照，支持设备传感器清单选择。
- 增加 1–3 秒归一化过渡编辑器、线性/S/自定义曲线和整数阶梯预览；独立采样与过渡调度。
- Development implementation; C3, physical DDC and a real MCP host still require acceptance.


## 未发布 / Unreleased

- 新增 FCS Gateway 最小独立运行进程、本机用户专属 Named Pipe 协议、局域网设备发现/实时状态；随 FC Windows 安装包默认捆绑，FCS 页面显示已添加/新发现设备、在线 lux、USB 配网与热点配网引导，插件/规则/事件页明确标为待实现。
- FC 支持新版 C3 的公开只读 LAN 光照接口，保留已保存的签名订阅兼容；断线设备仍在列表显示，不再将旧值冒充实时数值。BLE 直接 Wi-Fi 配网尚未完成。
- Add a minimal standalone FCS Gateway process, per-user named-pipe discovery/status API, bundled Windows payload, an FC device panel and public LAN sensor read support. BLE Wi-Fi provisioning and the plugin/rule runtime remain planned.

- 新增 FCS 平台架构规划，记录多传感器对象模型、Gateway、插件/规则/事件、USB 代码安装、恢复策略及网站插件社区边界；本次仅更新文档，尚未实现通用 Gateway 或插件运行时。
- 当前分支许可调整为 PolyForm Noncommercial 1.0.0，保留历史 MIT 许可文本并明确既有授权不追溯撤销；FCW 网站仓库保持独立私有。
- Add the FCS platform architecture plan for multi-sensor models, gateway, plugins/rules/events, USB-only device code installation, recovery and the plugin community. Documentation only; the general gateway and plugin runtime are not implemented.
- Adopt PolyForm Noncommercial 1.0.0 for the current branch, preserve the historical MIT license text, and clarify that prior grants are not retroactively revoked. The separate FCW website repository remains private.

- 桌面组件在整体和单独模式下均显示受 HDR 影响的屏幕及调节限制，增加与主界面同步的自动调节开关；沿用已配置的规则和目标屏幕。
- 已接入的当前光照来源在桌面组件显示 lux 与采样时间，自动调光关闭后仍按订阅周期刷新，不强制开启历史记录；失联或样本过期不显示旧值为实时数据。
- Desktop panels show per-display HDR limitations in both modes and share the automatic-adjustment switch and configured targets with the main window. Selected light subscriptions refresh lux and sample time even with automatic brightness off, without enabling history; stale/failed samples are not shown as live.


## [0.3.145](https://github.com/chuanjianghe1024-cmd/FluentControl/releases/tag/v0.3.145) — 2026-10-22

[中英文发布说明 / Release notes](docs/releases/v0.3.145.md)。Windows build #145 全部自动化验证通过；真实 Wi-Fi 和显示器联动仍需实机复测。

- 自动调节提升为独立侧栏入口，包含预设与调节、设备与接入、历史记录；提供连接、发现、Wi-Fi 向导和硬件选购兼容说明。
- 已保存设备逐条显示，支持重命名和确认删除。同名设备以 ID/地址区分；重命名保留身份和历史，删除非当前设备不打断当前规则，删除当前设备停止自动调节。删除不重置硬件、不清除历史或无线绑定。
- AUTO 增加 CH55xDuino USB 标识（1209:C550）候选，仍须通过 FC 身份握手才接受；不同 USB 标识可手动指定 COM 端口。
- Promote automatic adjustment to its own sidebar page with presets, device onboarding and history. Rename or remove individual sources, preserving identity/history on rename and stopping automation when its active source is removed. Inactive deletion leaves the current rule running; removal does not reset hardware or erase bindings/history.
- AUTO includes CH55xDuino USB 1209:C550 candidates, still requiring a valid FC handshake. Other USB identifiers require an explicit COM port.

- 新增六步 Wi-Fi 配网向导：设备检查、网络选择、密码、自动等待连接、签名光照读取验证、完成。自动保存绑定并在完成后选择无线订阅；失败可原步重试，连接期间暂停后台串口采集。保留原有 USB 管理入口。
- Add a six-step Wi-Fi wizard with automatic binding, connection polling, authenticated light-read verification and final subscription selection. Retry LAN verification without re-entering credentials; pause background serial collection during setup. Existing USB management remains available.

- 光照自动调节改为固定区间档位：区间内不插值，跨阈值后一次写入目标亮度；3 点中值过滤短暂遮挡。目标显示器支持多选，独立保留各屏读回和失败状态。
- 光照历史新增按时间排列的折线图，断线和长时间缺测处留空，保留分页数值列表；调光编辑器用柱形显示区间亮度。
- 旧单屏配置迁移为多选列表；旧光照曲线保留原阈值转换为 v2 档位，备份迁移前配置并暂停自动调节，需检查后重新开启。时间曲线保持原有插值和渐变。
- Ambient adjustment now uses constant brightness bands, with one target-level write per band change and a 3-sample median for brief shadows. Multiple selected displays retain independent readback/fault state.
- Add a time-series light-history chart with explicit gaps and paginated values; show ambient editing as bars. Back up legacy settings, migrate single targets and ambient curves, and pause migrated ambient automation for review. Clock curves retain interpolation and bounded transitions.

## [0.3.140](https://github.com/chuanjianghe1024-cmd/FluentControl/releases/tag/v0.3.140) — 2026-10-19

[中英文发布说明 / Release notes](docs/releases/v0.3.140.md)。Windows build #140 通过自动化验收；真实硬件仍待联调。

- USB-authoritative device setup, Wi-Fi configuration/cancellation/reset reference firmware, signed LAN light subscriptions, stable device-history identity, and subscription URI activation. See [scope and limitations](docs/device-signals.md).
- USB 直连管理、配网与取消/清除配置参考固件、签名局域网光照订阅、稳定历史身份和订阅链接启动；BLE、热点配网与实体硬件验收尚未完成。

记录正式版与明确标记的预览版；普通开发构建由 GitHub Actions 单独提供。

## [0.3.135](https://github.com/chuanjianghe1024-cmd/FluentControl/releases/tag/v0.3.135) — 2026-10-18

主干构建 #135 通过完整 Windows、MSI 与 Setup 验收；[中英文发布说明](docs/releases/v0.3.135.md)。

Main build #135 passed full Windows, MSI and Setup validation.

- 光照订阅新增本地 USB 传感器发现与设备身份绑定，直接读取兼容主板，无需 Python 桥接；采样间隔支持输入、滑块与预设（0.2 秒至 24 小时）。增加本机 64 MiB 滚动历史及分页查看，可在关闭自动亮度时独立采集；Windows 自动化验证通过，实体设备联动待验证。
- Discover and subscribe to compatible local USB light sensors without a Python bridge; bind device identity and configure sampling using input, slider or presets (0.2 s to 24 h). Add a local 64 MiB rolling history and paginated viewer, with recording independent of brightness adjustment. Windows automation passed; physical-device integration remains unverified.

- 托盘右键菜单改用 WinUI Fluent 菜单：圆角、图标、标准悬停/键盘交互及 FC 明暗主题；独立透明宿主允许主面板隐藏时弹出，关闭菜单后隐藏宿主，保留六项原有操作。
- Replace the legacy tray context menu with a themed WinUI Fluent menu, icons and standard keyboard interaction. A separate transparent owner supports a hidden main panel and is hidden on dismissal; all six commands are retained.

- 修复透明托盘宿主在应用主题切换时的崩溃，并验证明暗主题下的托盘操作。
- Fix the transparent tray host crashing on app theme changes; validate tray actions in both themes.

## [0.3.129-snap](https://github.com/chuanjianghe1024-cmd/FluentControl/releases/tag/v0.3.129-snap) — 2026-10-14 · Preview

基于通过完整 Windows、模拟界面、MSI 和 Setup 验收的主干构建 #129；这是 Windows 快照预览，不替换 Latest 正式版。[中英文发布说明](docs/releases/previews/v0.3.129-snap.md)。

Windows snapshot preview from fully validated main build #129; it does not replace the latest stable release.

- 新增自动亮度：订阅统一 JSON 光照数据（真实/模拟来源同协议），独立 lux 与 24 小时曲线，按经纬度/时区计算日出日落，日夜亮度及 FC 浅色/深色主题切换；默认关闭，显式开启后作用于选定屏幕。加入采样平滑、写入限速、手动保持、HDR/断线暂停和可配置的本地模拟服务器。硬件固件与 USB/BLE 桥接尚待实物接入验证。
- Add opt-in automatic brightness from common JSON light feeds, separate lux/time curves, local sunrise/sunset schedules and FC theme switching. Include smoothing, rate limits, manual holds, HDR/disconnect guards and a configurable localhost simulator. Physical hardware firmware and USB/BLE bridges remain pending.

- 新增 Windows WMI 内屏亮度控制：按完整设备实例匹配，只读发现后接入单屏、多屏联动、FC 菜单、桌面面板与场景；按驱动支持档位调节并回读真实亮度。没有接口或身份不明确时不猜测绑定，不把 Windows 亮度标成 DDC 功能。实际笔记本仍需验证。
- Add Windows WMI panel brightness: match complete device instances, discover read-only, and use existing individual/linked controls, FC menus, desktop panels and scenes. Respect driver-supported levels and read back actual brightness; never guess identities or advertise Windows brightness as DDC. Physical notebook validation remains pending.

- 修复深色模式下设置开关、选择器所在卡片背景与文字不匹配的问题；卡片、型号标记和识别提示改为随主题更新的资源。
- 设置 → 常规新增应用主题：跟随系统、浅色、深色，立即生效并保存；主窗口、导航、标题栏、对话框及 FC 屏幕菜单保持一致，桌面面板文字颜色仍独立设置。
- Fix mismatched card backgrounds and text around switches and selectors in dark mode. Cards, model badges and identification labels now use live theme resources.
- Add Settings → General → App theme with system, light and dark choices, applied immediately and saved. Keep navigation, title bars, dialogs and the FC on-screen menu consistent; desktop panel text remains independently configured.

## [0.3.118](https://github.com/chuanjianghe1024-cmd/FluentControl/releases/tag/v0.3.118) — 2026-10-13

首次正式提供 Win11 风格离线 Setup，同时发布单独 MSI。Setup 53.48 MB，相比首版现代安装器开发构建 #115 缩小约 52%；完整 Windows、MSI 和 Setup 验收通过，直接采用主干构建 #118。[中英文发布说明](docs/releases/v0.3.118.md)。

First stable Fluent Setup release, alongside standalone MSI. Setup is 53.48 MB, about 52% smaller than the initial modern-installer development build #115. Promote main build #118 after full Windows, MSI and Setup validation.

- 缩小现代安装包：前端改用 Windows 自带 .NET Framework 4.8，保留 Fluent 界面和完整离线 MSI；启用更高压缩，增加体积预算与逐文件报告。目录选择仍使用现代系统界面，新增目录选择/取消及旧 .NET 8 安装器升级验收。
- Reduce Setup size by using Windows-provided .NET Framework 4.8 for the frontend, retaining the Fluent UI and complete offline MSI. Enable high compression, size budgets and file-level reports, with native folder-picker and previous .NET 8 installer upgrade checks.

- 新增 Win11 风格离线 Setup：明暗主题、八种语言、目录与自启动选择、显式完成后启动、修复和卸载；内部继续使用原 MSI，升级卸载保留配置。单独 MSI 继续提供，v0.3.92 附件不变。
- Add a Fluent offline Setup with themes, eight languages, folder/startup choices, explicit launch, repair and uninstall. Retain the existing MSI backend and personal settings; standalone MSI and published v0.3.92 assets remain available.
- 手动更新优先使用经过校验的官方 Setup，兼容旧 MSI；发布流程支持从同一成功构建发布双安装包与 SHA-256。新增真实向导、正式 MSI 迁移和 Bundle 升级回归。详见[现代安装器](docs/modern-installer.md)。
- Manual updates prefer the verified official Setup with legacy MSI compatibility. Release automation supports matching EXE/MSI artifacts and checksums from one successful build, with real setup UI and migration/upgrade regressions.

- 显示安全修复：保留未确认 Gamma 写入在恢复失败后的归属，写入前复核外部曲线，取消跨刷新/HDR 切换的旧总配置请求；新增模拟回归、[v0.3.92 审查](docs/reviews/v0.3.92-display-safety.md)与[实机验收表](docs/display-hardware-acceptance.md)。修复收入 v0.3.118，旧版 MSI 附件不变，真实硬件待验收。
- Display safety: preserve ownership across interrupted gamma recovery, recheck external curves immediately before writes, and cancel stale global profiles across refresh/HDR changes. Add simulated regressions and a hardware acceptance checklist; fixes ship in v0.3.118 while old MSI assets remain unchanged and physical validation remains pending.

- 社区协作：贡献与行为/安全报告指南、PR 和型号兼容性模板、CODEOWNERS、Dependabot，以及分层 CI。新增默认禁用的 PR/评审规则模板，保留维护者当前直推 main 的能力；发布流程限制官方仓库。
- Community setup: contribution/conduct/security guides, PR and monitor issue templates, CODEOWNERS, Dependabot and layered CI. Add a disabled PR/review ruleset template while retaining current maintainer direct pushes; restrict publishing to the official repository.

- 文档与许可：新增 MIT LICENSE、中英文用户手册、Wiki 导航及同步流程；README 增加手册入口和许可说明。旧版 v0.3.92 附件保持不变。
- Documentation and licensing: add the MIT LICENSE, bilingual user guide, Wiki navigation and synchronization workflow, with guide and license links in the README. Published v0.3.92 assets remain unchanged.

## [0.3.92](https://github.com/chuanjianghe1024-cmd/FluentControl/releases/tag/v0.3.92) — 2026-10-12

汇总 v0.3.75 之后的系统色温、HDR 控制与提示、显示器通信及 OSD 配对增强。[中英文发布说明](docs/releases/v0.3.92.md)。

- HDR 开启时，在单屏、整体控制和 FC 屏幕菜单中显示常驻限制提示，说明亮度、对比度、色温可能被显示器锁定，并提示调节无效时关闭 HDR 后重新读取设备；按屏幕显示，关闭并刷新后移除，不统一禁用仍可用的控制。
- Show a persistent per-display HDR notice in individual, overall and FC on-screen controls. Explain possible brightness, contrast and color-temperature restrictions and suggest disabling HDR and refreshing if adjustments have no effect. Remove it after HDR is off and devices refresh; keep supported controls usable.

- 新增独立的「系统色温（软件）」：每屏 2500–10000 K 近似冷暖调整，显式应用、原曲线恢复、读回确认、本机总配置保存和异常恢复记录；与 DDC 硬件色温分离。仅支持已确认的 SDR 独立输出，HDR/ACM/复制模式禁用并提供 Windows 夜间模式入口。正常退出尝试恢复自己的曲线，外部颜色变更不会被持续覆盖。
- Add independent per-display software temperature controls (approximate 2500–10000 K), explicit Apply, original-curve restoration, verified readback, local global-profile values and crash recovery records. Keep them separate from DDC presets. Restrict legacy gamma to confirmed independent SDR outputs; block HDR/ACM/duplicated modes and offer Windows night light. Normal exit attempts to restore FC-owned curves without overwriting newer external color settings. [Scope and usage](docs/system-color-temperature.md).

- 新增每屏 Windows HDR 开关：单屏和整体视图均可独立切换，不依赖 DDC/CI；内屏在 Windows 报告支持时也可用。切换后核对状态并重建显示器控制连接，不自动恢复旧参数。区分 HDR、广色域、策略限制和读取失败；HDR 开启仅提示可能受限，不一律禁用亮度/对比度。
- Add per-display Windows HDR switches in individual and overall views, independently of DDC/CI, including internal panels when Windows reports support. Verify state and rediscover monitor controls after switching without replaying old values. Distinguish HDR from WCG, policy limits and unknown states; keep available picture controls usable.
- 本次不包含 WMI 内屏亮度和厂商专有屏幕接口；Windows HDR 与笔记本控制边界见 [说明](docs/windows-hdr.md)。实体屏幕切换仍待本机验证。
- WMI laptop brightness and proprietary OEM display controls remain unimplemented. See [Windows HDR notes](docs/windows-hdr.md); physical-panel switching still needs local verification.

- 显示器分阶段读取：先显示真实亮度/对比度，再补齐高级能力与型号适配；保留基础通道对象和已有能力缓存边界。
- DDC/CI 移入每个逻辑显示连接独立的辅助进程，8 秒通信限时；中断后旧连接失效，刷新重建，绝不自动重发写入。高级扫描中断会留下本机恢复标记，下次跳过，用户可主动重新检测。
- 普通读取仅对指定传输错误有限重试；事件、配对、诊断和所有写入不自动重试，不将历史读数伪装成当前状态。
- OSD 配对新增已确认的自定义按钮/选项组、发送后等待时间与可选回读。v3 文件保留兼容设置、试验执行策略和用户填写的视频接口，兼容读取 v1/v2；导入不激活自定义控件。
- 增加 Twinkle Tray 设计来源、USB Monitor / HID 研究与 HDMI/DP 对照调试方案；USB 执行尚未接入。
- Show live brightness/contrast first, then enrich advanced monitor controls. Isolate DDC/CI per logical display connection with bounded IPC and recovery markers; never replay writes automatically.
- Retry only selected transient ordinary reads; retain real readback semantics and single-send pairing/diagnostic behavior.
- Add observed custom buttons/choice groups, configurable settling/readback and manually recorded video connections to v3 pairing files. Read v1/v2 files; imported evidence still requires local verification.
- Document Twinkle Tray design references, USB Monitor/HID research and controlled HDMI/DP comparisons. USB execution remains unimplemented.

## [0.3.75](https://github.com/chuanjianghe1024-cmd/FluentControl/releases/tag/v0.3.75) — 2026-10-12

新增本地 OSD 配对与未知指令试发，修复 OSD、色温和场景模式的重复选择。[中英文发布说明](docs/releases/v0.3.75.md)。

- 修正 OSD 配对只有三项开关和手工候选的局限：自动列出显示器声明的未知代码，直接单次试发并按实际效果绑定。有声明参数时使用声明值；无参数时提供明确标为试验值的按钮和自填值，不必手填代码或来源。已知标准、重置及信息功能另列分类。
- 配对文件记录候选来源、声明参数和采集时间，发送与重放前校验当前声明；全程可离线完成。
- Fix OSD pairing being limited to three OSD actions and manual candidates: automatically list advertised unknown codes for explicit single-send tests and observed action binding. Use advertised values where present; otherwise offer labelled experimental values and a custom value without requiring a code or source. Classify known standard, reset and information features separately.
- Record candidate provenance, advertised values and capture time in local pairing files; validate current declarations before sending or replaying. Everything remains available offline.

- 新增离线 OSD 配对助手：先测试启用/禁用，再由用户确认真实效果并绑定开关、上下、返回、确认等操作；未配齐也可保存和重新配对。禁用 OSD 与按键事件需要二次确认。
- 配对、诊断基线、发送/读回结果及按键事件记录自动保存在本机；不登录、不上传。支持按型号、固件、能力指纹校验的 JSON 导入导出，导入只提供候选，当前显示器确认后才能遥控。
- 菜单打开后 `0xCA` 读回失败时，可主动允许沿用本次窗口已读取的按键字段进行恢复测试；不使用磁盘旧状态。支持手工候选与只读事件采集，没有通用方向命令，也不将事件编号当作可写按键。
- Add offline OSD pairing with user-confirmed actions, partial mappings, repeatable remote controls and a second confirmation before disabling OSD and button events.
- Keep diagnostic baselines, trials, observations and event captures locally without sign-in or upload. JSON imports require matching model, firmware and capability fingerprints, followed by local verification.
- Allow explicit reuse of live-session button fields when OSD readback fails. Manual candidates and read-only event capture support investigation; no universal direction commands or automatic conversion of event numbers into writes.

- 色温与场景模式同样支持重复选择；单屏、整体、FC 菜单和桌面面板分开展示待发送选项与真实读回值。读回不一致继续报告原始编码；读回失败标为未确认，保存总配置或型号预设时跳过旧值。
- Color and picture presets also allow explicit replay, with actual readback shown separately across main, linked, FC and desktop controls. Mismatches retain raw hardware values; failed readback is marked unconfirmed and excluded from new snapshots.

- 修复 OSD 写入成功、读回失败后下拉框退回旧状态，导致无法再次选择禁用的问题。单屏、整体控制与 FC 屏幕菜单中的 OSD 下拉框改为可重复发送的指令入口，执行或取消后清空选择，不将旧状态当成当前选择。
- 区分“指令已发送但状态未确认”和真正的写入失败；保留确认、写前读取及其他按键字段保护。原厂菜单是否弹出按显示器固件实际行为说明，不再一概宣称不会弹出。
- Fix OSD selectors reverting to stale state after successful writes with failed readback. Individual, overall and FC menus now allow explicit command replay, clearing the selection after completion or cancellation.
- Distinguish sent commands with unconfirmed state from failed writes. Keep confirmation and button-field preservation; native-menu opening depends on the display firmware.

## [0.3.67](https://github.com/chuanjianghe1024-cmd/FluentControl/releases/tag/v0.3.67) — 2026-10-10

完善安装引导与启动选项。[中英文发布说明](docs/releases/v0.3.67.md)。

- 新安装可输入或浏览自定义目录，支持中文与空格路径；升级自动恢复并锁定原安装位置，避免产生第二套程序。迁移目录需先卸载再安装，个人配置保留。
- 完成页增加默认勾选的“立即启动 FluentControl”，点击“完成”后才打开主面板；取消勾选、静默安装、修复、卸载及需要重启时不自动启动。
- 安装选项增加“随 Windows 启动”，新安装默认关闭，升级按实际启动项预选；复用应用里的开关与当前用户启动快捷方式，登录后在托盘运行。应用内关闭后，修复安装不会重新开启。
- README 与分享图素材统一使用新版三滑轨图标；GitHub Social preview 设置仍需单独应用。
- 通过 Windows 编译、控制逻辑、应用界面、真实 MSI 向导、升级、修复及卸载验收；包含中文/空格目录、返回导航、自启动选择/取消、完成后启动/取消和静默安装不启动。复用 build #67 的安装包（50,621,951 字节 / 48.28 MiB）。

## [0.3.60](https://github.com/chuanjianghe1024-cmd/FluentControl/releases/tag/v0.3.60) — 2026-10-10

新增手动更新与设置子菜单，并优化仓库展示入口。[中英文发布说明](docs/releases/v0.3.60.md)。

- 优化中英文 README 的功能关键词、下载入口、版本和构建状态，加入复用现有图标的分享预览图及中英文问题模板；仓库简介、主题标签和 Social preview 素材集中维护，GitHub 设置需另行应用。
- 设置新增“关于与更新”：手动查询 GitHub 最新正式 Release，比较数字版本，下载匹配的 x64 MSI 并校验大小与 SHA-256；用户选择“退出并安装”后才启动安装，保留个人配置。支持取消、失败重试及版本说明入口，启动时不联网检查。
- 设置拆分为常规、显示器、全局快捷键、桌面控制面板、软件准星、关于与更新六个子菜单；导航固定在内容上方，设置重建和刷新后保留当前分类，长标题换行并保持八种语言覆盖。

## [0.3.57](https://github.com/chuanjianghe1024-cmd/FluentControl/releases/tag/v0.3.57) — 2026-10-10

第三个正式 Release，汇总 0.3.50 之后的系统音频与桌面前置改进，以及显示器诊断和配置库修复。[中英文发布说明](docs/releases/v0.3.57.md)。

直接发布通过全部 Windows CI 验收的 build #57 安装包（50,434,970 字节 / 48.10 MiB）。控制逻辑、音频模拟设备界面、真实鼠标双击前置、失焦锁定、Win+D、托盘及 MSI 安装/升级/卸载通过；实体显示器、虚拟音频驱动和混合 DPI 仍需本机复测。

- 修复桌面组件双击解锁后仍需再点一次才到前台：双击立即激活并前置，失焦后重新锁定回到底层；Win+D 后同样可双击解锁，旧的桌面恢复任务不会覆盖新的解锁操作。
- 声音页面简化为「音量与输入」，只保留「音量」「输入」及各自的音量/静音控制，不展示设备名称、其他设备列表或单独的通话设备。每次读写跟随 Windows 当前默认输出/输入，物理与虚拟线路同样处理，不修改系统路由；系统切换、外部音量和静音变化会自动同步。
- 总配置与桌面面板使用稳定的系统音量/输入标识，切换设备后继续有效；保存配置前读取当前值，缺失的输入或输出不会覆盖已保存值。旧桌面音频项备份后迁移为两项系统控制；旧配置中的具体设备音量保留但不执行，提示更新配置，不猜测旧设备对应的输入/输出。
- 修复显示器未声明 OSD 控制时“显示器菜单”栏目整体消失的问题：保留原厂菜单适配状态，并明确标识 FC 软件菜单入口。
- 对未声明但可读取的标准 OSD 状态执行只读查询并展示；不会因此开放菜单锁定写入，也不会将菜单启用误当作原厂菜单弹出能力。
- 场景模式与色温统一执行有限次数的延迟回读校验；不一致时显示请求值和实际值的原始编码，下拉框在写入期间暂时禁用，不再把模式跳回误报为成功。补充带显示器身份、原始值和 Windows 错误码的读写日志，供型号适配排查；不猜测或交换未验证的色温编码。
- 型号配置库同时显示已连接型号和已有预设的型号；尚未保存预设的屏幕也有独立分组与保存入口，同型号仍合并，离线预设保留。
- 显示器 DDC/CI 音量不可读时，提示可在「音量与输入」中调节系统音量；不会把音频设备存在误判为支持显示器硬件音量控制。

## [0.3.50](https://github.com/chuanjianghe1024-cmd/FluentControl/releases/tag/v0.3.50) — 2026-10-09

第二个正式 Release，汇总首发 0.3.33 之后的中间更新。直接复用通过验收的 Windows build #50 安装包；已安装同一 0.3.50 测试包的用户无需重装。[完整发布说明](docs/releases/v0.3.50.md)。

### 新增

- 总配置与按型号分组的显示器预设库：完整切换屏幕、声音/麦克风与鼠标状态，保留离线设备参数；旧场景备份后迁移。
- 每屏可选用、保存当前预设，同型号复用；跨型号按实际能力和选项应用，提示另存，支持推荐命名、批量导入导出和去重。总配置快照不随库预设的编辑或删除改变。
- 每屏 FC 软件 OSD 菜单，复用实际可控参数，关闭时取消待执行调节；与原厂菜单启用、语言和导航区分。
- 分享文件附带可选显示器能力快照，包括厂商、固件、MCCS、参数范围和离散选项；保留离线值并兼容旧文件。
- 独立的数据型适配包机制：完整型号及固件精确匹配，支持查询、安装、离线使用和移除；仅接受校验后的选项映射与经验证的原厂菜单指令，不执行脚本或 DLL。
- 只读适配采集助手：记录原始基线、实体菜单变更、数值与读取错误差异，自动保存本地报告并支持导出；默认排除设备身份，未知私有码不做猜测写入。

### 修复与改进

- 修复 Win+D/系统“显示桌面”导致桌面组件最小化或被覆盖；保持锁定、不抢焦点，恢复后回到普通应用窗口下方。
- 修复高 DPI 下主窗口过小、识别显示器文字截断；按目标屏幕缩放换算，名称自动换行、提示框按内容增高，FC 软件菜单和准星同步适配。
- 安装包禁用 Restart Manager 自动关闭/重启其他程序，保留真实文件占用提示；同一回滚事务中先安装新版再移除旧版，复用稳定组件，避免删除未变更运行库触发旧策略。
- 修复自启动快捷方式句柄未释放就校验或移动的问题；独立暂存文件，对短暂占用有限重试，失败保留原快捷方式。
- 色温使用统一原始 VCP 读写与读回校验，区分 Windows 色温枚举和 MCCS 预设，处理容差高字节及延迟读回，继续兼容旧配置。
- 桌面整体控制扩展到实际支持的安全调节项，包括色温预设、RGB、Gamma、饱和度和显示模式；下拉选项取并集，部分支持明确标注目标屏幕。

### 验证与边界

- Windows 构建、逻辑、原生及模拟设备界面、真实 Win+D、托盘、MSI 安装/升级/卸载、文件占用取消、独立运行程序存活和配置保留检查通过。
- 4K 100%–200% 尺寸计算通过；原生界面在 100% 测试屏幕验证，实体 4K / 150% 与混合 DPI 多屏仍需实机复测。
- 配套网站的报告队列、审核发布及撤回已完成开发，**尚未部署上线**；本版不代表在线查询和报告提交已可用。具体型号的私有 OSD 指令仍需实机验证后发布适配包。
- 安装包 `50406298` 字节（48.07 MiB），**未签名**；保留个人配置。真实 DDC/CI 支持以对应硬件验证为准。

## [0.3.33](https://github.com/chuanjianghe1024-cmd/FluentControl/releases/tag/v0.3.33) — 2026-10-09

首个正式 Release，保留已验证安装包的版本号，未重新编号或重新编译。

### 功能

- WinUI 3 聚合面板：显示器、默认音频/通话设备、麦克风和鼠标。
- 31 项通用 VCP 目录；按硬件能力开放，支持单独/整体控制、跨型号联动、屏幕重命名和每屏亮度映射。
- 场景保存、分组、应用/型号/品牌筛选、批量导入导出、HTTPS JSON 下载；离线屏幕参数保留。
- 半透明桌面面板、移动/缩放、双击解锁、锁定保持底层、分组与配置切换。
- 托盘、单实例、可选自启动、四组全局快捷键、八种语言及深浅主题。
- 单文件、当前用户安装的离线 MSI；支持升级，卸载保留个人配置。

### 优化与修正

- 不同型号屏幕使用同一套整体亮度/对比度等控制，不再按型号拆分；部分支持明确标注。
- 显示器、音频和鼠标并行初始化；不同屏幕最多两台同时读取，同屏请求保持顺序。
- 有效能力缓存 7 天，当前值始终从硬件读取；支持强制重新检测和读取耗时日志。
- 修正色温无效选项、深色标题栏、桌面文字切换、自启动权限、提示残留等问题。
- 桌面仓库移除旧网站目录与构建；GitHub 官方 actions 升级至原生 Node 24 版本。

### 验证与限制

Windows 构建及 MSI 安装、升级、界面/托盘、卸载和数据保留检查通过。安装包 50,365,300 字节（48.03 MiB），尚未签名；具体显示器的 DDC/CI 与厂商功能仍以硬件验证为准。

详见[完整发布说明](docs/releases/v0.3.33.md)。
