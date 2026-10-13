# Fluent 安装器 / Fluent setup

状态：v0.3.118 首次正式发布现代 Setup，与单独 MSI 同时提供；复用主干构建 #118，旧版附件保持不变。

## 用户体验

- 新入口为 `FluentControl-<版本>-x64-Setup.exe`：Win11 风格卡片、开关、圆角，系统/浅色/深色主题，八种语言，键盘与辅助功能支持。Per-Monitor V2 DPI 和可滚动内容用于避免高缩放截断。
- 首次安装选择可写的本地目录和开机启动，新安装自启动默认关闭。升级锁定原目录，读取当前启动项，不恢复过期的自启动标记。
- 成功后点击“启动 FluentControl”才打开应用。“关闭”、静默、修复、卸载、失败、取消和需要重启均不启动应用。同版本打开提供修复和二次确认卸载，保留个人配置。
- EXE 包含原 MSI，全离线安装；前端使用受支持 Windows 自带的 .NET Framework 4.8，不再重复打包整套 WPF 运行时。独立 MSI 继续提供传统向导和部署参数。应用更新优先选择官方 Setup；旧版仅有 MSI 时兼容。Setup 存在但来源/摘要资料不合法时不静默退回 MSI。

## 实现与生命周期

`installer/FluentControl.Setup/` 是独立 .NET Framework 4.8 WPF 前端，使用 WPF UI 4.3.0 与 WiX 7 BootstrapperApplicationApi。Windows 10 2004 及项目支持的更新系统已包含所需 Framework。主程序仍使用自包含 .NET 8，不引用安装前端，不向应用安装目录引入 WPF，也不依赖 Node/WebView。原 MSI payload 保持完整。

目录选择使用 Windows Common Item Dialog，保留现代文件夹选择界面；语言资源使用系统自带 JSON 序列化器。应用配置与 manifest 开启 WPF Per-Monitor V2 支持。前端发布目录禁止包含 CoreCLR、私有 Framework/WPF 运行时或 runtimeconfig；未压缩前端限制为 20 MiB，最终 EXE 相对原 MSI 的额外体积限制为 12 MiB。

`installer/Bundle.wxs` 只包含一个当前用户 MSI。Burn 管理 Bundle 的缓存、升级、修复和卸载，隐藏 MSI 的重复应用列表入口。Bundle UpgradeCode 与 MSI UpgradeCode 分离且固定。MSI 原有组件 GUID、HKCU keypath、事务、安装目录及个人数据位置保持不变。

前端传递 `INSTALLFOLDER`、`START_WITH_WINDOWS`，规划前复核原目录，使用 `Plan → Apply → ApplyComplete` 的真实结果。取消通过 Burn 回调传递，等待回滚后退出；占用程序由用户自行关闭后重试或取消，不强杀进程、不自动忽略锁、不自动重启。需要重启返回 3010。

支持 `/quiet`、`/repair`、`/uninstall`、`/norestart`、`/log <路径>`。自定义静默目录使用既有 MSI 命令行，EXE 不接受任意 MSI 属性。仅支持当前用户模式，专用布局/缓存/强制清理操作不作为本版入口。

## 构建与验证

按[开发手册](development.md)生成 publish 与 MSI 后运行 `./scripts/Build-Setup.ps1 -Version $releaseVersion`。脚本检查 WiX/API 同版本、MSI 版本、Framework 配置和体积预算及签名要求；清理独立前端 publish，逐文件生成 payload，使用 Burn high 压缩，打包后复核原 MSI 未改变。`frontend-payload.json` 列出前端文件及大小，`size-report.json` 记录 MSI、前端和 EXE 体积及额外开销。签名时先签前端 EXE，再分离并签 Burn 缓存引擎、重新附加 payload，最后签整个 EXE。`-RequireSigned` 同时要求原 MSI 已签名，不允许无签名回退。当前未配置证书，真实签名流程尚未执行。

在可丢弃 Windows 环境先运行 `Test-Msi.ps1`，再运行 `Test-Setup.ps1`。后者操作真实 EXE，验证系统 Framework CLR 加载、目录选择/取消、安装取消、中文路径、自启动、主题/语言切换、完成后显式启动、修复保留自启动退出、正式 MSI 迁移、Bundle 升级、全部 payload 哈希、文件占用取消回滚、阻止降级及卸载保留配置。旧 Bundle 从固定提交 `3c4fd557c308e1a2c4394090ba09a51c77e76077` 重建，用于验证 .NET 8 前端升级到 Framework 前端及旧缓存卸载。日志和截图在 `artifacts/setup-logs/`。

`FluentControl.Setup.exe --preview zh-CN` 只预览外观，不连接 Burn，不是安装验证。Windows CI 提供 `FluentControl-MSI-x64` 和 `FluentControl-Setup-x64`，完整验收通过后才上传安装包；失败也上传诊断。实体 4K、混合 DPI、辅助功能与 Win11 Mica 效果仍需实际设备验收。

## 正式发布

原单 MSI 清单仍有效。双包发布时在 `.github/release.json` 增加 `setup` 对象：`artifactId`、`artifactName`（固定 `FluentControl-Setup-x64`）、`archiveSha256`、`fileName`、`bytes`、`sha256`。文件名固定为 `FluentControl-<版本>-x64-Setup.exe`，来源必须与顶层 MSI 同一个成功 main push、提交及 run ID。

两份来源和摘要验证成功后才能发布；`SHA256SUMS.txt` 包含两个安装包。任意来源、ZIP、文件或已存在附件不符都停止；重试不替换已发布附件。v0.3.118 由用户明确授权发布，复用主干构建 #118 的双安装包；旧版本标签与附件不变。

## 第三方

- [WPF UI 4.3.0](https://github.com/lepoco/wpfui)：MIT，许可及第三方声明随前端放入 `licenses/`。
- [WiX 7](https://github.com/wixtoolset/wix/tree/v7.0.0)：保留 MS-RL 与 OSMF EULA，见[已授权的工具链策略](wix-toolchain.md)。未复制 WiX 示例业务代码。
- 主应用 .NET 运行时随原 MSI 分发，保留相应许可；前端 Framework/WPF 运行时由 Windows 提供。主仓库 MIT 不覆盖第三方许可。

## English

v0.3.118 publishes the Fluent offline Setup EXE alongside the existing per-user MSI, promoting verified main-branch build #118. The frontend supports eight languages, system/light/dark themes, a native modern folder picker, startup options, explicit launch after success, repair and confirmed uninstall. It uses Windows-provided .NET Framework 4.8; the main application keeps its self-contained .NET 8 runtime inside the unchanged MSI payload. No runtime download is needed on supported Windows versions.

Burn uses high compression. Build checks reject bundled frontend runtimes, enforce a 20 MiB uncompressed frontend budget and a 12 MiB EXE-over-MSI budget, and publish file-level size diagnostics. Windows UI tests inspect the loaded CLR, exercise folder selection/cancellation, and upgrade from an immutable .NET 8 frontend fixture to verify migration and cached-bundle removal.

Burn owns installation, caching, upgrade, cancellation and rollback. Existing MSI identities, directories and personal data are preserved. Manual updates prefer the verified official Setup asset while retaining compatibility with older MSI-only releases. Windows CI exercises the real UI and MSI-to-Bundle/Bundle-to-Bundle upgrades. Physical DPI/accessibility checks remain a separate acceptance step. Published v0.3.92 assets are unchanged.
