# 计划、许可与参与 · Roadmap, license and contributing

[返回目录 / Home](Home.md)

## 中文

### 已有能力与后续方向

本手册描述 v0.3.92。显示器控制、系统音量与输入、鼠标、总配置和型号预设、桌面面板、软件色温、Windows HDR 以及本地 OSD 配对已提供；具体硬件效果以设备实测为准。

后续重点包括更多型号的色温/场景与原厂导航适配、USB/HID 协议研究、WMI 内屏亮度与厂商接口评估、混合 DPI 和热插拔体验，以及在线配置分享与适配报告服务。应用自动联动、定时配置、ICC/MHC2 管理也尚未实现。计划不代表已有支持或承诺日期。

### MIT 许可证

本仓库采用 **[MIT License](https://github.com/chuanjianghe1024-cmd/FluentControl/blob/main/LICENSE)**。允许使用、修改、分发和商业使用，包括在闭源项目中使用；分发软件副本或实质部分时，需要保留版权与许可声明。软件按“现状”提供，完整条款以 LICENSE 为准。

第三方依赖和单独注明许可的内容继续适用各自许可证。本仓库的 MIT 声明不改变独立网站仓库或外部参考项目的许可。

### 怎样参与

- 报告问题：按[诊断模板](Diagnostics-and-Compatibility.md)给出环境、复现步骤和实测证据。
- 提交型号适配：注明完整型号、固件、连接、HDR 状态、指令来源、请求/返回与实体效果，不把别的品牌指令当作通用协议。
- 改进翻译或文档：提交修正，说明对应界面或页面；保留八种界面的键和占位符一致性。
- 开发贡献：先看 [agent.md](https://github.com/chuanjianghe1024-cmd/FluentControl/blob/main/agent.md) 与[开发手册](https://github.com/chuanjianghe1024-cmd/FluentControl/blob/main/docs/development.md)。用户手册源文件在 `docs/wiki/`，工程结构和开发约束不混入普通使用步骤。

### 参考项目

| 项目 | 参考方向 |
| --- | --- |
| [Twinkle Tray](https://github.com/xanderfrangos/twinkle-tray) | 分阶段发现、通信隔离和兼容处理 |
| [ddcutil](https://github.com/rockowitz/ddcutil) / [ddccontrol-db](https://github.com/ddccontrol/ddccontrol-db) | DDC 能力解释、型号定义与适配库维护 |
| [msigd](https://github.com/couriersud/msigd) | 特定 MSI 显示器的 USB/HID 协议研究 |
| [ddc-mode-switcher](https://github.com/Gunther-Schulz/ddc-mode-switcher) / [ddc-toolkit](https://github.com/andres-valencia/ddc-toolkit) | 特定型号的原厂菜单映射和实测记录 |

这些项目是研究参考，不代表 FC 已集成其全部能力；不同品牌、型号和固件之间不能直接继承控制协议。引入外部代码或数据时仍需遵守对应许可证。

## English

This guide covers v0.3.92. Current features include monitor controls, system Volume/Input, mouse settings, global profiles and model presets, a desktop panel, software temperature, Windows HDR and local OSD pairing. Hardware behavior still requires verification.

Planned work includes broader model adaptation, USB/HID research, WMI internal-panel brightness and OEM interfaces, mixed-DPI/hot-plug improvements, and online sharing/adaptation services. Automatic application rules, schedules and ICC/MHC2 management are also unimplemented. Plans are not support claims or delivery dates.

### MIT license

This repository uses the **[MIT License](https://github.com/chuanjianghe1024-cmd/FluentControl/blob/main/LICENSE)**. It permits use, modification, distribution and commercial use, including in closed-source projects, provided the copyright and permission notice is retained in copies or substantial portions. The software is provided as-is; the LICENSE contains the full terms. Third-party dependencies and separately licensed content retain their own licenses, and this does not relicense the independent website repository or reference projects.

### Contribute

Report issues with the [diagnostic checklist](Diagnostics-and-Compatibility.md). Model adaptations need exact model/firmware, connection, HDR state, protocol provenance and observed results. Translation fixes should preserve locale keys and placeholders. For code contributions, read [agent.md](https://github.com/chuanjianghe1024-cmd/FluentControl/blob/main/agent.md) and the [development guide](https://github.com/chuanjianghe1024-cmd/FluentControl/blob/main/docs/development.md). User-guide sources live in `docs/wiki/`.

The reference projects above inform discovery, DDC interpretation, model libraries and specific USB/native-menu research. They are not promises of equivalent FC hardware coverage. Respect the relevant licenses when reusing external code or data.
