# 社区维护与流程切换 / Community maintenance

## 当前模式：维护者可直推，社区走 PR

仓库负责人为 [@chuanjianghe1024-cmd](https://github.com/chuanjianghe1024-cmd)。社区贡献通过短期分支或 fork 的 PR 进入 `main`；维护者目前保留直接推送能力。新增社区文件、CODEOWNERS 和 CI **不会自行开启分支保护**。严格规则只作为 `.github/rulesets/community-main.json` 中的禁用模板保存，本次没有向 GitHub 应用活动规则。

新增协作者应按实际职责授予权限；读者、问题整理者和开发者不必都拥有管理权限。模块负责人需先同意，再更新 CODEOWNERS。CODEOWNERS 用于评审路由，不授予仓库访问权；最后匹配行生效，同一路径有多个负责人时写在同一行。当前只有一名维护者，不强制代码所有者批准，避免负责人自己的 PR 没有其他人可批。

维护者负责确认 Issue 范围、评审与合并、型号证据核验及正式发布。以小 PR 为主；建议 squash 合并，但当前保留既有合并方式。依赖更新由 Dependabot 每周开 PR，不自动合并，也不自动修改发布清单。未承诺处理 SLA，不自动关闭长期未回复的问题。

## Actions 分层

| 稳定检查名称 / 流程 | 触发与职责 |
| --- | --- |
| `Repository checks` | 每次到 main 的 push、PR、merge group 或手动运行；使用固定版本及校验摘要的 actionlint 检查工作流语法，校验 YAML/JSON、社区链接、规则与检查名称，执行 CI 决策、Release 和 Wiki 的离线测试 |
| `Windows validation` | 相同事件始终产生最终检查；源代码、安装器、构建流程或未知文件变化时要求完整 Windows 验证，明确的文档/元数据改动可跳过 Windows 工作 |
| `Windows build` 内的 `build` | .NET 逻辑/原生测试、WinUI 构建与启动、DDC 辅助进程、MSI 安装/升级/卸载；成功后上传测试产物 |
| `Publish release` | 仅官方仓库 main 的发布资料变化或手动运行；校验并推广已通过的主分支 push 产物 |
| `Publish Wiki` | 仅官方仓库 main 的手册变化或手动运行；同步文档源到独立 Wiki |

必需检查工作流不使用顶层 `paths` 过滤，避免文档 PR 永久等待检查；Windows 按 job 决定是否运行，再由固定名称的 `Windows validation` 汇总。无法比较变更、首次分支、手动运行和 merge group 一律走完整 Windows 验证。`--no-renames` 保证把源文件移入 docs 也会验证删除的源代码。只有明确跳过或全部成功才放行；失败与取消不视作通过。

PR 更新会取消同一 PR 的旧 CI，main 的构建保留运行。两个 CI 工作流默认只读 token，不读取生产 secrets，不用 `pull_request_target` 执行贡献者代码，checkout 不保留凭据。仅独立发布 job 拥有写权限，并限制官方仓库和 main；fork 中的发布 job 跳过。外部贡献者首次运行可能需要维护者在 GitHub 批准运行；先检查工作流/脚本变化，不应为解决 fork CI 而开启写 token 或共享生产凭据。

CI 成功只代表对应自动检查通过，不代表实体 HKC/其他显示器已验证。PR 或手动构建的 MSI 不作为正式发布来源。文档提交可能没有新的 MSI；需要候选安装包时使用实际代码变更对应的主分支 push 构建。详细发布流程见 [development.md](development.md)。

## 将来切换为所有变更必须 PR + 评审

预置模板：[community-main.json](../.github/rulesets/community-main.json)。其默认 `enforcement` 为 `disabled`，没有绕过名单。导入本身不启用规则。

1. 至少准备两位可评审的维护者，并确认最近 PR 已出现且通过 `Repository checks` 与 `Windows validation`。PR 作者不能批准自己的 PR；“最后一次推送需他人批准”也需要另一位合格评审者。
2. 打开仓库 **Settings → Rules → Rulesets → New ruleset → Import a ruleset**，导入该 JSON。检查目标只包含 `main`，状态仍为 **Disabled**，绕过名单为空。
3. 确認要求：至少 **1 人批准**、新增可评审提交后旧批准失效、最后一次推送由其他人批准、解决全部评审讨论、两个固定检查由 **GitHub Actions** 提供且分支基于最新 main；禁止删除 main 和非快进推送。
4. 当负责人明确决定切换时，再把 **Enforcement status** 改为 **Active** 并保存。此后维护者自己的普通 push 也不能绕过，所有代码变更经 PR 进入主干。拥有管理权限的人仍能修改规则；这与允许绕过现有规则不同。
5. 开一个小文档 PR，核对未审批时无法合并、审批后两个检查满足规则。不要只看模板文件就宣布规则生效；读取 GitHub Rules 页面和实际 PR 状态确认。

当前模板不强制 code-owner review。以后有多个模块负责人且所有路径都能获得独立评审时，再按需要启用该项；不要在只有自己一名 CODEOWNER 时直接要求自己的 PR 获得代码所有者批准。模板中的 GitHub Actions app ID 为 GitHub.com 的 `15368`，迁移到其他 GitHub 实例需重新核对。合并队列尚未开启，但 CI 已接入 `merge_group`。

若流程必须回退，由有管理权限的负责人明确决定将规则改回 Disabled，记录原因并修正规则；不自动添加绕过者，不给 CI 发管理令牌。原有 classic branch protection 和其他规则会叠加，启用前检查是否存在冲突。

## 本地检查与 act

快速验证使用 Python 3.10+、Git 与 Bash（CI 门禁回归需要 Bash；Windows 可用 Git Bash）：

```sh
python3 -m venv .venv-ci
# Windows: .venv-ci\Scripts\python.exe; Linux/macOS: .venv-ci/bin/python
.venv-ci/bin/python -m pip install -r .github/requirements-ci.txt
.venv-ci/bin/python scripts/check-repository.py
.venv-ci/bin/python -m unittest discover -s tests/ci -v
python3 scripts/publish-release.py --validate-only
python3 scripts/publish-wiki.py --validate-only
python3 -m unittest discover -s tests/release -v
python3 -m unittest discover -s tests/wiki -v
```

如果你使用 `nektos/act`，安装 act 与 Docker 后，可以单独运行不含发布操作的 Ubuntu 快速检查：

```sh
act workflow_dispatch -W .github/workflows/ci.yml -j repository-checks --container-architecture linux/amd64
```

选用包含 Python 3、venv、Git、Bash、curl 和 Actions 所需 Node 运行时的 Ubuntu runner 镜像，初次拉取需要网络。这里的 Node 仅供官方 action 运行，不是桌面应用依赖。普通 Linux Docker 镜像不能替代 Windows/WinUI/MSI；act 的 Windows 宿主执行模式会在真实宿主运行脚本，因此不要在日常电脑上运行本仓库安装测试。Windows 完整验收以 GitHub 的可丢弃运行器或独立 Windows 测试环境为准。act 不模拟 GitHub 的权限和审批规则，不能据此宣布评审规则已验证。

参考：[GitHub Rulesets 导入](https://docs.github.com/en/repositories/configuring-branches-and-merges-in-your-repository/managing-rulesets/creating-rulesets-for-a-repository)、[必需检查与跳过行为](https://docs.github.com/en/pull-requests/how-tos/merge-and-close-pull-requests/troubleshooting-required-status-checks)、[act 运行器范围](https://nektosact.com/usage/runners.html)。

## English

Community contributions use branches/forks and PRs into `main`; the owner currently retains direct push access. CODEOWNERS routes reviews without granting permissions. The strict ruleset is a disabled JSON template, **not an active GitHub rule**. Add maintainers with appropriate access and consent before delegating ownership. Dependabot proposes updates; it does not merge or publish them.

Required check candidates have stable names: **Repository checks** and **Windows validation**. Both run for every PR, main push, merge group and manual dispatch. Documentation-only changes skip expensive Windows work at job level; the final check explicitly validates that decision. Unknown changes or unavailable comparisons run the full Windows suite. Failed/cancelled work never becomes a passing gate. PR CI has read-only permissions and no production secrets. Publishing is limited to the official repository's main branch; forks do not publish. Only validated main push artifacts may become releases.

To enable mandatory review later, first have another eligible reviewer and recent passing check runs. Import `.github/rulesets/community-main.json` under Settings → Rules → Rulesets. Review the disabled rule: main only, no bypass actors, one approval, dismissal of stale approvals, independent approval of the latest push, resolved conversations, up-to-date checks from GitHub Actions, no deletions or force pushes. Set **Active** only when the owner chooses to switch, then verify with a real PR. The author cannot approve their own PR. Code-owner approval is intentionally optional until ownership is shared. Administrators can still edit repository rules; they are not listed as bypass actors. Review overlapping classic protection and rulesets before activation.

Use the Python checks above locally. `act workflow_dispatch -W .github/workflows/ci.yml -j repository-checks --container-architecture linux/amd64` targets only the Ubuntu quick-check job; supply a compatible runner image with Python/venv/Git/Node. Linux containers cannot provide WinUI/MSI validation, and host-mode Windows tests can modify the host. GitHub permissions, required approvals and real monitor behavior require separate verification.
