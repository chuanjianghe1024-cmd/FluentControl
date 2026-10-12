# USB/HID 显示器适配研究

状态：参考与后续实机计划。本次未启用 USB 扫描、厂商指令发送或后台配对；当前配对执行传输只接受 `ddcci`。USB 控制与视频接口分开建模，不能从 HDMI / DP 的接口数量推断支持情况。

## 两条控制路线

| 路线 | 连接与用途 | 适配依据 |
| --- | --- | --- |
| DDC/CI | 视频线上的控制通道；HDMI / DVI 使用 DDC，DP 通过 AUX 承载 I²C 通信 | MCCS 能力、型号、固件、实际响应；转接器、KVM、驱动、HDR 与固件均可能影响 |
| USB Monitor / HID / 厂商 USB | 显示器的 USB 上行连接，可与 DP 视频线同时连接；USB-C 是否同时提供视频与数据取决于设备与线缆 | USB 接口与报告描述符、标准 Monitor Control usages，或明确型号对应的厂商协议 |

HID 包含报告描述符以及 Input / Output / Feature 报告。Feature 报告通常用于配置读写。HID 是通信框架，发现一个 HID 设备不等于已经知道其菜单按钮协议。已有 USB Monitor Control 标准，但并非所有显示器实现；厂商方案还可能使用私有 HID 报告，或不属于 HID 的 USB 控制传输。只有 Hub、音频或键鼠透传接口不构成显示器控制支持的证据。

## 后续实现顺序

1. **先收集设备身份，保持只读。** 记录插拔显示器上行线前后的 USB 设备差异：VID/PID、bcdDevice、接口号/类、Usage Page / Usage、报告 ID、报告长度、可公开的固件与型号。当前连接路径/序列号仅用于本机归属，公开导出不包含它们。枚举身份不发送厂商 Get/Set Feature 指令。
2. **将 USB 控制设备与视频显示器配对。** 不按相同品牌或名字自动绑定；同型号多屏需要用户逐台确认，插拔和 KVM 切换后重新校验。USB Hub 的存在不自动授予任何写入能力。
3. **选择协议适配器。** 标准 USB Monitor、具体厂商 HID、厂商 USB control-transfer 各自独立。匹配型号、固件、接口和报告结构；视频连接只作为环境信息。不要把 DDC VCP 代码直接填进 HID 字节流。
4. **从已有证据的单项操作验证。** 明确报告类型、ID、长度、编码、请求/响应、时序、校验、读回含义与适用型号。每次由用户点击；开启菜单、菜单导航、直接修改参数分别确认。不从 MSI / Apple 指令推断 HKC 指令。
5. **接入现有观察与绑定层。** 配对 UI / 试验记录与传输解耦，但扩充 transport schema 后才开放 USB 执行。保留每次请求与实际效果，菜单动作不得自动重发；不支持完整导航时只开放已验证功能。

当前 v3 配对会话增加用户手动选择的 `videoConnection`（unknown / HDMI / DP / USB-C / DVI / VGA），并保留连接说明、HDR 和环境备注。它不是驱动自动检测结果。未来 USB 身份和报告结构应使用独立诊断块，不能塞入现有 VCP 字段。

## HDMI / DP 对照验证

对同一台屏幕分别直连 HDMI 和 DP，固定固件、HDR、场景模式、DDC/CI 开关，记录显卡驱动、分辨率/刷新率、线缆和中间设备；导出两次诊断。先比较能力声明和只读结果，再由用户单次验证同一条已知操作。两台不同型号之间直接对比不能隔离接口变量。若同一型号/固件仅在某连接方式有效，适配结果需记录该限制，不能推广为“DP 不支持 OSD”。

## 一手参考

- [Microsoft HID 概览](https://learn.microsoft.com/windows-hardware/drivers/hid/) 与 [HID API](https://learn.microsoft.com/windows-hardware/drivers/hid/hid-api)：报告描述符、Feature/Output 数据的读写结构。
- [ddcutil: USB Connected Monitors](https://www.ddcutil.com/usb/)：遵循 USB Monitor 标准的控制路线。
- [ddcutil: DDC and DisplayPort](https://www.ddcutil.com/displayport/)：DP AUX 与 HDMI/DVI 转接中的 I²C 通信。
- [msigd](https://github.com/couriersud/msigd)：MSI 游戏显示器 USB/HID、按型号和固件划分的支持情况。仅研究协议与验证方式，尚未引入其代码；未来复用代码需单独核对许可证及项目兼容性。
- [Twinkle Tray / Monitors.js](https://github.com/xanderfrangos/twinkle-tray/blob/fec2ef0e7a3ad94092f230faa4d6d7b87a0cf188/src/Monitors.js)：Apple Studio Display 的专用 USB 亮度通道，它不是所有显示器的通用协议，也不等同于原厂菜单导航。
