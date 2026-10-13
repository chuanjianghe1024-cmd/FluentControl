# 安装与入门 · Getting started

[返回目录 / Home](Home.md)

## 中文

### 安装

开发构建新增 `FluentControl-<版本>-x64-Setup.exe`：Win11 风格、明暗主题、八种语言，仍为离线当前用户安装。选择目录和自启动后安装，完成时点击“启动 FluentControl”；也可直接关闭。同版本打开可修复或卸载。当前正式版 v0.3.92 仍提供下面的 MSI 向导。

1. 在 [Releases](https://github.com/chuanjianghe1024-cmd/FluentControl/releases/latest) 下载最新正式版 `FluentControl-<版本>-x64.msi`。主要面向 Windows 11 x64；安装包包含离线运行库。
2. 新安装可选择自己有写入权限的本地目录。默认位置是 `%LOCALAPPDATA%\Programs\FluentControl`，采用当前用户安装，无需管理员权限。
3. 按需勾选“随 Windows 启动”。新安装默认关闭；升级时参考实际启动项，不会因为以前安装过就强制开启。
4. 完成页可选择立即启动；只有点击成功安装后的“完成”才会启动，静默安装不会自动打开程序。

正式安装包目前未做代码签名，Windows 可能显示“发布者未知”。请从项目 Release 下载，可对照附件 `SHA256SUMS.txt` 核验文件；校验值不是发布者签名。

### 第一次调节

1. 在外接显示器的实体菜单中确认 **DDC/CI** 已开启；没有该选项时查看型号手册。先用正常的视频连接测试。
2. 打开 FC，等待设备读取。亮度、对比度可以先出现，高级项目随后补齐。
3. 用识别功能确认屏幕，再起“左屏”“右屏”等别名。M1/M2 是软件标签，不是型号。
4. 先单独调整一块屏幕，确认效果，再使用整体控制。读回失败或项目缺失时见[故障排查](Troubleshooting.md)。
5. 调整好后保存[总配置或型号预设](Profiles-and-Presets.md)。保存和启动本身不会自动应用其他配置。

### 更新与退出

- **设置 → 关于与更新**：手动检查 GitHub 正式 Release，下载并校验后，由你选择退出并安装。启动时不自动检查或安装。
- 手动运行 MSI 前，从托盘彻底退出旧版；普通关闭窗口默认隐藏到托盘。
- 升级沿用并锁定原安装目录；若要迁移目录，先卸载再安装。升级与卸载保留个人配置。
- 开机启动可在安装向导或应用设置里调整，登录后在托盘运行。
- GitHub Actions 的测试包可能包含尚未正式发布的变化，日常使用优先选择 Release。

## English

### Install and set up

Development builds add `FluentControl-<version>-x64-Setup.exe`: a Fluent offline per-user installer with themes and eight languages. Choose the folder and startup option, then explicitly launch or close after success. Reopening the same version offers repair/uninstall. Stable v0.3.92 still uses the MSI wizard described below.

1. Download `FluentControl-<version>-x64.msi` from the [latest stable release](https://github.com/chuanjianghe1024-cmd/FluentControl/releases/latest). FC primarily targets Windows 11 x64 and includes runtimes for offline installation.
2. A fresh installation lets you select a writable local folder. The default is `%LOCALAPPDATA%\Programs\FluentControl`; installation is per user without administrator privileges.
3. Choose whether to start with Windows. This is off by default for a fresh installation. The completion page can launch FC when you click Finish after success.
4. Enable DDC/CI in the external monitor's physical menu where available. Let FC read devices, identify and name each screen, then test an individual adjustment before using linked controls.
5. Save the result as a [global profile or monitor preset](Profiles-and-Presets.md). Startup does not automatically apply the last profile.

The installer is currently unsigned. Use the official Release and optionally verify `SHA256SUMS.txt`; checksums do not constitute a publisher signature.

### Update and exit

Use **Settings → About & updates** to check stable releases on demand. After download and verification, choose when to exit and install. There is no automatic startup update check or installation.

Close normally to keep FC in the tray; exit from the tray before manually installing an update. Upgrades keep the original installation folder and personal settings. Uninstalling also retains settings; uninstall and reinstall to change the installation folder. The startup option can be changed in the installer or application settings. Prefer stable Releases over Actions test packages for everyday use.
