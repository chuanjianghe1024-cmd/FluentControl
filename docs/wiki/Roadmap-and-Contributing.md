# 计划、许可与参与 · Roadmap, license and contributing

[返回目录 / Home](Home.md)

## 中文

### 已有能力与后续方向

本手册描述 v0.3.135。显示器控制、系统音量与输入、鼠标、总配置和型号预设、桌面面板、软件色温、Windows HDR 以及本地 OSD 配对已提供；另含 WMI 内屏亮度、原生 RP2040 USB / HTTP 光照订阅、采样设置、64 MiB 历史和按时间/日出日落调节亮度与 FC 主题；具体硬件效果以设备实测为准。

后续重点包括更多型号的色温/场景与原厂导航适配、显示器 USB/HID 协议研究、WMI 实机验收与厂商接口评估、混合 DPI 和热插拔体验，以及在线配置分享与适配报告服务。应用自动联动、定时应用整套配置、ICC/MHC2、C3/CH552 固件、HID/BLE 光照接入、FC 刷写和偏好学习也尚未实现。光照相关 Windows/WinUI/MSI/Setup 自动化已通过；实体 RP2040 完整联调、真实显示器及混合 DPI 仍待确认。计划不代表已有支持或承诺日期。

### 使用许可

本手册描述的历史 v0.3.135 仍按其随附 MIT 许可处理。当前主分支采用[PolyForm Noncommercial 1.0.0](https://github.com/chuanjianghe1024-cmd/FluentControl/blob/main/LICENSE)：允许许可定义的非商业使用、修改和分发，包括条款列明的机构用途；一般商业用途未获授权，具体范围以 LICENSE 为准。历史 MIT / PolyForm 授权不追溯撤销。第三方及独立仓库许可不受本次变更影响。

### 怎样参与

- 报告问题：按[诊断模板](Diagnostics-and-Compatibility.md)给出环境、复现步骤和实测证据。
- 提交型号适配：注明完整型号、固件、连接、HDR 状态、指令来源、请求/返回与实体效果，不把别的品牌指令当作通用协议。
- 改进翻译或文档：提交修正，说明对应界面或页面；保留八种界面的键和占位符一致性。
- 开发贡献：先看[贡献指南](https://github.com/chuanjianghe1024-cmd/FluentControl/blob/main/CONTRIBUTING.md)；社区以 PR 协作，维护者当前仍可直推，未来强制评审步骤见[维护手册](https://github.com/chuanjianghe1024-cmd/FluentControl/blob/main/docs/maintaining.md)。工程细节见 [agent.md](https://github.com/chuanjianghe1024-cmd/FluentControl/blob/main/agent.md) 与[开发手册](https://github.com/chuanjianghe1024-cmd/FluentControl/blob/main/docs/development.md)。用户手册源文件在 `docs/wiki/`，工程结构和开发约束不混入普通使用步骤。

### 参考项目

| 项目 | 参考方向 |
| --- | --- |
| [Twinkle Tray](https://github.com/xanderfrangos/twinkle-tray) | 分阶段发现、通信隔离和兼容处理 |
| [ddcutil](https://github.com/rockowitz/ddcutil) / [ddccontrol-db](https://github.com/ddccontrol/ddccontrol-db) | DDC 能力解释、型号定义与适配库维护 |
| [msigd](https://github.com/couriersud/msigd) | 特定 MSI 显示器的 USB/HID 协议研究 |
| [ddc-mode-switcher](https://github.com/Gunther-Schulz/ddc-mode-switcher) / [ddc-toolkit](https://github.com/andres-valencia/ddc-toolkit) | 特定型号的原厂菜单映射和实测记录 |

这些项目是研究参考，不代表 FC 已集成其全部能力；不同品牌、型号和固件之间不能直接继承控制协议。引入外部代码或数据时仍需遵守对应许可证。

## English

This guide covers v0.3.135. Current features include monitor controls, system Volume/Input, mouse settings, global profiles and model presets, a desktop panel, software temperature, Windows HDR and local OSD pairing, plus WMI panel brightness, native RP2040 USB/HTTP light subscriptions, sampling controls, 64 MiB history and scheduled brightness/FC themes. Hardware behavior still requires verification.

Planned work includes broader model adaptation, monitor USB/HID research, physical WMI validation and OEM interfaces, mixed-DPI/hot-plug improvements, and online sharing/adaptation services. Automatic application rules, scheduled whole-profile application, ICC/MHC2 management, C3/CH552 firmware, HID/BLE light input, flashing and learning are unimplemented. Light-related Windows/WinUI/MSI/Setup automation passed; physical RP2040 integration, real displays and mixed DPI remain unverified. Plans are not support claims or delivery dates.

### Usage license

Historical v0.3.135 retains its accompanying MIT terms. Current main uses the [PolyForm Noncommercial 1.0.0](https://github.com/chuanjianghe1024-cmd/FluentControl/blob/main/LICENSE), permitting noncommercial use, modification and distribution, including the organization uses listed in LICENSE. General commercial use is not granted; the full license governs. Earlier MIT / PolyForm grants are not revoked. Third-party and independent-repository terms remain unchanged.

### Contribute

Report issues with the [diagnostic checklist](Diagnostics-and-Compatibility.md). Model adaptations need exact model/firmware, connection, HDR state, protocol provenance and observed results. Translation fixes should preserve locale keys and placeholders. For code contributions, start with [CONTRIBUTING.md](https://github.com/chuanjianghe1024-cmd/FluentControl/blob/main/CONTRIBUTING.md). Community changes use PRs; the maintainer retains direct push access until the [strict review policy](https://github.com/chuanjianghe1024-cmd/FluentControl/blob/main/docs/maintaining.md) is activated. For engineering details, read [agent.md](https://github.com/chuanjianghe1024-cmd/FluentControl/blob/main/agent.md) and the [development guide](https://github.com/chuanjianghe1024-cmd/FluentControl/blob/main/docs/development.md). User-guide sources live in `docs/wiki/`.

The reference projects above inform discovery, DDC interpretation, model libraries and specific USB/native-menu research. They are not promises of equivalent FC hardware coverage. Respect the relevant licenses when reusing external code or data.
