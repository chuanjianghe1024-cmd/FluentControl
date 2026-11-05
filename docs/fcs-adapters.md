# Gateway 有线与 ROS 2 适配器 / Wired and ROS 2 adapters

开发预览，尚未进入正式 FC/FCSD 发行包。入口仍是 **FCS Gateway → 设备 → 传感器 → 信号**；FC 只勾选可订阅设备。现有自动亮度只消费 `illuminance / number / lx`，不把电压等任意数值当成光照。C3 固件、USB 恢复与 `fcs-node/2` 物理硬件声明不变。

Development preview, not a production FC/FCSD release. Gateway owns adapters and the device/sensor/signal catalog. FC consumes subscriptions; automatic brightness requires numeric illuminance in lux. C3 firmware, recovery and manufacturing declarations are unchanged.

## 本版能力 / Implemented scope

| 路径 / Path | 实现与边界 / Support and limits |
|---|---|
| CAN → Gateway | Linux SocketCAN，经典 CAN 与可选 CAN FD；11/29 位 ID；整字节整数、大小端、有符号、倍率和偏移。物理接口必须预设 listen-only，启动及运行中检查。/ Byte-aligned scalar mappings, read-only, hardware listen-only required. |
| ROS 2 → Gateway | 指定话题和字段；Float64、Int64、Bool、String、Illuminance、Temperature、BatteryState；显式可靠性，volatile、depth=1。/ Explicit scalar topic subscriptions and QoS. |
| Gateway → ROS 2 | 选定的本机 FCS 物理节点信号，发布到 `/fcs/telemetry/*`；`std_msgs/msg/String` 承载 `fcs-telemetry/1` JSON，包含单位、质量和接收时间，失败发布 null/invalid。/ Selected same-host signals as quality-bearing JSON telemetry. |
| Windows / Linux 管理 | 共用虚拟设备清单，显示适配器类型；只能查询，无配网、设备插件安装或刷写权限。/ Shared catalog and read-only virtual device UI. |
| OPC UA | 可把这些外部设备的信号加入已有 UA `exports`。原有一级 UA 挂载仍在 UA 目录，尚未回流到 FC 清单。/ Existing UA explicit exports can expose adapter signals; mounted UA resources do not yet appear in FC. |

外部设备 ID 保留 `ext-适配器ID-设备名`，`physical=false`，扩展放 `extensions["fcs.adapter"]`。可查询 `node.info / sensors.list / operations.list / data.read`。此处 `mode=fresh` 指有效期内的新近接收值，不触发总线主动询问；`latest` 返回缓存标记。信号过期值为 null，不回放历史充当实时值。

一个虚拟设备只能属于一个启用的适配器；不同适配器即使声明不同信号，也不能共同占用相同 deviceId。授权目录冲突会在启动时拒绝，不按配置顺序覆盖归属；接收数据还逐信号校验生产者授权。/ Each virtual device has exactly one enabled adapter owner. Conflicting grants fail at startup regardless of ordering; publishing also checks exact signal membership.

本版没有 CAN 发送、UDS/ISO-TP 诊断请求、CANopen/J1939 栈、DBC 自动导入、非整字节/多路复用解码、Modbus/LIN/SOME-IP 适配器、ROS service/action、关节/电机控制或硬实时控制保证。通过被动监听只能收到设备主动发送的报文；需要请求才响应的设备暂不可用。已有复杂控制仍留在机器人/车辆主控。

Reserved external IDs prevent USB/LAN fallback. Fresh reads return recently received cache entries, not on-demand bus acquisitions. No CAN transmission, diagnostics, automatic DBC import, multiplexed/bitfield decoder, Modbus/LIN/SOME-IP driver, ROS service/action or hard real-time control is implemented. Request/response-only devices need a future explicitly authorized adapter.

## 安装并启用 / Setup

1. 构建对应分支 Gateway：Linux `dotnet publish src/FluentControl.FcsHost -c Release -r linux-arm64 --self-contained true -o host-out`（x64 改 RID）；Windows `dotnet publish src/FluentControl.FcsGateway -c Release -o gateway-out`。CI 的预览 host 附件包含独立 HTTP host，不是 FC 安装包；Windows 的集成 Gateway 使用命名管道。
2. 安装 Python 3.10+；CAN worker 只用标准库与系统 `iproute2`，需 Linux CAN 驱动。ROS worker 使用已有 ROS 2 环境，本版 CI 使用 Jazzy、`rclpy/std_msgs/sensor_msgs`。将 `tools/gateway-adapters/*.py` 一起部署；它们是受信任的协议程序，不是 Gateway 的受限用户脚本。
3. 设置 Gateway 的 `FCS_DATA`。Linux 沿用宿主数据目录；Windows 默认 `%LOCALAPPDATA%\FluentControl\FcsGateway`。把 [adapters.example.json](../deploy/adapters/adapters.example.json) 复制为该目录的 `adapters.json`。按实际设备修改信号、单位与有效期，只将需要的条目改成 `enabled: true`，重启 Gateway。
4. Gateway 仅在有启用条目时监听 `127.0.0.1:18767`（端口可配置），生成 `adapter-tokens/<id>.token`。缺少配置或全部禁用时不启动生产者入口。token 不出现在设备清单、状态、URL 或日志；Unix token 目录 0700、文件 0600。Windows 使用同一用户私有目录，勿把此目录放进共享路径。
5. 复制对应 worker 配置，修改 token 文件**绝对路径**和映射。启动 Gateway 后再启动 worker。修改授权、单位、ID 或端口后须重启 Gateway 与 worker；停用条目并重启可撤销接入。轮换 token 时停进程、删除该 token 文件、重启 Gateway 生成新值，再重启 worker。

```sh
# CAN: interface must already be configured UP + listen-only by its administrator.
python3 tools/gateway-adapters/socketcan_adapter.py /etc/fcs/socketcan.json

# ROS 2: use the environment of the robot's supported ROS installation.
. /opt/ros/jazzy/setup.sh
python3 tools/gateway-adapters/ros2_adapter.py /etc/fcs/ros2.json
```

CAN 配置见 [socketcan.example.json](../deploy/adapters/socketcan.example.json)：`canId` 是 JSON 十进制（示例 291 = 0x123），`offset` 为从 0 开始的字节偏移，`length` 为 1..8 字节，结果为 `整数 × factor + bias`。可选 `invalidRaw` 数组列出厂商定义的无效原始整数（按 signed 解码后比较），命中时立即发布 null/invalid。示例报文/倍率只是测试数据，不能直接用于未知硬件。

ROS 输入见 [ros2.example.json](../deploy/adapters/ros2.example.json)；仅导出见 [ros2-export.example.json](../deploy/adapters/ros2-export.example.json)。导出使用独立的 **gateway.token 读取凭据**，不使用 manager.token 或生产者 token；当前导出 worker 连接同机 Linux/HTTP host 的 `127.0.0.1:<port>/rpc`，未实现 Windows 命名管道读取。可同时配置 `inputs` 与 `exports`。输出只允许 `/fcs/telemetry/*`，输入禁止该前缀，输出也不转发外部适配器资源，减少自动反馈环路。ROS remap 或其他中继仍应由部署者检查。

Build/publish the appropriate Gateway, install Python and optional ROS packages, copy the disabled examples, explicitly enable only your resources, and restart. Set absolute token-file paths in worker configurations. Imports use per-adapter producer tokens; ROS telemetry exports use the separate read-only Gateway token and currently require the same-host HTTP host. No keys are embedded in the examples. Missing/disabled configuration opens no producer listener.

## CAN 及其他有线接入需要准备什么

| 准备项 | CAN / CAN FD | 其他接口 |
|---|---|---|
| 所有者/厂商允许的范围 | 明确允许接入哪个总线、读取哪些 ID/信号；受保护诊断网关还可能需要厂商 SDK、认证或维修授权 | 同样先确定允许读取的资源；连接线本身不代表控制权限 |
| 硬件 | 与 Linux SocketCAN 驱动兼容的 USB-CAN 或 CAN HAT；按需求选择隔离、CAN FD、listen-only 支持 | RS-485 需要匹配的 USB-RS485；工业以太网需要对应接口或转换器 |
| 接线 | 设备连接器针脚、CAN H/L、参考地/隔离说明、供电、拓扑和终端要求；不要猜 OBD 引脚或盲目增加终端 | RS-485 A/B、偏置/终端要求；以太网地址、路由、端口 |
| 链路参数 | nominal bitrate；FD 还要 data bitrate、控制器/设备兼容性；接口由管理员预配置 | Modbus RTU 的波特率、校验、站号；网络协议的地址与安全模式 |
| 语义资料 | DBC、厂商报文表或你自己定义的协议：ID、DLC、字节序、符号、缩放、单位、周期、无效值、复用条件。本版手动映射支持的整字节字段 | Modbus 寄存器表/数据类型；OPC UA 的 namespace/NodeId、证书信任及读取角色 |
| Gateway 授权 | 显式信号目录 + 每个适配器独立 token，不能扩大到设备写入 | 后续适配器沿用相同目录与授权入口 |

经典 CAN 帧本身没有通用的用户名/密码登录流程。**总线接入许可、厂商应用层认证、操作系统接口权限、FCS 信号发布授权是不同层次。** CAN ID 是报文标识，不是密码。FCS 不自动解锁受保护网关，不把猜出的报文当成已验证型号驱动。

如要让 C3 自己接 CAN，ESP32-C3 的 TWAI 还需要外部收发器，不能把 GPIO 直接接 CAN H/L；其控制器不支持 CAN FD。**本版使用 Gateway 主机的 CAN 适配器，没有给现有 C3 传感器固件增加 CAN 驱动。** 参考 [Espressif C3 TWAI](https://docs.espressif.com/projects/esp-idf/en/v5.5/esp32c3/api-reference/peripherals/twai.html)。

Preparation: obtain the owner's/vendor's permission and message definitions, a supported CAN adapter and driver, verified wiring/termination instructions and nominal/data bitrates. Classical CAN has no universal user-login mechanism; device application authentication, OS interface access and FCS publish grants remain separate. C3 requires an external transceiver and cannot handle CAN FD. This preview uses the host's CAN interface, not new C3 firmware.

## 运行、延迟与授权边界 / Runtime and security

- Gateway 每个进程最多 16 个适配器、32 个虚拟设备、256 个信号，每个适配器最多 64 个信号。仅 RAM 保存最近值；有效期 500..60000 ms，不新增持续写盘日志。生产者每秒最多 10 批、单批最多 32 KiB；快源合并成最近值，并非逐帧录波。
- 数据带批次/样本序号及进程 boot ID；重试保留原始时间，心跳不刷新数据年龄。每个适配器最多记住 256 个旧 boot ID，超过须重启 Gateway；重启后缓存为空。CAN 使用内核接收时间，ROS 使用回调接收时间，均不冒充设备采样时间。重启 worker 不回放持久历史。
- 生产者协议是 4 字节大端长度 + `fcs-adapter/1` JSON，回复同样分帧 `{ "ok": true/false }`；最多 4 个并发连接、每次 3 秒期限。只绑定 IPv4 loopback。不得通过代理/端口映射把生产者入口公开到 LAN/互联网。
- 独立 token 只允许发布预先登记的 `deviceId/sensorId/signalId` 与类型，不能通过数据包添加设备、修改单位或调用 USB。它证明生产者持有本机授权，**不证明源 CAN 报文或 ROS 发布者可信**；受信任 worker 不是恶意代码沙箱。同一用户/管理员能访问 token 和内核接口，不能把它当作进程隔离边界。
- ROS_DOMAIN_ID 用于发现隔离，不能代替身份认证。已有 SROS2 部署应继续使用其 keystore、enclave、DDS permissions 及 `ROS_SECURITY_ENABLE=true` / `ROS_SECURITY_STRATEGY=Enforce`。不要关闭机器人的安全策略来“接通”；未启用 DDS Security 的话题仍受其原网络信任边界约束。
- Linux Docker 需要可用的宿主 CAN 驱动和同一网络命名空间；桥接网络看不到宿主 `can0`。可用 host network，显式挂载只读配置/所需 token；接口由宿主管理员设置，worker 不需要自行获取 NET_ADMIN 或 privileged。容器是否能访问 CAN/ROS 取决于 NAS/Pi 内核及网络配置；Docker Desktop/任意路由器不保证可用。
- worker 需由系统服务或 ROS launch 单独托管。本版没有图形化安装/授权编辑器、自动安装 ROS/CAN 驱动或 systemd 服务，也不自动共享/上传云。上级 OPC UA 共享仍须单独配置 export 和证书信任。

The producer channel is bounded, local-only and explicitly scoped. Values expire; retries cannot renew freshness. These trusted workers are not sandboxed plugins. Tokens authenticate producers, not underlying CAN frames or ROS publishers. Preserve SROS2 permissions where deployed. Container CAN access needs the host kernel and correct network namespace; no automatic hardware configuration, driver installation, service installation or cloud upload is performed. This is sampled telemetry, not deterministic control or lossless recording.

## 验证 / Validation

`tests/fcs-adapters/integration.py` 使用真实 Gateway HTTP/TCP 端点验证作用域、错误令牌、重放、过期、虚拟目录与不可写；`windows_pipe.py` 通过 Windows 命名管道验证同一行为。`test_workers.py` 覆盖 CAN 位标志、扩展帧、FD 长度、字节序、有符号、映射拒绝和配置边界。CI 另外运行真实 Linux vcan、ROS Jazzy 进程间发布/订阅、过期质量及原 OPC UA 回归。ROS 输出测试使用明确的 C3 协议 fixture，不冒充实体传感器。

硬件验收仍需真实 USB-CAN/FD、接线、listen-only 读回、总线负载、拔插、长时间断线、目标机器人 QoS/SROS2、NAS/Pi 服务启动及 FC 实际自动亮度联动。CI 的绿色结果只证明对应软件测试通过。

Tests exercise real Gateway transports, the Windows pipe, virtual CAN and ROS process communication. Physical adapter/robot/NAS/Pi and end-user FC behavior remain separate acceptance work. Check the linked PR/Actions for the exact commit's results.

参考 / Primary references: [Linux SocketCAN](https://www.kernel.org/doc/html/latest/networking/can.html), [iproute2 CAN flags](https://github.com/iproute2/iproute2/blob/main/ip/iplink_can.c), [ROS QoS](https://docs.ros.org/en/jazzy/Concepts/Intermediate/About-Quality-of-Service-Settings.html), [ROS security](https://docs.ros.org/en/jazzy/Tutorials/Advanced/Security/Introducing-ros2-security.html). No third-party driver code was copied into the workers; ROS runtime packages retain their original licenses.
