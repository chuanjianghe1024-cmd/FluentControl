# Wiki 维护与发布 / Wiki maintenance

`docs/wiki/` 是用户手册的维护源。正文按 v0.3.92 编写，后续随实际功能更新；不要将计划写成已实现能力。十篇页面（含首页）、侧边栏和页脚均在该目录维护，中英文内容保持一致。

## 首次初始化

1. 确认仓库 Settings → General → Features 中启用了 Wiki。
2. 若 Wiki 还没有任何页面，打开 [Wiki](https://github.com/chuanjianghe1024-cmd/FluentControl/wiki)，选择 **Create the first page**，保存一个 `Home` 页面。首次页面需通过 GitHub 创建，才能克隆 Wiki 的独立 Git 仓库。已有页面时跳过此步。
3. 在 Actions → **Publish Wiki** 中运行 `main` 工作流，或重新运行先前因未初始化而失败的任务。

依据：[GitHub 官方 Wiki 编辑说明](https://docs.github.com/en/communities/documenting-your-project-with-wikis/adding-or-editing-wiki-pages)。工作流使用本仓库的临时 `GITHUB_TOKEN` 与 `contents: write`，不需要个人访问令牌，也不访问其他仓库。若无法克隆，检查首次页面、Wiki 开关和工作流权限；不能把所有连接错误都认定为未初始化。

## 日常编辑与验证

在 `docs/wiki/` 中编辑 Markdown，页名使用 ASCII 字母、数字和连字符；保留 `Home.md`、`_Sidebar.md` 和 `_Footer.md`。同目录链接使用 `[名称](Page-Name.md)`，发布时转换成 Wiki 地址，因此仓库内也能正常阅读。外部资料使用完整 HTTPS 链接。

```sh
python3 scripts/publish-wiki.py --validate-only
python3 -m unittest discover -s tests/wiki -v
```

提交到 `main` 后 `.github/workflows/wiki.yml` 自动执行，亦可手动运行。验证包括页面命名与链接目标，以及本地 Git 仓库中的同步、保留额外页面和重复发布不产生提交。

同步只更新源目录列出的文件，保留 Wiki 历史和其他页面；删除或重命名源文件不会自动删除线上旧页，需维护者单独确认处理。推送不使用 force；遇到并发编辑会停止，先审阅差异再重试。若在 GitHub Wiki 直接改了这些受管理页面，请先把修改带回源文件，否则下次同步将恢复为源文件内容。

工作流摘要报告 Wiki 提交 SHA。首次发布及导航变更后核查首页、侧边栏、正文交叉链接和页脚；仅主仓库提交成功不能当作 Wiki 已发布。该流程独立于 MSI 发布，不移动应用版本标签，不替换正式安装包。

## English

Maintain the bilingual user guide in `docs/wiki/`. If the Wiki has never been initialized, enable Wiki in repository settings and save the first `Home` page on GitHub, then run **Publish Wiki** on `main`. Existing Wikis do not need reinitialization.

Use same-directory `.md` links in source pages; the publisher validates and rewrites them to GitHub Wiki URLs. Run the two commands above before publishing. Pushes affecting the sources, maintenance guide, workflow or publisher on `main` trigger synchronization; manual dispatch is also available.

The workflow uses the repository's temporary `GITHUB_TOKEN` with `contents: write`. It preserves unrelated pages and Git history, uses normal pushes, and reports the resulting Wiki commit. Source deletions do not automatically remove old online pages. Bring direct Wiki edits back into the source repository before the next sync. Verify the actual Wiki and navigation after publishing; a source commit alone is not evidence of publication. Application releases and installer assets are unaffected.
