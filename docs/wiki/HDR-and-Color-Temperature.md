# HDR 与系统色温 · HDR and color temperature

[返回目录 / Home](Home.md)

## 中文

### Windows HDR

单屏和整体视图均提供每屏独立的 **Windows HDR** 开关。它调整 Windows 的显示输出模式，与 DDC/CI 分离；Windows 报告支持时，笔记本内屏也可使用。

点击开关可能短暂黑屏。FC 发送一次请求、核对状态，随后重新读取显示器控制项，不自动重放旧亮度、对比度或场景。通过 FC 开启 HDR 前，会先尝试恢复 FC 当前使用的软件色温曲线。

HDR 开启时，主界面及 FC 屏幕菜单显示常驻提示：显示器可能锁定亮度、对比度、色温等调节。若调整无效，可以关闭 HDR 后重新读取。提示不一律禁用仍然有效的硬件项目。

如果从 Windows 或其他软件切换 HDR，请在 FC 中重新读取设备；当前没有后台 HDR 轮询。状态不明、策略限制与不支持会分别提示。HDR 不保存在总配置或启动动作里，关闭 HDR 也不能保证所有固件马上解除限制。

### 系统色温（软件）

1. 在对应屏幕下展开 **系统色温（软件）**。
2. 选择 2500–10000 K 的近似目标，再点击“应用”；仅移动滑条不会写入。
3. 点击“恢复原颜色”回到本次接管前捕获的曲线。**6500 K 保留原曲线，不代表测量或校准到了 6500 K。**
4. 已确认的目标可保存到本机总配置；硬件色温预设不会被改写。

| 场景 | 行为 |
| --- | --- |
| 已确认的 SDR 独立输出且接口可用 | 可尝试软件色温 |
| HDR、自动颜色管理、复制屏幕共用输出或状态未知 | 不写该兼容曲线，提供 Windows 夜间模式入口 |
| 隐藏到托盘 | 保留当前效果 |
| 正常退出 | 尝试恢复仍属于 FC 的颜色曲线 |
| 崩溃或强制结束后重启 | 提供恢复记录，由用户明确恢复；不自动重放旧色温 |
| 其他软件或显示模式改变曲线 | 停止接管，保留新的设置 |

请避免同时使用夜间模式、f.lux 或校色加载器。软件色温只是冷暖效果，可能影响亮度和色阶；不能代替专业校色，读回成功也不能证明所有游戏渲染路径都会呈现相同效果。

## English

### Windows HDR

Individual and overall views provide a separate Windows HDR switch for each screen. It controls Windows output independently of DDC/CI, including internal panels when Windows reports support.

Switching may briefly blank the screen. FC sends once, checks state and rediscovers monitor controls without replaying old values. Before enabling HDR through FC, it attempts to restore its active software color curve. A persistent per-display notice explains that HDR may restrict brightness, contrast, temperature or other controls; supported hardware controls remain available.

Refresh in FC after changing HDR through Windows or another program. HDR has no background polling and is not saved in profiles or startup actions. Unsupported, restricted and unknown states are distinguished. Disabling HDR does not guarantee immediate firmware recovery.

### Software temperature

Expand **System temperature (software)**, choose approximately 2500–10000 K and click Apply. Moving the slider alone makes no change. Restore original colors returns to the captured baseline; **6500 K preserves that baseline rather than calibrating a measured white point**. Confirmed targets can be stored in local global profiles, separately from hardware presets.

Only confirmed independent SDR outputs with a usable driver interface can be adjusted. HDR, auto color management, duplicated outputs and unknown states block this compatibility path and offer a Windows night light entry point.

The tray retains the effect. Normal exit attempts to restore FC-owned curves. A crash or forced termination may prevent restoration; the next launch offers explicit recovery without automatically applying old settings. External curve changes release control instead of triggering repeated writes.

Avoid competing night-light or calibration tools. This is an approximate warmth effect, not professional calibration; brightness and tonal levels may change. Driver readback cannot prove identical effects across all rendering paths. [Implementation scope](https://github.com/chuanjianghe1024-cmd/FluentControl/blob/main/docs/system-color-temperature.md).

## 后续修复 / Development fixes

v0.3.92 审查后的开发分支补充连续失败恢复、写前曲线复核与跨刷新总配置失效保护；不属于旧版安装包。仍需双屏和笔记本实机验收。

Post-audit development changes add interrupted-recovery ownership preservation, a pre-write curve check and stale-profile cancellation across refresh. They are not part of the v0.3.92 installer and still require physical display validation.
