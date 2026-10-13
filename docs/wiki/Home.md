# FluentControl 用户手册 · User guide

**把多屏、音量、输入和鼠标的常用调节放在一个面板里。**

本手册按正式版 **v0.3.118** 的实际功能编写，更新于 **2026-10-13**。FluentControl（FC）面向 Windows x64，提供显示器 DDC/CI 控制、系统色温与 HDR、总配置、型号预设、桌面面板和本地 OSD 配对。

[下载最新正式版](https://github.com/chuanjianghe1024-cmd/FluentControl/releases/latest) · [本版更新](https://github.com/chuanjianghe1024-cmd/FluentControl/releases/tag/v0.3.118) · [反馈问题](https://github.com/chuanjianghe1024-cmd/FluentControl/issues)

采用 [MIT 许可证](https://github.com/chuanjianghe1024-cmd/FluentControl/blob/main/LICENSE)。

## 从这里开始

第一次使用，请先看[安装与入门](Getting-Started.md)。调不动亮度或对比度，先看[故障排查](Troubleshooting.md)；开启了 HDR 的屏幕另见[HDR 与系统色温](HDR-and-Color-Temperature.md)。

| 想完成的事 | 对应页面 |
| --- | --- |
| 安装、更新、设置自启动 | [安装与入门](Getting-Started.md) |
| 单独或一起调节多块显示器 | [显示器与多屏控制](Monitor-Controls.md) |
| 保存办公、游戏和夜间配置 | [总配置与型号预设](Profiles-and-Presets.md) |
| 切换 HDR、调整软件色温 | [HDR 与系统色温](HDR-and-Color-Temperature.md) |
| 使用桌面面板、音量与快捷键 | [桌面、音频与快捷操作](Desktop-Audio-and-Shortcuts.md) |
| 尝试配对原厂 OSD 菜单 | [OSD 菜单与本地配对](OSD-Pairing.md) |
| 排查无响应、读回不一致和安装问题 | [常见问题](Troubleshooting.md) |
| 找日志、导出诊断和报告兼容性 | [诊断与兼容性反馈](Diagnostics-and-Compatibility.md) |
| 了解计划、参考项目与参与方式 | [计划与参与](Roadmap-and-Contributing.md) |

## 先了解三个边界

- **按能力开放。** 型号、固件、视频连接、HDR 和当前画面模式都会影响可调项目；显示器有某个实体菜单选项，不一定允许软件遥控。
- **三种菜单能力分别判断。** FC 软件菜单、原厂 OSD 启用状态、原厂菜单的方向键导航不是同一功能。能唤出原厂菜单，也不能保证能远程导航。
- **本地功能可离线使用。** 配置、预设、配对和文件交换无需上传；在线分享站与在线型号适配服务尚未部署。USB/HID 控制、应用自动切换和定时配置仍是后续方向。

## English

FluentControl brings monitor controls, system audio, input and mouse settings into one Windows panel. This guide describes **v0.3.118**, updated **2026-10-13**. Start with [installation and setup](Getting-Started.md), or use the pages below.

- [Monitor and multi-display controls](Monitor-Controls.md)
- [Global profiles and model presets](Profiles-and-Presets.md)
- [HDR and software color temperature](HDR-and-Color-Temperature.md)
- [Desktop panel, audio and shortcuts](Desktop-Audio-and-Shortcuts.md)
- [OSD menus and local pairing](OSD-Pairing.md)
- [Troubleshooting](Troubleshooting.md)
- [Logs, diagnostics and compatibility reports](Diagnostics-and-Compatibility.md)
- [Roadmap and contributing](Roadmap-and-Contributing.md)

Licensed under the [MIT License](https://github.com/chuanjianghe1024-cmd/FluentControl/blob/main/LICENSE). Each page includes an English section. Available hardware controls depend on the display, firmware, connection and active mode. The FC software menu, native OSD enablement and native navigation are separate capabilities. Local profiles and pairing work offline; the online sharing service and adapter catalog are not deployed, and USB/HID control is not implemented.

For source structure and engineering conventions, see [agent.md](https://github.com/chuanjianghe1024-cmd/FluentControl/blob/main/agent.md). For changes after this guide's baseline, see the [changelog](https://github.com/chuanjianghe1024-cmd/FluentControl/blob/main/CHANGELOG.md).
