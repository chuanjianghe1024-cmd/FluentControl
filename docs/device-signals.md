# FC 设备接入与信号规范 v1 / Device signals v1

## 本次实现范围

开发中的设备接入版本（发布前不是 v0.3.135 的能力）：

- FC 运行时每 5 秒检查串口列表变化，只对 RP2040 VID 和 Espressif 原生 USB Serial/JTAG VID/PID 进行只读 PING 身份握手；其他 USB-UART 端口需要手选。新增设备提示不自动开启亮度控制。
- 设置 → 自动调节 → USB 设备设置与配网。USB 数据直连即管理权限，不要求绑定、密码或登录；危险操作的确认仅防误触。
- 配套 MicroPython 程序提供状态、扫描/更换/取消/忘记 Wi-Fi、关闭/开启无线、轮换绑定、重启和清除配置。RP2040 不支持无线，按能力禁用相应按钮。
- C3 连接新网络成功后才保存凭据；20 秒超时或取消时恢复旧网络。仅供电时连接已保存网络，无数据线不能走 USB 管理。
- 已通过 USB 保存绑定的 C3 可在同一局域网被主动发现并读取光照。请求和响应以 HMAC-SHA256 认证；Windows 使用当前用户 DPAPI 保存绑定密钥。密钥不进入链接、用户配置导出或历史。
- 同一设备切换 USB/局域网订阅复用原记录 ID，历史不因 COM/IP 改变而新建。当前需手动选择连接方式；IP 变化后再次发现。选中新来源默认只记录，自动调节关闭。
- 安装包注册 `fluentcontrol://subscribe?device=<id>`；新实例通过当前用户命名管道通知已有实例。链接仅定位订阅页面，不执行管理命令，不自动订阅。

**尚未实现：BLE 数据和配网、热点配网页、未安装 FC 时插入自动打开网页、USB/Wi-Fi 自动切换、OTA/FC 刷写、多信号的通用订阅界面。** 通用信号解析和规范已加入，目前产品消费仍只接光照。没有把纯供电首次配置或完整无线管理写成已完成。

## 固件安装与真实验证

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

This implementation adds USB-authoritative management, capability-gated controls, C3 reference firmware for USB Wi-Fi setup, rollback/cancel/forget operations, signed LAN light reads, user-scoped DPAPI binding storage, and installer-registered subscription links. Device discovery never enables automatic brightness. Existing RP2040 firmware remains readable but requires the two-file reference firmware for management.

USB requires no binding; wireless access does not inherit USB privilege. The LAN endpoint is read-only. Same-device USB/LAN subscriptions retain their history ID; transport selection and rediscovery after IP changes are manual. The generic numeric signal contract rejects non-light types in the illuminance consumer.

BLE, SoftAP provisioning, firmware flashing, automatic transport failover, generic multi-signal subscription UI, and first-install USB auto-launch are not implemented. Charge-only first setup still needs a USB data connection. State-machine and desktop automated checks do not constitute physical C3/RP2040, radio, or monitor validation. Download both firmware files and verify board pinout before use.
