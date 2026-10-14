# 总配置与型号预设 · Profiles and presets

[返回目录 / Home](Home.md)

## 中文

### 先选对保存方式

| 内容 | 总配置 | 型号预设 |
| --- | --- | --- |
| 用途 | 在本机切换一整套桌面设备状态 | 保存可复用的单屏硬件参数 |
| 显示器参数 | 多块屏幕的可保存状态 | 对应型号的单屏设置 |
| 系统音量、输入、鼠标 | 包含可用项 | 不包含 |
| 系统软件色温 | 可保存已确认的本机目标 | 不包含 |
| Windows HDR、重置、电源和按键锁 | 不保存为自动重放项 | 不保存 |
| 主要复用范围 | 当前电脑 | 按型号整理、文件交换后按能力应用 |

### 保存与更新

先完成实际调节，再保存总配置，例如“办公”“夜间游戏”。总配置保留独立参数快照，后来编辑型号预设不会悄悄修改已保存的总配置。

修改已有配置时使用“更新”。暂时离线的屏幕、当前不可读的项目和已有亮度映射会按字段保留，不会因为这次没检测到就整套清空。读回尚未确认的值不会作为新的真实状态保存。

型号配置库会显示已连接的型号以及已有预设的型号。没有预设的已连接型号也有保存入口；同型号设备共用型号分组，但应用时仍检查目标屏幕的能力和合法选项。

### 导入、导出与使用

- 型号配置库支持导出全部或指定型号的预设。导入文件先入库，**不会立即向显示器发送指令**。
- 相同名称与内容去重，同名但内容不同会区分保存；离线型号也可以先导入。
- 跨型号应用只使用目标支持的参数与选项，之后可另存到目标型号组。型号一致也不保证固件、HDR 和当前模式一致。
- 应用/游戏标签用于描述与筛选，**目前不会自动检测你启动了哪个游戏并切换配置**。
- 本地总配置、公开型号预设、OSD 配对文件是不同用途的数据，不要混为同一种导入文件。
- 分享前检查自己填写的别名、场景名、应用标签和备注。公共型号文件不包含本机设备实例、序列号或音频路由身份。

想获得其他人的配置，目前可以交换本地 JSON 文件；在线分享服务尚未部署。

## English

A **global profile** captures a local setup across displays, system Volume/Input, mouse settings and confirmed software-temperature targets. A **model preset** stores reusable hardware settings for one display model. HDR, resets, power and button locks are not saved as automatic replay actions; model presets also exclude system audio, mouse and software temperature.

Adjust devices first, then save a profile such as Work or Night gaming. Global profiles retain independent snapshots: editing a library preset later does not alter existing profiles. Use Update to refresh a saved setup. Offline or temporarily unreadable fields are preserved rather than cleared, and unconfirmed readback is not captured as a new known value.

The model library includes connected models and models with saved presets. Identical models share a library group, but each target is checked when applying settings. Matching model names do not guarantee matching firmware or HDR behavior.

Export all model presets or a selected model. Import stores data without applying it; matching content is deduplicated and differing content is retained separately. Offline models can be imported. Cross-model application uses only supported parameters and options and can be saved under the target model.

Application/game labels are descriptive filters, not automatic app-switch rules. Global profiles, portable model presets and OSD pairing files serve different purposes. Review user-written names, labels and notes before sharing. Portable model files exclude local device instances, serial numbers and audio identities. Local JSON exchange is available; the online sharing service is not deployed.

## 自动亮度与数据来源 / Automatic brightness and data sources

设置 → 自动调节预设：光照 JSON 订阅、独立的 lux/24 小时曲线、按地区/坐标计算日出日落，以及 FC 明暗主题。默认关闭，明确启用后会调整选定屏幕；支持相同协议的真实或模拟数据。参见[使用与格式](https://github.com/chuanjianghe1024-cmd/FluentControl/blob/main/docs/adaptive-presets.md)。

Settings → Adaptive presets supports live light-data subscriptions, lux/time curves, local sunrise/sunset calculation and FC themes. Off by default; enabling adjusts the selected display. Real and simulated feeds share one schema. USB/BLE bridges and physical validation remain pending.
