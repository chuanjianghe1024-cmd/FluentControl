# Windows HDR 与笔记本内屏

开发版增加每屏 **Windows HDR** 开关，单屏与整体控制视图都保留独立目标。它改变 Windows 输出模式，不向显示器发送所谓“通用 HDR VCP”，也不等同于切换原厂 OSD 中的 HDR 模拟、画面预设或色调映射。

## 使用与边界

- 读取设备时只查询，不改变 HDR。Windows 报告支持且当前策略允许时才能切换；无 DDC/CI 的内屏也独立查询 HDR 能力。
- 点击开关后画面可能短暂变黑；发送一次，等待并核对状态，随后重建 DDC 连接，重新检测控制项。不会重放旧亮度、对比度或场景参数，不将 HDR 加入总配置、预设或启动动作。
- HDR 开启时提示部分屏幕可能限制亮度、对比度和色温，但不一律禁用这些控制。关闭 HDR 也不能保证固件立即解除所有限制。
- 新版 Windows 区分“用户启用 HDR”和“当前 HDR 信号”。两者不一致或读取失败时明确提示，不能把 API 接受写入当成切换成功。
- 通过 Windows 设置或其他程序切换后，点击 FC 的读取设备按钮同步；当前没有后台 HDR 轮询或显示拓扑监听。
- 单屏查询失败、身份无法确认、远程会话、驱动不支持等显示不可确认状态，不能当作硬件不支持。查询成功且支持标志为否才显示不支持。

## 实现约束

`Services/DisplayHdr.cs` 独立于 DDC。`QueryDisplayConfig(QDC_ONLY_ACTIVE_PATHS)` 与 `GET_TARGET_NAME` 提供精确显示器接口路径，匹配 `EnumDisplayDevices(EDD_GET_DEVICE_INTERFACE_NAME)` 返回的身份。不用型号、名称或屏幕次序猜测；目标重复时禁止写入。写前重新枚举，避免热插拔后复用旧 adapter LUID / target ID。

Windows 11 24H2（build 26100）起使用 `GET_ADVANCED_COLOR_INFO_2`（15）和 `SET_HDR_STATE`（16）。读取支持位、用户启用位和 `activeColorMode`；WCG/自动颜色管理不能被当成 HDR。新版查询失败不降级到含义模糊的旧接口。旧系统使用 `GET_ADVANCED_COLOR_INFO`（9）和 `SET_ADVANCED_COLOR_STATE`（10），遇到 `wideColorEnforced` 保持未知、不写入。策略限制时允许关闭已启用 HDR，不主动绕过限制开启。

同一 UI 操作在主窗口 `gate` 下执行，切换前递增 `generation`，使旧滑条、选择项、桌面回调及高级扫描失效。写入最多一次，12 次间隔 250 ms 的只读核对；未确认不自动重写。切换后释放锁，再执行完整刷新重建辅助进程。新界面绑定发生前不保存旧通道的 HDR 状态，HDR 不进能力缓存。

## 笔记本控制方式

内屏亮度通常由 Windows Monitor.sys、显示驱动与 BIOS/ACPI 协同提供。应用层可查询 `WmiMonitorBrightness`，经 `WmiMonitorBrightnessMethods.WmiSetBrightness` 设置支持的亮度级别；仍需按实例识别目标、回读及处理混合内外屏。

OEM 软件还可能调用自家驱动、固件或 SDK。色彩模式也可能属于 GPU/系统颜色处理，不能据此推断有可通用调用的原厂 OSD 导航接口。FC 本次只实现 Windows HDR；WMI 内屏亮度、厂商显示模式/护眼模式/EC 协议尚未接入。

## 验证与实机步骤

逻辑回归覆盖 SDK 结构大小、WCG/HDR 区分、策略限制、目标重定位、同屏重复身份、独立双屏、写失败、读取失败、回读不一致和断开。WinUI smoke 使用模拟 HDR 后端，验证两个视图、开关及刷新、旧回调失效；不会更改 CI 主机的 HDR。

实体验证待用户本机完成：

1. 记录 Windows build、GPU/驱动、内屏或外屏、连接方式；先检查 Windows 设置报告的 HDR 能力。
2. 双屏中只切换一屏，对照 Windows 设置和实体屏信号信息；另一屏保持原状。同型号屏幕也分别验证。
3. 关闭 HDR 后观察亮度/对比度能否读写；开启后验证固件限制，记录具体失败，不能假定所有屏幕相同。
4. 在 Windows 外部切换并重新读取；测试插拔、扩展/复制模式和笔记本电池策略；无法确认时应禁用开关或显示未确认，不控制其他目标。

## 原始参考

- [Microsoft Windows SDK wingdi.h](https://github.com/microsoft/win32metadata/blob/main/generation/WinSDK/RecompiledIdlHeaders/um/wingdi.h)：结构、位域与设备请求枚举；C# 互操作独立实现。
- [DISPLAYCONFIG_DEVICE_INFO_TYPE](https://learn.microsoft.com/en-us/windows/win32/api/wingdi/ne-wingdi-displayconfig_device_info_type)
- [HDR settings in Windows](https://support.microsoft.com/en-us/windows/hardware/display-graphics/hdr-settings-in-windows)
- [Supporting brightness controls on integrated display panels](https://learn.microsoft.com/en-us/windows-hardware/drivers/display/supporting-brightness-controls-on-integrated-display-panels)
- [WmiMonitorBrightnessMethods](https://learn.microsoft.com/en-us/windows/win32/wmicoreprov/wmimonitorbrightnessmethods)
