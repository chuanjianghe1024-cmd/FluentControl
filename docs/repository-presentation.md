# 仓库展示维护

`.github/repository-metadata.json` 保存仓库简介、主页和主题标签的期望值，便于维护者同步。它不是 GitHub 自动识别的配置文件；提交该文件不会修改仓库 About 区域。

- 简介以实际提供的 DDC/CI 多屏调节、系统音频和桌面组件为核心，同时保留中英文可检索用语。
- 主页暂指向可用的正式 Release 下载入口。`fctrl.app` 服务实际部署后再更换，不把尚未上线的服务作为下载入口。
- Topics 仅包含实际技术和用途，不堆砌无关热门词。
- `docs/assets/social-preview.png` 是 1280 × 640 的仓库分享图，复用现有图标；SVG 为可编辑源文件。README 使用同一图片，但 GitHub 的 Social preview 仍需在仓库 Settings 中单独上传。
- README 的功能文字、图片替代文本、最新 Release 与构建状态入口帮助用户理解和找到项目；不承诺搜索引擎收录时间或排名。

## 同步到 GitHub

1. 仓库首页 **About → Edit**：应用 JSON 中的 description、homepage 和 topics。
2. 仓库 **Settings → General → Social preview → Edit → Upload an image**：上传 PNG 并保存。
3. 重新读取仓库 About 和 Settings，确认实际保存。不能把素材已提交等同于设置已生效。

首次准备时间：2026-10-10。当前素材和元数据已纳入源码；GitHub About / Social preview 的实际应用状态以仓库页面为准。

官方说明：[Topics](https://docs.github.com/en/repositories/managing-your-repositorys-settings-and-features/customizing-your-repository/classifying-your-repository-with-topics) · [Social preview](https://docs.github.com/en/repositories/managing-your-repositorys-settings-and-features/customizing-your-repository/customizing-your-repositorys-social-media-preview)。
