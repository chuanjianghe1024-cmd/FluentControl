# 诊断与兼容性反馈 · Diagnostics and compatibility

[返回目录 / Home](Home.md)

## 中文

### 日志与本机文件

按 `Win+R`，输入 `%LOCALAPPDATA%\FluentControl` 可打开用户数据目录。

| 路径（相对该目录） | 用途 |
| --- | --- |
| `Logs\startup.log` | 启动、设备读取、写入和异常日志 |
| `Diagnostics\` | 本机显示器诊断报告 |
| `OsdPairings\` | 每台连接独立的 OSD 配对记录及归档 |
| `color-recovery.json` | 系统色温的原曲线与未完成恢复信息 |
| `user-state.json` | 设置、总配置和型号预设 |
| `monitor-names.json` | 屏幕名称和偏好 |
| `monitor-capabilities.json` | 能力元数据缓存，不是实时控制值 |

需要备份配置时，先从托盘退出应用再复制数据；不要在运行中修改或删除恢复记录。日志可能包含本机设备身份、路径和你填写的名称，公开前请检查。

### 怎样采集有用的信息

1. 在目标屏幕的 **型号适配 → 采集 / 提交适配报告** 中填写连接方式、HDR 和相关环境；采集与导出可以在本机完成，网站提交不是前置条件。
2. 先采集基线，再用显示器实体菜单**只改一项**，记录选项的原厂名称与变化。不要同时切换 HDR、接口和色温，否则难以判断原因。
3. 保存实际状态、原始值、最大值、错误码、耗时与显示效果。读取失败不应写成 0；“声明支持”“读成功”“写成功”“效果正确”要分别说明。
4. 导出 JSON。默认排除系统设备实例、显示路径和原始能力串；原始串可显式勾选，分享前检查其中的标识以及自填备注。

只读诊断不通过恢复出厂、切电源或遍历未知写指令探测功能。若要探索未知命令，使用独立的[本地 OSD 配对](OSD-Pairing.md)，逐次观察确认。

### 反馈时带上这些

```text
FC 版本：
Windows 版本 / 构建：
显卡及驱动版本：
显示器型号 / PnP 型号 ID / 固件：
连接：HDMI / DP / USB-C，是否经过扩展坞或 KVM：
HDR / 节能 / 画面模式：
操作步骤与预期结果：
实际画面 / 实体菜单结果：
请求值、读回值、错误码与对应时间：
附件：已检查隐私的日志片段、诊断或配对记录
```

通过 [Issues](https://github.com/chuanjianghe1024-cmd/FluentControl/issues) 提交即可。不需要公开凭据、完整个人数据目录或无关内容。在线适配报告服务尚未部署，不能把网页入口存在理解为提交已经成功。

### USB 与 HDMI / DP

有 USB 集线器或 Type-B 上行口，只能证明存在 USB 连接，不能证明提供显示器控制协议。更换 HDMI / DP 可能改变连接表现，但不能据此断言某个接口必然支持更多 OSD 操作。请在同一屏幕、固件和模式下记录对比结果。FC 目前未实现 USB/HID 控制；相关[研究步骤](https://github.com/chuanjianghe1024-cmd/FluentControl/blob/main/docs/usb-hid-adaptation.md)用于后续实机验证。

## English

Open `%LOCALAPPDATA%\FluentControl` with Win+R. Logs are in `Logs\startup.log`, diagnostics in `Diagnostics\`, pairing records in `OsdPairings\`, settings/profiles in `user-state.json`, names/preferences in `monitor-names.json`, and capability metadata in `monitor-capabilities.json`. `color-recovery.json` stores software-color recovery information. Exit before backing up; do not modify active recovery records.

Use the selected display's diagnostic collector locally. Capture a baseline, change one physical-menu setting, and record its exact name and observed effect. Keep HDR, connection and other settings unchanged while comparing one variable. Preserve raw values, errors and timing; failed reads are not zero. Advertised support, successful reads/writes and correct visual behavior are separate observations.

Exports exclude system device instances, display paths and raw capabilities by default. Raw capabilities are optional. Review logs, identifiers and user-written notes before posting. Read-only diagnostics do not reset displays or scan unknown writes; explicit command experiments belong in [OSD pairing](OSD-Pairing.md).

For an [issue report](https://github.com/chuanjianghe1024-cmd/FluentControl/issues), provide FC/Windows versions, GPU and driver, monitor model/PnP ID/firmware, connection and dock/KVM, HDR and picture mode, reproduction steps, expected and actual results, timestamps and relevant redacted evidence. Do not post credentials or a whole personal data folder. The online adaptation-report service is not deployed.

A USB hub or upstream port does not prove a monitor-control protocol. Compare HDMI and DP under otherwise identical conditions rather than assuming universal differences. USB/HID control is not implemented in FC.
