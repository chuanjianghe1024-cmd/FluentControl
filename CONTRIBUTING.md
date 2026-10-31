# 参与 FluentControl / Contributing

[中文](#中文) · [English](#english)

## 中文

欢迎贡献代码、翻译、文档、问题复现和显示器兼容性记录。当前分支采用 [PolyForm Noncommercial 1.0.0](LICENSE)，允许许可定义范围内的非商业使用、修改和分发，不限个人自用；此前合法取得的授权不追溯撤销。提交贡献前请确认你有权按项目当前许可提供贡献；引入第三方代码或数据前，记录来源和原许可证。详见 [许可说明](docs/licensing-policy.md)。

### 从哪里开始

- 使用问题先查 [Wiki](https://github.com/chuanjianghe1024-cmd/FluentControl/wiki)，一般交流用 [Discussions](https://github.com/chuanjianghe1024-cmd/FluentControl/discussions)。
- 提交前搜索已有 Issue。Bug、功能建议、型号适配分别使用 [Issue 模板](https://github.com/chuanjianghe1024-cmd/FluentControl/issues/new/choose)。Bug 附软件与 Windows 版本、最小复现、预期/实际结果及脱敏日志；功能建议说明使用场景；兼容性报告按模板记录硬件证据。
- 大功能、协议变更、重构先讨论范围；小修复可以直接开 PR。初次参与可从文档、翻译、可复现的小问题开始，认领任务前留言避免重复工作。
- 安全问题按 [SECURITY.md](SECURITY.md) 处理，不在普通 Issue 中公开漏洞细节；交流遵循 [行为准则](CODE_OF_CONDUCT.md)。

### 开发环境与规范入口

应用使用 **C# / .NET 8 / WinUI 3，目标为 Windows x64**。本机开发需要 Windows、.NET 8 SDK、Windows SDK 和 WinUI 构建组件，可通过 Visual Studio 2022 安装相应组件。MSI 使用仓库工具清单中的 WiX 5。桌面项目不需要 Node.js；网站在独立的 FluentControl-Web 仓库。

先阅读 [agent.md](agent.md)：它是工程规则的统一维护位置，[AGENTS.md](AGENTS.md) 仅作发现入口。本页说明贡献流程；完整构建、启动、安装和测试命令以 [开发手册](docs/development.md) 为准，不另建一套规范。

```powershell
dotnet build src/FluentControl/FluentControl.csproj -c Debug -p:Platform=x64
dotnet run --project src/FluentControl/FluentControl.csproj -p:Platform=x64
```

### 分支、评审与合并

GitHub 将合并请求称为 **Pull Request（PR）**，对应其他平台的 MR。社区贡献采用 fork → 分支 → PR 到 `main`；有写入权限的协作者也使用短期分支。分支名建议 `fix/…`、`feat/…` 或 `docs/…`，不强制特定提交前缀。

1. 从最新 `main` 创建分支，每个 PR 解决一个清楚的问题，关联已有 Issue（如有）。不要堆入无关格式化、依赖升级或功能。
2. 按下表完成相关验证，在 PR 模板中写清问题、行为变化、检查结果和未覆盖项。界面改动附截图；未完成可先开 Draft PR。
3. 维护者评审工程边界、兼容性、测试与硬件证据；按意见更新并解决讨论，由维护者合并。首次 fork Actions 可能需要维护者批准运行，无需贡献者提供令牌。
4. PR 应通过 `Repository checks` 和 `Windows validation`。这是贡献流程要求，不表示 GitHub 已强制拦截合并；文档改动可以明确跳过 Windows 构建，但最终检查仍应成功。

**当前阶段保留仓库所有者直接推送 `main` 的能力，不在本次启用强制 PR 规则。** 仓库内严格规则模板保持禁用；规则核验边界及未来独立启用步骤见 [维护手册](docs/maintaining.md)。

### 按改动选择验证

| 改动 | 应提供的验证 |
| --- | --- |
| 文档、模板、元数据 | 下方仓库检查；确认中英文和链接一致。修改 Wiki 或发布资料时，运行对应的 validate-only 与离线测试 |
| 控制逻辑、协议、配置 | .NET 逻辑回归与 Release 构建，覆盖失败、兼容性及本次改动边界 |
| WinUI、桌面面板、文案 | 上述检查，加开发手册中的启动/模拟 UI 回归；截图记录主题、缩放和语言 |
| 安装器、发布或 CI | 按开发/维护手册执行对应脚本回归；安装变更在可丢弃 Windows 环境做安装、升级、卸载与数据保留验证 |
| 硬件行为、型号适配 | 自动检查加下节实机证据；没有设备时明确写未验证，不能用 CI 成功替代 |

仓库检查需要 Python 3.10+、Git、Bash 和 [.github/requirements-ci.txt](.github/requirements-ci.txt)。按 [维护手册](docs/maintaining.md) 在虚拟环境安装依赖，再用该环境的 Python 运行：

```sh
python3 scripts/check-repository.py
python3 scripts/publish-release.py --validate-only
python3 scripts/publish-wiki.py --validate-only
python3 -m unittest discover -s tests/ci -v
python3 -m unittest discover -s tests/release -v
python3 -m unittest discover -s tests/wiki -v
```

Windows 代码验证：

```powershell
dotnet run --project tests/FluentControl.Tests -c Release
dotnet build src/FluentControl/FluentControl.csproj -c Release -p:Platform=x64
```

记录实际命令、结果、环境和未执行原因；无需为纯文档改动新增测试。`--native` 会短暂修改并恢复鼠标设置；MSI 验收会实际安装、升级和卸载，只在可丢弃的 Windows 环境执行。Linux/无设备环境不能验证 WinUI、MSI 或实体 DDC/CI。

### 硬件验证边界

普通能力发现保持只读；未知 OSD 指令只能按 [agent.md](agent.md) 和 [配对协议](docs/osd-pairing.md) 通过明确的单次试发与用户观察验证，不自动扫描、穷举或跨型号试写。

报告应包含型号、固件（未知则注明）、显卡/驱动、接口/转接方式、DDC/CI、HDR/节能状态，以及请求原始码/值、回读值/错误和实际画面或原厂 OSD 效果。区分“声明支持”“读取成功”“传输成功”“回读匹配”和“实际效果正确”；某型号、固件或连接上的结果不能推广为全部设备支持。模拟设备和实体设备结果分别记录。不要上传个人配置目录、设备序列号、本机身份或访问凭据。

### 文档与发布

界面文案同步八种语言的键和占位符；用户手册同步中英文，在 `docs/wiki/` 修改源文件，复用现有 Wiki 发布工作流。功能说明和 CHANGELOG 未发布区随实现更新。

贡献者无需调整版本、发布清单或创建 Release。维护者按 [开发手册](docs/development.md) 选择已通过 Windows 验收的官方 `main` push 构建，核对产物身份、大小和 SHA-256，再更新发布说明及 `.github/release.json`，由现有工作流发布。PR 构建不能直接推广为正式安装包；合并、CI 通过和正式发布是不同阶段。已发布标签和附件不移动、不覆盖，修复使用新版本。

## English

Code, translations, documentation, reproductions and monitor compatibility evidence are welcome. The current branch uses [PolyForm Noncommercial 1.0.0](LICENSE), permitting noncommercial use, modification and distribution as defined in the license, not limited to personal use; earlier lawful grants remain effective. Submit only work you have the right to license under the project terms. Preserve third-party notices and provenance. See the [licensing policy](docs/licensing-policy.md).

### Start here

- Consult the [Wiki](https://github.com/chuanjianghe1024-cmd/FluentControl/wiki) and use [Discussions](https://github.com/chuanjianghe1024-cmd/FluentControl/discussions) for usage questions.
- Search existing issues, then choose the appropriate [Issue form](https://github.com/chuanjianghe1024-cmd/FluentControl/issues/new/choose). For bugs, include app/Windows versions, minimal steps, expected/actual results and redacted logs. Explain the use case for feature requests; provide hardware evidence for compatibility reports.
- Discuss large features, protocol changes and refactoring first. Small fixes can go directly to a PR. Documentation, translations and small reproducible bugs are good first contributions; comment before claiming work to avoid duplication.
- Follow the [Code of Conduct](CODE_OF_CONDUCT.md). Use the [security policy](SECURITY.md) for vulnerabilities instead of publishing technical details in an issue.

### Environment and authoritative documentation

The desktop application targets **Windows x64 with C# / .NET 8 / WinUI 3**. Local development needs Windows, the .NET 8 SDK, Windows SDK and WinUI build components, available through Visual Studio 2022. MSI tooling uses WiX 5 from the repository tool manifest. Node.js is not an application dependency; FluentControl-Web is a separate repository.

Read [agent.md](agent.md), the single source of engineering rules; [AGENTS.md](AGENTS.md) is its discovery entry point. This guide covers contribution workflow. The [development manual](docs/development.md) owns the complete build, startup, installation and test commands. Use the Debug build/run commands in the Chinese section above to start the application.

### Branches and review

Fork the repository, create a focused branch from current `main`, and open a Pull Request targeting `main`. Collaborators with write access should also use short-lived branches. Suggested names are `fix/…`, `feat/…` or `docs/…`; no commit prefix is mandatory.

1. Solve one clear problem and link the relevant issue if one exists. Avoid unrelated formatting, dependency and feature changes.
2. Fill in the PR template with the problem, resulting behavior, actual checks and untested areas. Include screenshots for UI changes. Draft PRs are welcome.
3. Maintainers review engineering boundaries, compatibility, tests and hardware evidence. Address feedback and resolve discussions before a maintainer merges. First-time fork workflows may need approval; contributors do not need to supply tokens.
4. PRs should pass **Repository checks** and **Windows validation**. This is a contribution requirement, not a claim that GitHub currently enforces it. Documentation changes can explicitly skip Windows work while still producing a successful final check.

**The owner retains direct push access to `main` at this stage; this change does not activate mandatory PR rules.** The strict ruleset template remains disabled. See [maintaining.md](docs/maintaining.md) for visibility limits and the separate, optional future transition.

### Choose relevant validation

| Change | Evidence to provide |
| --- | --- |
| Documentation, templates, metadata | Repository checks above; consistent translations and links. Run the matching validate-only command and offline tests when changing Wiki or release material |
| Control logic, protocols, configuration | .NET regression tests and Release build, including failure paths, compatibility and the changed behavior |
| WinUI, desktop panels, UI strings | The above plus startup/simulated UI checks from the development manual; screenshots with theme, scaling and language |
| Installer, release or CI | Relevant script regressions from the development/maintenance manuals; installer changes require install, upgrade, uninstall and data-retention checks on disposable Windows |
| Hardware behavior or model adaptations | Automated checks plus physical evidence below; explicitly mark missing device coverage |

Repository checks require Python 3.10+, Git, Bash and [.github/requirements-ci.txt](.github/requirements-ci.txt). Install dependencies in a virtual environment as described in the [maintenance manual](docs/maintaining.md), then run the Python commands above with that environment's interpreter. Run the .NET test and Release build commands above for code changes on Windows.

Report commands, results, environment and reasons for omitted checks. Documentation-only changes do not need new tests. `--native` temporarily changes and restores mouse settings; MSI tests actually install, upgrade and uninstall. Use disposable Windows environments for these tests. Linux or a device-free runner cannot establish WinUI, MSI or physical DDC/CI correctness.

### Hardware evidence

Normal discovery is read-only. Unknown OSD commands must follow [agent.md](agent.md) and the [pairing protocol](docs/osd-pairing.md), using explicit single-command trials and user observation; do not automatically scan, brute-force or try commands from unrelated models.

Record model, firmware (or unknown), GPU/driver, cable/adapter, DDC/CI, HDR/ECO state, raw requested code/value, readback/errors, and visible or physical OSD effects. Distinguish advertised support, successful reads, transport success, matching readback and correct visible behavior. Results for one model, firmware or connection do not establish support for all devices. Separate simulated and physical results; never publish settings folders, serial numbers, local device identities or credentials.

### Documentation and releases

Keep all eight locale keys/placeholders and both user-guide languages consistent. Edit Wiki sources under `docs/wiki/` and reuse the existing publishing workflow. Update feature documentation and the unreleased changelog with implementation changes.

Contributors do not need to change versions, release manifests or create releases. Maintainers follow the [development manual](docs/development.md): select an official `main` push build that passed Windows validation, verify artifact identity, size and SHA-256, then update release notes and `.github/release.json` for the existing publishing workflow. PR installers cannot be promoted directly. Merge, passing CI and release are separate stages. Published tags and assets must not be moved or overwritten; fixes use a new version.
