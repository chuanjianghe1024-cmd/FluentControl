# 桌面、音频与快捷操作 · Desktop, audio and shortcuts

[返回目录 / Home](Home.md)

## 中文

### 音量与输入

声音页面提供两个逻辑控制：**音量**和**输入**，分别调节 Windows 当前默认输出与默认输入的音量、静音。FC 不要求你选择具体设备，也不把显示器音频端点与硬件音量强行绑定。

Voicemeeter、虚拟声卡或 NVIDIA Broadcast 等线路由你在系统或对应软件中配置。FC 跟随系统默认选择，不修改音频路由，也不会在每个应用上增加均衡器或独立处理链。当前没有单独的“默认通信设备”控制。

如果输入设备不存在或暂不可用，会保留不可用状态；更新总配置时，不会因此删除原来保存的输入值。旧版本按具体设备保存的音量会跳过，提示更新为“音量 / 输入”系统项。

### 桌面面板

在 **设置 → 桌面控制面板** 中开启，并选择整体或单独模式的控制项。面板可移动、缩放，透明度为 10%–85%；超过可见行数的内容滚动查看。

- **双击**：立即解锁并进入正常前台，随后可以调整、移动或缩放。
- **失焦或 Esc**：锁定回到底层，避免日常操作误触。
- **Win+D**：支持系统“显示桌面”；返回桌面后仍可双击解锁。
- 锁定状态不会通过点击写入设备。若遇到前置或 DPI 问题，请记录 Windows 版本和缩放比例后[反馈](Diagnostics-and-Compatibility.md)。

桌面组件在整体和单独模式下都显示 HDR 屏幕名称与调节限制。HDR 下部分显示器会锁定亮度等参数；关闭 HDR 后重新读取设备可更新提示。

组件的“自动调节”开关与主界面共用同一规则和目标屏幕，不会因切换整体/单独模式而改变目标；先在自动调节页面配置好规则，解锁组件后即可切换。手动调整仍保留原有 10 分钟保持逻辑。

选择已接入的光照来源后，组件显示 lux 与采样时间。即使关闭自动调光，也会按该来源的采样周期刷新，不强制开启历史记录；失联或过期时显示不可用。锁定组件不影响只读采样。外部更改显示器 HDR 后需刷新设备状态。

### 默认全局快捷键

| 快捷键 | 动作 |
| --- | --- |
| `Ctrl+Alt+Shift+Space` | 打开主面板 |
| `Ctrl+Alt+Shift+←` / `→` | 跨组切换总配置 |
| `Ctrl+Alt+Shift+↓` | 隐藏主面板 |

可在设置中整体关闭。快捷键被其他程序占用时会报告冲突。切换配置会应用保存的设备设置；普通打开、隐藏和锁定不会切换配置。

## English

### System Volume and Input

The audio page controls the current Windows default output and input levels and mute states. FC follows physical or virtual endpoints without selecting devices or changing routing. Configure Voicemeeter, virtual cables or NVIDIA Broadcast in Windows and the relevant software. FC does not provide per-application equalizers or processing chains, or a separate communications-device control.

Unavailable input stays unavailable, and updating a profile preserves previously saved values. Legacy device-specific audio entries are skipped with a prompt to update the profile to the system controls.

### Desktop panel and shortcuts

Enable the panel under **Settings → Desktop controls**, choose linked or individual controls, and adjust position, size and opacity (10%–85%). Extra rows scroll. Double-click to unlock and bring it to the foreground; losing focus or pressing Esc locks it at the desktop layer. Win+D is supported. Locked clicks do not write to devices.

Both desktop modes show affected display names and HDR limitations; refresh devices after changing HDR externally. The automatic-adjustment switch shares the main window's configured rule and targets, independently of desktop grouping. Unlock before toggling; manual brightness changes retain the existing ten-minute hold.

The selected light subscription shows lux and sample time at its configured polling interval, including with automatic brightness off. This does not enable history recording. Failed or stale readings show unavailable; locking does not stop read-only sampling.

Default shortcuts are `Ctrl+Alt+Shift+Space` to open the main panel, `Ctrl+Alt+Shift+Left/Right` to switch global profiles across groups, and `Ctrl+Alt+Shift+Down` to hide the panel. They can be disabled together; registration conflicts are reported. Profile switching applies saved settings, while opening or hiding the window does not.
