# FC 设备接入与信号规范 v1 / Device signals v1

## 本次实现范围

v0.3.140 设备接入版本（不属于旧 v0.3.135 安装包）：

- FC 运行时每 5 秒检查串口列表变化，只对 RP2040 VID 和 Espressif 原生 USB Serial/JTAG VID/PID 进行只读 PING 身份握手；其他 USB-UART 端口需要手选。新增设备提示不自动开启亮度控制。
- 设置 → 自动调节 → USB 设备设置与配网。USB 数据直连即管理权限，不要求绑定、密码或登录；危险操作的确认仅防误触。
- 配套 MicroPython 程序提供状态、扫描/更换/取消/忘记 Wi-Fi、关闭/开启无线、轮换绑定、重启和清除配置。RP2040 不支持无线，按能力禁用相应按钮。
- C3 连接新网络成功后才保存凭据；20 秒超时或取消时恢复旧网络。仅供电时连接已保存网络，无数据线不能走 USB 管理。
- 已通过 USB 保存绑定的 C3 可在同一局域网被主动发现并读取光照。请求和响应以 HMAC-SHA256 认证；Windows 使用当前用户 DPAPI 保存绑定密钥。密钥不进入链接、用户配置导出或历史。
- 同一设备切换 USB/局域网订阅复用原记录 ID，历史不因 COM/IP 改变而新建。当前需手动选择连接方式；IP 变化后再次发现。选中新来源默认只记录，自动调节关闭。
- 安装包注册 `fluentcontrol://subscribe?device=<id>`；新实例通过当前用户命名管道通知已有实例。链接仅定位订阅页面，不执行管理命令，不自动订阅。

**以下指 FC v0.3.140 桌面应用及其发布时两文件参考固件尚未实现：BLE 数据和配网、热点配网页、未安装 FC 时插入自动打开网页、USB/Wi-Fi 自动切换、OTA/FC 刷写、多信号的通用订阅界面。** 通用信号解析和规范已加入，目前产品消费仍只接光照。没有把纯供电首次配置或完整无线管理写成已完成。

## 固件安装与真实验证

本节两文件对应 FC 发布时参考实现；采用 FCS 后续 C3 功能时必须整套上传五文件，见下方兼容说明。不要混用两个来源的 main.py 和核心文件。

参考程序：[main.py](../tools/light-source/device-v1/main.py) 与 [fc_device.py](../tools/light-source/device-v1/fc_device.py)，两文件一起复制到 MicroPython 文件系统根目录。保留原程序备份；FC 暂不负责刷写。

RP2040 默认 SDA=GP0、SCL=GP1；ESP32-C3 默认 SDA=GPIO4、SCL=GPIO5。按实物确认引脚，BH1750 用 3.3V，四芯线为 VCC/GND/SDA/SCL，ADDR 在传感器端接地。C3 必须使用支持 USB 控制台、network、socket 的对应 MicroPython 固件，不能把 RP2040 UF2 拖入 C3。

C3 的 USB 端口接线及 MicroPython USB 控制台还未实机验证。当前运行了模拟后端状态机测试，不能等同于板卡配网、射频、掉电恢复或显示器联动验证。内部仍应保留烧录恢复触点；正常固件不需要外露 BOOT/RESET。

## 协议

保持旧 `PING <32位十六进制请求ID>` / `READ <ID>` / `HISTORY <ID>` 行协议兼容。
PING 增加 `management: "fc-device/1"`、`signals` 和 `operations`。旧程序没有 management 时，FC 不发送管理命令。

USB 管理：`FC <ID> {"operation":"wifi.configure","arguments":{"ssid":"example","password":"..."}}`。
响应包含相同 requestId/sourceId 和 ok。操作只执行一次；通信失败提示结果未确认，不自动重发重置或配置命令。USB 的权限来自实际传输入口，忽略载荷中的 transport 身份声明。重启先应答 accepted，至少 500ms 后再执行；应答不能证明重启完成。

状态包含 USB 可读取的绑定密钥，不包含 Wi-Fi 密码。配置仅变更时写闪存；历史最多 64 条 RAM，断电丢失。Windows 光照历史仍由现有 64 MiB 记录器管理。

通用读数示例：

```json
{"protocol":"fc-device/1","sourceId":"c3-bh1750-example","signal":"illuminance","unit":"lx","value":42.5,"valid":true,"sequence":12,"uptimeMs":8000}
```

信号为有限数值、单位与类型明确；标识稳定。未来温度可用 temperature/Cel，但不会被自动亮度误当光照。设备 uptimeMs 不是 UTC；FC 光照历史使用明确的接收时间，不伪造采样绝对时间。

局域网发现：向 UDP 42735 发送 `FC-DISCOVER/1`，返回 protocol/sourceId；只用数据包真实发送者地址与固定 TCP 8766、`/v1/sample` 路径，不执行广告中的 URL 或命令。防火墙、访客隔离和广播限制可阻止发现。

局域网读取：`GET /v1/sample`，`X-FC-Nonce` 为随机 32 位十六进制；`X-FC-Signature = HMAC(key, "READ\n" + sourceId + "\n" + nonce)`。
响应签名为 `HMAC(key, nonce + "\n" + 原始UTF-8响应正文)`。认证绑定本次请求，旧响应不能冒充新读数。采用 HTTP，不提供读数内容加密。网络端没有写接口，所有管理调用均被拒绝。该密钥当前表示单一共享读权限，USB 轮换会撤销所有旧客户端。

## English

Released in v0.3.140, this implementation adds USB-authoritative management, capability-gated controls, C3 reference firmware for USB Wi-Fi setup, rollback/cancel/forget operations, signed LAN light reads, user-scoped DPAPI binding storage, and installer-registered subscription links. Device discovery never enables automatic brightness. Existing RP2040 firmware remains readable but requires the two-file reference firmware for management.

USB requires no binding; wireless access does not inherit USB privilege. The LAN endpoint is read-only. Same-device USB/LAN subscriptions retain their history ID; transport selection and rediscovery after IP changes are manual. The generic numeric signal contract rejects non-light types in the illuminance consumer.

BLE, SoftAP provisioning, firmware flashing, automatic transport failover, generic multi-signal subscription UI, and first-install USB auto-launch are not implemented. Charge-only first setup still needs a USB data connection. State-machine and desktop automated checks do not constitute physical C3/RP2040, radio, or monitor validation. Download both firmware files and verify board pinout before use.

## 后续 FCS 五文件固件兼容说明（2026-10-20）

FC 正式发布提交为 `768fd670aab805833da8c28d9ee1a31bf3327e31`。FCS 从 `e87e2750dc04e1265fd28c85490f519a09720ed5` 增加临时热点和 BLE；这些是板上参考固件能力，**不是 FC v0.3.140 新增界面功能**。

- PING/READ、USB 管理、稳定 sourceId 与认证 LAN 的原请求/响应格式保留。新增 operations 不要求 FC 识别全部操作；FC 仍只开放本版十项管理指令。
- 最新 C3 上传 main.py、fc_device.py、fc_wireless.py、fc_ble.py、setup.html 五文件。保留 fc-config.json 中原网络、bindingKey、wireless 设置，不需要为普通升级恢复出厂。setup.json 是可选的每设备热点密码配置，已有文件不要重新生成覆盖；缺省会生成密码，可由 FCS 控制台 USB status 查看。RP2040 仍用两文件。
- 正式 FC 可做 USB 订阅、既有配网/取消/忘记、无线开关、重启、恢复出厂、保存绑定及认证 Wi-Fi 订阅。新增 ap.start/ap.stop、BLE 开关及 BLE 查询使用 FCS 调试台；首次热点网页配网用手机/电脑浏览器。
- 网页首次配网后，**FC 仍需一次 USB 读取状态保存绑定**，然后发现局域网设备；本版不能导入网页导出的绑定 JSON，也不能订阅 BLE。
- USB 不要求绑定且可接管网页配网；FCS 修正了网页覆盖/取消 USB 配网的冲突，USB 配网期间网页返回 usb_busy。恢复出厂仅实际 USB 入口接受；临时热点只有有限配置权限，不能借传输字段声明获得 USB 权限。
- 原 v0.3.140 Release 中固定到发布提交的两文件安装链接仍正确，适用当时的功能。不要用最新 FCS 安装清单倒推旧 Release 文件缺失。

完整安装、迁移、功能表和测试入口见 [FCS 兼容报告](https://github.com/chuanjianghe1024-cmd/FluentControl-Sensors/blob/main/docs/fc-v0.3.140-compatibility.md)（需要该仓库访问权限）。FCS 增加固定版本 C# 消费端回归：在 Windows 执行发布版原解析/HTTP 读取代码，传输及密钥存储为模拟；这不是新版固件的真实 COM、DPAPI、WinUI、手机或实体 C3 验收。既有 Windows build #140 的安装验收不扩大为无线硬件验收。本次不发布新的 FC 安装包。

English: Later FCS C3 firmware adds SoftAP/BLE while preserving the v0.3.140 USB and signed-LAN contracts. Upload its five matching files together; preserve fc-config.json and existing setup.json. The latter is optional per-device setup data, not a mandatory sixth code file. No factory reset is needed for ordinary upgrades. Original release-pinned two-file instructions remain valid for the original feature set. FC v0.3.140 still requires one USB status read to save a binding after AP onboarding, cannot import binding JSON, and has no BLE subscription or AP-control UI. Use the FCS console for added operations. Pending USB provisioning takes precedence over AP changes; reset is USB-only. Pinned C# consumer tests on Windows use simulated transport/key storage and do not validate physical radios, serial ports, DPAPI, WinUI or phones. No new FC binary is released.

## 开发版 Wi-Fi 配网向导（未发布） / Unreleased Wi-Fi wizard

设置提供独立的六步配网向导：选择设备、选择 2.4 GHz 网络、输入密码、等待联网、验证无线光照、完成。通过 USB 自动读取和保存本机绑定；只有已联网的有效 IP 和通过认证的真实光照样本均成功后，才能完成并选择 Wi-Fi 订阅。自动调光保持关闭。

配网期间暂停后台采样和端口发现，避免竞争同一串口。隐藏 SSID 可手输，无密码网络需明确勾选。等待阶段自动刷新；无线读取失败可单独重试，不重复提交密码。取消未完成配网会尽力发送 wifi.cancel，结果未确认时明确提示，不宣称取消成功。旧固件 20 秒失败后可能重连旧网络，因此向导在 18 秒内确认新连接，否则取消并要求重试，避免把旧网络当成本次成功。当前固件未报告具体认证错误，界面只提示检查网络、密码及信号，不断言密码错误。

The six-step wizard saves USB binding automatically, polls connection state and requires an authenticated valid light sample before Finish selects Wi-Fi. It keeps automatic brightness off, pauses competing background serial work, supports hidden/open networks and retries LAN verification separately. Pending setup cancellation is best-effort and reports uncertainty. An 18-second confirmation window avoids mistaking older firmware's 20-second fallback to a previous network for successful setup. Firmware does not expose a precise authentication-failure reason; messages do not claim that the password is wrong.
