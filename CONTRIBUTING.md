# 参与 FluentControl / Contributing

欢迎贡献代码、翻译、文档、问题复现和显示器兼容性记录。项目采用 [MIT](LICENSE)；贡献者保留署名，提交的原创贡献按本项目 MIT 许可提供。引入第三方代码或数据前，请记录来源和原许可证。

## 从哪里开始

- 使用问题先查 [Wiki](https://github.com/chuanjianghe1024-cmd/FluentControl/wiki)，一般交流用 [Discussions](https://github.com/chuanjianghe1024-cmd/FluentControl/discussions)。
- Bug、功能建议、型号适配分别使用 [Issue 模板](https://github.com/chuanjianghe1024-cmd/FluentControl/issues/new/choose)。大功能、协议变更、重构先讨论范围；小修复可以直接开 PR。
- 初次参与可从文档、翻译、可复现的小问题开始。认领任务前留言说明打算做什么，避免重复工作。
- 安全问题按 [SECURITY.md](SECURITY.md) 处理；交流遵循 [行为准则](CODE_OF_CONDUCT.md)。

## 分支和 Pull Request

GitHub 将合并请求称为 **Pull Request（PR）**，对应其他平台的 MR。社区贡献采用 fork → 分支 → PR 到 `main`；有写入权限的协作者也使用短期分支。分支名建议 `fix/…`、`feat/…` 或 `docs/…`，不强制特定提交前缀。

1. 从最新 `main` 创建分支，每个 PR 解决一个清楚的问题。
2. 阅读 [agent.md](agent.md) 的工程边界和 [开发手册](docs/development.md)。应用使用 C#/.NET 8/WinUI 3，网站在独立仓库。
3. 运行与改动相关的检查。PR 写清问题、方案、验证结果与仍需实机确认的部分；界面改动附截图。尚未完成可先提交 Draft PR。
4. 按评审意见更新，解决讨论，再由维护者合并。不要把无关格式化、依赖升级和功能改动堆在一起。

**当前阶段：维护者仍可直接推送 `main`，尚未强制 PR 审批。** 预置的严格规则默认禁用；未来启用后，所有人通过 PR、至少一名合格评审者批准、讨论解决并通过 CI 才能合并。详细切换步骤见 [维护手册](docs/maintaining.md)。

## 验证与硬件证据

```sh
python3 scripts/check-repository.py
python3 scripts/publish-release.py --validate-only
python3 scripts/publish-wiki.py --validate-only
python3 -m unittest discover -s tests/ci -v
python3 -m unittest discover -s tests/release -v
python3 -m unittest discover -s tests/wiki -v
```

仓库检查依赖 Python 3.10+、Git、Bash 和 `.github/requirements-ci.txt`。Windows 开发环境执行：

```powershell
dotnet run --project tests/FluentControl.Tests -c Release
dotnet build src/FluentControl/FluentControl.csproj -c Release -p:Platform=x64
```

`--native` 和 MSI 验收会操作测试机器，只在可丢弃的 Windows 环境执行。普通发现保持只读；未知 OSD 指令只能通过明确的单次试发与用户观察验证。请附型号、固件、接口、HDR/节能状态、请求与回读原始值、实际画面效果，区别模拟测试和实机结果。不要上传个人配置目录、设备序列号或访问凭据。

界面文案同步八种语言与占位符；用户手册同步中英文。功能说明、CHANGELOG 的未发布区与实现一起更新。贡献者无需调整发布版本、清单或创建 Release；正式发布由维护者使用已验证的主分支产物完成。

## English

Contributions of code, translations, documentation and hardware evidence are welcome under the [MIT License](LICENSE). You retain attribution; original contributions are provided under MIT. Preserve third-party license notices and provenance.

Use the [Wiki](https://github.com/chuanjianghe1024-cmd/FluentControl/wiki) for help, Discussions for questions, and the relevant Issue form for bugs, ideas or monitor compatibility. Discuss major features and protocol changes before implementation. For small fixes, open a PR directly.

Fork the repository, create a focused branch from current `main`, read [agent.md](agent.md), and open a Pull Request targeting `main`. Collaborators with write access should also use feature branches. Describe the problem, resulting behavior, relevant checks and remaining hardware verification; include screenshots for UI changes. Draft PRs are welcome. Address review feedback before a maintainer merges.

The maintainer currently retains direct push access to `main`; mandatory review is not enabled. The strict ruleset template is disabled by default. Once activated, every change requires a PR, one eligible approval, resolved conversations and passing CI. See [maintaining.md](docs/maintaining.md) for the transition.

Run the Python checks above (Python 3.10+, Git, Bash and `.github/requirements-ci.txt`) and the relevant .NET checks on Windows. Native mouse and MSI tests belong on disposable Windows runners. Distinguish simulated results from physical display tests, include exact device/firmware/connection/HDR context, and redact private identifiers. Preserve all eight locale keys/placeholders and both documentation languages. Maintainers handle release versions and artifact promotion. Follow the [Code of Conduct](CODE_OF_CONDUCT.md) and [security reporting policy](SECURITY.md).
