# 自动亮度、时间主题与光照数据订阅

发布基线：[v0.3.135 Release](https://github.com/chuanjianghe1024-cmd/FluentControl/releases/tag/v0.3.135)，北京时间 2026-10-19 发布。本文描述该正式安装版的功能；验证来源为 [Windows build #135](https://github.com/chuanjianghe1024-cmd/FluentControl/actions/runs/39144637273)，源码提交 `d7221cdfbca31a6a584da58aef250cb24d649021`。

设置 → 自动调节预设。默认关闭；选中一台目标屏幕后显式启用，会实际调节其亮度。启用状态在本机保存，FC 下次运行时继续规则。关闭后保留当前屏幕亮度，FC 主题恢复手动偏好。当前只有一条活动规则/一个目标屏幕，不是每屏同时独立运行。

## 三种调节依据

- **光照强度**：订阅兼容 RP2040 的本地 USB 来源，或不断返回最新照度的 HTTP/HTTPS 链接，使用本地 lux→亮度曲线计算目标。RP2040 可由 FC 直接通过串口读取，无需 Python 桥接；网络设备或第三方桥接服务可实现下面的 HTTP JSON 协议。这不代表已经提供 C3 Wi-Fi 固件。
- **指定时间**：独立的 24 小时亮度折线，以所选时区的当前时间插值；可以指定 FC 浅色、深色主题开始时间。小数小时如 7.5 表示 07:30，支持跨午夜主题区间。0/24 时亮度相等，避免午夜跳变。
- **日出日落**：选择地区快捷填入，或输入经纬度与时区；本地估算每天日出日落，分别设置日间、夜间亮度，同步 FC 浅色/深色主题。考虑闰年、时区规则/夏令时、极昼与极夜。地形遮挡、天气和高纬折射不建模，时间是近似值。

两条曲线分别保存，切换不会重置。图表支持点击添加、拖动节点、数值编辑与 JSON 导入导出；导出文件只是曲线，不是光照数据来源。图表模拟输入只预览，不写屏幕；但选择“内置模拟光照”作为活动数据来源并打开自动调节，会实际写屏幕。

**主题仅控制 FC 自身，不改变 Windows 系统/其他应用主题，也不调整 Gamma 或色温。** 手动在“常规”选择主题会关闭自动主题切换。原生 RP2040 USB 串口订阅已发布；C3/CH552 固件、HID/BLE 传输、FC 内刷写、硬件历史回灌及用户偏好学习尚未实现。

## 统一光照数据格式 v1

链接每次 GET 返回最新一帧（JSON 原始内容，不是 HTML 页面）：

```json
{
  "format": "fluentcontrol.light-sample",
  "version": 1,
  "sourceId": "my-light-sensor-01",
  "timestamp": "2026-10-14T06:00:00Z",
  "lux": 123.4,
  "valid": true,
  "simulated": false
}
```

- 真实设备和模拟源使用同一结构；示例时间不可直接长期使用，服务需要实时更新。
- `sourceId`：非空稳定 ID，最长 100 字符；不需要显示器型号或 FC 私有代码。
- `timestamp`：带 UTC/偏移的采样时间；客户端检查过期、乱序和未来时间。保持设备时钟同步。重复时间戳不增加滤波样本数。
- `lux`：有限数值 0–200000；曲线目前覆盖 0–10000 lux，超出曲线范围按端点限制。缺少 lux、null、负值不当作黑暗。
- `valid`：测量是否成功；false 时 FC 暂停本轮，不下发亮度。
- `simulated`：模拟标记，界面显示来源与标记；它不改变数据处理协议。
- 本地 USB 使用下文的 READ/PING 串口协议；FC 收到新 READ 响应后使用主机时间标记样本，不要求 RP2040 提供 UTC 时钟。
- 不包含命令、脚本、DLL、私有 VCP 指令或自动执行动作。字段白名单、版本校验、16 KiB 上限；不跟随重定向、不使用 Cookie 或嵌入用户名密码。HTTP 用于用户明确添加的本机/LAN 接口；公网建议 HTTPS。

添加 HTTP 来源时先校验并保存名称与 URL，不做即时联网探测；随后默认开启“保存光照历史”，在光照模式下轮询当前选中的来源，自动亮度保持关闭。USB 添加流程会执行身份握手，详见下文。一次只采集一个活动来源，不会扫描局域网或同时轮询全部订阅。新增/删除来源会关闭自动调节，避免无意切换输出；若不希望继续采集，同时关闭历史记录和自动调节。目标屏幕和经纬度不发送给来源；GET 请求仅访问用户提供的地址。

## 采样与屏幕写入策略

- 默认订阅间隔 **1000ms（1Hz）**，允许 200–86400000ms（0.2 秒至 24 小时），USB 和 HTTP 来源均支持滑块、数字输入及预设；本机/局域网优先 1s，公网低频来源可用 5–30s，取决于服务频率与配额。
- BH1750 高分辨率测量典型 120ms、最长 180ms（默认测量时间设置），1Hz 是本项目的初始工程选择，不是传感器强制频率。当前 RP2040 固件每次 READ 后等待 200ms 完成一次转换；来源采样、FC 请求和 DDC 写入是不同频率，最快档不保证每秒五个新样本。
- 三个新样本取中值，随后 EMA 系数 0.25 平滑；短时单点遮挡/尖峰不会直接决定输出。
- HTTP 样本过期阈值为 max(5s, 3×订阅间隔)，未来超过 5s 拒绝；HTTP 单次超时 3s、本地 USB 读取总取消时限 25s（单次串口交换等待上限约 2.5s），不重叠请求；异常数据/连接失败暂停输出，故障重试退避约 10s（手动取消除外）。不把失联转换成 0 lux。
- 每屏自动操作最多每 2s 一次；与目标差异不足 2 个百分点不写；单次最多推进 5 个百分点，实际值以驱动支持档位和回读为准。
- 手动亮度操作触发 **10 分钟保持期**，自动输出不会触发自己的保持期；检测到外部读值偏离上次回读也进入保持。手动关闭再开启可以解除保持。
- 写入失败暂停自动执行，需重新启用，不持续重写失败命令。HDR 开启/未知或显示器离线时暂停亮度；按时主题不依赖显示器亮度是否可写。
- 使用既有 gate/generation 通道；刷新、关闭、换来源、改规则使旧请求失效；睡眠/恢复或时钟跳变丢弃旧光照样本，不追放睡眠期间计划。

## 无硬件测试

安装 Python 3 后在仓库根目录运行：

```powershell
python tools/light-source/server.py
```

在 FC 添加 `http://127.0.0.1:8765/light.json`。服务器只绑定本机回环地址，默认每分钟循环从暗到亮再变暗。编辑 `tools/light-source/pattern.json` 的 seconds/lux 节点后重启服务器；也可指定 `--pattern 路径 --port 8765`。这只是 HTTP 模拟来源。RP2040 硬件使用下文已发布的原生 USB 接入；其他网络设备或自建桥接服务可继续返回相同 HTTP JSON 结构。

## 参考与验证

- [ROHM BH1750 数据手册](https://www.mouser.com/datasheet/2/348/bh1750fvi-e-186247.pdf)，测量时间见 Electrical Characteristics。
- [NOAA 通用太阳位置公式](https://www.gml.noaa.gov/grad/solcalc/solareqns.PDF)，按公开公式独立实现日出日落估算，不调用网站运行时服务。
- 纯逻辑回归覆盖格式、时间新鲜度、尖峰/重复采样、对数插值、跨午夜、极昼极夜、输出步幅、手动/自动通知隔离。现有显示器/音频/配置回归通过。2026-10-14：Linux .NET 8 纯逻辑回归、C# 12 语法解析及本机模拟 HTTP 端点实测通过。正式版对应的 [Windows build #135](https://github.com/chuanjianghe1024-cmd/FluentControl/actions/runs/39144637273) 已通过完整 Windows 编译、控制逻辑、WinUI 模拟界面/调度、MSI 与 Setup 安装验收；覆盖本地光照协议与配置、曲线编辑持久化、午夜衔接、百分比步幅、写入间隔、手动保持和关闭门控。实体 RP2040 与 FC 完整联调、真实显示器、混合 DPI 及休眠恢复实测仍未确认；自动化通过不等于实机通过。

# Automatic brightness and light-data subscriptions

Release baseline: [v0.3.135 Release](https://github.com/chuanjianghe1024-cmd/FluentControl/releases/tag/v0.3.135), published on 2026-10-19 in Asia/Shanghai. The installers originate from successful [Windows build #135](https://github.com/chuanjianghe1024-cmd/FluentControl/actions/runs/39144637273), source `d7221cdfbca31a6a584da58aef250cb24d649021`.

Three independent modes: illuminance from a compatible RP2040 USB subscription or a live HTTP(S) JSON feed, a fixed 24-hour brightness curve, or locally estimated sunrise/sunset using coordinates and a time zone. The two curves retain separate edits. Optional light/dark switching affects FC only, not Windows. The default is off; explicitly enabling applies real brightness changes to one chosen display and persists across app restarts.

Real and simulated HTTP providers return the same `fluentcontrol.light-sample` document. Use `tools/light-source/server.py` with the configurable pattern for a local test endpoint. Default polling is 1Hz, configurable from 0.2 seconds to 24 hours for USB and HTTP sources; a three-sample median and EMA smooth readings, writes are limited to one per 2 seconds with a 2-point deadband and 5-point step limit. Manual changes hold automation for 10 minutes. Invalid/stale data, unavailable displays and HDR pause brightness; write failures require re-enabling. Native RP2040 serial subscriptions are released and need no running Python bridge. Network devices may implement the HTTP schema, but C3/CH552 firmware, HID/BLE transport, flashing from FC, hardware-history backfill and preference learning are not implemented. Adding a source enables history collection for the selected source in illuminance mode while leaving brightness automation off. Only one active source is polled; turn off both recording and automation to stop collection. The HTTP timestamp/freshness rules apply to HTTP feeds; local USB samples receive a host timestamp after a fresh READ response.


## 本地 USB 订阅（v0.3.135 已发布） / Local USB subscription (released in v0.3.135)

设置 → 自动调节预设 → 光照强度：端口保留 `auto`，点击“发现并订阅本地传感器”。FC 仅对 Raspberry Pi USB VID `2E8A` 的已连接串口进行身份握手，最多 8 个候选；其他设备需手动指定 `COM5` 等端口，且必须运行兼容固件；指定端口本身不会使 C3/CH552 获得支持。多块设备分别加入订阅，选择要使用的一块。发现过程只握手读取设备身份；添加后默认开始记录当前来源，自动调节保持关闭，确认目标屏幕和曲线后再显式开启。

In Settings → Automatic adjustment → Illuminance, leave the local port as `auto` and select “Discover and subscribe to local sensors”. Only connected Raspberry Pi USB VID `2E8A` ports are automatically probed (up to eight); other boards require an explicit COM port and compatible firmware. Selecting a port alone does not add C3/CH552 support. Each compatible sensor becomes a separate subscription. Discovery only handshakes for identity; adding the source then enables recording for the active source while keeping automatic adjustment off until explicitly enabled.

本地来源使用 `fc-usb://auto` 或 `fc-usb://com5`，同时保存固件 `sourceId`。自动模式 COM 编号变化后重新枚举，必须匹配原设备身份；不会悄悄换成另一块探头。手动端口模式需在端口改变后重新发现。读数失败不写屏幕，10 秒后重试。长间隔期间不持续测量或监测拔出，下一次读取时发现断线；等待期间没有新亮度写入。

Local sources store a `fc-usb://auto` or explicit COM address and the firmware's stable `sourceId`. Automatic mode re-enumerates ports and requires the same identity. Explicit-port sources must be rediscovered if the port changes. Read failures suspend display writes and retry after ten seconds. Long intervals do not continuously monitor unplug events; loss is detected on the next read, with no new brightness writes between measurements.

采样间隔同时支持滑块、数字输入与最快/1 秒/5 秒/10 秒/1 分钟/10 分钟预设，范围 0.2 秒至 24 小时，配置随 FC 保存。最快档是请求间隔下限；转换、USB、Windows 调度开销会降低实际频率。默认 1 秒；滤波需要 3 个新样本，10 分钟间隔首次输出约需 20 分钟。HTTP 订阅沿用此间隔，旧样本有效期为至少 5 秒或 3 倍间隔；不会刷新旧样本时间。

Sampling supports a slider, numeric entry and fast/1s/5s/10s/1min/10min presets, persisted by FC. Range: 0.2 seconds to 24 hours. Fast mode specifies a minimum request interval, not guaranteed throughput. The default is one second. Three distinct samples are needed for smoothing, so ten-minute sampling takes about twenty minutes to warm up. HTTP freshness remains at least five seconds or three polling intervals, without rewriting old sample timestamps.

当前原型固件：[RP2040 main.py](../tools/light-source/rp2040/main.py)。先用 Thonny 保存到 RP2040 根目录，再退出 Thonny 并正常重插 USB；不要同时运行旧桥接程序。FC 使用 `System.IO.Ports`，不需要用户安装 Python。READ/PING 协议沿用验证包，115200/8N1，每次请求包含 32 个十六进制字符的随机 ID，响应必须匹配。PING 返回 `protocol: fc-light-usb/1` 和 `sourceId`；READ 返回 `sourceId`、`valid`、`lux`。畸形、超时、错误身份与无效 lux 均不作为有效光照。

Prototype firmware is in [RP2040 main.py](../tools/light-source/rp2040/main.py). Save it to the RP2040 with Thonny, close Thonny and reconnect normally. Do not run the standalone bridge concurrently. FC uses System.IO.Ports; Python is not required at runtime. The bounded READ/PING serial protocol uses a random 32-hex-character request ID, verified firmware identity and validated readings. A future C3 Wi-Fi implementation could provide the existing HTTP schema; C3/CH552 firmware and HID/BLE transport are not implemented here.

验证边界：v0.3.135 已包含本地 USB 订阅、采样设置和本地历史。[Windows build #135](https://github.com/chuanjianghe1024-cmd/FluentControl/actions/runs/39144637273) 的 Windows 编译、协议/配置回归、WinUI 界面及 MSI/Setup 安装验收已通过；实体 RP2040 与 FC 完整联调、真实显示器及混合 DPI 验收仍未确认。

Validation boundary: v0.3.135 includes local USB subscriptions, sampling controls and local history. [Windows build #135](https://github.com/chuanjianghe1024-cmd/FluentControl/actions/runs/39144637273) passed Windows compilation, protocol/configuration regression, WinUI UI checks and MSI/Setup installation validation. End-to-end physical RP2040 integration, real displays and mixed-DPI behavior remain unverified.

## 光照历史 / Light history

添加本地或 HTTP 来源后默认开启“保存光照历史”，自动亮度仍保持关闭。可以只记录而不改屏幕；取消历史开关后，只有启用自动调节才继续读取。FC 关闭或电脑休眠期间不补造记录。

本机目录 `%LOCALAPPDATA%\FluentControl\LightHistory\` 保存原始 lux、采样和接收时间、来源与模拟标记。重复时间戳不重复写入；读取失败保存缺口，连续失败合并。所有来源合计最多 64 MiB，按约 1 MiB 分段，满后删除最早分段。“查看光照历史”可按订阅分页浏览，每页 100 条。删除订阅不自动清除其已有日志。

History recording is enabled when adding a source, independently of brightness adjustment. Logs stay under the user's local FluentControl/LightHistory directory, with raw readings, timestamps and gap events. Retention is capped at 64 MiB across all subscriptions, rotating oldest segments. Each subscription has a paginated history view. Closing FC or suspending the computer does not invent missing readings; deleting a subscription does not erase its retained files.

固件、组合目录和官方资料入口：[FluentControl-Sensors](https://github.com/chuanjianghe1024-cmd/FluentControl-Sensors)。目前为私有仓库，仅授权账户可访问。RP2040 固件另有 64 条 RAM 诊断缓存，断电丢失，HISTORY 使用设备 ticks_ms，当前 FC 不自动回灌。

Firmware and official setup resources are in the private [FluentControl-Sensors](https://github.com/chuanjianghe1024-cmd/FluentControl-Sensors) repository. The public RP2040 prototype is also linked above. Its 64-entry RAM diagnostic buffer is lost on power-off; HISTORY uses device ticks_ms, and FC does not backfill it into local history.
