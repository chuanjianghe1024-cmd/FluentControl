# 显示器与多屏控制 · Monitor controls

[返回目录 / Home](Home.md)

## 中文

### 什么可以调

在硬件支持并允许当前模式调节时，FC 可提供亮度、对比度、硬件色温预设、RGB、Gamma、饱和度、画面模式、输入源和显示器硬件音量等项目。以实际显示的控制项和读回结果为准。

显示器声明支持、一次读取成功、接受写入、最终画面变化是不同的证据。特别是色温、画面模式和原厂菜单，不能只看到“发送成功”就认定已经生效。

### 单独与整体控制

| 使用方式 | 行为 |
| --- | --- |
| 单独控制 | 调整所选屏幕，适合初次验证与精细设置 |
| 整体控制 | 不同型号也能联动；只写入支持该项目和所选值的目标 |
| 部分支持 | 标明支持数量，某项并非一定对全部屏幕生效 |
| 不同 / Mixed | 多块屏幕当前值不一致，不代表读取失败 |
| 亮度映射 | 给每块屏幕设置自己的范围、偏移或曲线，改善统一滑条下的相对明暗 |

亮度百分比一致不代表实际亮度、色彩或观感一致；映射也不是仪器校准。

### 读取与重新检测

正常发现设备只读取信息，不通过重置、关机或试写来探测功能。基础控制先显示，再补齐高级功能；能力缓存只保存能力元数据，不把历史值当作当前亮度。

设备或显示模式变化后先重新读取。如果高级项目被异常扫描保护跳过，可主动重新检测。长期卡住时看[日志与诊断](Diagnostics-and-Compatibility.md)，不要连续反复发送同一指令。

### 色温、模式与屏幕音量

- 硬件色温/场景下拉框是“选择要发送的选项”，真实读回值单独展示。选择完成后清空，便于再次发送同一个选项。
- 写入 6500 K 却读回 9300 K，表示请求和显示器返回值不一致；FC 保留原始结果，不擅自交换编码。请记录实体菜单显示什么。
- **系统色温（软件）** 是独立的显卡颜色调整，见[HDR 与色温](HDR-and-Color-Temperature.md)。
- 显示器有 HDMI/DP 音频，不代表支持 DDC 硬件音量。电脑声音请在“音量与输入”里调节。
- 输入切换、电源、重置和按键锁等可能影响当前连接或操作，保留确认步骤。

## English

FC offers brightness, contrast, hardware color presets, RGB, gamma, saturation, picture modes, inputs and monitor volume where supported. Capability declarations, successful reads, accepted writes and visible effects are separate evidence.

Use individual controls to test one display. Overall controls can link different models, applying only to targets supporting the selected feature and value. A supported-count indicator means partial coverage; **Mixed** means the screens currently have different values. Per-display brightness mappings adjust ranges, offsets or curves for more useful linked control, but equal percentages are not equal measured brightness.

Discovery is read-only. Basic controls appear first, followed by advanced features. Capability caches contain metadata, not historical values presented as live readings. Refresh after connection or mode changes, and use explicit redetection when an interrupted advanced probe has been skipped.

Hardware color and picture selectors are command choices; actual readback is displayed separately. A mismatch such as writing 6500 K and reading 9300 K remains visible rather than being silently remapped. Record the physical menu's result when reporting it. [Software temperature](HDR-and-Color-Temperature.md) is a separate GPU effect.

An HDMI/DP audio endpoint does not prove DDC monitor-volume support. Use the system Volume control for PC audio. Input, power, reset and button-lock operations retain confirmation where applicable. See [troubleshooting](Troubleshooting.md) for unavailable or ineffective controls.
